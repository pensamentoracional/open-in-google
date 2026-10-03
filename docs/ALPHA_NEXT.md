# Próxima evolução alpha — ZagoSheetsWin

Decisões e prioridades alinhadas com Fernando em 03/10/2026. Este documento descreve trabalho aprovado e propostas; não declara implementação nem homologação concluídas. Pacote aprovado: 0.9.2 (etapa 1); CI Windows/Linux e instalador aprovados. Teste Google real desta correção permanece pendente.

## Evidências do piloto real

Fernando confirmou XLSX e os demais formatos testados, exceto ODS. XLS conservou a fonte como projetado. Arquivos pequenos e um de aproximadamente 600 KB funcionaram. Reabertura após edição online, restauração e falhas reais ainda devem ter aceite específico. ODS não bloqueia a próxima entrega: identificar como experimental.

CSV fornecido para investigação: 1.315.517 bytes, UTF-8 com BOM, separador ponto e vírgula, 5.000 linhas e 40 colunas (200.000 células). O limite de 100.000 células impede processamento. Reproduzido no parser: a detecção automática mascara o limite e retorna erro de tabela irregular, exibido pela UI como configuração/conexão/arquivo aberto. Não publicar o CSV do usuário no repositório; usar dados sintéticos para regressão.

## 1. CSV, capacidade e diagnóstico

Implementada na versão 0.9.2; evidências e limites em [ALPHA_1_CSV.md](ALPHA_1_CSV.md).

- Corrigir a detecção de separador para não descartar erros de capacidade e não interpretar silenciosamente uma tabela como uma única coluna.
- Mensagens específicas para tamanho, linhas/células, encoding, separador, conversão e rede; explicar o que foi preservado.
- Elevar capacidade com leitura/normalização/conferência controladas, cancelamento e testes de memória/tempo. Objetivo: maior capacidade viável; não prometer ausência absoluta de limites nem retirar limites de recursos sem evidência.
- Provar o caso sintético 5.000 × 40, incluindo a conferência após exportação; impedir retirada se houver truncamento ou divergência.

## 2. XLS com substituição por padrão

- Opção de substituir XLS por atalho deve vir marcada por padrão e ser acessível nas configurações. Permitir desmarcar para importar cópia.
- Aplicar backup, conversão, conferência, publicação do atalho e retirada somente após sucesso; configurar a escolha uma vez, não a cada arquivo.
- Explicar macros incompatíveis e possíveis diferenças de fórmulas, vínculos e formatação. Não afirmar que macros são a única diferença possível.
- A decisão de padrão não elimina bloqueios de integridade, falhas, origem OneDrive/rede ou operações não verificáveis. Se for necessário conservar a fonte, informar o motivo claramente.

## 3. Instalação e primeiro uso

- Reduzir wizard: instalação padrão por usuário, destino automático e menos páginas obrigatórias. Manter logo, créditos e MIT visíveis/acessíveis sem páginas extras obrigatórias para cada conteúdo.
- Abrir primeiro uso ao concluir instalação interativa, sem abrir UI na instalação silenciosa/CI. Em atualização, considerar configuração existente para evitar repetir onboarding.
- Primeiro uso: conectar Google, escolher pasta e explicar substituição/backup. JSON OAuth deve migrar para área avançada quando houver cliente desktop de distribuição configurado pelo mantenedor; OAuth e o consentimento Google continuam necessários.
- Orientar escolha dos formatos em Aplicativos padrão do Windows, com botão para a tela oficial e instrução de retorno. Não forçar UserChoice nem prometer definição automática sem interação.

## 4. Interface mínima e processamento

