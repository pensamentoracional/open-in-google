# Etapa 9 — Consolidação e homologação do ZagoSheetsWin

Etapa de fechamento acrescentada após a fase 8, conforme alinhamento com Fernando. Nome do produto: **ZagoSheetsWin**, evolução Zagotools do Open in Google, de Swati K (SwatiK425), sob MIT. Versão 0.9, branch `feature/zagosheetswin`.

## Entrega consolidada

- Instalador por usuário com logo Zagotools, painel verde, ícone próprio, página de créditos/origem e licença MIT original.
- Interface nativa com identidade do template Zagotools, logo incorporada, área Sobre / MIT e links para autora, repositório original e evolução. Não carrega assets da internet.
- Nome ZagoSheetsWin nas telas, atalhos e lista de aplicativos padrão do Windows.
- Migração do nome anterior apenas em valores de registro reconhecidos como nossos; valores modificados/terceiros são conservados e impedem migração automática.
- Pacote inclui LICENSE, ATTRIBUTION.md e licença da ExcelDataReader.
- Regressões de backup, upload resumable, conta, conversão, atalho, crash, SMB, restauração, registro e instalação continuam cobertas pelos testes automatizados.

A compatibilidade de atualização conserva AppId, pastas `LOCALAPPDATA/SheetsWindows` e `LOCALAPPDATA/Programs/SheetsWindows`, arquivo técnico `SheetsWindows.exe`, ProgID e namespaces internos. Isso evita romper associações, configuração OAuth, bancos, sessões, snapshots e atalhos. O nome público do projeto/app é ZagoSheetsWin; não foi renomeado o repositório GitHub nem alterado o upstream.

## Escopo que será homologado

Windows x64. Cinco extensões: XLSX, CSV, TSV, ODS e XLS. CSV/TSV/ODS/XLS exigem habilitação uma vez na configuração. CSV/TSV são texto literal; ODS simples exige comparação de valores exportados. XLS e ODS complexos preservam o original. OneDrive e rede são cópia, sem retirada na origem. XLSM/XLSB, Word, PowerPoint, PDF e Linux não entram nessa entrega.

A conversão não promete fidelidade integral de formatação, gráficos ou recursos do Excel. O snapshot preserva a versão inicial; não é backup das edições futuras do Sheets. Google real/Explorer/Windows 11 serão testados **depois** desta consolidação. Testes automatizados não serão adiados.

## Roteiro único do piloto real

1. Baixar o instalador aprovado nesta página; verificar o SHA-256 do ZIP antes de extrair, usando `installer/Verify-Update.ps1` ou Get-FileHash. A distribuição é artefato de CI, sem assinatura de editor; usar o run/commit aprovado, não um pacote aleatório.
2. Instalar no Windows 11. Confirmar ZagoSheetsWin, logo Zagotools, página de créditos, licença e Sobre / MIT. Verificar tamanho das janelas, contraste, botões, teclado e escala do Windows em 100%, 125% e 150%. Os links devem abrir autora/original/fork corretos.
3. Criar pasta local dedicada, sem sincronização, e usar **somente cópias de teste**. Preservar os arquivos de referência fora dela. Preparar XLSX simples, CSV com zeros à esquerda e `=1+1` como texto, TSV, ODS simples, ODS complexo e XLS.
4. Configurar piloto: JSON OAuth desktop e pasta; habilitar formatos e escolher encoding/delimitador quando aplicável. Seguir a configuração de projeto Google/Drive API descrita em STAGE_6.md. Autorizar a conta Google real e escolher ZagoSheetsWin pelos mecanismos oficiais de Aplicativos padrão.
5. Dar dois cliques no XLSX de teste: conferir planilha nativa, valores, conta Google, atalho na pasta original e backup disponível. Registrar ID/URL remotos e ID da operação, sem publicar credenciais.
6. Editar o Sheets online; fechar e reabrir o .url algumas vezes. Conferir que usa a mesma URL/ID, mantém a edição online e não cria documentos adicionais no Drive.
7. Testar CSV/TSV/ODS simples: conferir abas/posições/valores e texto literal. A retirada só pode acontecer após os valores exportados corresponderem. Testar XLS/ODS complexo: original deve continuar presente e atalho privado deve abrir a mesma cópia remota.
8. Interromper a internet durante uma importação e cancelar outra. Conferir original/backup conservados; usar Recuperar operação / backup para retomar como cópia ou concluir substituição. Conta/arquivo alterados devem bloquear retomada; ambiguidade/expiração não deve causar nova criação cega.
9. Restaurar em arquivo de nome inexistente, mesmo offline. Comparar SHA-256 com a referência inicial. Tentar um nome ocupado: deve conservar o arquivo já existente. Não apagar bancos ou snapshots para destravar operações.
10. Se quiser validar OneDrive/rede, usar Importar cópia em um arquivo disponível localmente. Conferir fonte conservada e atalho privado. Placeholders online-only não devem ser hidratados silenciosamente.
11. Atualizar/reinstalar e desinstalar: conferir Google Sheets, .url e backups conservados, configuração preservada e padrão do Windows sem alteração forçada. Não executar `installer/Test-Lifecycle.ps1` no computador pessoal: esse script exige perfil descartável de CI.
12. Registrar resultados na tabela abaixo, corrigir problemas encontrados e repetir os cenários afetados antes de liberar uso cotidiano em arquivos importantes.

