# Decisões de arquitetura — etapa 1

Data: 2026-10-01. Decisões aceitas para o design; detalhes de runtime/SDK e integrações precisam de validação ao implementar.

| ADR | Decisão | Alternativas e motivo | Custo / limite |
|---|---|---|---|
| 001 | Fork independente com histórico e MIT preservados | Reescrita sem histórico perde rastreabilidade; depender de PR bloqueia produto | Manter divergências e sincronização conscientemente |
| 002 | C#/.NET para aplicativo Windows | PowerShell é útil como referência, mas concentra estado/UI/IO; Electron adiciona peso sem benefício identificado | Exige SDK e testes Windows; versão não fixada nesta fase |
| 003 | SQLite para mapa e journal | JSON é simples, mas precisa recriar atomicidade, recuperação e concorrência | Schema, migrações e backup do banco; transações curtas |
| 004 | .url na pasta original após conversão | HTML adiciona redirecionamento; .lnk é menos portátil; deixar XLSX incentiva reimportação | Não recupera remoto automaticamente e não concede acesso |
| 005 | Original removido da pasta, backup privado sem expurgo automático | Deleção definitiva conflita com proteção de dados; manter original na pasta não atende experiência | Consumo de disco, ferramenta de restauração e política futura |
| 006 | Identidade local + conta + evidências de conteúdo | Nome/caminho isolados são frágeis; hash global funde cópias independentes | Renomeação/cópia e arquivos em FS diferentes precisam de política |
| 007 | Journal e locks desde núcleo | Retry cego e transação só de SQLite não cobrem Drive/filesystem | Reconciliação e testes de crash essenciais |
| 008 | Google APIs e OAuth desktop seguro | Automação visual é frágil; tokens plaintext são evitáveis | PKCE/state, DPAPI, callback robusto e revogação |
| 009 | MVP XLSX em pasta local não sincronizada | Todos formatos/OneDrive/rede elevam risco inicial | Detectar e bloquear substituição nos ambientes não aprovados |
| 010 | Registrar candidato a app padrão por usuário | Alterar UserChoice silenciosamente viola fluxo oficial | Usuário escolhe padrão na configuração inicial |

Não haverá atualização remota silenciosa, sincronização bidirecional ou exclusão de documentos Google na desinstalação. Fidelidade de conversão é gate adicional; remoto existir não prova conteúdo equivalente.

## ADR 011 — Instalador por usuário e estado separado

Fase 6: Inno Setup para instalar o pacote self-contained em `%LOCALAPPDATA%\Programs\SheetsWindows`; o estado permanece em `%LOCALAPPDATA%\SheetsWindows`. Evita implementar um instalador próprio e permite desinstalar o programa sem incluir dados no manifesto de remoção. O instalador invoca manutenção idempotente de associação, bloqueia destinos arbitrários e preserva conflitos de propriedade. Configuração e recuperação são UI nativa; recuperação offline não depende de OAuth. Gate manual no Windows 11 permanece obrigatório.

## ADR 012 — Formatos com conferência e ambientes como cópia

Fase 7: CSV/TSV são normalizados para XLSX com texto literal; ODS/XLS usam MIME nativo. Para novos formatos que permitem retirada, exportar XLSX e comparar células antes de aposentar o original; não confundir MIME nativo confirmado com fidelidade. XLS e ODS complexos continuam como cópia até certificação própria. OneDrive/rede também usam cópia e atalho privado, sem escrita/retirada na origem. O registry amplia formatos pelo schema 2 com migração transacional, sem recriar registros. Opções de texto requerem opt-in e não mudam silenciosamente operações existentes.
