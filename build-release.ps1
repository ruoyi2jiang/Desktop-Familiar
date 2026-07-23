param(
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$folderName = if ($SelfContained) { "DesktopFamiliar-standalone-win-x64" } else { "DesktopFamiliar-win-x64" }
$output = Join-Path $PSScriptRoot "Artifacts\$folderName"
$arguments = @(
    "publish",
    (Join-Path $PSScriptRoot "DesktopFamiliar.csproj"),
    "-c", "Release",
    "-r", "win-x64",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-o", $output
)

if ($SelfContained) {
    $arguments += "--self-contained"
    $arguments += "true"
} else {
    $arguments += "--self-contained"
    $arguments += "false"
}

dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Desktop Familiar publish failed with exit code $LASTEXITCODE."
}

Write-Host "Desktop Familiar was published to: $output"
