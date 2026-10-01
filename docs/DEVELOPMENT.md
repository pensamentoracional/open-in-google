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
