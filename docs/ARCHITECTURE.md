# Arquitetura alvo

Status: design, não implementação. Regras de produto vêm do roadmap.

| Componente | Responsabilidade | Limite |
|---|---|---|
| WindowsLauncher | Receber caminho e conduzir UI mínima | Sem lógica de upload |
| ImportCoordinator | Orquestrar transições e conflitos | Não atualizar remoto silenciosamente |
| FileSnapshot | Identidade local, leitura consistente, SHA-256 | Hash não define identidade sozinho |
| BackupStore | Guardar e verificar snapshot recuperável | Sem expurgo automático no MVP |
| LocalRegistry | Associações e isolamento por conta | SQLite; transações curtas |
| OperationJournal | Estado durável de cada importação | Não supor transação única com Drive/filesystem |
| GoogleAuth | OAuth e tokens protegidos | PKCE/state; serializar autenticação |
| DriveImporter | Criar/converter/verificar e reconciliar | Nunca adotar por nome |
| ShortcutWriter | Publicar .url sem colisão | Host HTTPS permitido, ID validado |
| BrowserLauncher | Pedir abertura no navegador padrão | Sucesso não prova carregamento |
| RecoveryService | Recuperar interrupções e restaurar backup | Nenhuma exclusão remota como rollback |
| WindowsIntegration | Registro HKCU e app candidato | Usuário escolhe padrão oficialmente |
| Logging | Diagnóstico e retenção limitada | Sem tokens ou conteúdo |

## Estados duráveis propostos

Prepared → SnapshotReady → RemoteCreationPending → RemoteVerified → MappingSaved → ShortcutPublished → BrowserRequested → SourceRetired → Completed. Estados excepcionais: NeedsReconciliation, Conflict, RecoverableFailure. Registrar intenção antes de efeito externo e resultado depois. Uma falha pode ocorrer entre efeito e persistência: recuperação deve inspecionar evidência, não reenviar automaticamente.

## Dados conceituais

Account: identidade estável, OAuth client e pasta. Source: identidade Windows quando disponível, aliases de caminhos, formato e evidências. Import: operation_id, source_id, account_id, hash dos bytes, backup, ID remoto e status. Shortcut: caminho, destino e importação. Eventos: transição, horário e erro sanitizado.

Não criar restrição UNIQUE global para hash. Locks por fonte coordenam concorrência; SQLite coordena estado mas não permanece bloqueado durante rede. OAuth e criação da pasta têm locks separados por conta. A estratégia para identificar/reconciliar criação Drive após resposta perdida é um gate da etapa 3: avaliar marcador appProperties e pesquisa por operation_id, permissões e limites da API oficial. Não prometer exatamente uma criação só com retry HTTP.

## Retirada e recuperação

Importar snapshot, confirmar remoto, salvar mapping, publicar .url, solicitar browser e retirar apenas a mesma identidade/versão original protegida contra alterações entre teste e retirada. Usar handles/semântica de sharing Windows na implementação; validar em Windows. Backups não tornam atalho+remoção atômicos: journal deve reparar operação interrompida. Falha no browser mantém original. Documento remoto removido posteriormente exige ferramenta de recuperação; .url direto não intercepta erros.

## Estrutura futura

src/SheetsWindows.Core (regras e contratos), src/SheetsWindows.Infrastructure (SQLite/Drive/backups), src/SheetsWindows.Windows (launcher, UI e filesystem Windows), tests/Core e tests/WindowsIntegration. Criar projetos e escolher versões na etapa 2, verificando suporte e dependências em fontes oficiais. Nome do executável pode ser SheetsOpener; nome comercial ainda não é uma decisão irrevogável.
