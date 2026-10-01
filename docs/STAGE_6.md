# Fase 6 — Piloto instalável

Branch: `feature/installable-pilot`. Aplicativo 0.6, Windows x64, instalação por usuário em `%LOCALAPPDATA%\Programs\SheetsWindows`, sem administrador. O pacote inclui o runtime .NET; não instalar .NET separadamente.

## Primeiro uso

1. Baixar o artefato **SheetsWindows-Setup-win-x64** do workflow desta branch, extrair o ZIP e executar `SheetsWindows-Setup-win-x64.exe`.
2. Abrir **Sheets Windows** no menu Iniciar e clicar em **Configurar piloto**.
3. Escolher o JSON OAuth de **aplicativo desktop** do próprio projeto Google, com Drive API habilitada e acesso OAuth de teste configurado. O instalador não fornece credenciais Google. Veja STAGE_3.md para preparação do projeto. O JSON é copiado para a área privada do usuário.
4. Escolher uma pasta dedicada em disco local, fora de OneDrive, rede e outros sincronizadores. Confirmar uma única vez a declaração e o comportamento de conversão. O aplicativo bloqueia redirecionamentos e raízes OneDrive conhecidas, mas não detecta todos os sincronizadores: a declaração continua necessária.
5. Salvar, fechar a configuração e clicar em **Autorizar Google**. Depois clicar em **Escolher aplicativo padrão** e escolher Sheets Windows para XLSX na interface oficial do Windows.
6. Fechar o Excel e abrir um XLSX elegível dessa pasta. O aplicativo importa, verifica o Sheets e o backup, publica `.url`, solicita abertura do navegador e retira o mesmo original por handle. O atalho abre diretamente o navegador, sem novos uploads.

MVP: XLSX até 5 MiB e sem VBA. Recursos Excel podem perder fidelidade na conversão. O backup é do original inicial, não das futuras edições online; não há sincronização bidirecional. A configuração preserva cliente/pasta previamente registrados e pode retomar uma configuração parcial. Mudanças de cliente ou pasta precisam de migração específica; não apague o estado para contornar conflitos.

Se um arquivo for aberto antes da configuração, ele permanece local: configurar e abrir novamente. A autorização não ocorre silenciosamente durante a abertura de uma planilha.

## Restaurar offline

Abrir **Restaurar backups** no menu Iniciar ou na tela principal, selecionar a operação e escolher um novo nome `.xlsx`. O programa verifica o hash do backup comprometido no registro e não sobrescreve destinos existentes, mesmo se a caixa de diálogo perguntar sobre sobrescrita. Tokens Google e internet não são necessários. O backup, o Sheets e o atalho permanecem intactos. A lista exibe o ID para diagnóstico; a retomada avançada por ID continua disponível na CLI conforme STAGE_4.md.

A restauração recupera o snapshot inicial. Para recuperar edições online, exportar o documento pelo Google; esse fluxo é independente do backup local.

## Atualizar e desinstalar

Executar novamente o instalador atualiza os arquivos do programa no mesmo diretório. Fechar o app antes. A associação registrada usa o mesmo caminho; a escolha do padrão pertence ao usuário.

Usar **Aplicativos instalados** ou **Desinstalar Sheets Windows**. A manutenção remove somente a associação pertencente ao aplicativo e os arquivos registrados pelo instalador. Preserva `%LOCALAPPDATA%\SheetsWindows` inteiro: bancos, journal, configuração, tokens protegidos e backups. Preserva atalhos nas pastas de documentos e nunca chama exclusão de arquivos Google. Não altera UserChoice nem padrões de outros aplicativos. Reinstalar com o mesmo usuário permite acessar backups e configuração existentes.

Se já utilizava o pacote portátil, desregistre sua integração pela CLI da fase 5 antes de instalar em outro caminho. Conflitos de propriedade bloqueiam a manutenção em vez de sobrescrever registros alheios. O diretório do instalador é fixo; caminhos arbitrários não são aceitos. Não mover o programa instalado manualmente.

## Validação e limites

Testes automatizados cobrem declaração obrigatória, configuração idempotente, preservação de configuração anterior, lista de backups sem criar estado, restauração offline, recusa de sobrescrita e corrupção. O workflow Windows compila Inno Setup e executa instalação silenciosa, atualização, desinstalação e reinstalação em perfil descartável, verificando preservação de estado, snapshot, atalho e escolhas padrão.

Os testes de Google usam HTTP simulado; a UI e a seleção de padrão no Explorer precisam de aceite manual. **O gate “uso completo no Windows 11 com Google real” continua pendente.** O instalador piloto não está assinado digitalmente; assinatura e distribuição estável são trabalho posterior. Evidências de CI serão registradas após a execução.

## Aceite manual no Windows 11

- Instalar sem administrador, concluir configuração e OAuth; selecionar o padrão pelos Ajustes.
- Importar um XLSX descartável, verificar backup e atalho, editar online e reabrir o atalho sem upload.
- Restaurar offline para outro nome e conferir os bytes originais; testar destino ocupado.
- Desinstalar: abrir o atalho e o Sheets; reinstalar e restaurar o backup conservado.
- Testar cancelamento OAuth, arquivo bloqueado e pasta sincronizada: original preservado.
