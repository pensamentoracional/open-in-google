# Fase 8 — Robustez e contribuições

Branch `feature/robustness`, piloto 0.8. A implementação acrescenta retomada de transferência, recuperação por ID na interface, diagnósticos limitados e atualização explícita preservando o estado. O aceite manual Windows 11/Google real continua pendente; os testes de rede usam HTTP simulado.

## Transferência e retomada

Launcher e importação CLI usam sessão resumable da Drive API. O registro de intenção existe antes do POST de início. A URL da sessão é validada (HTTPS, host e endpoint Google), protegida por DPAPI CurrentUser e persistida atomicamente na pasta privada `uploads` antes de enviar conteúdo. Conta, operação, hash do original, hash do payload, MIME e tamanho delimitam a sessão. Payloads normalizados de texto têm ZIP determinístico, permitindo conferir os mesmos bytes depois de reiniciar.

A transferência usa blocos de 256 KiB. Toda retomada começa com PUT vazio de consulta; o cabeçalho Range do servidor determina o próximo byte. Resposta perdida ao concluir é recuperada pela consulta, sem retransmitir conteúdo. PUT se limita à sessão de criação; não existe PATCH de planilha existente nem DELETE remoto. Fontes continuam limitadas a 5 MiB. As sessões protegidas permanecem para evidência de recuperação, junto aos journals e backups.

Falhas transitórias de transporte, 429 e 5xx permitem até três tentativas de consulta com espera limitada. OAuth 401 permite uma renovação durante a sessão, sempre conferindo a conta. Cancelamento e timeout conservam a sessão. Há prazo total de 90 segundos por invocação de upload/retomada ou consulta/exportação, incluindo a leitura do corpo após os cabeçalhos; a retomada seguinte consulta o progresso já aceito. GET de metadados também tem repetição limitada; POST de criação/início nunca é repetido automaticamente. JSON de metadados até 1 MiB; resposta final de upload até 64 KiB; paginação até 100 tokens distintos. Sem progresso repetido, Range inválido ou expiração bloqueiam a transferência.

Se a sessão expirar ou o POST inicial perder sua resposta, procurar a associação pelo marcador persistente. Se não houver exatamente uma planilha validável, exigir reconciliação e conservar original/backup. Não iniciar outra sessão silenciosamente. Isso é uma restrição deliberada para evitar duplicatas; retomada não é uma promessa de recuperação automática de qualquer resultado ambíguo.

