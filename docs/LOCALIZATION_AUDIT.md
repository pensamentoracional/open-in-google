# Auditoria inicial de localização — etapa 3

Tecnologias confirmadas: aplicação WinForms (.NET 10); instalador Inno Setup; nenhuma superfície HTML de execução. Interface atual em português brasileiro; mensagens internas/instalador incluem inglês. Não existem packs de tradução, resolvedor de locale ou catálogo central. Não declarar suporte a 51 idiomas ainda.

| Superfície | Fonte atual | Namespace futuro |
|---|---|---|
| Primeiro uso/configuração, confirmação, conexão, avançado, formatos | src/SheetsWindows.Windows/SetupForm.cs | app.setup |
| Principal, progresso, cancelamento, atualização, cópia | src/SheetsWindows.Windows/LauncherForm.cs | app.home / app.progress |
| Recuperação, backup, diagnóstico | src/SheetsWindows.Windows/RecoveryForm.cs | app.recovery |
| Nome, slogan, Sobre/MIT, atribuição | src/SheetsWindows.Windows/Branding.cs | shared.brand / app.about |
| Explicação pública de erros | src/SheetsWindows.Infrastructure/Launcher.cs (LauncherErrors) | app.errors |
| Limites de capacidade exibidos ao usuário | src/SheetsWindows.Infrastructure/SpreadsheetFormats.cs | app.errors.capacity |
| Instalador, crédito de rodapé, conflito/manutenção/downgrade | installer/SheetsWindows.iss | installer / shared.brand |
| Diálogos de arquivo e pastas, labels/filtros, captions | Forms acima | app.dialogs |
| Shell e menus de associação registrados | src/SheetsWindows.Infrastructure/WindowsAssociations.cs | app.shell |

Chaves semânticas, placeholders e plurais precisam ser extraídos por significado, incluindo chamadas MessageBox, títulos e textos de acessibilidade. Não contar literais de código como matriz canônica: concatenar strings/hashes internos inflaria o volume. Contagem integral de chaves, catálogo único e auditoria de todos os packs ainda pendentes na etapa 8.

Na revisão de telas, avisos extensos usam Labels com MaximumSize/quebra de linha; áreas extras recolhidas e AutoScroll. Isso prepara expansão de texto, mas não comprova RTL, shaping, cobertura de fontes ou QA de traduções. Idioma/tema devem ser independentes; formatação regional de números/datas deve manter os contratos de valores das planilhas. Não copiar textos traduzidos concorrentes entre Inno e WinForms: gerar os recursos a partir da base de autoria única quando implementada.

Matriz e regras a seguir: seção 8 de docs/ALPHA_NEXT.md. Os três arquivos canônicos referenciados ali precisam ser lidos na execução da etapa 8; este inventário não substitui seu conteúdo nem uma auditoria integral de mensagens.
