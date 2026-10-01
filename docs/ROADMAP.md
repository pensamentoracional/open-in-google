# ZagoSheetsWin — plano de desenvolvimento

Data: 2026-10-01. Complementa SW_00_PRINCIPAL.md.
Upstream: https://github.com/SwatiK425/open-in-google
Revisão auditada: 124419b9ffce1f42c696ab9068fa9db6a4a9c199.

## Experiência e novo requisito

Depois da configuração inicial, o usuário abre uma planilha local. O aplicativo importa e converte para Google Sheets, abre o navegador e substitui o arquivo na pasta original por um atalho de Internet .url para o documento remoto. Nas próximas aberturas, o atalho abre o Google Sheets sem upload.

Exemplo: C:\Users\Fernando\Desktop\Relatorio.xlsx passa a ser C:\Users\Fernando\Desktop\Relatorio.url. É um atalho, não um arquivo XLSX nem uma página HTML. O nome base e a pasta são preservados. A extensão precisa mudar. Para nomes base iguais com extensões distintas, usar nome sem colisão, como Relatorio.xlsx.url; nunca sobrescrever um atalho preexistente sem verificar sua identidade.

O padrão do produto é substituir o original depois da importação confirmada, sem pedir confirmação a cada abertura. A configuração inicial deve explicar essa operação. O original sai da pasta de origem, mas uma cópia recuperável permanece em armazenamento privado local. Não haverá eliminação definitiva automática desses backups no MVP.

Não é sincronização bidirecional: edições online não voltam ao arquivo original. O Google Sheets passa a ser o documento de trabalho. Backup do original protege a importação inicial, não preserva edições online posteriores. O atalho depende de acesso à conta e ao documento remoto.

## Transação de importação e substituição

1. Validar arquivo, formato, permissões de leitura e de escrita na pasta e política de substituição. MVP apenas para arquivos locais do usuário; pastas compartilhadas, rede e diretórios sincronizados exigem tratamento posterior explícito.
2. Adquirir lock por identidade local e preparar uma operação persistente com identificador próprio.
3. Capturar snapshot consistente, calcular SHA-256 e salvar backup recuperável; importar exatamente esses bytes.
4. Autenticar e criar documento remoto, registrando sua identidade e o identificador da operação para recuperação. Não procurar identidade por nome.
5. Confirmar via API ID, MIME nativo, ausência de lixeira e acesso ao documento. Não basta receber a URL de uma sessão de upload.
6. Persistir associação, hash importado e estado da operação antes de retirar o original.
7. Gravar atalho temporário na mesma pasta, validar o conteúdo e publicar sem sobrescrever outro arquivo. Aceitar apenas URL HTTPS do editor Google esperado e ID validado.
8. Solicitar abertura no navegador padrão. Registrar sucesso ou falha do lançamento; isso não prova que a página carregou. Falha explícita de lançamento mantém o original.
9. Confirmar novamente que o arquivo original é o mesmo e continua com os bytes importados. Evitar janela de corrida entre verificação e retirada por meio de handle/identidade e bloqueio apropriados no Windows. Se mudou, manter o original e sinalizar divergência.
10. Retirar o original da pasta somente com backup, associação e atalho confirmados; concluir a operação no registro persistente.

Falhas em qualquer etapa mantêm uma representação local recuperável. Se a nuvem já recebeu o documento, recuperar a operação existente em vez de criar outra. Atalho e remoção não formam uma única transação de filesystem; um journal persistente deve reconciliar estados após crash. Não excluir documentos remotos como rollback automático.

## Plano por entregas

