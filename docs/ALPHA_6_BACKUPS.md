# Alpha 0.9.6 — Gestão de backups

Implementação da etapa 6 aprovada no CI 37119038565, commit `ef04ed01c9c53d60afd19d0ecd8057e79a8fc90b`. Aceite manual final Windows 11/Google real pendente.

## Uso

Abra **Backups** na tela inicial. A lista mostra nome, tamanho, data da captura inicial e situação: concluído, protegido/pendente, ausente, limpeza pendente ou limpo. O resumo informa o uso total e a quantidade de backups protegidos. Restauração continua offline, verifica o conteúdo e exige um arquivo de destino inexistente.

As regras começam em **30 dias e 200 MB**, com limpeza automática **desligada**. Retenção configurável de 1 a 365 dias; teto de 1 a 1.000 MB (máximo 1 GB). MB e GB usam unidades decimais: 200 MB = 200.000.000 bytes. Estes limites são de armazenamento de backups, não do tamanho aceito para importação.

**Salvar regras** grava a política. Ativar a limpeza automática exige confirmação que explica a aplicação aos backups atuais e a perda da restauração local. Ela ocorre nas próximas importações, sem serviço residente; portanto 30 dias não é um agendamento exato de exclusão. A idade começa na captura inicial, e uma operação incompleta permanece protegida mesmo após esse prazo.

**Apagar backup selecionado** remove apenas um backup concluído elegível, após confirmação. **Limpar vencidos / excesso** usa a política salva, apresenta a quantidade e pede confirmação. A seleção prioriza os mais antigos. Não apaga originais mantidos pelo modo cópia, atalhos, documentos do Google, autorizações ou histórico de operações.

## Quota e atualização

O teto inclui todos os arquivos da pasta de backups, inclusive arquivos desconhecidos ou órfãos; estes últimos contam no uso, mas não são removidos automaticamente. Bancos, logs, ícones e demais configurações não entram nesta quota. Arquivos temporários de captura podem exigir espaço adicional transitório em disco.

Uma atualização não apaga backups existentes nem ativa limpeza retroativa. Se o armazenamento antigo superar 200 MB, continua preservado. Uma nova captura poderá ser bloqueada até que o usuário limpe backups elegíveis, aumente o teto ou aceite a política automática. Se backups protegidos consumirem a capacidade, o original permanece intacto e não há novo upload. Um arquivo maior que o teto escolhido não provoca limpeza inútil de backups existentes.

O backup é a versão local inicial, não um histórico das alterações futuras no Google. Depois da limpeza, use o atalho para abrir o documento online. O histórico local conserva a identidade da operação para impedir que retomadas de backups apagados gerem novos uploads.

## Proteções

- Somente substituições com aposentadoria local concluída e associação Google verificada, ou cópias com conclusão registrada, são elegíveis.
- Cópias de versões antigas sem prova de conclusão ficam protegidas; uma retomada bem-sucedida pode registrar essa conclusão, reutilizando a associação existente.
- Operações incompletas, ambíguas ou em recuperação são protegidas. Limpeza e restauração compartilham o lock da fonte; orçamento e captura usam um lock global adicional.
- A intenção de apagar é registrada antes da exclusão. Uma interrupção pode ser reconciliada sem reupload e sem confundir ausência não explicada com limpeza concluída.
- Antes de apagar, o app verifica tamanho e SHA-256. No Windows, apaga pelo mesmo handle verificado e recusa links/reparse points. Arquivos alterados não são apagados.
- A interface informa a perda da restauração; histórico limpo permanece na lista, com restauração/retomada indisponíveis.

## Validação

Testes automatizados cobrem política, consentimento padrão, quota, concorrência, proteção de operações pendentes, interrupção, integridade, preservação de histórico e bloqueio de retomada após limpeza. O CI também verifica a interface WinForms, gera prévias light/dark e testa instalação, atualização, rejeição de downgrade, desinstalação e reinstalação preservando política e catálogo.

Aceite manual Windows 11/Google real permanece para o teste final combinado, incluindo aparência dos ícones no Explorer, DPI 125/150/200% e alto contraste.

## Evidências e instalador

[CI 37119038565](https://github.com/zagozago/ZagoSheetsWin/actions/runs/37119038565): Windows **211 testes aprovados / 1 exclusivo Unix ignorado**, Linux **181 aprovados / 31 exclusivos Windows ignorados**, total 212, zero falhas. Build local WinForms sem erros ou warnings. Verificação nativa das regras padrão, opt-in desligado e controles completos aprovada. As quatro prévias de Backups (claro/escuro, repouso/retomada) foram revisadas; o CI gerou 17 prévias no total.

Instalação por usuário, atualização, rejeição de downgrade, desinstalação e reinstalação aprovadas. Sentinelas confirmam preservação da política e do catálogo de backups, além dos backups, autorizações, preferências, histórico, atalhos, ícone e padrões do Windows. Instalação silenciosa continua sem abrir interface.

[Baixar instalador alpha 0.9.6 — Windows x64](https://github.com/zagozago/ZagoSheetsWin/actions/runs/37119038565/artifacts/11273180494). ZIP contém `ZagoSheetsWin-Setup-win-x64.exe`. SHA-256 do ZIP: `4d21e81c0d419b0312ed22a01eb8e068f53f51f9d79e603815537a5e096c46b6`. Download exige login no GitHub; retenção até 01/01/2027. Pode atualizar a instalação atual preservando configuração e backups; não é necessário limpar a máquina para esta etapa.
