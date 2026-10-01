# Desenvolvimento e preparação do fork

Base upstream e histórico preservados. Branch atual docs/foundation; main permanece na revisão original. Não criar develop antes de precisar de uma linha de integração adicional. Feature branches saem da linha do produto; upstream/* devem partir do upstream ou conter somente mudanças independentes.

## Fork remoto confirmado

Fork: https://github.com/pensamentoracional/open-in-google. A conta e a integração têm acesso de escrita ao fork após a seleção do repositório na instalação GitHub. Origin aponta para esse fork; upstream aponta para SwatiK425/open-in-google e mantém push bloqueado. Branch de documentação: docs/foundation. Main ainda preserva a base upstream.

```bash
git remote add origin https://github.com/pensamentoracional/open-in-google.git
git push -u origin docs/foundation
```

## Recuperar pacote

Extrair o ZIP da etapa 1 em uma pasta nova. Ele contém sheets-windows.bundle e working-tree/. O bundle contém histórico e branches, sem tokens.

```bash
git clone sheets-windows.bundle sheets-windows
cd sheets-windows
git remote remove origin
git remote add upstream https://github.com/SwatiK425/open-in-google.git
git config remote.upstream.pushurl DISABLED
git switch docs/foundation
```

Após recuperar o pacote, adicionar origin usando a URL do fork confirmado acima. O bundle não transporta configuração de remotes; estes comandos a reconstituem.

## Sincronização

```bash
git fetch upstream
git log main..upstream/main --oneline
```

Comparar com a base auditada, revisar alterações e escolher merge/rebase/cherry-pick. Não atualizar automaticamente. Ao promover documentação ao main do fork, main deixa de ser espelho puro; a revisão auditada permanece registrada por SHA.

## Gates antes de implementar

Escolher versão suportada de .NET e SDKs verificando documentação oficial; preparar execução de testes no Windows 11; validar restauração de backups; especificar fidelidade XLSX e reconciliação de criação remota. Não instalar/rodar os scripts upstream como produto seguro. Nenhum teste de comportamento Windows/Drive foi executado nesta etapa.

Publicação realizada via plugin GitHub na branch docs/foundation após liberar o fork na instalação. Main permanece na revisão upstream. O histórico dos commits locais está preservado no pacote Git entregue; a publicação via API consolida a documentação em um commit derivado da base upstream.

## Compilar e testar o núcleo

Instalar SDK .NET 10.0.401 (global.json). Executar `dotnet restore SheetsWindows.slnx --locked-mode` e `dotnet test SheetsWindows.slnx --configuration Release --no-restore`. A solução não executa os scripts PowerShell herdados nem realiza chamadas Google. Ver detalhes em STAGE_2.md.

## Etapa 4

Branch feature/shortcut-replacement deriva de feature/google-import. Fluxo CLI, configuração de pasta local e recuperação em STAGE_4.md. Main não foi alterada.

## Etapa 5

Branch feature/windows-integration deriva da fase 4. Novo projeto WinForms net10.0-windows com EnableWindowsTargeting para compilar também no Linux. Publish Windows self-contained win-x64 pelo CI; instruções de registro e piloto em STAGE_5.md. O runtime de UI não é executado no Linux.

## Etapa 6

Branch feature/installable-pilot deriva da fase 5. `--setup`, `--recovery`, `--register` e `--unregister` são entradas explícitas do executável. A manutenção usa o lock de registro e não lê bancos/OAuth. `installer/SheetsWindows.iss` empacota a publicação self-contained com Inno Setup 6; o CI Windows compila e valida o ciclo completo em um perfil descartável com `installer/Test-Lifecycle.ps1`. Não executar esse teste contra uma instalação pessoal. Guia de uso: STAGE_6.md.

## Etapa 7

Branch feature/formats-environments deriva da fase 6. ExcelDataReader 3.9.0 fixado, licença em third-party. `--copy` permite origem de rede/nuvem hidratada sem escrita nela. CI acrescenta comparação de formatos e teste SMB de leitura apenas em perfil Windows descartável. A retirada dos novos formatos exige IConversionVerifier; sem ela o coordinator falha fechado. Instruções e gates: STAGE_7.md.