Referência de protocolo: [Drive API — uploads resumable e conversão](https://developers.google.com/workspace/drive/api/guides/manage-uploads).

## Recuperação pelo aplicativo

Abrir **Recuperar operação / backup**, selecionar o registro e escolher:

- **Retomar como cópia**: retomar transferência/associação existente e abrir atalho privado; conservar a fonte.
- **Concluir substituição**: retomar a operação existente e aplicar todas as verificações de backup, identidade, formato, pasta, remoto e atalho antes da retirada. Não adota um original recriado no mesmo caminho.
- **Restaurar em…**: restauração offline em arquivo inexistente, sem modificar o remoto.
- **Exportar diagnóstico…**: criar arquivo novo com eventos sanitizados.

É possível cancelar a retomada. Uma operação sem intenção remota não é reimportada por esse painel: abrir a fonte pelo fluxo normal. Original ausente só permite reconciliação de retirada previamente registrada (passos 3/4). Para origem renomeada antes de haver journal de substituição, reabrir o caminho atual da fonte para que a identidade local seja conferida; o painel não procura arquivos pelo nome. Retirada em XLS, ODS complexos, OneDrive e rede continua bloqueada.

## Diagnóstico limitado

`logs/events.jsonl` e três arquivos rotacionados, até 64 KiB cada: máximo 256 KiB de diagnóstico. Eventos contêm apenas horário, código enumerado e ID de operação opcional. Não gravar caminhos, nomes, contas, URL de sessão/documento, tokens, conteúdo de planilha ou texto de exceções. Exportação reserializa somente esses campos e nunca sobrescreve um arquivo existente. Falha na gravação do diagnóstico não muda o resultado da importação.

Journals transacionais de operação não são logs descartáveis e permanecem íntegros: esta rotação não apaga associações, sessões, provas de retirada nem backups. Não há telemetria nem envio automático do diagnóstico.

## Atualização explícita

**Atualizações** abre o workflow de pacotes no fork. A distribuição desta entrega usa artefatos de CI, com expiração indicada no GitHub, e não tem canal assinado de atualização automática. Selecionar somente o pacote e commit aprovados no registro de validação abaixo. Obter o digest do ZIP pelo guia/run aprovado, baixar o ZIP e verificar antes de extrair:

```powershell
powershell -NoProfile -File .\installer\Verify-Update.ps1 -Archive 'C:\Downloads\SheetsWindows-Setup-win-x64.zip' -ExpectedSha256 '8dfd685a4e0a1b3f5b565ba45035f03a6dc3f75996a2e90f609d6ae649120477'
```

O [verificador](../installer/Verify-Update.ps1) aceita caminhos literais e recusa hash divergente ou reparse point. Não baixa, extrai nem executa o pacote. Hash confere integridade em relação ao registro aprovado; não substitui assinatura de editor. Fechar o aplicativo, extrair e executar o instalador explicitamente. O instalador conserva ID/pasta, registra a versão 0.8 e bloqueia instalação sobre uma versão superior. Não apagar os bancos para atualizar. OAuth, associações, opções, sessões, diagnósticos, atalhos e backups permanecem fora do manifesto de instalação/desinstalação.

O CI compila um pacote de versão declarada 0.7 **com os mesmos binários atuais** para testar a política do instalador: instalação 0.7, upgrade 0.8, tentativa 0.7 recusada e preservação após desinstalação/reinstalação. Esse fixture não simula executar os binários antigos; migrações reais dos bancos anteriores continuam cobertas pelos testes das fases anteriores.

## Matriz de falhas automatizada

| Situação | Comportamento exigido | Evidência |
|---|---|---|
| Conexão cai após bloco aceito | Reinício consulta offset e envia apenas restantes; um POST | RobustnessTests.RestartAfterLostChunk… |
| Resposta final se perde | Consulta recupera ID, sem novo conteúdo; fluxo Windows retoma a mesma operação | LostCompletionResumes… / LauncherRestartRecoversLostUploadCompletion… |
| Sessão expira | Reconciliação; nenhum segundo POST | ExpiredSessionRequires… |
| Range inválido / ausência de progresso | Bloqueio e limite de requisições | MalformedOrComplete308… / NoProgress… |
| Conta, conteúdo, MIME ou fonte muda | Bloqueio antes de continuar | ChangedPayloadAccountAndMime… / RecoveryByIdCanChooseCopy… |
| Sessão aponta a outro host / arquivo protegido alterado | Rejeitar; sem enviar conteúdo | UntrustedSession… / ProductionSessionUses… |
| OAuth 401 / GET 503 / POST 503 | Renovação limitada / GET repetido / POST único | UnauthorizedStatus… / ReadOnlyMetadata… / CreatePost… |
| Resposta trava depois dos cabeçalhos | Cancelar pelo prazo total e conservar a sessão | ResponseBodyAfterHeaders… |
| Paginação cíclica / resposta grande | Limites explícitos | RepeatedPaginationToken… |
| Navegador falha | Fonte mantida; painel conclui sem novo upload | RecoveryByIdCompletesBrowserFailure… |
| Diagnóstico cresce / contém campo estranho / pasta bloqueada | Rotação limitada / exportação sanitizada / operação independente | DiagnosticsRotate… / BrokenDiagnosticDirectory… |
| Atualização, downgrade e remoção | Upgrade permitido; downgrade bloqueado; dados e padrões mantidos | Test-Lifecycle.ps1 / Test-UpdateVerification.ps1 |

A matriz complementa concorrência, crash por transição, fidelidade, SMB, migração, restauração e permissões já cobertos nas fases 2–7. A validação automatizada não certifica fidelidade de toda planilha nem substitui o piloto com credenciais reais.

## Contribuição genérica isolada

A branch `fix/literal-paths` parte diretamente da revisão original `124419b9`, sem nenhum componente C# ou política de substituição do fork. Corrige leitura de tamanho e existência com `-LiteralPath`: um nome com colchetes não deve selecionar outro arquivo como wildcard. O teste extrai somente a função de upload por AST, usa dois arquivos com tamanhos diferentes e HTTP falso, e roda no Windows PowerShell 5.1 sem OAuth, interface ou registro. A contribuição está no [PR independente #1, em rascunho no fork](https://github.com/pensamentoracional/open-in-google/pull/1), com [CI PowerShell aprovado](https://github.com/pensamentoracional/open-in-google/actions/runs/36879481261). Não foi enviada nem mesclada no upstream; sua aceitação upstream não condiciona o produto.

## Registro de validação

Código aprovado: `dfe2922da406a5bb1904849e097c5dd19af5e858` na branch `feature/robustness`.

[CI aprovado em Windows e Linux — execução 36882411612](https://github.com/pensamentoracional/open-in-google/actions/runs/36882411612): restore com lockfiles, build sem warnings, **169 testes**. Windows: **168 aprovados, 1 ignorado** (teste Unix). Linux: **142 aprovados, 27 ignorados** (verificações Windows/DPAPI/SMB). Zero falhas. No Windows, o CI também aprovou o pacote autossuficiente, verificação de hash, política de atualização/downgrade, desinstalação e reinstalação com todos os sentinelas e padrões do usuário preservados.

[Baixar instalador 0.8 aprovado](https://github.com/pensamentoracional/open-in-google/actions/runs/36882411612/artifacts/11171354492). Artefato `SheetsWindows-Setup-win-x64`, ZIP de **37.727.610 bytes**, expira em **30/12/2026**. SHA-256 do ZIP (não do EXE extraído):

```text
8dfd685a4e0a1b3f5b565ba45035f03a6dc3f75996a2e90f609d6ae649120477
```

[Pacote portátil 0.8](https://github.com/pensamentoracional/open-in-google/actions/runs/36882411612/artifacts/11172180630), ZIP de 52.734.132 bytes, digest `ef8a1f7fe65b959a4b496f3bce509dc385a7ff7f7b96ab057b4d026e47d4acd8`. Backups, sessões e estado permanecem no perfil do usuário, fora do pacote.

A matriz automatizada está aprovada para os cenários simulados e recursos nativos exercitados. Google usa HTTP simulado; DPAPI, handles Windows, registro e compartilhamento SMB foram exercitados no runner. Não houve upload de planilha real nem OAuth com credenciais do usuário. Aceite manual Windows 11/Google/OneDrive reais permanece pendente; não declarar encerramento universal do piloto.
