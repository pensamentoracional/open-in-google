# Fase 7 — Formatos e ambientes

Branch `feature/formats-environments`, aplicativo e instalador 0.7. A implementação amplia o piloto sem liberar retirada de arquivos nos casos cuja fidelidade ou ambiente não pode ser comprovado.

## Matriz de comportamento

| Origem | Importação | Retirada do original | Atalho |
|---|---|---|---|
| XLSX na pasta local configurada | Fluxo das fases 3–6, sem VBA | Após backup e verificação da associação | Mesma pasta |
| CSV/TSV na pasta local configurada | Leitura estrita e XLSX intermediário com células de texto literal | Somente após exportar o Sheets e comparar abas, posições, tipos e valores | Mesma pasta |
| ODS simples, com texto/número/booleano | ODS nativo, MIME correto | Somente após comparação dos valores exportados | Mesma pasta |
| ODS com fórmulas, datas/horas, mesclagem, objetos ou recursos não verificáveis | Cópia nativa | **Nunca** nesta entrega | Pasta privada do aplicativo |
| XLS binário | Validação de leitura com ExcelDataReader e cópia nativa | **Nunca** nesta entrega: macros/formulas legadas não são certificadas | Pasta privada do aplicativo |
| OneDrive disponível localmente ou rede UNC/unidade mapeada | Entrada explícita **Importar cópia** | **Nunca**: não propagar exclusão | Pasta privada do aplicativo |
| XLSM/XLSB, ODS com scripts, links/junctions, placeholders online-only | Bloqueada | Nunca | Nenhum |

O caminho de duplo clique `--open` para substituição continua limitado à pasta local configurada, sem rede ou sincronização. XLS sempre usa leitura de cópia, inclusive no duplo clique, sem abrir handle de retirada. Para fontes externas, usar o botão **Importar cópia…** ou `SheetsWindows.exe --copy "caminho absoluto"`. A importação de cópia não exige escrita na origem. O atalho pode ser aberto pelo botão **Abrir pasta de atalhos**, e suas próximas aberturas fazem zero uploads.

Reabrir um original ainda conservado consulta a associação existente e não repete POST. Se os bytes mudarem, a operação para com conflito e conserva ambas versões; não atualiza o Sheets silenciosamente. Em modo de cópia, a representação local do trabalho no Sheets é o atalho privado, enquanto o original continua sendo a fonte inicial.

## Configuração dos novos formatos

Atualizar pelo instalador e abrir **Configurar piloto**. Marcar **Habilitar CSV, TSV, ODS e XLS**; isso acrescenta consentimento uma vez, sem alterar configurações XLSX existentes. Escolher a codificação e o delimitador CSV antes de salvar:

- Automático: UTF-8 estrito, com ou sem BOM; UTF-16 LE/BE somente com BOM. Bytes inválidos são rejeitados; não adivinhar Windows-1252.
- Windows-1252: escolha explícita para um conjunto de arquivos desse encoding; conflito com BOM é rejeitado.
- CSV: autodetecção apenas entre vírgula e ponto e vírgula, com tabela retangular e interpretação única. Ambiguidade ou quantidade irregular de campos é erro. É possível fixar o delimitador na configuração inicial.
- TSV: tabulação fixa. Aspas duplas, aspas escapadas, quebras de linha em campo citado e campos vazios são suportados pelo parser estrito.

Zeros à esquerda, datas escritas como texto, números longos e prefixos `=`, `+`, `-` e `@` em CSV/TSV permanecem **texto literal**: o programa não infere tipos nem cria fórmulas. Pode ser necessário converter tipos conscientemente dentro do Sheets depois. A comparação também rejeita fórmulas no arquivo exportado, inclusive quando o valor calculado coincide com o texto esperado.

As opções são persistentes e imutáveis para evitar reinterpretar uma operação existente de outro jeito. Uma migração de interpretação ainda precisa de fluxo próprio; não apagar bancos/configuração. Usar um conjunto coerente com a configuração, ou converter um arquivo ambíguo conscientemente antes de importá-lo.

