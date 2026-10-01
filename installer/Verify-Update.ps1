param(
    [Parameter(Mandatory = $true)][string]$Archive,
    [Parameter(Mandatory = $true)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedSha256
)
$ErrorActionPreference = 'Stop'
# Obtain the expected archive digest from the approved CI run/version guide, separately from the download.
$file = Get-Item -LiteralPath $Archive
if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Use a regular downloaded archive.' }
$actual = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
if ($actual -cne $ExpectedSha256.ToUpperInvariant()) { throw 'Update digest mismatch. Do not extract or run this package.' }
Write-Host 'Archive SHA-256 verified. Close Sheets Windows, extract the installer and run it explicitly.'
# This checker never extracts, downloads, executes, migrates or deletes anything.
