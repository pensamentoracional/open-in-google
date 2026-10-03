# Alpha 0.9.5 — interface mínima e processamento

Etapa 4 do plano alpha. Implementação WinForms nativa; sem serviço residente. Pacote aprovado no CI 37101501901; aceite manual final Windows 11/Google real pendente.

## Uso

Tela inicial com Abrir planilha, Configurações e Backups. Abrir planilha usa o mesmo fluxo `OpenAsync` do duplo clique, respeitando a pasta elegível, preferência XLS, conferência, backup e os bloqueios existentes. Não transforma abrir pelo seletor em importação de cópia. Avançado recolhido contém Importar cópia, pasta dos atalhos de cópia e Atualizações. Sobre/MIT permanece no cabeçalho como link discreto. Criação de nova planilha permanece fora deste incremento.

Após configuração/autorização, duplo clique abre somente a janela compacta de processamento: estado e Cancelar. Sucesso fecha essa janela; navegador e atalho seguem as mesmas regras existentes. Abertura a partir da tela inicial fecha somente o diálogo de processamento, conservando a tela inicial. Não usar progresso percentual fictício. Falha mostra a causa, Recuperação / backups, Exportar diagnóstico e Configurações; não repete o upload automaticamente. Cancelamento ou fechamento durante operação solicita interrupção e aguarda o worker, preservando o mecanismo de recuperação.

Cliques do caminho comum já configurado: Explorer exige dois cliques no arquivo; seletor exige Abrir planilha e escolher/confirmar o arquivo no diálogo do Windows. Nenhuma confirmação adicional no sucesso. O fluxo de cópia continua explícito em Avançado. Esses são os caminhos previstos, não uma medição de usuários reais.

## Tema

Claro por padrão, inclusive em instalações anteriores sem escolha salva. Toggle discreto à direita do cabeçalho, com símbolos do template `𖤓` e `☾`, tooltip da próxima ação, nome e estado acessíveis, navegação Tab e ativação Space/Enter. Atualiza as janelas abertas sem reiniciar/importar novamente; escolha persistida de forma atômica em `theme.json`, independente de XLS, cliente OAuth, pasta, backup ou dados. Estado preservado no ciclo do instalador. Preferência inválida é conservada: a interface usa claro, mas não sobrescreve o arquivo inválido ao salvar.

Paleta clara/escura baseada no template; logo e créditos preservados. Alto contraste utiliza cores do Windows e renderização nativa do botão, mantendo a escolha de tema independente. Mudança das preferências do Windows reaplica a aparência das janelas abertas. DPI e legibilidade no Windows 11 com alto contraste real permanecem no aceite final.

## Diagnóstico e medidas

Diagnóstico limitado aos mesmos quatro arquivos de 64 KiB; registros incluem apenas eventos, horários, ID opcional e métricas numéricas. Exportação reserializa os campos permitidos e recusa métricas negativas; não inclui caminhos, contas, conteúdo, tokens ou URLs.

- `ReadyMs`: início do launcher até evento Shown da janela de processamento. Se houver primeiro uso antes dela, inclui esse intervalo. Pelo seletor, começa ao construir o diálogo; não inclui tempo de escolha no seletor.
- `ElapsedMs`: intervalo total até resultado da operação; exclui o tempo que o usuário deixa a tela de erro aberta e a gravação final do diagnóstico.
- `ConversionMs`: preparação/validação local do payload. `UploadMs`: envio ou reconciliação e verificação da identidade remota, incluindo resolução da pasta; não equivale apenas ao tráfego de bytes.
- `VerificationMs`: preparação do esperado, exportação e conferência de valores antes da retirada dos formatos estendidos. Não é medido para XLSX no fluxo atual nem para cópias sem essa conferência.
- `CpuMs`: delta de CPU do processo entre entrada no processamento (Shown) e captura; inclui suas threads e exclui a construção da janela. `PeakCpuPercent`: maior uso médio observado por amostras de aproximadamente 100 ms, normalizado aos processadores lógicos. Intervalos inferiores a 80 ms não contam; tarefas curtas sem amostra válida ficam null. É uma estimativa por amostragem, não pico instantâneo nem porcentagem de apenas um núcleo. O timer é encerrado no resultado e não mantém serviço residente. `PeakWorkingSetBytes`: pico do processo desde seu início, podendo incluir janelas e trabalhos anteriores da mesma instância.
- Fase não alcançada fica null; não atribuir zero a upload inexistente. Escopos encerram também em falhas/cancelamento. Medição não muda journal, locks ou decisões de retirada.

