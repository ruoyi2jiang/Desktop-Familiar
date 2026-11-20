param([Parameter(Mandatory)][string] $Compiler)
$ErrorActionPreference = 'Stop'

# Exercise the production startup entries with a private registry key and a disposable app directory.
$startupScript = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\Installer\Startup.iss')).Path
$id = [guid]::NewGuid().ToString('N')
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture = Join-Path $tempRoot "DesktopFamiliar-StartupTest-$id"
$testKey = "Software\DesktopFamiliar-StartupTest-$id"
$valueName = 'Desktop Familiar'

function Compile-Fixture([string] $name, [string] $sections) {
    $path = Join-Path $fixture "$name.iss"
    @"
#define MyAppName "$valueName"
#define MyAppExeName "DesktopFamiliar.exe"
#define StartupRunKey "$testKey"
#define StartupDataRoot "{app}\UserData"
[Setup]
AppId=DesktopFamiliarStartupTest$id
AppName=Startup test fixture
AppVersion=1
DefaultDirName=$fixture\App
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
CreateUninstallRegKey=no
OutputDir=$fixture
OutputBaseFilename=$name
$sections
"@ | Set-Content -LiteralPath $path -Encoding UTF8
    & $Compiler /Q $path
    if ($LASTEXITCODE) { throw "Could not compile $name. Exit code $LASTEXITCODE" }
}

function Invoke-Fixture([string] $executable, [string] $appDirectory) {
    $process = Start-Process -FilePath $executable -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$appDirectory`"" -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode) { throw "$executable exited with $($process.ExitCode)" }
}

$null = New-Item -ItemType Directory -Path $fixture
$key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($testKey)
try {
    # Earlier builds appended this unconditional deletion to Inno's uninstall log.
    Compile-Fixture 'legacy' @"
[Registry]
Root: HKCU; Subkey: "{#StartupRunKey}"; ValueName: "{#MyAppName}"; Flags: uninsdeletevalue dontcreatekey
"@
    Compile-Fixture 'current' ('#include "{0}"' -f $startupScript)

    foreach ($scenario in @('fresh', 'upgrade', 'opt-out')) {
        $app = Join-Path $fixture $scenario
        $installedCommand = '"{0}\DesktopFamiliar.exe"' -f $app
        $portableCommand = '"{0}\portable copy\DesktopFamiliar.exe"' -f $fixture
        if ($scenario -eq 'upgrade') {
            Invoke-Fixture (Join-Path $fixture 'legacy.exe') $app
            $key.SetValue($valueName, $portableCommand)
        }
        Invoke-Fixture (Join-Path $fixture 'current.exe') $app
        $expected = if ($scenario -eq 'upgrade') { $portableCommand } else { $installedCommand }
        if ($key.GetValue($valueName) -cne $expected) { throw "$scenario registered the wrong command" }
        if ($scenario -eq 'opt-out') {
            $key.DeleteValue($valueName)
            Invoke-Fixture (Join-Path $fixture 'current.exe') $app
            if ($null -ne $key.GetValue($valueName)) { throw 'Reinstall reversed the opt-out' }
        }
        Invoke-Fixture (Join-Path $app 'unins000.exe') $app
        $expected = if ($scenario -eq 'upgrade') { $portableCommand }
        if ($key.GetValue($valueName) -cne $expected) { throw "$scenario left the wrong command after uninstall" }
        $key.DeleteValue($valueName, $false)
        Write-Output "$scenario passed"
    }
} finally {
    $key.Dispose()
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($testKey, $false)
    $resolved = (Resolve-Path -LiteralPath $fixture).Path
    if ($resolved -ne [IO.Path]::Combine($tempRoot, "DesktopFamiliar-StartupTest-$id")) { throw 'Unexpected fixture directory' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
