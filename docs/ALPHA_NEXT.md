# Próxima evolução alpha — ZagoSheetsWin

Decisões e prioridades alinhadas com Fernando em 03/10/2026. Este documento descreve trabalho aprovado e propostas; não declara implementação nem homologação concluídas. Pacote aprovado: 0.9.6 (etapas 1 a 6; ícones integrados desde 0.9.3); CI Windows/Linux, instalador e prévias nativas aprovados. Teste Google real desta correção permanece pendente.

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

Implementada em 0.9.3, com ícone dos novos atalhos; ver [ALPHA_2_XLS.md](ALPHA_2_XLS.md). CI e instalador aprovados; aceite real pendente.

- Opção de substituir XLS por atalho deve vir marcada por padrão e ser acessível nas configurações. Permitir desmarcar para importar cópia.
- Aplicar backup, conversão, conferência, publicação do atalho e retirada somente após sucesso; configurar a escolha uma vez, não a cada arquivo.
- Explicar macros incompatíveis e possíveis diferenças de fórmulas, vínculos e formatação. Não afirmar que macros são a única diferença possível.
- A decisão de padrão não elimina bloqueios de integridade, falhas, origem OneDrive/rede ou operações não verificáveis. Se for necessário conservar a fonte, informar o motivo claramente.

## 3. Instalação e primeiro uso

Implementada em 0.9.4; ver [ALPHA_3_SETUP.md](ALPHA_3_SETUP.md). CI 37097216126, instalador e prévias nativas aprovados; aceite manual Windows 11/Google real pendente.

- Reduzir wizard: instalação padrão por usuário, destino automático e menos páginas obrigatórias. Manter logo, créditos e MIT visíveis/acessíveis sem páginas extras obrigatórias para cada conteúdo.
- Abrir primeiro uso ao concluir instalação interativa, sem abrir UI na instalação silenciosa/CI. Em atualização, considerar configuração existente para evitar repetir onboarding.
- Primeiro uso: conectar Google, escolher pasta e explicar substituição/backup. JSON OAuth deve migrar para área avançada quando houver cliente desktop de distribuição configurado pelo mantenedor; OAuth e o consentimento Google continuam necessários.
- Orientar escolha dos formatos em Aplicativos padrão do Windows, com botão para a tela oficial e instrução de retorno. Não forçar UserChoice nem prometer definição automática sem interação.

## 4. Interface mínima e processamento

Implementada em 0.9.5; ver [ALPHA_4_UI.md](ALPHA_4_UI.md). CI 37101501901, interface nativa, 15 prévias e instalador aprovados; aceite manual Windows 11/Google real pendente.

- Tema: toggle discreto no canto direito do cabeçalho, usando os símbolos do template universal (sol `𖤓` e lua `☾`). Claro inicial; salvar a escolha localmente e aplicá-la às telas do programa. Incluir tooltip, nome acessível, teclado e respeito ao alto contraste do Windows. Validar a renderização dos símbolos no Windows 11.
- Tela inicial: Abrir planilha, Configurações, Backups; Sobre/MIT acessível de forma discreta. Identidade Zagotools preservada; ícones acompanhados de rótulo/tooltip acessível.
- Abrir planilha pelo seletor deve usar o fluxo de importação/substituição quando elegível; ação de importar cópia separada nas opções avançadas.
- Duplo clique abre somente progresso compacto com estado e cancelar. Sucesso encerra; erro explica causa e oferece recuperação/diagnóstico.
- Não instalar serviço residente apenas para esconder a janela. Medir cliques, latência até progresso, tempo de conversão/upload/conferência, pico de memória/CPU e término do processo.
- Criar nova planilha: proposta de funcionalidade futura. Escolher destino do atalho, criar Sheets nativo e registrar identidade remota; só publicar atalho após criação confirmada. Não faz parte do primeiro pacote de correções.

## 5. Atalhos com ícone próprio

- Implementado desde 0.9.3: atalhos .url (InternetShortcut), com IconFile/IconIndex usando o .ico fornecido por Fernando.
- Ícone fornecido por Fernando recebido e validado (16 a 256 pixels); integrado aos novos atalhos na 0.9.3; aparência/cache no Explorer ainda aguarda aceite manual. Não substituir o logo por uma imagem presumida.
- Guardar ícone em caminho local persistente e estável para que atalhos sobrevivam a atualizações/desinstalação; não depender de arquivo temporário ou da pasta de programa removida.
- Preservar URL, codificação, nomes e publicação atômica. Validar Explorer Windows 11 e comportamento com cache de ícones; considerar atualização explícita dos atalhos existentes.

