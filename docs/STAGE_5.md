# Etapa 5 — launcher e integração Windows

Branch feature/windows-integration, derivada da fase 4. Novo projeto SheetsWindows.Windows, WinForms/WinExe, executável SheetsWindows.exe. Manifesto asInvoker: registro por usuário, sem elevação. LongPathAware declarado. O app recebe um caminho XLSX absoluto pelo Explorer, executa importação/substituição e fecha após solicitar o navegador. Atalho .url continua abrindo diretamente o navegador, sem executar este app.

## Entrada e experiência

Comando de associação: `"C:\pasta estável\SheetsWindows.exe" --open "%1"`. As duas partes variáveis são tratadas como argumentos literais, sem cmd.exe, PowerShell ou expansão de curingas. Nomes de arquivo com espaços, Unicode, colchetes, & e % são mantidos. A instalação deve estar num disco local fixo, sem reparse points. Caminho do executável precisa ter nome SheetsWindows.exe, ser absoluto e não conter aspas, controles ou %, para não se confundir com placeholders do shell. Uma ativação aceita somente um XLSX; formatos posteriores e .url não são registrados.

O launcher aceita também um caminho sozinho, para escolha manual do .exe em Abrir com, e `--login`/`--defaults`. `--version` permite verificar inicialização sem interface ou efeitos. Sem argumentos, mostra uma janela com Autorizar Google, Escolher aplicativo padrão e Fechar. O progresso é pequeno; Cancelar/fechar pede cancelamento e aguarda a interrupção segura. Erros são visíveis e sanitizados, sem tokens ou conteúdo de exceções. Não há confirmação por arquivo após a configuração inicial. A tela de onboarding completa pertence à fase 6.

WindowsLauncher compõe os componentes das fases anteriores diretamente, sem iniciar o CLI como subprocesso. Antes de qualquer upload, exige configuração de cliente e política de pasta, e verifica elegibilidade do arquivo. OAuth ausente/revogado pede Autorizar Google; não repete upload cegamente nem inicia novo login silenciosamente. Falha de browser mantém o original e uma nova abertura retoma a associação existente. As regras de XLSX até 5 MiB, backup privado, comparação de identidade/hash, nuvem e colisões continuam válidas.

## Registro como candidato

Somente HKCU; nenhuma escrita em HKLM ou UserChoice, e nenhuma troca do valor padrão de .xlsx. O usuário escolhe o padrão nas Configurações do Windows. O registro usa:

| Entrada | Finalidade |
|---|---|
| Software/SheetsWindows/Integration/Capabilities | Nome, descrição, ícone e associação .xlsx |
| Software/RegisteredApplications, valor Sheets Windows | Descoberta em Aplicativos padrão |
| Software/Classes/SheetsWindows.Xlsx | ProgID próprio e comando de abertura |
| Software/Classes/Applications/SheetsWindows.exe | Nome amigável, comando e SupportedTypes |
| Software/Classes/.xlsx/OpenWithProgids, valor SheetsWindows.Xlsx | Candidato em Abrir com |

Após registrar/remover, SHChangeNotify(SHCNE_ASSOCCHANGED) invalida o cache de associações. A janela abre `ms-settings:defaultapps?registeredAppUser=Sheets%20Windows`; em versões que não navegam diretamente à página, procurar o aplicativo manualmente.

Registro tem marcador de propriedade e verificação prévia de colisões. Repetir no mesmo local é idempotente e pode terminar registro parcial. Valores modificados por terceiros bloqueiam a atualização. Mover o executável exige remover o registro anterior primeiro; atualizar os arquivos no mesmo local é o caminho para upgrades futuros. Escritas de Registry não são uma transação única: se houver falha, repetir registro no mesmo local. CLI serializa registro/removal pelo lock de integração.

Remoção elimina apenas valores ainda iguais aos registrados e limpa somente chaves conhecidas vazias. Preserva comandos modificados, valores/chaves alheios, escolha do usuário, JSON de configuração, tokens, banco, backups, atalhos e documentos Google. Remover o candidato que era padrão pode exigir que o usuário escolha outro aplicativo no Windows; não restauramos um padrão antigo por manipulação de UserChoice.

## Configurar o piloto

