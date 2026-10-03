# ZagoSheetsWin — textos de ajuda e primeiro uso

Versão para revisão, baseada nas oito imagens enviadas.

Redação com a naturalidade do Evair e princípios de linguagem clara inspirados no ASD: frases curtas, termos consistentes e condições explícitas. Adaptação em português, sem alegação de conformidade formal com ASD-STE100.

Os títulos, legendas, textos e rótulos abaixo são propostas para a interface. Os separadores `---` indicam divisões visuais dentro de cada tela.

## 1. Tutorial — Suas planilhas no Google Sheets

**Imagem:** image(4).png · Página 1 de 4

### Título

Abra suas planilhas no Google Sheets

### Legendas da ilustração

Arquivo no computador → Conferir importação → Atalho para a planilha

### Texto

A ideia é simples: você abre um arquivo de planilha, e o ZagoSheetsWin importa esse arquivo para o Google Sheets.

Depois, você continua trabalhando na planilha pelo navegador.

---

No lugar do arquivo original, o aplicativo cria um atalho para a planilha no Google Sheets.

O original só é retirado depois das verificações e da criação de um backup no computador.

Se o aplicativo não conseguir conferir a conversão, ele mantém o original.

### Controles

- Checkbox: **Não mostrar este tutorial ao abrir o aplicativo**
- Botões: **Pular tutorial** · **Voltar** · **Próximo**
- Indicador: **1 de 4**

## 2. Tutorial — Conta Google

**Imagem:** image(5).png · Página 2 de 4

### Título

Conecte sua conta Google

### Legendas da ilustração

ZagoSheetsWin → Autorizar no navegador → Seu Google Drive

### Texto

Você usa sua própria conta Google. As planilhas importadas ficam no Google Drive dessa conta.

---

Nas configurações, escolha a pasta local de planilhas e confirme a substituição do original por um atalho, com backup.

Depois, clique em **Salvar e conectar Google**.

---

O navegador vai abrir para você escolher a conta e autorizar o acesso.

Quando terminar, volte ao ZagoSheetsWin para continuar.

### Controles

- Checkbox: **Não mostrar este tutorial ao abrir o aplicativo**
- Botões: **Pular tutorial** · **Voltar** · **Próximo**
- Indicador: **2 de 4**

## 3. Tutorial — Como abrir os arquivos

**Imagem:** image(6).png · Página 3 de 4

### Título

Escolha como abrir suas planilhas

### Legendas da ilustração

Arquivo de planilha → Abrir com ZagoSheetsWin → Google Sheets

### Texto

Quer abrir uma planilha com dois cliques?

Nos **Aplicativos padrão** do Windows, procure o ZagoSheetsWin e escolha o aplicativo para os formatos **CSV, XLS e XLSX**.

---

Essa escolha é opcional.

Você também pode clicar com o botão direito no arquivo e escolher **Abrir com → ZagoSheetsWin**.

Ou usar **Abrir planilha** dentro do aplicativo.

---

Também há suporte a **TSV**. O formato **ODS** ainda é experimental.

### Controles

- Checkbox: **Não mostrar este tutorial ao abrir o aplicativo**
- Botões: **Pular tutorial** · **Voltar** · **Próximo**
- Indicador: **3 de 4**

## 4. Tutorial — Backup e recuperação

**Imagem:** image(7).png · Página 4 de 4

### Título

Guarde o original. Saiba como recuperar.

### Legendas da ilustração

Arquivo original → Backup no computador → Restaurar arquivo

### Texto

O backup guarda uma cópia completa do arquivo original.

Para recuperar essa cópia, abra **Backups** no aplicativo.

O backup não inclui as alterações que você fizer depois no Google Sheets.

---

A configuração padrão é de **30 dias** e **200 MB**. O limite de espaço pode ser ajustado até **1 GB**.

A limpeza automática só funciona quando você a ativa. Ela não apaga os arquivos protegidos para recuperação.

---

Na conversão, fórmulas e formatação podem mudar. Macros de arquivos XLS não funcionam no Google Sheets.

### Controles

- Checkbox: **Não mostrar este tutorial ao abrir o aplicativo**
- Botões: **Pular tutorial** · **Voltar** · **Começar**
- Indicador: **4 de 4**

## 5. Configurações — Primeiro uso

**Imagem:** image(8).png

### Introdução

Vamos preparar o aplicativo para abrir suas planilhas no Google Sheets.

Após importar e conferir a planilha, o ZagoSheetsWin substitui o arquivo original por um atalho.

Antes de retirar o original, o aplicativo guarda um backup no computador. Essa cópia não inclui as alterações feitas depois no Google Sheets.

---

### Etapa 1 — Escolha a pasta de planilhas

Escolha uma pasta no próprio computador, fora do OneDrive e de pastas de rede.

**Botão:** Escolher pasta…

**Checkbox:** Confirmo que a pasta é local e aceito substituir o original por um atalho, com backup.

**Botão de expansão:** Opções avançadas ▸

---

### Etapa 2 — Conecte sua conta Google

**Texto quando a conta ainda não está conectada:**

Clique em **Salvar e conectar Google**. No navegador, escolha sua conta e autorize o acesso.

As planilhas importadas ficam no Google Drive dessa conta.

Depois da autorização, volte ao aplicativo.

**Texto quando a conta já está conectada, como na imagem:**

Sua conta Google já está conectada. Mantivemos sua configuração.

As planilhas importadas ficam no Google Drive dessa conta.

**Botões:**

- Salvar e conectar Google
- Salvar configurações

---

### Etapa 3 — Quer abrir com dois cliques?

Abra os **Aplicativos padrão** do Windows e procure o ZagoSheetsWin.

Escolha o aplicativo para abrir arquivos **CSV, XLS e XLSX**. Depois, volte aqui.

