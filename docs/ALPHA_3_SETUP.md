# Alpha 0.9.4 — instalação e primeiro uso

Etapa 3 do plano alpha atualizado com a internacionalização futura em 51 idiomas. Instalador Inno Setup, aplicação C#/.NET 10 WinForms. Traduções e troca de tema continuam nas etapas próprias; tema claro inicial permanece a decisão explícita vigente, apesar da referência antiga a escuro no anexo v2.

## Instalação curta

Instalação por usuário no destino existente automático, preservando AppId, executável, armazenamento e integração. Removidas páginas obrigatórias de boas-vindas, licença, créditos separados e resumo pré-instalação; permanecem progresso e conclusão. Logo e créditos Zagotools / Open in Google / Swati K / MIT continuam no instalador. LICENSE e atribuição continuam incluídos e acessíveis em Sobre / MIT.

Ao instalar interativamente, executar `--first-use` automaticamente, sem checkbox adicional. Instalação silenciosa não abre essa UI. Atualização com configuração e autorização salvas encerra esse comando sem onboarding; não registra uma nova autorização nem modifica a conta. Se faltar configuração ou autorização, abrir o primeiro uso. O launcher também orienta primeiro uso antes de abrir planilha ainda não configurada; fechar sem conectar mantém o arquivo e impede upload.

Detecção usa cliente/pasta e tokens existentes, sem marker que apague histórico ou finja nova instalação. Refresh token salvo permite preservar a autorização mesmo com access token expirado; revogação remota só será detectada ao acessar Google. Configuração inválida é sinalizada sem reset automático.

## Primeiro uso e configurações

Tela única:

1. Escolher pasta local fora de OneDrive/rede e confirmar substituição com backup.
2. Salvar e conectar Google, com OAuth no navegador; cancelamento disponível durante conexão.
3. Abrir Aplicativos padrão do Windows, procurar ZagoSheetsWin, escolher formatos e voltar para concluir.

Não alterar UserChoice nem prometer associação automática. O botão apenas abre a tela oficial; a escolha efetiva ainda precisa de aceite manual Windows 11. O aplicativo não afirma que a associação foi realizada só porque abriu Configurações.

Avançado fica recolhido: cliente JSON, CSV/encoding/separador, formatos e preferência XLS. Sem cliente embutido de distribuição nesta alpha, o JSON próprio ainda é necessário: a tentativa de conectar explica isso, expande Avançado e foca Escolher JSON. Escolher o arquivo recolhe a área novamente. Nenhum segredo/login do mantenedor foi incorporado.

Pasta, cliente, interpretação de texto e escolha XLS já salvos são preservados. Campos de pasta/cliente existentes não permitem mudança acidental; migração continua separada. Confirmação de pasta já configurada não é repetida. CSV/TSV/XLS/ODS vêm habilitados para novas configurações, com ODS identificado como experimental; uma interpretação previamente registrada não é substituída. Preferência XLS continua alterável com botão próprio.

Sem serviço residente. Não implementa criação de nova planilha nem gestão automática de backups. O processo iniciado por duplo clique pode seguir com o arquivo solicitado somente após concluir conexão; fechar o primeiro uso incompleto não importa nada.

## Validação

Local: 153 testes aprovados / 30 exclusivos Windows ignorados, zero falhas; build WinForms sem warnings/erros. Regressões validam primeiro uso incompleto, instalação existente com refresh token e access token expirado, identidade incompatível, configuração inválida preservada e parsing do comando sem arquivo. CI verifica que instalação/atualização silenciosa não abre janela do launcher e conserva estado, preferências, ícone, atalhos e padrões do Windows.

Prévias nativas: home, configurações compactas, primeiro uso, configurações avançadas, recuperação e Sobre. CI, prévias e instalador ainda aguardando aprovação deste pacote. Prévias automatizadas são do DPI padrão do runner; DPI 125/150/200%, Google real e associação no Windows 11 permanecem para aceite manual. Não afirmar homologação dessas variantes pelo build.

## Internacionalização

Seção 8 de ALPHA_NEXT_v2.md incorporada ao plano do repositório sem regredir os resultados das etapas 1/2 ou o padrão claro já decidido. Matriz, seleção pelo Windows, scripts/RTL, catálogo, fallback inglês e integração dos 51 idiomas permanecem aprovados para planejamento. Auditoria inicial das superfícies em [LOCALIZATION_AUDIT.md](LOCALIZATION_AUDIT.md); recursos traduzidos não implementados nesta etapa.
