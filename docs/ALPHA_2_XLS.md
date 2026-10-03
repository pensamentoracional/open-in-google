# Alpha 0.9.3 — XLS padrão e ícone dos atalhos

Etapa 2 da evolução alpha. Substituição de XLS habilitada por padrão, com preferência independente em Configurar piloto → Substituir XLS por atalho → Salvar preferência XLS. Desmarcar importa como cópia e conserva o original. O padrão também vale para instalações existentes que ainda não tenham salvo esta preferência. Uma escolha já salva é preservada nas atualizações.

## Fluxo e limites

O XLS original é salvo integralmente no backup privado e enviado com MIME `application/vnd.ms-excel`. Leitura binária obtém os valores esperados; exportação XLSX do Sheets deve conferir nomes das abas, posições, tipos e valores das células antes da retirada. Mantêm-se locks, identidade/hash, journal, publicação de atalho, proteção de backup e política de pasta local não sincronizada. OneDrive/rede permanecem no fluxo explícito de cópia.

Macros não funcionam no Google Sheets. Fórmulas, vínculos, gráficos e formatação também podem mudar. A conferência é de valores, não uma promessa de equivalência funcional/visual. Fórmulas presentes no exportado continuam bloqueando retirada; divergência, erro de exportação ou qualquer falha mantém o original. Não se executam macros localmente. Backup conserva o XLS completo da importação inicial, não as edições online posteriores.

A recuperação de XLS agora pode concluir substituição quando a preferência estiver habilitada. Com preferência desabilitada, o comando Concluir substituição é recusado e Retomar como cópia continua disponível. Operações já importadas reaproveitam o ID remoto; não criam outro documento para mudar o modo de abertura. Arquivos já retirados não reaparecem por desmarcar: usar Restaurar em para recuperá-los.

## Ícone fornecido

O arquivo `branding/sheet-shortcut.ico` é o ICO enviado por Fernando. Novos atalhos .url, tanto da substituição quanto da importação de cópia, incluem IconFile e IconIndex=0. O aplicativo materializa o recurso em `%LOCALAPPDATA%\SheetsWindows\shortcut-icon-v1.ico`, caminho estável dentro do estado preservado por atualização/desinstalação. Validação de bytes evita adotar ícone alterado; publicação é atômica.

Atalhos antigos sem ícone mantêm seus bytes para permitir recuperação sem conflito. Não há varredura/regravação automática de atalhos antigos nesta etapa. O ícone do executável/logotipo continua o existente; o novo ICO personaliza os atalhos de planilha. Explorer Windows 11/cache de ícones ainda exige aceite manual.

## Validação

Suite local Linux: 151 aprovados, 30 testes exclusivos Windows ignorados, zero falhas. Regressões cobrem leitura/conferência XLS, preferência padrão e alteração sem mudar CSV, configurações inválidas, recurso de ícone estável e recuperação de atalho legado. Testes Windows cobrem retirada XLS com backup íntegro e ícone, modo cópia desmarcado, divergência com original conservado e retomada do mesmo upload após habilitar novamente. Instalador verifica preservação dos arquivos de preferência e ícone.

Versão 0.9.3; CI Windows/Linux, instalador e prévia de configurações aguardando aprovação. XLS no Google real e aparência do ícone no Explorer ficam para a validação final.

Gestão de backup 30 dias / 200 MB (máximo configurável 1 GB), temas com claro inicial, wizard simplificado e OAuth de distribuição continuam planejados nas próximas etapas.