**Botão:** Abrir aplicativos padrão do Windows

---

Essa escolha é opcional. Você também pode usar **Abrir com → ZagoSheetsWin**, no botão direito do arquivo, ou **Abrir planilha**, dentro do aplicativo.

Também há suporte a **TSV**. O formato **ODS** ainda é experimental.

**Botão final:** Fechar

## 6. Configurações — Opções avançadas

**Imagem:** image(9).png

### Botão de expansão

Opções avançadas ▾

### Conexão Google

O aplicativo já inclui a configuração necessária para conectar ao Google. Cada pessoa entra com sua própria conta e autoriza o acesso.

Um arquivo JSON próprio é opcional. Ele serve para quem quer usar uma configuração de conexão diferente.

Se você já configurou essa conexão antes, o aplicativo mantém sua escolha.

**Botão:** Escolher arquivo JSON de conexão…

**Status quando já existe uma configuração:** Configuração de conexão existente mantida.

---

### Outros formatos de planilha

**Checkbox:** Habilitar CSV, TSV, XLS e ODS (experimental).

**Seletor de codificação:** Codificação do texto: UTF-8 / UTF-16 com BOM

**Seletor de separador:** CSV: detectar separador automaticamente

O limite é de **20 MiB por arquivo**.

Para CSV e TSV, os limites são de **500 mil células**, **50 mil linhas** e **mil colunas**.

O conteúdo de CSV e TSV é tratado como texto literal.

Se o aplicativo não conseguir conferir a conversão de um arquivo ODS, ele mantém o original.

As preferências de texto que você já salvou são mantidas.

---

### O que fazer com arquivos XLS

**Checkbox:** Substituir o arquivo XLS por um atalho após as verificações.

Com essa opção marcada, o aplicativo guarda um backup do original antes de substituí-lo pelo atalho.

Se quiser importar como cópia e manter o XLS no lugar, desmarque a opção.

---

Macros não funcionam no Google Sheets. Fórmulas, vínculos e formatação podem mudar na conversão.

O backup guarda o arquivo original completo.

**Botão:** Salvar escolha para arquivos XLS

## 7. Backups — Recuperação e limpeza

**Imagem:** image(20261003-203017).png

### Título da janela

Recuperar arquivos e gerenciar backups — ZagoSheetsWin

### Introdução

Precisa recuperar o original? Selecione um backup e clique em **Restaurar em…** para escolher onde salvar a cópia.

A restauração funciona sem internet. O aplicativo confere o backup e não substitui arquivos que já existem.

A planilha no Google Sheets e o atalho continuam disponíveis.

---

### Regras de armazenamento

**Campo de dias:** Prazo dos backups (dias)

**Campo de espaço:** Limite de espaço (MB)

**Checkbox:** Limpar automaticamente os backups de operações concluídas

**Botão:** Salvar regras

Com a limpeza automática ativada, o aplicativo remove backups de operações concluídas conforme o prazo e o limite de espaço.

Os backups protegidos para recuperação são mantidos.

### Resumo de uso

**Modelo:** Espaço usado: {uso} MB · Limite: {limite} MB · Backups: {total} · Protegidos: {protegidos}

**Exemplo da imagem:** Espaço usado: 4,72 MB · Limite: 200 MB · Backups: 12 · Protegidos: 4

---

### Lista de backups

**Título:** Escolha um backup

**Estado “concluído”:** Operação concluída

**Estado “protegido / pendente”:** Operação pendente · Backup protegido

O backup guarda o arquivo original. As alterações feitas depois no Google Sheets não entram nessa cópia.

---

### Ações para operações pendentes

**Botão:** Retomar como cópia

**Botão:** Concluir substituição

### Diagnóstico e limpeza

**Botão:** Exportar diagnóstico…

**Botão:** Apagar backup selecionado…

**Botão:** Limpar backups vencidos ou acima do limite…

### Restaurar o original

**Botão principal:** Restaurar em…

Selecione o backup que quer recuperar. Depois, escolha onde salvar o arquivo restaurado.

## 8. Tela principal

**Imagem:** image(20261003-203320).png

### Título

ZagoSheetsWin

### Texto de apresentação

Abra uma planilha do computador e continue no Google Sheets.

Depois de importar e conferir, o aplicativo guarda um backup do original e cria um atalho no lugar do arquivo.

### Ação principal

**Botão:** Abrir planilha…

---

### Configuração, recuperação e ajuda

**Botão:** Configurações

**Botão:** Backups

**Botão:** Ajuda — como funciona

---

### Opções avançadas

**Botão de expansão:** Opções avançadas ▾

**Texto de apoio:** Quer manter o arquivo original no lugar? Importe como cópia.

**Botão:** Importar como cópia…

**Botão:** Abrir pasta de atalhos das cópias

### Cabeçalho

**Link:** Sobre / MIT

**Descrição acessível do controle de tema:** Alternar entre tema claro e escuro

## Notas para aplicação dos textos

- Os separadores dentro de cada tela representam linhas visuais, com espaço acima e abaixo.
- Mantenha os mesmos rótulos nos botões e nas instruções que mencionam esses botões.
- “Arquivo original” é o arquivo no computador. “Planilha no Google Sheets” é o resultado importado. “Backup” é a cópia local do original.
- O checkbox do tutorial começa desmarcado. A escolha de não mostrar novamente só é salva quando o usuário marcar a opção. O tutorial continua disponível em Ajuda.
- Preserve a diferença entre **MB**, apresentada na configuração de backups, e **MiB**, apresentada no limite de importação.
- Confira a altura disponível nas telas antes de aplicar: os novos parágrafos precisam de mais espaço vertical. Evite reduzir a fonte para acomodar o texto.
