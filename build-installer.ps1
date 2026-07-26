$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "DesktopFamiliar.csproj"
$publishDirectory = Join-Path $PSScriptRoot "Artifacts\InstallerStaging\DesktopFamiliar-standalone-win-x64-0.9.3"
$installerScript = Join-Path $PSScriptRoot "Installer\DesktopFamiliar.iss"

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDirectory

if ($LASTEXITCODE -ne 0) {
    throw "Desktop Familiar publish failed with exit code $LASTEXITCODE."
}

$candidates = @(
    (Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 7\ISCC.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

$iscc = $candidates | Select-Object -First 1
if (-not $iscc) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        $iscc = $command.Source
    }
}
if (-not $iscc) {
    throw "Inno Setup 7 or 6 is required to build the installer. Install it from https://jrsoftware.org/isdl.php and run this script again."
}

& $iscc "/DMyAppSourceRoot=$publishDirectory" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Installer compilation failed with exit code $LASTEXITCODE."
}

$output = Join-Path $PSScriptRoot "Artifacts\Installer\Desktop-Familiar-Setup-0.9.3.exe"
if (-not (Test-Path -LiteralPath $output)) {
    throw "Installer compilation completed but the expected output was not found at $output."
}

Write-Host "Desktop Familiar installer created at: $output"