| Grupo | Resultado esperado | Aceite real |
|---|---|---|
| Marca / créditos / DPI | Identidade legível, MIT e origem visíveis | Pendente |
| OAuth / duplo clique | Conta correta, planilha abre no Google | Pendente |
| Atalho / reabertura | Mesmo documento, nenhuma sobrescrita online | Pendente |
| Formatos / cópia | Comportamento corresponde à matriz acima | Pendente |
| Falha / retomada | Fonte recuperável, sem duplicata automática | Pendente |
| Restauração / atualização / remoção | Integridade e dados conservados | Pendente |

O aceite será registrado após execução real; a implementação pronta não preenche esses campos automaticamente.

## Validação e pacote

Código aprovado: `190e3521d4c23a4811721caa2d026adf9c4e17a0`. [CI aprovado — run 36923406047](https://github.com/pensamentoracional/open-in-google/actions/runs/36923406047).

- Windows: 170 testes aprovados, zero falhas; 1 ignorado por verificar permissões Unix.
- Linux: 142 aprovados, zero falhas; 29 específicos de Windows ignorados. Isso não distribui o aplicativo para Linux.
- Build sem warnings; publicação self-contained x64 e dependências verificadas.
- Instalação por usuário, atualização, bloqueio de downgrade, remoção e reinstalação aprovados; sentinelas de configuração, backups e atalhos conservadas e padrões do Windows intactos. A fixture 0.7 usa o payload atual para verificar a política de versão; a migração do nome antigo é verificada separadamente pelos testes de registro.
- Prévias nativas de início, configuração, recuperação e Sobre revisadas visualmente na escala padrão do runner: logo, nome, créditos e controles sem sobreposição. [Baixar prévias](https://github.com/pensamentoracional/open-in-google/actions/runs/36923406047/artifacts/11192178905). DPI 125%/150% e teclado continuam no aceite real.
- LICENSE original permanece sem alterações.

[**Baixar instalador ZagoSheetsWin 0.9 — Windows x64**](https://github.com/pensamentoracional/open-in-google/actions/runs/36923406047/artifacts/11192533884). O ZIP contém `ZagoSheetsWin-Setup-win-x64.exe`; SHA-256 **do ZIP**: `aaa6e914968c654697e1213dc3fc4588a482a68877f8373b30153bef90c2fc49`. Download de artefatos exige login no GitHub e tem retenção até 30/12/2026.

[Pacote portátil aprovado](https://github.com/pensamentoracional/open-in-google/actions/runs/36923406047/artifacts/11192308801), SHA-256 do ZIP: `7cd4521939636a1968d0f9f35a648ed011c37509f4accf45fedb01a4e62b37f6`.

A consolidação técnica da etapa 9 está concluída. O roteiro real acima permanece pendente para o fechamento acordado; este pacote é o candidato de homologação, não um aceite automático de Google real.