CI Windows gera medidas offline de exibição/fechamento, cancelamento e erro, além de conversão/conferência sintética de CSV 5.000 × 40. Não acessa Google, não publica o CSV fornecido e não mede upload real. O encerramento do processo é verificado pelo timeout do comando nativo; não significa teste de todos os problemas possíveis do Explorer.

## Validação

Local: 158 testes aprovados / 30 exclusivos Windows ignorados; build WinForms sem warnings/erros. Regressões adicionais cobrem tema claro sem criação de estado, alternância/persistência independente de XLS, tema inválido preservado, métricas só das fases alcançadas, descarte de campos sensíveis/inesperados e rejeição de métricas negativas. Testes nativos no CI verificam troca de tema nas janelas abertas e estado acessível, fechamento automático no sucesso, fechamento que cancela o worker e erro que permanece com recuperação/diagnóstico e layout do cancelamento de retomada sem sobreposição com a lista.

Prévias previstas: início compacto e avançado, primeiro uso, configurações compactas e avançadas, backups em repouso e com cancelamento de retomada, Sobre, progresso e erro em claro; início compacto e avançado, configurações, progresso e erro em escuro. As 15 prévias nativas foram revisadas: rótulos, créditos, símbolos e ações completos, áreas avançadas com rolagem quando necessário e cancelamento de retomada sem sobreposição. CI e pacote aprovados. Fontes/ícones, DPI 125/150/200%, alto contraste e Google real no Windows 11 ficam no aceite final; não confundir QA do runner com essa homologação.

## Evidências e pacote aprovado

[CI 37101501901](https://github.com/zagozago/ZagoSheetsWin/actions/runs/37101501901), commit `6be12ef84dd1366787105f0cde719b189b49b54b`: Windows 187 testes aprovados / 1 exclusivo Unix ignorado; Linux 158 aprovados / 30 exclusivos Windows ignorados. Zero falhas. Verificação nativa de interface, encerramento do processo, compilação do instalador, instalação, atualização, bloqueio de downgrade, desinstalação e reinstalação aprovados; configuração, autorização, preferências, ícone, backups, atalhos e padrões preservados. Instalação silenciosa sem janela mantida.

[Medidas brutas do runner Windows](metrics/alpha-0.9.5-native.json), offline, uma execução e instância já aquecida:

| Sonda | Janela pronta | Resultado total | Conversão | Conferência |
|---|---:|---:|---:|---:|
| Sucesso com worker simulado | 58 ms | 74 ms | — | — |
| Cancelamento com worker simulado | 40 ms | 72 ms | — | — |
| Erro simulado | 31 ms | 69 ms | — | — |
| CSV sintético 5.000 × 40, 2.505.600 bytes | — | 1.894 ms | 459 ms | 1.432 ms |

CSV fez round-trip local de valores, sem Google/exportação remota; CPU acumulada 2.375 ms e pico de working set do processo 137,6 MiB. CPU acumulada pode superar tempo de parede por incluir várias threads. Pico amostrado de CPU 3,97% pertence somente à sonda de interface com espera de 350 ms; não representa a conversão CSV nem upload. O round-trip síncrono offline não teve sampler de UI e registra PeakCpuPercent null. A importação normal executa o worker fora da UI e amostra CPU enquanto processa. Não extrapolar essas sondas para latência de lançamento a frio pelo Explorer, upload Google ou desempenho em outra máquina.

[Baixar instalador alpha 0.9.5 — Windows x64](https://github.com/zagozago/ZagoSheetsWin/actions/runs/37101501901/artifacts/11265857512). ZIP contém `ZagoSheetsWin-Setup-win-x64.exe`; SHA-256 do ZIP: `1251fa5a06c6f7b32fb958e2139d535c91400afd2627cc7f1f19858b705e684e`. Download exige login no GitHub; retenção até 01/01/2027. Pode atualizar a instalação atual preservando estado; não limpar configuração para experimentar o tema. Gestão de expiração/quota de backups, cliente OAuth oficial e internacionalização completa continuam nas etapas próprias.
