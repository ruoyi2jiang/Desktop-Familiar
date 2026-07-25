[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$addonDirectory = $PSScriptRoot
$projectPath = Join-Path $addonDirectory 'DesktopFamiliar.Addons.AutoClicker.csproj'
$manifestPath = Join-Path $addonDirectory 'addon.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$assemblyPath = Join-Path $addonDirectory "bin\Release\net8.0-windows\$($manifest.assemblyFileName)"
$resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

& dotnet build $projectPath -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    throw 'The Auto Clicker addon build failed.'
}
if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "The built addon assembly was not found: $assemblyPath"
}

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
$packagePath = Join-Path $resolvedOutputDirectory "DesktopFamiliar.AutoClicker-$($manifest.version).zip"
if (Test-Path -LiteralPath $packagePath) {
    throw "Refusing to overwrite an existing addon package: $packagePath"
}

$stagingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "DesktopFamiliar.AutoClicker.$([Guid]::NewGuid().ToString('N'))"
try {
    New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stagingDirectory 'addon.json')
    Copy-Item -LiteralPath $assemblyPath -Destination (Join-Path $stagingDirectory $manifest.assemblyFileName)
    # The explicit existence check above prevents replacement; Windows PowerShell's
    # Compress-Archive does not support the PowerShell 7-only -NoClobber switch.
    Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $stagingDirectory -File | Select-Object -ExpandProperty FullName) -DestinationPath $packagePath

    $hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "Package: $packagePath"
    Write-Host "SHA-256: $hash"
    Write-Host "Add this version, URL, and SHA-256 to addons/catalogue.json in the release commit."
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}
