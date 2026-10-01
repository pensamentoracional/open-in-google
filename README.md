# Sheets Windows

Projeto em preparação: abrir planilhas locais no Google Sheets e substituir o original por um atalho de Internet na mesma pasta, com backup recuperável.

**Estado:** etapa 6 implementa instalador por usuário, configuração visual e restauração offline, além do launcher e do fluxo recuperável de importação/atalho. O aceite manual no Windows 11 com Google real permanece pendente. Os scripts PowerShell herdados continuam com o comportamento original: reabrir um arquivo atualiza sua cópia no Drive. Não utilizá-los como se já implementassem a proteção descrita no roadmap.

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

O MVP será XLSX em pasta local não sincronizada, com OAuth, snapshot, backup, journal, lock, conversão verificada e atalho .url. ODS, XLS, CSV, TSV, OneDrive e rede são entregas posteriores. Não há sincronização bidirecional. O backup guarda os bytes da importação inicial, não edições online futuras.

### Piloto instalável — fase 6

A branch `feature/installable-pilot` produz o instalador por usuário **SheetsWindows-Setup-win-x64** no GitHub Actions. [Baixar o instalador validado](https://github.com/pensamentoracional/open-in-google/actions/runs/36866831248/artifacts/11163847812). Configuração visual, OAuth, escolha do padrão e restauração offline estão no aplicativo. [Guia de instalação e recuperação](docs/STAGE_6.md). A desinstalação conserva backups, configuração, atalhos e documentos Google. O aceite manual completo no Windows 11 com Google real ainda está pendente.
