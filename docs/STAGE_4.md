# Etapa 4 — substituição recuperável por atalho

A branch feature/shortcut-replacement acrescenta o fluxo CLI de substituição. O launcher e a associação de arquivos são a etapa 5; o instalador e sua tela de configuração são a etapa 6. `import` continua sendo diagnóstico que preserva o original; o futuro launcher utilizará `replace` após configuração.

## Comportamento

ImportReceipt liga operação local, snapshot e URL à associação Google verificada. Após a importação, o coordenador adquire o lock da identidade e relê a associação local/remota. Confere backup e conserva seu handle aberto, protegido contra escrita e retirada no Windows. Publica `.url` UTF-8 com apenas `[InternetShortcut]` e URL HTTPS canônica do editor Sheets. Não aceita query, fragmento, credenciais, outro host ou ID inválido.

O nome é primeiro `nome.url`, depois `nome.xlsx.url`, depois `nome.xlsx.<operation-id>.url`. Nunca sobrescreve arquivos ou diretórios existentes. A publicação grava temporário na mesma pasta, flush para disco e move sem overwrite. Na retomada, conteúdo existente precisa ser idêntico ao esperado; um atalho divergente bloqueia a retirada. O atalho fica aberto sem compartilhar escrita/exclusão durante a operação.

Solicita abertura da URL ao shell. Falha explícita mantém o original; aceitação não prova carregamento da página. A retirada acontece pelo handle Windows do original, aberto com GENERIC_READ e DELETE, sem compartilhar escrita/exclusão. Identidade, tamanho e SHA-256 são conferidos nesse mesmo handle. SetFileInformationByHandle(FileDispositionInfo) marca esse arquivo para exclusão quando fechado; não existe File.Delete(path) na implementação produtiva. Um original recriado não é adotado por nome nem apenas por hash.

Backups privados conservam os bytes importados e não são expurgados automaticamente. Não incluem alterações online futuras. A conversão não tem prova automática de fidelidade; a configuração explícita aceita esse limite para planilhas de piloto. XLSX com VBA e arquivos acima de 5 MiB continuam bloqueados.

## Journal e retomada

replacement.db separado, versionado, synchronous=FULL, com transições e códigos de falha sem tokens. Não há transação única entre os três bancos, Google e filesystem.

| Passo | Evidência persistente | Retomada |
|---|---|---|
| 0 | Intenção, destino e URL | Publicar ou validar bytes de atalho já publicado |
| 1 | Atalho validado | Conferir original e solicitar navegador |
| 2 | Shell aceitou abertura | Conferir original; persistir intenção de retirada |
| 3 | Retirada autorizada com backup/atalho/associação | Se original ainda existe, conferir sua identidade antes de retirar; se ausente, concluir |
| 4 | Concluído | Verificar backup/atalho e retornar; nunca tocar em original recriado |

Falhas do browser não avançam o passo 1. Um crash entre abertura do browser e seu registro pode abrir uma segunda aba na retomada, mas não provoca novo upload. Um original ausente antes do passo 3 exige investigação. Um original alterado ou recriado no passo 3 é preservado. Não apagar a planilha Google como rollback. Cópias temporárias órfãs de publicação podem permanecer após encerramento abrupto; não são consideradas atalhos válidos.

A recuperação é explícita pelo ID, exibido antes da substituição; listagem e recuperação automática na inicialização serão integração posterior. `resume` consulta novamente o remoto e exige a mesma conta. Não chama upload. Os testes exercitam estados persistidos de interrupção; não são um simulador de queda de energia em cada instrução do kernel.

## Configuração e piloto Windows

Usar uma pasta particular num disco fixo, sem OneDrive, Dropbox, Google Drive ou outro sincronizador. Declarar isso ao executar a configuração uma vez. O piloto bloqueia UNC/rede, pontos de reparse, atributos cloud/offline, raízes OneDrive conhecidas e hard links. Não existe detecção universal de sincronizadores: uma pasta hidratada de outro fornecedor pode parecer local, por isso a declaração explícita de pasta sem sincronização é parte do limite deste piloto. Configuração não é autorização para testar com arquivos insubstituíveis.

Após o login da etapa 3, em uma pasta contendo somente cópias de teste:

```powershell
dotnet run --project src/SheetsWindows.Cli -- configure-replacement "C:\seguro\desktop-client.json" "C:\PilotoSheets"
dotnet run --project src/SheetsWindows.Cli -- replace "C:\seguro\desktop-client.json" "C:\PilotoSheets\Relatório.xlsx"
# Retomar pelo GUID informado, sem criar outra planilha:
dotnet run --project src/SheetsWindows.Cli -- resume "C:\seguro\desktop-client.json" "GUID-da-operacao"
# Restaurar snapshot no caminho original, somente se esse caminho estiver livre:
dotnet run --project src/SheetsWindows.Cli -- restore "C:\seguro\desktop-client.json" "GUID-da-operacao"
```

Configuração é publicada por temporário com flush e rename sem overwrite, para não adotar configuração parcial após crash. Configuração persiste em LOCALAPPDATA/SheetsWindows/replacement-root.txt. Sem ela, `replace` não importa nem retira o arquivo. Não requer confirmação por arquivo. `restore` verifica o backup e nunca sobrescreve destino; não remove atalho ou documento Google. O JSON OAuth ainda é parâmetro comum do CLI, inclusive no restore, mas restauração não acessa Google nem exige tokens.

Abrir o `.url` no Explorer usa diretamente o navegador: não chama o CLI e faz zero uploads. Editar online, reabrir o atalho e confirmar o mesmo ID são verificações manuais pendentes do piloto real. ACL privada dos backups não protege contra processos maliciosos executados pelo mesmo usuário; proteção dos diretórios de origem contra alterações deliberadas por esse usuário também não é uma fronteira de segurança prometida.

## Fontes e validação

- https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle
- https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew

Testes cobrem URL, colisões, backup corrompido, original alterado, navegador recusado, atalho adulterado, concorrência, restauração sem overwrite, journal fora de ordem e retomada de cada estado. Windows acrescenta retirada real pelo handle, exclusão de escrita/rename, limite da pasta e arquivo recriado com os mesmos bytes. Login/conversão Google reais e piloto no Windows 11 continuam pendentes. Compilação local Release sem avisos/erros; 71 testes aprovados, 8 específicos de Windows pulados, zero falhas (79 testes). Evidência CI será registrada após execução.
