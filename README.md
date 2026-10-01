# Sheets Windows

Projeto em preparação: abrir planilhas locais no Google Sheets e substituir o original por um atalho de Internet na mesma pasta, com backup recuperável.

**Estado:** etapa 7 adiciona CSV/TSV/ODS/XLS e importação de cópia para OneDrive/rede ao piloto instalável. CSV/TSV/ODS simples exigem conferência de valores antes da retirada; XLS/ODS complexos e ambientes compartilhados preservam o original. O aceite manual no Windows 11 com Google real permanece pendente. Os scripts PowerShell herdados continuam com o comportamento original: reabrir um arquivo atualiza sua cópia no Drive. Não utilizá-los como se já implementassem a proteção descrita no roadmap.

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

O MVP inicial cobre XLSX em pasta local não sincronizada, com OAuth, snapshot, backup, journal, lock, conversão verificada e atalho .url. A fase 7 amplia formatos e adiciona cópia de origens compartilhadas conforme a matriz do guia; a retirada nesses ambientes permanece bloqueada. Não há sincronização bidirecional. O backup guarda os bytes da importação inicial, não edições online futuras.

### Piloto instalável — fase 6

A branch `feature/installable-pilot` produz o instalador por usuário **SheetsWindows-Setup-win-x64** no GitHub Actions. [Pacote histórico 0.6](https://github.com/pensamentoracional/open-in-google/actions/runs/36866831248/artifacts/11163847812). Configuração visual, OAuth, escolha do padrão e restauração offline estão no aplicativo. [Guia de instalação e recuperação](docs/STAGE_6.md). A desinstalação conserva backups, configuração, atalhos e documentos Google. O aceite manual completo no Windows 11 com Google real ainda está pendente.

### Novos formatos — fase 7

Usar a branch `feature/formats-environments` e habilitar os formatos na configuração visual. [Baixar o instalador 0.7 validado](https://github.com/pensamentoracional/open-in-google/actions/runs/36875411056/artifacts/11168732158). [Matriz de comportamento e limites](docs/STAGE_7.md). A retirada automática em XLS, ODS complexos, OneDrive e rede permanece bloqueada; esses casos usam cópia com original preservado.
