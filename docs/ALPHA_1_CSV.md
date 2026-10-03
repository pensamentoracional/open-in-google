# Alpha 0.9.2 — CSV, capacidade e diagnóstico

Etapa 1 da evolução posterior ao piloto. Mantém instalação e armazenamento compatíveis com 0.9.1; XLS, temas, ícones, política de backup e OAuth de distribuição permanecem em etapas seguintes.

## Correção

O CSV do piloto continha 5.000 linhas × 40 colunas (200.000 células) e 1.315.517 bytes. Não era falha de OAuth nem necessidade de reiniciar Windows: o limite de 100.000 células era excedido e a detecção de separador escondia esse erro.

O detector agora conserva falhas de capacidade e não usa outro separador para contornar o limite. Casos ambíguos continuam exigindo escolha explícita. UTF-8/UTF-16 com BOM e Windows-1252 explícito mantêm as regras anteriores; a normalização conserva strings, zeros iniciais, aspas, Unicode e fórmulas como texto literal.

Mensagens distinguem limite de bytes/células/linhas/colunas/campo, codificação, separador, estrutura, divergência de conversão, rede e rejeição HTTP pelo Google. Divergência pode ocorrer depois do upload: a mensagem orienta recuperação sem alegar que nenhuma cópia remota foi criada.

## Capacidade e recursos

| Controle | 0.9.1 | 0.9.2 |
|---|---:|---:|
| Fonte / XLSX normalizado | 5 MiB | 20 MiB |
| Células | 100.000 | 500.000 |
| Linhas | 10.000 | 50.000 |
| Colunas | 1.000 | 1.000 |
| Caracteres por campo de texto | 32.767 | 32.767 |

Todos os limites se aplicam em conjunto. Os limites de leitura de planilhas também se aplicam ao arquivo exportado para conferência. ZIP: até 96 MiB descompactados; parte XML: até 64 MiB. Índice ZIP, metadata e proteção contra DTD continuam limitados. Não há promessa de capacidade ilimitada ou fidelidade visual.

A exportação `files.export` do Google é limitada a 10 MB: https://developers.google.com/workspace/drive/api/reference/rest/v3/files/export. Fonte maior não garante exportação possível. Se a conferência falhar, conservar original/backup e recuperar a operação existente; não repetir uploads cegamente.

XLSX passa a ser escrito sequencialmente e a busca por fórmulas no exportado usa XmlReader. Comparação de valores por posição evita dois dicionários adicionais. Preparação/conferência rodam fora da thread de interface, com cancelamento durante parsing, escrita, leitura e comparação. Upload retomável ganha margem de requisições para os blocos do limite de 20 MiB.

## Evidências locais

Linux, .NET 10, build Release. Medição de uma execução por caso em processo separado; inclui preparar e conferir o próprio XLSX, sem rede. Não é medição de tempo no computador do usuário ou na API Google.

| Caso | Resultado | Preparação | Total | Pico de memória |
|---|---|---:|---:|---:|
| CSV real, 5.000 × 40 | Todos os valores conferidos | 337 ms | 1.216 ms | 91,0 MiB |
| Sintético, 12.500 × 40 | 500.000 células conferidas | 819 ms | 2.317 ms | 167,6 MiB |

Suite local: 149 aprovados, 29 testes específicos Windows ignorados, zero falhas. Novas regressões cobrem 200.000 e 500.000 células, exportação truncada, limites em detecção automática e explícita, cancelamento, importação/conferência com Drive simulado e reabertura sem novo upload. Casos Windows incluem retirada do CSV grande somente após conferência e retomada sem novo upload após divergência.

CSV pessoal não incluído no repositório, artefatos ou logs; somente dimensões e resultados são registrados. CI [37095090039](https://github.com/zagozago/ZagoSheetsWin/actions/runs/37095090039) aprovado para o commit `fe33ac860962215fec5300c180922a9e676d0cdf`: Windows 177 aprovados / 1 teste exclusivo Unix ignorado; Linux 149 aprovados / 29 exclusivos Windows ignorados; zero falhas. Build, publicação, prévias nativas, instalação/atualização/desinstalação e preservação de estado aprovados. Prévia de configuração revisada sem cortes no texto de limites.

## Pacote aprovado

[Baixar instalador alpha 0.9.2 — Windows x64](https://github.com/zagozago/ZagoSheetsWin/actions/runs/37095090039/artifacts/11264096007). O ZIP contém `ZagoSheetsWin-Setup-win-x64.exe`. SHA-256 do ZIP: `fa2c3bd69d9d1316d09b1c9d6dd8ca868e7f33bce9f3f7f8f473919d905e7355`. Artefatos exigem login no GitHub e têm retenção até 01/01/2027. Atualizar preserva configuração, autorização e backups; não é preciso limpar a máquina para esta atualização.

## Teste real ao final das etapas

Atualizar pelo instalador aprovado preservando configuração e backups. Testar uma cópia do CSV que falhou, conferir linhas/colunas e valores no Sheets, atalho, original retirado apenas após sucesso e reabertura sem novo upload. Testar também cancelamento e erro de rede com original preservado. Aceite Google real/Windows 11 permanece pendente.