## 6. Gestão de backups

Implementada em 0.9.6; CI 37119038565, interface e instalador aprovados. Aceite manual final pendente. Ver [ALPHA_6_BACKUPS.md](ALPHA_6_BACKUPS.md).

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

## 8. Internacionalização — instalador e interface em 51 idiomas

Etapa adicionada em 03/10/2026. Escopo aprovado para planejamento; traduções, integração e homologação ainda pendentes. Fontes canônicas: `T51-00_PRINCIPAL.md`, `i18n-51-locales-universal-template.json` e `i18n-51-locales-universal-guide.md`. O template contém a matriz e os contratos, mas seus packs não contêm traduções do ZagoSheetsWin.

### Matriz, ordem e variantes

- Preservar os 51 packs, nomes nativos, códigos, direção, scripts e variantes definidos no template. Ordem editorial e do seletor: `en, zh, hi, es, ar, fr, bn, pt, id, ur, ru, de, ja, vi, tr, ko, it, th, fil, ms, sw, pcm, mr, te, ha, pa, ta, yue, fa, am, jv, gu, kn, yo, bho, ps, or, my, ml, pl, su, mai, uk, om, uz, sd, ne, ig, az, nl, gn`.
- A ordem é editorial e fixa para repetibilidade; não representa uma classificação exata de falantes nem determina o idioma selecionado automaticamente.
- Preservar a distinção entre código interno, locale e badge: Filipino usa `fil`, `fil-PH` e badge `TL`; a lista editorial usa `FIL`.
- Adotar português brasileiro como catálogo-fonte proposto, a confirmar na auditoria das strings existentes: um pack-fonte e 50 alvos se confirmado. Inglês será o fallback, independentemente da língua-fonte.
- O pack `pt` usa `pt-BR`; não declarar suporte a português europeu. Mandarim simplificado (`zh-CN`) e cantonês tradicional (`yue-Hant-HK`) são idiomas distintos. Preservar Punjabi Gurmukhi, Sindhi árabe e Uzbeque/Azerbaijano latinos; variantes adicionais exigem expansão explícita da matriz.

### Identificação automática e preferência

- Resolver a divergência entre o JSON e o documento principal adotando: escolha explícita atual > preferência persistida > idiomas de interface preferidos do usuário no Windows > inglês. Adaptar a referência universal ao navegador para o ambiente Windows; não usar IP, país, teclado ou conta Google como identificação do idioma.
- Oferecer modo `Automático — seguir o Windows` e escolha de idioma específico. Persistir o modo e, quando fixo, o pack escolhido em configuração por usuário; não transformar detecção automática em preferência fixa.
- No primeiro uso, sugerir o idioma compatível do Windows e permitir troca antes de continuar. Transferir ao aplicativo a escolha manual feita no instalador. Atualizações preservam a preferência atual do aplicativo e não repetem onboarding nem redefinem idioma silenciosamente.
- Mapear variantes regionais compatíveis, como `en-US`/`en-GB` para `en`, `es-ES`/`es-MX` para `es` e `pt-BR`/`pt-PT` para o único pack `pt`, identificado como brasileiro.
- Não fazer correspondência indiscriminada apenas pelo idioma-base. `zh-Hant`/`zh-TW` não significam cantonês; `pa-Arab`, `sd-Deva` e `uz-Cyrl` não devem selecionar automaticamente packs em outra escrita. Procurar a próxima preferência compatível do Windows e, sem correspondência, usar inglês. Manter seleção manual dos 51 packs.
- Seletor discreto nas configurações, com nomes nativos, busca, variante quando relevante, nome acessível e teclado; sem bandeiras. Manter a ordem canônica entre os resultados disponíveis.

### Catálogo e integração