- Tema: toggle discreto no canto direito do cabeçalho, usando os símbolos do template universal (sol `𖤓` e lua `☾`). Claro inicial; salvar a escolha localmente e aplicá-la às telas do programa. Incluir tooltip, nome acessível, teclado e respeito ao alto contraste do Windows. Validar a renderização dos símbolos no Windows 11.
- Tela inicial: Abrir planilha, Configurações, Backups; Sobre/MIT acessível de forma discreta. Identidade Zagotools preservada; ícones acompanhados de rótulo/tooltip acessível.
- Abrir planilha pelo seletor deve usar o fluxo de importação/substituição quando elegível; ação de importar cópia separada nas opções avançadas.
- Duplo clique abre somente progresso compacto com estado e cancelar. Sucesso encerra; erro explica causa e oferece recuperação/diagnóstico.
- Não instalar serviço residente apenas para esconder a janela. Medir cliques, latência até progresso, tempo de conversão/upload/conferência, pico de memória/CPU e término do processo.
- Criar nova planilha: proposta de funcionalidade futura. Escolher destino do atalho, criar Sheets nativo e registrar identidade remota; só publicar atalho após criação confirmada. Não faz parte do primeiro pacote de correções.

## 5. Atalhos com ícone próprio

- Os atalhos atuais são arquivos .url (InternetShortcut), não páginas HTML; implementar IconFile/IconIndex usando .ico fornecido por Fernando.
- Ícone fornecido por Fernando recebido e validado (16 a 256 pixels); integração e validação no Explorer ainda pendentes. Não substituir o logo por uma imagem presumida.
- Guardar ícone em caminho local persistente e estável para que atalhos sobrevivam a atualizações/desinstalação; não depender de arquivo temporário ou da pasta de programa removida.
- Preservar URL, codificação, nomes e publicação atômica. Validar Explorer Windows 11 e comportamento com cache de ícones; considerar atualização explícita dos atalhos existentes.

## 6. Gestão de backups

- O armazenamento cresce aproximadamente com os originais únicos preservados, mais metadados; o snapshot cobre a versão inicial, não as futuras edições online.
- Mostrar espaço ocupado, quantidade, data e ação Restaurar/Limpar. Limpeza deve distinguir backups concluídos de operações pendentes.
- Política aprovada: retenção padrão de 30 dias e quota padrão de 200 MB, configurável até no máximo 1 GB. Limpar os backups mais antigos elegíveis quando vencerem ou quando necessário para respeitar a quota. Mostrar os valores e explicar que a limpeza elimina a possibilidade de restaurar o original por esse backup.
- Não ativar expiração/exclusão automática retroativa sem escolha informada; nunca remover snapshot necessário a uma operação incompleta/ambígua ou recuperação em andamento. Coordenar limpeza com locks, bancos e journal.
- A limpeza preserva atalhos e arquivos do Google. Se não houver espaço para um backup obrigatório, conservar a fonte e explicar a falha.

## 7. OAuth de distribuição — proposta em definição

- Cliente OAuth desktop oficial identifica ZagoSheetsWin; não embutir login, senha ou tokens pessoais do mantenedor. Cada usuário autoriza com sua própria conta Google e os arquivos ficam no Drive desse usuário.
- Projeto Google Cloud sob controle do Zagotools; conta dedicada é recomendação organizacional, não requisito técnico. Configurar público externo, produção, identidade da marca, contato de suporte, privacidade e exigências aplicáveis do Google antes de distribuição pública.
- Manter permissões mínimas (`drive.file`) e separar projetos de teste e produção. O modo de teste tem restrições de usuários e duração da autorização, incompatíveis com distribuição cotidiana.
- JSON próprio pode ficar opcional em Configurações avançadas para instalações que precisam controlar o próprio projeto, desenvolvimento e forks. Não integra o primeiro uso comum nem muda a conta Google do usuário. A necessidade de manter essa opção ainda será decidida com Fernando.
- Troca de cliente exige tratar reautorização e preservar histórico de operações, atalhos e backups. Cliente oficial ainda não provisionado nem incorporado ao instalador.

## Ordem e critérios de entrega

1. CSV/diagnóstico/capacidade, com testes de regressão e medidas de desempenho.
2. XLS padrão configurável com conferência e backup; atalhos com ícone após receber .ico.
3. Wizard, primeiro uso e telas mínimas; prévias nativas em DPI e teste de associação no Windows 11.
4. Gestão de backup com os padrões aprovados; criação de nova planilha em incremento separado.

Manter versão alpha e distribuir pacote novo com evidências Windows/Linux/instalador. Não marcar aceite manual restante como concluído por inferência do relato.