| Etapa | Entrega | Critério de saída |
|---|---|---|
| 0 — Auditoria e base | Registrar achados, revisão upstream, licença, matriz de herança e testes de caracterização | Comportamento atual documentado; limitações de testes estáticos explícitas |
| 1 — Fork e arquitetura | Fork próprio, origin/upstream, atribuição MIT e decisões arquiteturais | Desenvolvimento independente do upstream; responsabilidades separadas |
| 2 — Núcleo seguro | Registro local transacional, journal, snapshots, backups e locks | Falhas e concorrência não perdem associações nem removem originais |
| 3 — OAuth e importação XLSX | Tokens protegidos, PKCE/state, recuperação OAuth e conversão pela Drive API | Primeira importação retorna Sheets nativo verificado; recuperação evita repetição cega |
| 4 — Substituição por atalho | .url na pasta original e retirada recuperável do XLSX | Abrir novamente o atalho faz zero uploads; crash em cada etapa é recuperável |
| 5 — Integração Windows | Executável, Open With, registro de formatos e menu de contexto opcional | Usuário escolhe o app padrão pelos mecanismos oficiais; duplo clique funciona |
| 6 — Piloto instalável | Instalador por usuário, configuração inicial, restauração e desinstalação | Uso completo no Windows 11; desinstalação preserva atalhos, documentos e backups por padrão |
| 7 — Formatos e ambientes | ODS, XLS, CSV, TSV; encoding/delimitadores; OneDrive e rede | Cada formato/ambiente ganha substituição apenas depois de testes de fidelidade e permissões |
| 8 — Robustez e contribuições | Retomada de upload, erros, logs limitados, atualização e melhorias upstream isoladas | Matriz de falhas aprovada; PRs genéricos independentes da evolução |
| 9 — Consolidação e homologação | Nome ZagoSheetsWin, identidade Zagotools, créditos MIT, pacote e roteiro único | CI/revisão do pacote; aceite real Windows 11/Google executado ao fim |

Segurança, concorrência e proteção contra perda são requisitos das primeiras entregas; a etapa 8 amplia sua cobertura. XLSM permanece fora do MVP e exige avaliação de macros e consentimento específico sobre perda de funcionalidades antes de substituir o original.

## Arquitetura proposta

C#/.NET é a direção proposta para o aplicativo Windows, sujeita ao registro da decisão e à avaliação do empacotamento. Manter PowerShell como referência e, se útil, em ferramentas auxiliares.

Componentes: WindowsLauncher; ImportCoordinator; FileSnapshot; LocalRegistry; OperationJournal; BackupStore; GoogleAuth; DriveImporter; ShortcutWriter; BrowserLauncher; RecoveryService; WindowsIntegration; Logging.

SQLite é a proposta para associações e journal por oferecer transações e controle de concorrência. Não manter transação do banco aberta durante requisições de rede. Identidade de conta deve delimitar pasta, operações e associações. Hash identifica conteúdo, não sozinho a identidade de um documento. Serialização de OAuth/pasta evita corridas adicionais entre importações distintas.

## Comportamentos excepcionais

- Arquivo local conhecido e inalterado: abrir remoto existente; concluir substituição pendente sem reenviar conteúdo.
- Arquivo local conhecido e alterado: preservar original; oferecer abrir remoto, atualizar explicitamente ou criar novo. Atualização precisa de especificação própria de proteção e confirmação; nunca automática.
- Original recriado posteriormente no mesmo caminho: tratar como nova evidência local; não adotar automaticamente o documento por nome.
- Nome de atalho ocupado: gerar nome determinístico sem colisão ou informar erro; jamais substituir arquivo alheio.
- Sem escrita na pasta, arquivo bloqueado ou disco cheio: manter original e informar substituição não concluída.
- Conversão com perda de recursos: manter original recuperável e aplicar política de compatibilidade; existência do remoto não prova fidelidade.
- Documento remoto apagado/permissão revogada: atalho direto não detecta nem repara; ferramenta de recuperação permite restaurar original ou reimportar conscientemente.
- Compartilhamento do atalho: compartilhar a URL não concede acesso ao documento Google.
- OneDrive e pastas compartilhadas: remover localmente pode propagar remoção para outros dispositivos/pessoas; suporte requer política e testes próprios, sem tratá-los como uma pasta local comum.

## Testes de aceitação prioritários

