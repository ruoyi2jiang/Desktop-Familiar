[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AddonDirectory,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$ApplicationDataRoot,

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-SafePackageSegment {
    param(
        [Parameter(Mandatory)]
        [string]$Value,

        [Parameter(Mandatory)]
        [string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or
        $Value -notmatch '^[A-Za-z0-9.-]+$' -or
        $Value.StartsWith('.', [StringComparison]::Ordinal) -or
        $Value.EndsWith('.', [StringComparison]::Ordinal)) {
        throw "The addon $Name '$Value' is not safe for a package directory."
    }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$addonsSourceRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resolvedAddonDirectory = [IO.Path]::GetFullPath($AddonDirectory)
if (-not (Test-Path -LiteralPath $resolvedAddonDirectory -PathType Container)) {
    throw "The addon directory was not found: $resolvedAddonDirectory"
}
if (-not $resolvedAddonDirectory.StartsWith($addonsSourceRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The addon directory must be inside the repository's Addons directory: $addonsSourceRoot"
}

$manifestPath = Join-Path $resolvedAddonDirectory 'addon.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "The addon manifest was not found: $manifestPath"
}
try {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
}
catch {
    throw "The addon manifest could not be read: $manifestPath"
}

$addonId = [string]$manifest.id
$addonVersion = [string]$manifest.version
$assemblyFileName = [string]$manifest.assemblyFileName
Assert-SafePackageSegment -Value $addonId -Name 'ID'
Assert-SafePackageSegment -Value $addonVersion -Name 'version'
if ([string]::IsNullOrWhiteSpace($assemblyFileName) -or
    [IO.Path]::GetFileName($assemblyFileName) -cne $assemblyFileName -or
    -not $assemblyFileName.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
    throw "The addon assembly filename '$assemblyFileName' is not safe."
}

$projectPaths = @(Get-ChildItem -LiteralPath $resolvedAddonDirectory -Filter '*.csproj' -File)
if ($projectPaths.Count -ne 1) {
    throw "The addon directory must contain exactly one project file; found $($projectPaths.Count)."
}
$projectPath = $projectPaths[0].FullName

$usesDefaultApplicationDataRoot = [string]::IsNullOrWhiteSpace($ApplicationDataRoot)
if ($usesDefaultApplicationDataRoot) {
    $ApplicationDataRoot = Join-Path (
        [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'DesktopFamiliar'
    $runningApplication = Get-Process -Name 'DesktopFamiliar' -ErrorAction SilentlyContinue
    if ($null -ne $runningApplication) {
        throw 'Exit Desktop Familiar before deploying a local addon build.'
    }
}

if (-not $SkipBuild) {
    & dotnet build (Join-Path $repositoryRoot 'DesktopFamiliar.slnx') -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw 'The Desktop Familiar solution build failed.'
    }
}

$targetPathOutput = @(& dotnet msbuild $projectPath -getProperty:TargetPath -property:Configuration=$Configuration)
if ($LASTEXITCODE -ne 0) {
    throw "The addon build output could not be resolved: $projectPath"
}
$targetPathText = $targetPathOutput |
    Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
    Select-Object -Last 1
if ([string]::IsNullOrWhiteSpace([string]$targetPathText)) {
    throw "The addon project did not report a $Configuration target path: $projectPath"
}

$assemblyPath = [string]$targetPathText
if (-not [IO.Path]::IsPathRooted($assemblyPath)) {
    $assemblyPath = Join-Path $resolvedAddonDirectory $assemblyPath
}
$assemblyPath = [IO.Path]::GetFullPath($assemblyPath)
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "The built addon assembly was not found: $assemblyPath"
}
if ([IO.Path]::GetFileName($assemblyPath) -cne $assemblyFileName) {
    throw "The project output '$([IO.Path]::GetFileName($assemblyPath))' does not match manifest assembly '$assemblyFileName'."
}

$resolvedApplicationDataRoot = [IO.Path]::GetFullPath($ApplicationDataRoot)
$addonsDirectory = Join-Path $resolvedApplicationDataRoot 'Addons'
$packagesDirectory = Join-Path $addonsDirectory 'packages'
$registryPath = Join-Path $addonsDirectory 'installed-addons.json'
$relativePackageDirectory = Join-Path (Join-Path 'packages' $addonId) $addonVersion
$targetDirectory = [IO.Path]::GetFullPath((Join-Path $addonsDirectory $relativePackageDirectory))
$packagesPrefix = [IO.Path]::GetFullPath($packagesDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $targetDirectory.StartsWith($packagesPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The local addon destination resolved outside the package directory.'
}

New-Item -ItemType Directory -Path $packagesDirectory -Force | Out-Null
if (Test-Path -LiteralPath $registryPath -PathType Leaf) {
    try {
        $registry = Get-Content -LiteralPath $registryPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "The local addon registry could not be read: $registryPath"
    }
    if ([int]$registry.schemaVersion -ne 1) {
        throw "The local addon registry schema '$($registry.schemaVersion)' is not supported by this script."
    }
}
else {
    $registry = [PSCustomObject][ordered]@{
        schemaVersion = 1
        addons = @()
    }
}

$installedAddons = @($registry.addons)
$previous = $installedAddons |
    Where-Object { [string]::Equals([string]$_.id, $addonId, [StringComparison]::OrdinalIgnoreCase) } |
    Select-Object -First 1
$enabled = if ($null -eq $previous) { $true } else { [bool]$previous.enabled }
$previousDirectory = $null
if ($null -ne $previous -and -not [string]::IsNullOrWhiteSpace([string]$previous.packageDirectory)) {
    if ([IO.Path]::IsPathRooted([string]$previous.packageDirectory)) {
        throw 'The existing addon package directory is not relative to the addon data root.'
    }
    $previousDirectory = [IO.Path]::GetFullPath((Join-Path $addonsDirectory ([string]$previous.packageDirectory)))
    if (-not $previousDirectory.StartsWith($packagesPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The existing addon package directory resolves outside the package store.'
    }
}

$newInstallation = [PSCustomObject][ordered]@{
    id = $addonId
    installedVersion = $addonVersion
    enabled = $enabled
    packageDirectory = $relativePackageDirectory
    manifest = $manifest
}
$registry.addons = @(
    $installedAddons | Where-Object {
        -not [string]::Equals([string]$_.id, $addonId, [StringComparison]::OrdinalIgnoreCase)
    }
) + @($newInstallation)

$operationId = [Guid]::NewGuid().ToString('N')
$stagingDirectory = Join-Path $packagesDirectory ".staging-local-$operationId"
$backupDirectory = Join-Path $packagesDirectory ".backup-local-$operationId"
$temporaryRegistryPath = Join-Path $addonsDirectory ".installed-addons-$operationId.tmp"
$registryBackupPath = Join-Path $addonsDirectory ".installed-addons-$operationId.backup"
$installedDestination = $false
$backedUpDestination = $false

try {
    New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stagingDirectory 'addon.json')

    $buildOutputDirectory = Split-Path -Parent $assemblyPath
    foreach ($item in Get-ChildItem -LiteralPath $buildOutputDirectory) {
        if ($item.Name.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($item.Name, 'DesktopFamiliar.AddonSdk.dll', [StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($item.Name, 'DesktopFamiliar.AddonSdk.xml', [StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($item.Name, 'addon.json', [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        Copy-Item -LiteralPath $item.FullName -Destination $stagingDirectory -Recurse
    }
    if (-not (Test-Path -LiteralPath (Join-Path $stagingDirectory $assemblyFileName) -PathType Leaf)) {
        throw "The staged local addon does not contain its declared assembly: $assemblyFileName"
    }

    if (Test-Path -LiteralPath $targetDirectory) {
        Move-Item -LiteralPath $targetDirectory -Destination $backupDirectory
        $backedUpDestination = $true
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $targetDirectory) -Force | Out-Null
    Move-Item -LiteralPath $stagingDirectory -Destination $targetDirectory
    $installedDestination = $true

    $registryJson = $registry | ConvertTo-Json -Depth 20 -Compress
    [IO.File]::WriteAllText($temporaryRegistryPath, $registryJson, [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $registryPath -PathType Leaf) {
        [IO.File]::Replace($temporaryRegistryPath, $registryPath, $registryBackupPath)
    }
    else {
        [IO.File]::Move($temporaryRegistryPath, $registryPath)
    }
}
catch {
    if ($installedDestination -and (Test-Path -LiteralPath $targetDirectory)) {
        Remove-Item -LiteralPath $targetDirectory -Recurse -Force
    }
    if ($backedUpDestination -and (Test-Path -LiteralPath $backupDirectory)) {
        Move-Item -LiteralPath $backupDirectory -Destination $targetDirectory
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
    if (Test-Path -LiteralPath $temporaryRegistryPath) {
        Remove-Item -LiteralPath $temporaryRegistryPath -Force
    }
    if (Test-Path -LiteralPath $registryBackupPath) {
        try {
            Remove-Item -LiteralPath $registryBackupPath -Force
        }
        catch {
            Write-Warning "The temporary local addon registry backup could not be removed: $registryBackupPath"
        }
    }
}

foreach ($obsoleteDirectory in @($backupDirectory, $previousDirectory)) {
    if ([string]::IsNullOrWhiteSpace([string]$obsoleteDirectory) -or
        [string]::Equals([string]$obsoleteDirectory, $targetDirectory, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $obsoleteDirectory)) {
        continue
    }
    try {
        Remove-Item -LiteralPath $obsoleteDirectory -Recurse -Force
    }
    catch {
        Write-Warning "The obsolete local addon directory could not be removed: $obsoleteDirectory"
    }
}

Write-Host "Deployed $addonId $addonVersion to $targetDirectory"
Write-Host "Enabled state preserved: $enabled"
Write-Host 'Start the source application with: dotnet run --project .\DesktopFamiliar.csproj'
