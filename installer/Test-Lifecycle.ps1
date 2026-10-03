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
$extensions = @('.xlsx', '.ods', '.xls', '.csv', '.tsv')
function Defaults-Snapshot {
    foreach ($extension in $extensions) {
        $defaultKey = "HKCU:/Software/Classes/$extension"
        $choiceKey = "HKCU:/Software/Microsoft/Windows/CurrentVersion/Explorer/FileExts/$extension/UserChoice"
        $default = if (Test-Path $defaultKey) { (Get-Item $defaultKey).GetValue('') } else { $null }
        $choice = if (Test-Path $choiceKey) { (Get-ItemProperty $choiceKey | Select-Object ProgId, Hash) | ConvertTo-Json -Compress } else { '<absent>' }
        "$extension-default:$default"; "$extension-choice:$choice"
    }
}
function Current-Uninstaller {
    $key = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{D970FA65-0364-4F10-A6AA-D4302F31B607}_is1'
    $command = (Get-ItemProperty $key).UninstallString
    $path = $command.Trim('"')
    if ([IO.Path]::GetDirectoryName($path) -ne $installed -or [IO.Path]::GetFileName($path) -notmatch '^unins[0-9]+\.exe$' -or !(Test-Path $path)) { throw 'Unexpected uninstall command' }
    return $path
}
$before = Defaults-Snapshot
$oldSetup = (Resolve-Path 'artifacts/installer-old/ZagoSheetsWin-Setup-win-x64.exe').Path
$setup = (Resolve-Path 'artifacts/installer/ZagoSheetsWin-Setup-win-x64.exe').Path
Write-Host 'Running installer'
Run-Checked $oldSetup '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
$exe = Join-Path $installed 'SheetsWindows.exe'
if ((Get-ItemProperty 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{D970FA65-0364-4F10-A6AA-D4302F31B607}_is1').DisplayName -ne 'ZagoSheetsWin') { throw 'Product name incorrect' }
if (!(Test-Path $exe)) { throw 'Executable not installed' }
Run-Checked $exe '--version'
if (!(Test-Path 'HKCU:/Software/SheetsWindows/Integration')) { throw 'Association registration missing' }
foreach ($extension in $extensions) {
    if ((Get-ItemProperty 'HKCU:/Software/SheetsWindows/Integration/Capabilities/FileAssociations').$extension -ne 'SheetsWindows.Xlsx') { throw "Missing association: $extension" }
}
# Data sentinels are outside the installation manifest; Google network access is never needed.
foreach ($directory in @('backups', 'auth', 'shortcuts', 'uploads', 'logs')) { New-Item (Join-Path $state $directory) -ItemType Directory -Force | Out-Null }
$sentinels = @('registry.db', 'google.db', 'replacement.db', 'launcher-client.json', 'replacement-root.txt', 'backups/preserved.snapshot', 'formats.json', 'auth/preserved.dat', 'shortcuts/preserved.url', 'uploads/preserved.session', 'shortcut-icon-v1.ico', 'xls-replacement.json', 'logs/events.jsonl')
foreach ($name in $sentinels) { [IO.File]::WriteAllText((Join-Path $state $name), "preserve:$name") }
$shortcut = Join-Path $env:RUNNER_TEMP 'preserved.url'
[IO.File]::WriteAllText($shortcut, "[InternetShortcut]`r`nURL=https://docs.google.com/spreadsheets/d/test/edit`r`n")
$shortcutBefore = [IO.File]::ReadAllText($shortcut)
Write-Host 'Running installer'
Run-Checked $setup '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
# The previous-version package uses the same payload to exercise installer version policy.
$key = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{D970FA65-0364-4F10-A6AA-D4302F31B607}_is1'
if ((Get-ItemProperty $key).DisplayVersion -ne '0.9.3') { throw 'Upgrade version missing' }
$reject = Start-Process -FilePath $oldSetup -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -PassThru
if (!$reject.WaitForExit(120000)) { $reject.Kill(); throw 'Downgrade rejection timed out' }
if ($reject.ExitCode -eq 0 -or (Get-ItemProperty $key).DisplayVersion -ne '0.9.3') { throw 'Downgrade was not blocked' }
Write-Host 'Running registered uninstaller'
Run-Checked (Current-Uninstaller) '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
if (Test-Path $exe) { throw 'Installed executable remains' }
if (Test-Path 'HKCU:/Software/SheetsWindows/Integration') { throw 'Owned registration remains' }
foreach ($name in $sentinels) { if ([IO.File]::ReadAllText((Join-Path $state $name)) -ne "preserve:$name") { throw "Data changed: $name" } }
if ([IO.File]::ReadAllText($shortcut) -ne $shortcutBefore) { throw 'Shortcut changed' }
if (Compare-Object $before (Defaults-Snapshot)) { throw 'Windows defaults changed' }
# Reinstall and remove again demonstrates retained state does not block maintenance.
Write-Host 'Running installer'
Run-Checked $setup '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
Write-Host 'Running registered uninstaller'
Run-Checked (Current-Uninstaller) '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
Write-Host 'Per-user install, upgrade, uninstall and reinstall passed; backups, state, shortcut and Windows defaults preserved.'
