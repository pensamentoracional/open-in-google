# Etapa 2 — núcleo local

## Implementado

- Solução C#/.NET 10: Core, Infrastructure, testes xUnit e processo auxiliar para testar locks.
- SQLite com schema versionado, conta/fonte separadas, aliases de caminhos, transações imediatas e compare-and-swap por versão. Estado e evento de journal são gravados na mesma transação. Banco inválido ou mais novo gera erro, nunca reset silencioso.
- Preparação idempotente de uma fonte por conta: Prepared → SnapshotReady. Estados de upload, mapping remoto e retirada serão adicionados nas etapas 3/4.
- Snapshot streaming, SHA-256, verificação por segunda leitura do handle e verificação do backup publicado. Escrita temporária, flush e publicação sem overwrite.
- Recuperação de backup órfão entre publicação do snapshot e commit SQLite: reaproveitar somente se os bytes corresponderem; divergência preserva as duas versões e bloqueia.
- Restauração com validação antes/depois da cópia e publicação sem sobrescrever destino existente.
- Locks entre processos por identidade da fonte; timeout/cancelamento explícitos e liberação pelo sistema ao encerrar processo. Arquivos de lock persistem; não são apagados para evitar corridas entre donos.
- Diretórios privados: ACL Windows restrita ao usuário e SYSTEM; Unix 0700 e backups 0600. Backup guarda conteúdo original sem criptografia; não guardar em raiz compartilhada.

## Identidade e escopo

No Windows: serial de volume, índice de arquivo e criação obtidos do handle. Renomeação preserva identidade; cópia tem identidade própria. A implementação é direcionada ao NTFS e requer validação manual Windows 11 antes do piloto. No Linux: fallback por caminho absoluto apenas para testes do núcleo; não promete identificar renomeações. SHA-256 é evidência de conteúdo, não chave global de deduplicação.

O leitor rejeita UNC Windows e reparse points. Isso não detecta todo diretório sincronizado: bloqueio completo de OneDrive/pastas compartilhadas precisa ocorrer antes da retirada do original. Esta etapa não remove originais, faz upload ou cria atalhos. Ela também não valida fidelidade/conteúdo de um workbook: extensão XLSX aqui delimita o fluxo; validação de formato/conversão pertence à etapa 3.

A conta é um identificador fornecido pelo chamador; será vinculada à identidade autenticada na etapa 3. Não usar e-mail presumido como identidade final.

## Testes e verificação

SDK instalado localmente: 10.0.401; Microsoft.Data.Sqlite 10.0.12. Dependências transitivas registradas em packages.lock.json. Compilação Release com warnings tratados como erros. Suíte: 28 testes, sendo 3 específicos de Windows e 1 de permissões Unix.

Comando normal:

```bash
dotnet restore SheetsWindows.slnx --locked-mode
dotnet test SheetsWindows.slnx --configuration Release --no-restore
```

No ambiente local gerenciado, executar MSBuild em processo único para evitar a restrição de sockets dos workers:

```bash
dotnet test SheetsWindows.slnx --configuration Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

Cenários: reabertura sem duplicação, 16 preparações concorrentes, cópias iguais independentes, isolamento por conta, corrupção/ausência do backup, falha na leitura/escrita/persistência, recuperação do snapshot órfão, fonte alterada, rollback da transição quando evento falha, versão futura/banco corrompido, restauração sem overwrite, cancelamento/timeout e lock entre processos com crash. Testes Windows cobrem proteção do handle, renomeação/cópia e ACL privada.

Resultados locais: 25 aprovados, 3 pulados por requerer Windows, 0 falhas. CI Windows/Linux em .github/workflows/local-core.yml aprovada no run 36815471525, commit dcb32daa5cca3832408ebb7e7402c34442fed4df. Linux: 25 aprovados e 3 pulados; Windows: 27 aprovados e 1 pulado (permissões Unix). Zero falhas em ambos. Evidência: https://github.com/pensamentoracional/open-in-google/actions/runs/36815471525. windows-latest valida runtime Windows em runner servidor, não substitui piloto manual Windows 11.

## Limites e próxima etapa

Flush e transações cobrem recuperação de processos; não foi certificado comportamento sob corte de energia ou corrupção física. Não apagar temporários órfãos automaticamente: eles podem ser evidência recuperável e política de coleta vem depois. VerifyRecovery verifica snapshots comprometidos e retorna Prepared para retomada contra uma fonte válida; não promove um arquivo órfão pelo nome sem evidência.

O lock da preparação não cobre operações Google futuras. A etapa 3 deve manter/coordenar lock e estado da operação durante importação, adicionar marcador/reconciliação de criação remota e não repetir criação cegamente. A retirada do original depende de uma etapa 4 própria, que verificará identidade/versão no handle e atalhos antes de qualquer remoção.

Próximo: OAuth seguro e importação XLSX a partir dos bytes do snapshot; a etapa 2 não tem launcher final nem instalador.
