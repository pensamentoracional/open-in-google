# ZagoSheetsWin

Projeto em preparação: abrir planilhas locais no Google Sheets e substituir o original por um atalho de Internet na mesma pasta, com backup recuperável.

**Em validação — alpha 0.9.5:** [Interface mínima, temas e processamento](docs/ALPHA_4_UI.md).

**Alpha 0.9.4 aprovada:** [Instalação e primeiro uso](docs/ALPHA_3_SETUP.md). [Baixar instalador Windows x64](https://github.com/zagozago/ZagoSheetsWin/actions/runs/37097216126/artifacts/11264632553). CI Windows/Linux, instalador e prévias nativas aprovados; aceite manual Google real/Windows 11 pendente. [Plano atualizado com internacionalização](docs/ALPHA_NEXT.md).

**Alpha 0.9.3:** [XLS padrão e ícone dos atalhos](docs/ALPHA_2_XLS.md). [Baixar instalador aprovado](https://github.com/zagozago/ZagoSheetsWin/actions/runs/37096149671/artifacts/11264247583). CI Windows/Linux e instalador aprovados; aceite Google real/Explorer pendente.

**Atualização alpha 0.9.2:** [CSV, capacidade e diagnóstico](docs/ALPHA_1_CSV.md). [Baixar instalador aprovado 0.9.2](https://github.com/zagozago/ZagoSheetsWin/actions/runs/37095090039/artifacts/11264096007). CI Windows/Linux e instalador aprovados; teste Google real desta correção pendente.

**Estado:** etapa 9 consolida o piloto 0.9.1 com nome ZagoSheetsWin, identidade Zagotools e créditos à origem. [Guia único de instalação, escopo e homologação](docs/STAGE_9.md). Suporte atual: planilhas no Windows; Google real/Explorer/Windows 11 serão homologados no fechamento. Os scripts PowerShell herdados ainda atualizam a cópia no Drive e não representam o fluxo seguro do aplicativo novo.

## Relationship with Open in Google

Based on / derived from [Open in Google](https://github.com/SwatiK425/open-in-google), de Swati K. Base auditada: `124419b9ffce1f42c696ab9068fa9db6a4a9c199`. Os três scripts, SETUP.md, LICENSE e .gitignore foram preservados nesta etapa. O README original, com links ajustados à pasta de arquivo, está em docs/upstream/README.original.md. A licença MIT e o copyright original permanecem em LICENSE.

A evolução pertence ao fork e não depende de PRs aceitos. Correções genéricas poderão voltar ao upstream em branches independentes.

## Documentação

- [Auditoria](docs/AUDIT.md)
- [Arquitetura](docs/ARCHITECTURE.md)
- [Decisões](docs/adr/DECISIONS.md)
- [Roadmap](docs/ROADMAP.md)
- [Desenvolvimento e fork](docs/DEVELOPMENT.md)
- [Conclusão da etapa 1](docs/STAGE_1.md)
- [Núcleo local e testes — etapa 2](docs/STAGE_2.md)
- [OAuth, importação e piloto — etapa 3](docs/STAGE_3.md)
- [Atalho, retirada e recuperação — etapa 4](docs/STAGE_4.md)
- [Launcher e Abrir com — etapa 5](docs/STAGE_5.md)
- [Instalação, configuração e restauração — etapa 6](docs/STAGE_6.md)
- [Formatos e ambientes — etapa 7](docs/STAGE_7.md)
- [Robustez, recuperação e atualização — etapa 8](docs/STAGE_8.md)
- [Consolidação ZagoSheetsWin e piloto final — etapa 9](docs/STAGE_9.md)
- [Autoria e licença](ATTRIBUTION.md)

O MVP inicial cobre XLSX em pasta local não sincronizada, com OAuth, snapshot, backup, journal, lock, conversão verificada e atalho .url. A fase 7 amplia formatos e adiciona cópia de origens compartilhadas conforme a matriz do guia; a retirada nesses ambientes permanece bloqueada. Não há sincronização bidirecional. O backup guarda os bytes da importação inicial, não edições online futuras.

### Piloto instalável — fase 6

A branch `feature/installable-pilot` produz o instalador por usuário **SheetsWindows-Setup-win-x64** no GitHub Actions. [Pacote histórico 0.6](https://github.com/zagozago/ZagoSheetsWin/actions/runs/36866831248/artifacts/11163847812). Configuração visual, OAuth, escolha do padrão e restauração offline estão no aplicativo. [Guia de instalação e recuperação](docs/STAGE_6.md). A desinstalação conserva backups, configuração, atalhos e documentos Google. O aceite manual completo no Windows 11 com Google real ainda está pendente.

### Novos formatos — fase 7

Usar a branch `feature/formats-environments` e habilitar os formatos na configuração visual. [Baixar o instalador 0.7 validado](https://github.com/zagozago/ZagoSheetsWin/actions/runs/36875411056/artifacts/11168732158). [Matriz de comportamento e limites](docs/STAGE_7.md). A retirada automática em XLS, ODS complexos, OneDrive e rede permanece bloqueada; esses casos usam cópia com original preservado.

### Robustez — fase 8

Usar a branch `feature/robustness`, versão 0.8. [Baixar instalador 0.8 aprovado](https://github.com/zagozago/ZagoSheetsWin/actions/runs/36882411612/artifacts/11171354492). [Retomada, matriz de falhas e atualização](docs/STAGE_8.md). Sessões de upload são protegidas por DPAPI; a recuperação reaproveita a operação existente. Diagnósticos têm limite de 256 KiB e não incluem caminhos, contas ou conteúdo. O instalador preserva os dados e bloqueia downgrade. O teste manual com Google real continua pendente.

### ZagoSheetsWin — etapa 9

Versão 0.9.1 na branch `feature/zagosheetswin`. [Baixar instalador aprovado](https://github.com/zagozago/ZagoSheetsWin/actions/runs/36926691912/artifacts/11193993461). CI Windows/Linux aprovado, incluindo instalação/atualização/remoção e revisão visual das quatro telas. Logos e créditos incorporados ao instalador e às telas; Sobre / MIT identifica Open in Google e Swati K (SwatiK425). O nome novo preserva a instalação/estado anteriores por compatibilidade. [Pacote e roteiro final](docs/STAGE_9.md). A licença MIT original permanece íntegra.
