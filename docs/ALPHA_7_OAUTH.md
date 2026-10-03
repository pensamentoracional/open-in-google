# Etapa 7 — OAuth de distribuição

Status: especificação e auditoria concluídas em 03/10/2026; provisionamento Google Cloud, incorporação ao pacote e teste real pendentes. Instalador aprovado continua 0.9.6. Não existe ainda cliente oficial incorporado.

## Experiência proposta

Em uma instalação nova, o cliente desktop oficial vem no aplicativo. O usuário escolhe a pasta e clica em **Conectar Google**; autoriza no navegador com sua própria conta. Não cria projeto Google Cloud nem escolhe JSON no fluxo comum. Cada documento fica no Drive da pessoa que autorizou.

Proposta: manter JSON próprio somente em Avançado para configuração inicial, desenvolvimento e forks. Uma instalação já configurada mantém seu cliente atual; não permitir troca silenciosa por atualização. Esta alternativa preserva compatibilidade enquanto uma migração explícita é estudada.

## Auditoria do código atual

- `GoogleOAuth` já usa navegador externo, PKCE/state, callback loopback e escopo `drive.file`. Identifica a conta pelo permissionId do Drive, sem solicitar nome ou e-mail.
- Tokens ficam localmente protegidos por DPAPI, vinculados ao usuário Windows e cliente OAuth; não devem ser incorporados nem transmitidos ao Zagotools.
- `AccountAsync` retorna `client.Id + ":" + permissionId`. O cliente participa das identidades de conta, pasta remota, operações e tokens.
- `LauncherConfiguration.SaveClient` recusa substituir um cliente por outro ID. Este bloqueio precisa permanecer.
- Hoje `SetupForm` e `FirstUseState` exigem o JSON local. Ainda precisam ganhar resolução do cliente oficial e fluxo sem seleção de JSON.
- Trocar cliente/projeto não é apenas pedir outro login: pode afetar acesso autorizado via `drive.file`, associação de documentos e reconciliação. Não renomear namespaces nem apagar bancos para forçar a troca.

## Identidade e exposição

O JSON de cliente desktop identifica o aplicativo; não contém a senha Google nem deve conter tokens pessoais. Aplicativos desktop não conseguem manter um client_secret confidencial: o fluxo continua dependendo da autorização individual e PKCE. Incorporar somente os campos de configuração necessários, sem dump do projeto, credenciais de serviço ou tokens.

O e-mail escolhido como **User support email** aparece ao usuário na tela de consentimento. Não usar o e-mail pessoal de Fernando nesse campo se a intenção é manter um contato separado. A conta administradora do projeto não precisa ser o contato público de suporte. Usar uma conta ou Google Group de suporte elegível e monitorado; não inventar endereço ou domínio.

## Provisionamento — ações no Google Cloud

1. Definir contato público de suporte e domínio controlado pelo Zagotools, com página do produto, política de privacidade e termos; verificar o domínio conforme exigência do Google.
2. Criar projeto de produção separado do projeto usado no piloto/desenvolvimento, sob controle do Zagotools. Conta exclusiva é uma opção organizacional, não exigência para o login dos usuários.
3. Habilitar **Google Drive API**. Em Google Auth Platform, configurar Branding com ZagoSheetsWin, contato de suporte e URLs reais; contato técnico do projeto separado quando desejado.
4. Em Audience, escolher **External**. Testing pode servir para validar com usuários explicitamente cadastrados; para distribuição cotidiana, configurar **In production** e atender a verificação aplicável.
5. Em Data Access, manter somente `https://www.googleapis.com/auth/drive.file`. Não ampliar para acesso a todo Drive, Gmail ou e-mail/perfil.
6. Em Clients, criar **Desktop app** e baixar o JSON. Não escolher Web application, service account, Android ou client ID do navegador.
7. Fornecer esse JSON desktop para integração. Confirmar identidade do projeto, escopo, estado de publicação e contato público antes de produzir o instalador oficial.

Testing admite até 100 usuários de teste e, para o escopo usado pelo app, a autorização/refresh token expira após sete dias. Produção não promete tokens eternos: revogação e demais regras do Google continuam válidas. `drive.file` é não sensível, mas isso não elimina as exigências de identidade, marca e políticas. Publicar o app não equivale a ter nome/logo verificados.

## Integração após receber o cliente

1. Validar JSON desktop e incorporar uma configuração mínima ao build; não usar valores fictícios como credenciais de distribuição. O pipeline deve distinguir pacote alpha sem cliente oficial de pacote oficial.
2. Resolver cliente com precedência: configuração local existente, escolha explícita inicial em Avançado, cliente oficial do pacote. Persistir a escolha para que mudanças futuras no pacote não troquem a identidade de instalações já configuradas. Configuração existente inválida deve gerar erro, nunca fallback silencioso.
3. Ajustar primeiro uso para conectar sem JSON na instalação nova com cliente oficial. Exibir o uso da conta individual e tornar a informação de privacidade acessível. Manter falha clara quando um pacote de desenvolvimento não tiver cliente oficial.
4. Manter tokens e histórico por cliente atual. Não oferecer migração de instalações antigas até existir um fluxo separado, com reautorização e prova de acesso aos documentos existentes; sem novo upload cego ou exclusão de originais/backups.
5. Testar cliente embutido, configuração própria prioritária, ausência/invalidez, mudança de versão, revogação, conta diferente, reautorização, instalação/atualização e preservação de operações. Validar duas contas Google reais independentes antes de declarar distribuição pronta.

## Pendências de Fernando

- Contato público de suporte desejado e domínio/página oficial sob seu controle.
- Criar/configurar o projeto Google Cloud e fornecer o JSON do cliente **Desktop app**, ou indicar que quer orientação guiada durante esse processo.
- A alternativa JSON próprio em Avançado permanece como proposta; sua remoção não é necessária para simplificar o primeiro uso comum.

Não houve publicação de projeto Google Cloud nem alteração de credenciais nesta etapa. A configuração real depende de acesso à conta administradora e das informações acima.

## Fontes oficiais consultadas

- [Fluxo OAuth para aplicativos desktop](https://developers.google.com/identity/protocols/oauth2/native-app).
- [Escopos Google Drive](https://developers.google.com/workspace/drive/api/guides/api-specific-auth).
- [Audience: teste, produção e expiração](https://support.google.com/cloud/answer/15549945?hl=en).
- [Branding: suporte público, domínio e verificação](https://support.google.com/cloud/answer/15549049?hl=en).
