# Reformulação de interface — alpha 0.9.10

Implementação em Windows Forms com Segoe UI, marca Z isolada, ações principais verdes, separadores e textos revisados.

- Tela principal compacta; importação como cópia em opções avançadas.
- Tutorial de quatro páginas, corpo rolável e rodapé estável; preferência de ocultar exige escolha explícita.
- Configurações em três seções; opções avançadas após o fluxo principal.
- Backups em tabela; ações secundárias no menu Mais ações. IDs continuam associados aos registros, sem exposição na lista.
- Ilustrações locais desenhadas em código, sem dependências externas.
- Nenhuma mudança de escopos OAuth, política de backups ou semântica de importação.

## Validação

Compilação e testes devem passar antes do aceite. O pipeline Windows executa verificações nativas e gera prévias em claro/escuro. Conferência visual em 100%, 125%, 150% e 200%, teclado, atualização, importação e restauração compõem a fase 2. Não considerar a imagem gerada como evidência da implementação.

## Evidência local

- Build da solução e do aplicativo Windows: aprovado, sem avisos ou erros.
- Suíte: 185 aprovados; 31 específicos do Windows não executados neste ambiente.
- Verificações nativas de UI atualizadas, ainda pendentes de execução no Windows.
- Candidata 0.9.10 autorizada para publicação na branch de desenvolvimento e validação no Windows. O aceite visual e funcional continua pendente.


## Compatibilidade de importação — candidata 0.9.12

- XLSX: resolver o tipo de conteúdo por Override e, na ausência dele, Default. Rejeitar declarações ambíguas e tipos incompatíveis.
- XLS: quando a exportação Google contém fórmulas não verificáveis, abrir a mesma importação como cópia, preservar original e backup e mostrar um aviso. Divergências de valores continuam bloqueando a substituição.
- Diagnóstico: categorias de falha sem mensagens de exceção, caminhos, contas ou conteúdo.
- Arquivos reais enviados pelo usuário: leitura/preparação local de XLS e XLSX aprovada. Arquivos e dados privados não incluídos no repositório.
- Testes sintéticos cobrem Default, precedência de Override, duplicidade, categorias do diagnóstico e reutilização do upload XLS com fórmulas. Validação integrada Google/Windows pendente.
