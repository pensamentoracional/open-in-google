$ErrorActionPreference = 'Stop'
function Run-Checked($file, $arguments) {
    $process = Start-Process -FilePath $file -ArgumentList $arguments -PassThru
    if (!$process.WaitForExit(120000)) { $process.Kill(); throw "Timeout: $file" }
    if ($process.ExitCode -ne 0) { throw "Nonzero exit: $file" }
}
# Disposable Windows CI account only. Never run against a user's existing installation.
$state = Join-Path $env:LOCALAPPDATA 'SheetsWindows'
$installed = Join-Path $env:LOCALAPPDATA 'Programs/SheetsWindows'
if ((Test-Path $state) -or (Test-Path $installed) -or (Test-Path 'HKCU:/Software/SheetsWindows/Integration')) { throw 'Lifecycle test requires a clean disposable profile' }
$defaultKey = 'HKCU:/Software/Classes/.xlsx'
$choiceKey = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Explorer/FileExts/.xlsx/UserChoice'
function Defaults-Snapshot {
    $default = if (Test-Path $defaultKey) { (Get-Item $defaultKey).GetValue('') } else { $null }
    $choice = if (Test-Path $choiceKey) { (Get-ItemProperty $choiceKey | Select-Object ProgId, Hash) | ConvertTo-Json -Compress } else { '<absent>' }
    @("default:$default", "choice:$choice")
}
$before = Defaults-Snapshot
$setup = (Resolve-Path 'artifacts/installer/SheetsWindows-Setup-win-x64.exe').Path
Run-Checked $setup '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
$exe = Join-Path $installed 'SheetsWindows.exe'
if (!(Test-Path $exe)) { throw 'Executable not installed' }
Run-Checked $exe '--version'
if (!(Test-Path 'HKCU:/Software/SheetsWindows/Integration')) { throw 'Association registration missing' }
# Data sentinels are outside the installation manifest; Google network access is never needed.
New-Item (Join-Path $state 'backups') -ItemType Directory -Force | Out-Null
$sentinels = @('registry.db', 'google.db', 'replacement.db', 'launcher-client.json', 'replacement-root.txt', 'backups/preserved.snapshot')
foreach ($name in $sentinels) { [IO.File]::WriteAllText((Join-Path $state $name), "preserve:$name") }
$shortcut = Join-Path $env:RUNNER_TEMP 'preserved.url'
[IO.File]::WriteAllText($shortcut, "[InternetShortcut]`r`nURL=https://docs.google.com/spreadsheets/d/test/edit`r`n")
$shortcutBefore = [IO.File]::ReadAllText($shortcut)
Run-Checked $setup '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
Run-Checked (Join-Path $installed 'unins000.exe') '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
if (Test-Path $exe) { throw 'Installed executable remains' }
if (Test-Path 'HKCU:/Software/SheetsWindows/Integration') { throw 'Owned registration remains' }
foreach ($name in $sentinels) { if ([IO.File]::ReadAllText((Join-Path $state $name)) -ne "preserve:$name") { throw "Data changed: $name" } }
if ([IO.File]::ReadAllText($shortcut) -ne $shortcutBefore) { throw 'Shortcut changed' }
if (Compare-Object $before (Defaults-Snapshot)) { throw 'Windows defaults changed' }
# Reinstall and remove again demonstrates retained state does not block maintenance.
Run-Checked $setup '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
Run-Checked (Join-Path $installed 'unins000.exe') '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
Write-Host 'Per-user install, upgrade, uninstall and reinstall passed; backups, state, shortcut and Windows defaults preserved.'
