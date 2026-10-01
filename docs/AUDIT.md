# Auditoria da base upstream

Data: 2026-10-01. Revisão: 124419b9ffce1f42c696ab9068fa9db6a4a9c199. Todos os sete arquivos versionados foram lidos. Auditoria estática em Linux; sem PowerShell, execução Windows ou acesso ao Drive. Achados não equivalem a testes de integração aprovados.

| Arquivo | Responsabilidade | Herança |
|---|---|---|
| Open-InGoogle.ps1 | OAuth, DPAPI, mapa, Drive, conversão, navegador, erros | REFACTOR; substituir decisões de atualização e identidade |
| Install.ps1 | Copiar worker, configurar cliente e menus HKCU | REFACTOR; candidato upstream |
| Uninstall.ps1 | Remover menus e opcionalmente dados locais | REFACTOR; preservar documentos e recuperação |
| README.md | Uso e limites | REPLACE na evolução; original arquivado |
| SETUP.md | Credenciais e instalação | REFACTOR; candidato upstream |
| LICENSE | MIT, Swati K, 2026 | REUSE integral |
| .gitignore | Exclusões de estado e credenciais | REUSE/expandir quando houver código novo |

## Fluxo atual

Explorer → powershell.exe → validação por extensão → config.json → token DPAPI/OAuth/refresh → pasta Drive → mapa por caminho → fallback por nome → atualização ou criação → mapa salvo → navegador.

Estado em LOCALAPPDATA/OpenInGoogle: config.json (cliente e folder_id), tokens.dat (DPAPI CurrentUser), filemap.json (caminho para ID), open-in-google.log. Dependências: Windows PowerShell, .NET System.Security e Windows.Forms, Registry HKCU, navegador e Google OAuth/Drive; não há dependências externas declaradas, testes ou CI nesta revisão.

## Achados com evidência

| ID | Gravidade | Evidência no worker | Risco e direção |
|---|---|---|---|
| A01 | Crítica | main chama Update-ConvertedFile sempre que targetId existe | Pode substituir edições online; abrir sem upload quando inalterado |
| A02 | Crítica | Find-ExistingFile + adoção no main | Nome não prova identidade; mapa ausente pode adotar documento errado |
| A03 | Alta | Get-FileMap/Save-FileMap e ausência de lock | Corridas, uploads duplicados e atualizações perdidas |
| A04 | Alta | Save-FileMap usa Set-Content; erro de leitura retorna mapa vazio | Escrita não atômica; corrupção perde identidade |
| A05 | Alta | Start-OAuthFlow sem state/PKCE | Reforçar protocolo e validação do callback |
| A06 | Alta | Get-FreePort libera listener; Start-Process precede Wait-ForOAuthCode | Corridas de porta e callback |
| A07 | Alta | Wait-ForOAuthCode usa ReadLine sem timeout de stream | Conexão local incompleta pode bloquear; sucesso visual prematuro |
| A08 | Alta | Invoke-DriveWithRefresh repete bloco inteiro | Repetição de efeitos após criação; retries por operação e recuperação |
| A09 | Alta | config/map/tokens sem namespace de conta | Troca de cliente/conta deixa estado antigo |
| A10 | Média | New-ConvertedFile faz um PUT de todos os bytes | Sessão resumable sem retomada/chunks implementados |
| A11 | Média | Find-ExistingFile pageSize=100 sem nextPageToken | Fallback incompleto; busca exata pageSize=1 esconde ambiguidades |
| A12 | Média | Test-DriveFileExists solicita fields=id | Não verifica explicitamente lixeira, MIME ou capacidade de editar |
| A13 | Média | TypeMap só XLSX/XLS/CSV para Sheets | Faltam ODS/TSV; CSV sem política de encoding/delimitadores |
| A14 | Média | Test-Path/Get-Item e outros acessos por Path | Usar caminhos literais e validar arquivo, não apenas existência |
| A15 | Média | Logs persistentes sem rotação | Exposição de metadados e crescimento ilimitado |

Não há hash, comparação de estado remoto, identidade de arquivo Windows, snapshot consistente, timeout de aplicação, retry de quota/5xx, backups ou migração versionada. Não foi confirmada explorabilidade de command injection: o comando usa -File e argumentos entre aspas; robustez de caminhos deve ser testada. Não inferir segurança completa dessa observação.

## Instalador e documentos

Install copia worker antes de validar credenciais, aceita clientes web sem verificar adequação ao callback e substitui config sem reset/migração de tokens/mapa. Registra apenas menu de contexto, não aplicativo padrão. Uninstall oculta falhas globalmente. SETUP sugere wildcard de credenciais e atualização por extração: revisar resolução de caminho e lembrar que o worker executado foi copiado para LOCALAPPDATA. README promete autenticação única sem ressalvas de revogação/expiração. O escopo drive.file deve ser descrito segundo suas permissões reais, não como garantia absoluta de acesso apenas a arquivos criados.

## Reaproveitamento e contribuições

Reaproveitar DPAPI CurrentUser, refresh token preservado, APIs oficiais, conceitos de MIME/conversão, navegador padrão, instalação HKCU e atribuição. Refatorar OAuth, persistência, erros e upload. Substituir associação por nome e atualização automática. Criar no fork: launcher nativo, identidade persistente, journal, backups, .url e integração como app padrão.

Candidatos upstream independentes: PKCE/state/callback, literal paths, paginação, escrita atômica e locks, recuperação OAuth, formatos ODS/TSV, documentação de instalação/upgrade. Não preparar PR sem implementação e testes. A02 e A01 alteram comportamento e exigem comunicação cuidadosa com mantenedor.

## Caracterização a executar

Provar o fluxo original de novo arquivo, reabertura atualizante, fallback por nome, 404, retry 401, token sem refresh novo, mapa corrompido e duas execuções concorrentes. Usar mocks de Drive; executar componentes Windows no Windows. Nesta etapa há especificação, não testes executáveis.
