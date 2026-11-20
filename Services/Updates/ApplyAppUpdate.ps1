# Embedded update handoff. Paths and localized messages arrive as environment data.
$ErrorActionPreference = 'Stop'
$stage = $env:DF_UPDATE_STAGE
$target = $env:DF_UPDATE_TARGET
$backup = $stage + '.previous'
$installer = $stage + '.exe'
$parentExited = $false
$replaced = $false

function Start-UpdatedFile([string] $path) {
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $path
    $start.WorkingDirectory = [IO.Path]::GetDirectoryName($path)
    $start.UseShellExecute = $path -eq $installer
    $start.CreateNoWindow = $true
    return [Diagnostics.Process]::Start($start)
}

try {
    try { $parent = [Diagnostics.Process]::GetProcessById([int]$env:DF_UPDATE_PARENT) }
    catch [ArgumentException] { $parent = $null }
    if ($parent -and !$parent.WaitForExit(120000)) { throw 'The previous process is still running.' }
    $parentExited = $true
    $hash = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($stage)
    try { $actual = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $hash.Dispose() }
    if ($actual -ne $env:DF_UPDATE_SHA256) {
        throw 'SHA-256 verification failed.'
    }
    if ($env:DF_UPDATE_KIND -eq 'installer') {
        [IO.File]::Move($stage, $installer)
        $setup = Start-UpdatedFile $installer
        $setup.WaitForExit()
        if ($setup.ExitCode -ne 0) { throw ('Setup exit code ' + $setup.ExitCode) }
    } else {
        [IO.File]::Replace($stage, $target, $backup)
        $replaced = $true
        $updated = Start-UpdatedFile $target
        if ($updated.WaitForExit(3000) -and $updated.ExitCode -ne 0) {
            throw ('App startup exit code ' + $updated.ExitCode)
        }
        $replaced = $false
        Remove-Item -LiteralPath $backup -ErrorAction SilentlyContinue
    }
} catch {
    $detail = $_.Exception.ToString()
    try {
        if ($replaced) {
            [IO.File]::Replace($backup, $target, $stage)
        }
        if ($parentExited -and [IO.File]::Exists($target)) { [void](Start-UpdatedFile $target) }
    } catch {
        $detail += [Environment]::NewLine + $_.Exception.ToString()
        if ([IO.File]::Exists($backup)) { $detail += [Environment]::NewLine + $backup }
    }
    throw $detail
} finally {
    foreach ($file in @($stage, $installer)) {
        if ([IO.File]::Exists($file)) { Remove-Item -LiteralPath $file -ErrorAction SilentlyContinue }
    }
}
