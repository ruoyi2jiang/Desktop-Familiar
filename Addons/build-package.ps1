[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AddonDirectory,

    [Parameter(Mandatory)]
    [string]$OutputDirectory
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
        throw "The addon $Name '$Value' is not safe for a package name."
    }
}

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

$resolvedOutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
$packagePath = Join-Path $resolvedOutputDirectory "$addonId-$addonVersion.zip"
if (Test-Path -LiteralPath $packagePath) {
    throw "Refusing to overwrite an existing addon package: $packagePath"
}

$stagingDirectory = Join-Path ([IO.Path]::GetTempPath()) "DesktopFamiliar.AddonPackage.$([Guid]::NewGuid().ToString('N'))"
try {
    New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
    & dotnet build $projectPath -c Release --no-restore --output $stagingDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "The addon build failed: $projectPath"
    }
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stagingDirectory 'addon.json') -Force

    Get-ChildItem -LiteralPath $stagingDirectory -Filter '*.pdb' -File -Recurse |
        Remove-Item -Force
    foreach ($sharedSdkFile in 'DesktopFamiliar.AddonSdk.dll', 'DesktopFamiliar.AddonSdk.xml') {
        $sharedSdkPath = Join-Path $stagingDirectory $sharedSdkFile
        if (Test-Path -LiteralPath $sharedSdkPath -PathType Leaf) {
            Remove-Item -LiteralPath $sharedSdkPath -Force
        }
    }

    if (-not (Test-Path -LiteralPath (Join-Path $stagingDirectory $assemblyFileName) -PathType Leaf)) {
        throw "The staged package does not contain its declared assembly: $assemblyFileName"
    }

    $packageItems = Get-ChildItem -LiteralPath $stagingDirectory | Select-Object -ExpandProperty FullName
    Compress-Archive -LiteralPath $packageItems -DestinationPath $packagePath

    $hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "Package: $packagePath"
    Write-Host "SHA-256: $hash"
    Write-Host 'Add this version, URL, API range, and SHA-256 to Addons/catalogue.json in the approved release commit.'
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}