1. XLSX novo: uma importação, Google Sheets verificado, atalho válido, backup íntegro, original fora da pasta.
2. Abrir atalho repetidamente após editar online: nenhum upload e nenhuma atualização remota.
3. Falha de upload ou conversão: original preservado; nenhum atalho inválido.
4. Falha de backup, gravação do atalho ou persistência: nenhuma retirada do original.
5. Falha explícita ao lançar navegador: original mantido e recuperação disponível.
6. Crash após cada transição: recuperação consistente, sem exclusão definitiva e sem repetição cega de criação.
7. Aberturas simultâneas do mesmo arquivo: uma importação e uma substituição.
8. Arquivos diferentes com mesmo nome/conteúdo: nenhuma associação indevida ou sobrescrita de atalho.
9. Arquivo alterado durante a importação: versão nova local preservada; snapshot importado identificado.
10. Nomes Unicode, espaços, colchetes e caminhos longos: acesso literal e atalho correto.
11. Conta trocada, OAuth revogado, remoto na lixeira e acesso perdido: recuperação explícita sem atualizar documento de outra conta.
12. Restauração/desinstalação: não apagar Google Sheets, atalhos nem backups inadvertidamente.

## Ordem prática imediata

Formalizar a auditoria e as decisões; preparar fork; criar núcleo e testes sem Google real; implementar OAuth/importação; completar transação de atalho; validar no Windows 11; entregar instalador piloto XLSX. Expandir formatos apenas depois desse fluxo passar pelos testes de falha.

Não estimar datas antes de validar o primeiro fluxo no Windows e as restrições reais de conversão. Separar commits de correções genéricas (OAuth, paginação, literal paths, persistência, documentação) das funcionalidades específicas do fork.

## Progresso da etapa 4

Fluxo CLI implementado na branch feature/shortcut-replacement: configuração única de pasta particular não sincronizada, .url atômico sem sobrescrita, retirada por handle Windows, journal e retomada/restauração por ID. Ver STAGE_4.md para limites e evidências. Integração do duplo clique e configuração visual continuam nas etapas 5/6. O aceite manual no Windows 11 com Google real ainda é gate do piloto.

## Progresso da etapa 5

Launcher WinExe/WinForms e registro HKCU apenas para XLSX na branch feature/windows-integration. A escolha do padrão acontece na interface oficial do Windows, sem modificar UserChoice. CLI prepara configuração uma vez; launcher executa a substituição completa sem terminal. Pacote portátil win-x64 produzido pelo CI. Ver STAGE_5.md. Instalador/onboarding completo seguem na etapa 6; piloto Explorer + Google real no Windows 11 ainda pendente.

## Progresso da etapa 6

Piloto instalável implementado na branch feature/installable-pilot: Inno Setup por usuário, configuração visual, recuperação offline de snapshots e desinstalação com preservação de dados. O CI verifica instalação/atualização/desinstalação/reinstalação e escolhas de padrão. Ver STAGE_6.md. A implementação não encerra o gate de uso real no Windows 11 com OAuth/Explorer; esse aceite manual permanece pendente antes de ampliar formatos.

## Progresso da etapa 7

Branch feature/formats-environments: CSV/TSV com encoding/delimitador estritos, ODS/XLS com MIME e validação próprios, conferência de valores exportados antes da retirada de CSV/TSV/ODS simples, modo de cópia para XLS/ODS complexos/OneDrive/rede e migração preservando registro anterior. Formatos/ambientes sem prova de fidelidade/permissões conservam o original; a retirada nesses casos permanece bloqueada. Ver STAGE_7.md. O aceite manual Windows 11/Google/Cloud Files reais continua pendente; não declarar certificação universal da etapa.

## Progresso da etapa 8

Branch feature/robustness: sessões de upload protegidas e retomáveis, consultas limitadas, recuperação por ID na interface, diagnóstico rotacionado sem conteúdo sensível e atualização explícita com conferência de hash/bloqueio de downgrade. Correção PowerShell genérica em fix/literal-paths, baseada diretamente no upstream original. Ver STAGE_8.md para matriz, evidências e limites. O aceite real Windows 11/Google continua pendente antes de encerrar o piloto.

## Progresso da etapa 9

Consolidação técnica concluída na branch feature/zagosheetswin, versão 0.9 (CI 36923406047 aprovado, 170 testes Windows / 142 Linux, instalador e revisão visual aprovados): identidade Zagotools e nome público ZagoSheetsWin, origem/Swati K/MIT preservados, migração de nome com proteção de valores alheios e roteiro único STAGE_9.md. Conforme alinhamento, homologação manual Windows 11/Google real fica para depois do pacote final; os testes automatizados continuam sendo requisito de cada alteração. Sem expansão para Word/Linux.
