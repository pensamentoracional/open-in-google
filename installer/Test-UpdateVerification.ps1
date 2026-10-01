$ErrorActionPreference = 'Stop'
$root = Join-Path $env:RUNNER_TEMP ('update-check-' + [guid]::NewGuid().ToString('N'))
try {
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $file = Join-Path $root 'package [2026].zip'
    [IO.File]::WriteAllBytes($file, [byte[]](1,2,3,4))
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    & "$PSScriptRoot/Verify-Update.ps1" -Archive $file -ExpectedSha256 $hash
    $rejected = $false
    try { & "$PSScriptRoot/Verify-Update.ps1" -Archive $file -ExpectedSha256 ('0' * 64) } catch { $rejected = $true }
    if (!$rejected) { throw 'Wrong hash accepted' }
    if ([IO.File]::ReadAllBytes($file).Length -ne 4) { throw 'Verification changed package' }
    Write-Host 'Correct archive hash accepted; mismatch rejected; package untouched.'
} finally { if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root, $true) } }