Limites adicionais dos novos formatos: fonte até 5 MiB; 100.000 células, 10.000 linhas, 1.000 colunas e 100 abas; texto por campo até 32.767 caracteres. Repetições ODS e tamanho descompactado são limitados. ODS com repetições gigantes ou estrutura inválida é rejeitado, inclusive em cópia. Exportação para conferência até 10 MiB. A conferência valida valores, não fidelidade visual de estilos, gráficos ou toda semântica de planilhas; o backup original continua sendo a proteção integral.

## Segurança e recuperação

A comparação ocorre com backup e original presos por handles antes da intenção de retirada. Divergência, exportação indisponível, permissões revogadas ou cancelamento conservam o original e o backup. Um atalho já publicado pode permanecer após falha, conforme o journal das fases anteriores; isso não autoriza retirada. Repetir recupera a associação existente, sem recriar o documento. Um documento já concluído não é comparado novamente pela transação de retirada, para respeitar edições online posteriores.

O modo de cópia não usa o leitor de retirada nem grava na origem. Para nuvem, só admite tags Cloud Files conhecidas em arquivos disponíveis localmente; symlinks/junctions não são tratados como cloud. Arquivos somente online precisam ser marcados como disponíveis no dispositivo pelo usuário; o app não solicita hidratação automática. Identidades SMB são delimitadas por servidor/share; unidade mapeada é resolvida para UNC, e servidores sem identidade estável são rejeitados. Não se promete deduplicação entre aliases diferentes do mesmo servidor.

O registro migra do schema 1 para 2 em uma transação, conservando IDs, snapshots, aliases e journal. Renomear um arquivo para outra extensão não muda silenciosamente sua interpretação nem associa outro formato ao documento antigo. O launcher registra os cinco formatos como candidatos no HKCU; o usuário escolhe padrões na interface Windows. O ProgId histórico é conservado para compatibilidade. A desinstalação preserva também `formats.json`, tokens e atalhos privados.

A recuperação visual restaura a extensão e os bytes do formato original, sem Google/internet e sem sobrescrever destinos. Cópias em OneDrive/rede continuam com seus arquivos originais, independentemente de recuperação.

## Validação

Testes cobrem parsing, codificação, ambiguidade, textos literais, sequências de escape OpenXML, formatos binários reais, ODS repetido, comparação de valores/tipos/abas, rejeição de fórmulas exportadas, migração e opt-in. Testes Windows usam handles reais e tokens de teste protegidos por DPAPI, com HTTP Google simulado, para exercitar retirada após conferência, recusa de retirada, recuperação sem POST duplicado, XLS/ODS complexos como cópia e política OneDrive.

O teste SMB cria e remove exclusivamente um compartilhamento local de leitura em um perfil descartável de CI Windows. Não roda em máquinas pessoais; exige `GITHUB_ACTIONS=true`. Testa rede real local, com Google simulado. Não certifica NAS, todo servidor SMB nem um provedor OneDrive real.

Continuam pendentes: aceite manual Windows 11 + Google real; arquivos Cloud Files hidratados e offline reais; qualidade de conversão em documentos próprios; retirada em rede/OneDrive; retirada automática de XLS e de ODS complexos. Esses gates permanecem fechados, conforme o roadmap. A fase 7 entrega suporte controlado, não certificação universal dos formatos/ambientes.

## Referências técnicas

- Google Drive: [conversões de importação](https://developers.google.com/workspace/drive/api/guides/manage-uploads), [exportação](https://developers.google.com/workspace/drive/api/reference/rest/v3/files/export), [MIME de exportação XLSX](https://developers.google.com/workspace/drive/api/guides/ref-export-formats).
- [Esquema OpenDocument 1.3](https://docs.oasis-open.org/office/OpenDocument/v1.3/os/part3-schema/OpenDocument-v1.3-os-part3-schema.html).
- [ExcelDataReader](https://github.com/ExcelDataReader/ExcelDataReader), NuGet 3.9.0 fixado nos locks; licença MIT incluída no pacote em `third-party/ExcelDataReader-LICENSE.txt`.
- Microsoft: [reparse tags](https://learn.microsoft.com/en-us/windows/win32/fileio/reparse-point-tags).

Após a primeira abertura do registro nesta versão, seu schema passa a 2. Executáveis antigos recusam o schema novo; não fazer downgrade da instalação usando o mesmo estado. Isso evita que versões anteriores interpretem operações de formatos que desconhecem.
