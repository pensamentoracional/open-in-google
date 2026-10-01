$ErrorActionPreference = 'Stop'
# Extract only the upload function; never execute OAuth, UI, registry or the worker entry point.
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot '../Open-InGoogle.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Worker parse failed' }
$function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'New-ConvertedFile' }, $true)
. ([scriptblock]::Create($function.Extent.Text))
function Invoke-WebRequest {
    param($Uri, $Method, [switch]$UseBasicParsing, $Headers, $ContentType, $Body)
    $script:ReportedLength = [int64]$Headers['X-Upload-Content-Length']
    return @{ Headers = @{ Location = 'https://fixture.invalid/session' } }
}
function Invoke-RestMethod {
    param($Uri, $Method, $InFile, $ContentType)
    $script:UploadedPath = $InFile
    return @{ id = 'fixture-id' }
}
$root = Join-Path ([IO.Path]::GetTempPath()) ('literal-paths-' + [guid]::NewGuid().ToString('N'))
try {
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $path = Join-Path $root 'Relatorio [2026] acao.xlsx'
    $decoy = Join-Path $root 'Relatorio 2 acao.xlsx'
    [IO.File]::WriteAllBytes($path, [byte[]](1,2,3,4,5))
    [IO.File]::WriteAllBytes($decoy, [byte[]](9))
    $id = New-ConvertedFile 'folder' $path 'literal' 'application/test' 'application/test'
    if ($id -ne 'fixture-id' -or $script:ReportedLength -ne 5 -or $script:UploadedPath -cne $path) { throw 'Literal path selected the wrong file or length' }
    Write-Host 'Literal brackets/spaces upload test passed without Google access.'
} finally { if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root, $true) } }