- Auditar o código e a tecnologia real do instalador antes de implementar ou estimar volume. Catalogar todas as strings públicas: primeiro uso, configurações, progresso/cancelamento, OAuth, backups, recuperação, CSV e demais formatos, diagnósticos, notificações, menus, tooltips, ajuda e acessibilidade.
- Criar catálogo central com chaves semânticas estáveis, contexto, placeholders, invariantes e glossário. Deduplicar por significado, congelar a matriz antes de traduzir e evitar concatenação de fragmentos. Incluir plurais e mensagens completas.
- Usar uma única base de autoria, com namespaces para aplicativo, instalador e textos compartilhados. Gerar recursos próprios de cada tecnologia a partir dela, sem manter traduções concorrentes. Todos os packs terão a mesma matriz canônica; os recursos gerados podem conter apenas o subconjunto necessário ao destino.
- Serviço de localização com resolução de pack, fallback por chave, interpolação segura, plurais, persistência e atualização das telas. Troca de idioma não reinicia importação, não perde estado nem altera regras, journal, associações, backups ou autenticação.
- Separar idioma da interface e formatação regional de números/datas. Preservar valores funcionais, formatos de intercâmbio, URLs, hashes, IDs, extensões e nomes de marcas. Não renomear arquivos, pastas, atalhos ou documentos existentes ao trocar idioma.
- Traduzir a explicação das falhas e manter códigos/detalhes técnicos originais no diagnóstico. Telas Google e diálogos do sistema seguem regras próprias; não prometer controlar o idioma do Sheets ou do consentimento OAuth.

### Instalador multilíngue

- Distribuir um único instalador com os 51 idiomas incluídos, disponíveis offline; não exigir pacotes separados por idioma.
- Cobrir páginas personalizadas e mensagens padrão do mecanismo de instalação: navegação, opções, integração Windows, substituição/backup, erros, atualização, reparo quando oferecido e desinstalação. Preservar logo, créditos e MIT.
- Identificar o mecanismo existente. Se for Inno Setup, gerar/adaptar recursos `.isl`, mensagens personalizadas e metadados de direção a partir da base canônica. Validar correspondência entre seus identificadores de idioma e os 51 locales; não presumir arquivos prontos ou identificação nativa inequívoca para todos.
- Validar o seletor antes da instalação, persistência entre instalador/aplicativo e manutenção, e idioma em instalação silenciosa sem abrir UI. Não sobrescrever uma preferência já configurada em atualização.

### Escritas, QA e critérios de saída

- RTL obrigatório para `ar`, `ur`, `fa`, `ps` e `sd`: direção nativa dos controles ou `lang`/`dir` se houver superfície HTML. Testar bidi, caminhos/URLs LTR, números, placeholders, listas, inputs, navegação e ícones; não espelhar elementos de significado espacial fixo indiscriminadamente.
- Testar fontes e shaping em Windows 11 limpo, incluindo scripts índicos, etíope, birmanês, CJK e diacríticos. Avaliar fontes adicionais somente se necessário, com licença e impacto de tamanho registrados; não presumir cobertura completa.
- Layout flexível para expansão das traduções, quebra de linha e maior altura dos scripts; validar DPI 100%, 125%, 150% e 200%, teclado, acessibilidade e alto contraste. Tema e idioma permanecem preferências independentes.
- Validar JSON, 51 packs, identidade das chaves, valores obrigatórios, placeholders, invariantes, markup, Unicode, scripts, direção, plurais e ausência de vazamento indevido da fonte. Fallback é proteção de runtime, não evidência de pack completo.
- Testar detecção, escolhas manuais, modo automático, persistência, atualização, fallback, troca durante processamento e instalação/desinstalação. Usar pseudolocalização para expansão e QA visual representativo, além da validação individual de todos os packs.
- Publicar como suporte completo a 51 idiomas somente após cobertura integral, validação técnica, QA editorial e testes funcionais. Registrar `nativeReview` separadamente; PASS técnico não equivale a revisão nativa.
- Registrar contagem real de chaves, estado por pack, evidências e pendências. Não estimar quantidade de lotes, tamanho adicional ou declarar integração concluída antes da auditoria.

## Ordem e critérios de entrega

1. CSV/diagnóstico/capacidade, com testes de regressão e medidas de desempenho.
2. XLS padrão configurável com conferência e backup; atalhos com ícone após receber .ico.
3. Wizard, primeiro uso e telas mínimas; prévias nativas em DPI e teste de associação no Windows 11.
4. Gestão de backup com os padrões aprovados; criação de nova planilha em incremento separado.

5. Internacionalização: auditar e preparar catálogo/infraestrutura durante a revisão das telas; concluir os 51 packs, integrar instalador e aplicativo e validar antes de anunciar suporte multilíngue completo. Não bloquear as correções urgentes de CSV/XLS com a tradução integral.

Manter versão alpha e distribuir pacote novo com evidências Windows/Linux/instalador. Não marcar aceite manual restante como concluído por inferência do relato.
