# Etapa 3 — autenticação e importação Google

## Implementação

OAuth desktop no navegador padrão com PKCE S256 e state aleatório. O listener TCP ocupa 127.0.0.1 numa porta escolhida pelo sistema antes de abrir o browser. Callback valida caminho exclusivo, Host, state e parâmetros duplicados; conexões têm prazo de leitura, tamanho máximo e timeout global. A página informa apenas recebimento da resposta, não sucesso antecipado.

Tokens persistidos atomicamente com Windows DPAPI CurrentUser e entropia por OAuth client, em diretório com ACL privada. Tokens, cliente e sessão têm ToString redigido. Renovação preserva refresh token omitido, verifica escopo retornado e a identidade Drive user.permissionId. Conta local é client_id:permissionId; nunca e-mail presumido. Revogação exige login explícito. Clientes OAuth web são rejeitados.

Importação usa snapshot verificado da etapa 2, com um handle aberto no original durante a operação Google. Identidade e hash são conferidos novamente após adquirir lock. Não há PATCH/atualização de conteúdo nem DELETE. Reabertura inalterada verifica e abre a associação existente; local alterado bloqueia upload e mantém ambas as versões. Documento remoto removido não é recriado automaticamente. Documento movido e pasta inicial removida não invalidam por si só o vínculo da planilha.

## Registro remoto e resposta perdida

Google.db é um segundo banco versionado de operações remotas, distinto do registry.db do snapshot. Cada intenção é vinculada a conta, tipo e hash, com marcador privado appProperties sw_operation; pasta e planilha têm intenções independentes. Estado local e criação remota não são uma transação distribuída: reconciliar pelo marcador é obrigatório.

Sequência: persistir intenção → criar → guardar ID candidato → consultar MIME/lixeira/capacidade de editar/marcadores → marcar verificado. ID candidato não é associação concluída. Não se busca identidade pelo nome.

Em uma intenção anterior sem ID, a aplicação pagina a busca por marcador. Um resultado válido é adotado; zero ou múltiplos interrompem com reconciliação pendente. Zero não prova que o upload nunca ocorreu. Não repetir POST cegamente. HTTP 401 em GET renova uma vez; POST nunca é repetido automaticamente. Mesmo falhas anteriores ao envio podem deixar intenção pendente: o piloto privilegia evitar duplicação e não oferece ainda reset manual dessas intenções.

## Limites explícitos

Piloto: XLSX sem VBA, até 5 MiB, upload multipart conforme a documentação Google para arquivos pequenos. Arquivos maiores exigirão sessão retomável/chunks na etapa de robustez. Validação verifica ZIP, metadata de workbook, MIME de XLSX e ausência de VBA; não prova fidelidade de fórmulas, gráficos, links ou outros recursos. DTD é proibido e a leitura XML é limitada.

Pasta dedicada criada pela aplicação: Sheets Windows. Seleção de pasta pelo usuário será configuração futura. Sem permissões amplas do Drive: somente drive.file. A chave appProperties é privada ao app cliente; mudar client_id muda o namespace de conta e não migra associações silenciosamente.

Esta etapa não remove originais e não cria .url. Conversão pode perder recursos do Excel; antes da retirada na etapa 4 haverá gate de compatibilidade/consentimento apropriado. Sem sincronização bidirecional.

## Piloto no Windows — teste real

O teste HTTP simulado não substitui login e conversão reais. Nenhuma credencial Google foi fornecida ou utilizada nesta execução.

1. Num projeto Google Cloud próprio, habilitar Google Drive API.
2. Configurar consentimento OAuth e adicionar sua conta como test user se o app estiver em Testing.
3. Criar OAuth client do tipo Desktop app e baixar o JSON. Guardá-lo fora do Git. Não colar tokens em conversas.
4. Usar SDK .NET 10.0.401 e a branch feature/google-import.

```powershell
dotnet run --project src/SheetsWindows.Cli -- login "C:\caminho\desktop-client.json" "$env:LOCALAPPDATA\SheetsWindows"
dotnet run --project src/SheetsWindows.Cli -- import "C:\caminho\desktop-client.json" "C:\caminho\teste.xlsx"
```

Login abre Google para consentimento; import cria a pasta e planilha e abre o navegador. Usar uma planilha pequena de teste, local, fora de OneDrive e sem dados sensíveis para o primeiro ensaio. Editar no Sheets e abrir o mesmo arquivo local novamente deve abrir o mesmo ID, sem novo upload. O original permanece. Contas diferentes têm registros separados.

Cliente OAuth em Testing pode exigir nova autenticação por expiração/revogação; não prometer login permanente. Políticas corporativas podem bloquear consentimento. O CLI é ferramenta de desenvolvimento, ainda não o launcher/instalador final.

## Fontes oficiais

- https://developers.google.com/identity/protocols/oauth2/native-app
- https://developers.google.com/workspace/drive/api/guides/manage-uploads
- https://developers.google.com/workspace/drive/api/guides/properties
- https://developers.google.com/workspace/drive/api/reference/rest/v3/about

## Validação

Testes HTTP simulados cobrem criação/conversão, reabertura sem POST, concorrência, resposta perdida da pasta/planilha, busca atrasada/ambígua, remoto apagado, lixeira/falta de edição, MIME incorreto, mudança local, limite de tamanho, formato/macros, refresh/escopo/conta e state/PKCE. Windows tem testes adicionais de DPAPI e callback real de loopback com browser simulado. Validação local: 47 aprovados, 5 pulados por exigir Windows, 0 falhas, em 52 testes. CI Windows/Linux será confirmada após publicação. Login Google real e fidelidade real de conversão permanecem gates do piloto.