Ainda é preparação de desenvolvimento: SDK .NET 10.0.401 para os comandos de configuração; o launcher publicado inclui runtime e não exige SDK para abrir planilhas. Não há instalador, assinatura de distribuição nem suporte ARM64 nesta fase. A publicação é win-x64; não mover o executável após registrar.

1. Obter o JSON OAuth Desktop de seu projeto conforme STAGE_3.md. Não colocá-lo no Git.
2. No run aprovado do GitHub Actions, baixar o artifact SheetsWindows-win-x64 e extrair **todo** seu conteúdo em `%LOCALAPPDATA%\Programs\SheetsWindows`. Não copiar somente o .exe: dependências nativas e runtime precisam permanecer juntos.
3. Usar a branch feature/windows-integration para configuração, e uma pasta particular, local, sem sincronizadores, contendo cópias de teste.

```powershell
$exe = "$env:LOCALAPPDATA\Programs\SheetsWindows\SheetsWindows.exe"
$client = "C:\seguro\desktop-client.json"
dotnet run --project src/SheetsWindows.Cli -- configure-replacement $client "C:\PilotoSheets"
dotnet run --project src/SheetsWindows.Cli -- configure-launcher $client $exe
& $exe --login
# Após concluir o consentimento:
& $exe --defaults
```

Se a pasta de substituição da fase 4 já foi configurada, não repetir configure-replacement. configure-launcher copia atomicamente o JSON desktop para LOCALAPPDATA/SheetsWindows/launcher-client.json, sob ACL privada. Esse arquivo contém configuração do cliente, não tokens; os tokens continuam em DPAPI. Um client_id diferente é rejeitado para não trocar o namespace de conta. Login anterior do CLI com o mesmo cliente continua válido.

Nas Configurações > Aplicativos > Aplicativos padrão, procurar Sheets Windows e associar .xlsx. Alternativa: botão direito em um XLSX > Abrir com > Escolher outro aplicativo > Sheets Windows; a escolha de usar sempre depende da interface da versão do Windows. Não alterar padrão via scripts de hash/UserChoice.

Depois, dar dois cliques no XLSX de teste. Deve abrir Sheets, conservar backup e substituir o XLSX por .url na mesma pasta. Editar online e abrir o .url novamente deve acessar o mesmo ID, sem upload. Em erro de autorização, abrir SheetsWindows.exe, Autorizar Google, e repetir o XLSX. Se houver conflito de conteúdo/identidade ou reconciliação remota pendente, seguir STAGE_4.md; não apagar banco nem criar um upload manual duplicado.

Para remover somente o candidato:

```powershell
dotnet run --project src/SheetsWindows.Cli -- unregister-windows $client $exe
```

## Validação e limites

Testes portáveis: parser, opções, rejeição de caminhos relativos/URLs/múltiplos arquivos, plano restrito a XLSX, configuração limitada e namespace, erro sanitizado e ausência de configuração antes de rede/efeitos. Testes Windows usam uma subárvore HKCU temporária, sem trocar associações reais: registro idempotente, preservação de defaults/terceiros, colisões, retomada parcial, remoção seletiva e CommandLineToArgvW para nomes literais. Testes compostos do launcher usam DPAPI real e HTTP Google simulado, conferindo importação, retirada, backup e retomada sem novo POST após browser recusado.

O CI compila também o projeto WinForms no Linux com EnableWindowsTargeting, mas não executa sua interface nesse ambiente. Windows publica o pacote self-contained win-x64, verifica runtime, SQLite e subsistema PE de GUI e executa --version antes de disponibilizar artifact. O CI não escolhe um app padrão real e não simula cliques no Explorer ou avalia layout visual da janela. O piloto no Windows 11 com configuração, Explorer, Google real e fidelidade de conversão continua pendente; não confundir testes simulados com esse aceite.

Evidências de compilação, testes e pacote serão registradas após CI.

## Fontes oficiais

- https://learn.microsoft.com/en-us/windows/win32/shell/default-programs
- https://learn.microsoft.com/en-us/windows/win32/shell/app-registration
- https://learn.microsoft.com/en-us/windows/win32/shell/how-to-include-an-application-on-the-open-with-dialog-box
- https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-default-apps-settings
- https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify
