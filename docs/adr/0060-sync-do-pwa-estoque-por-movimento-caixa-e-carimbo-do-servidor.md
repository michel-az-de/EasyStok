# ADR-0060 · Sync do PWA: estoque por movimento, caixa com edição e exclusão, carimbo do servidor

- Status: Aceito (2026-10-10; Felipe delegou a decisão ao pedir a correção e a publicação da #1520)
- Data: 2026-10-10
- Decisor: Felipe
- Emenda: nenhuma. Prepara o ADR-0059 D3 (módulo de merge), sem implementá-lo.
- Relacionados: issue #1520 (achados da banca #1509), #1516, #1458, #1464, #1474, #1493, #1522,
  ADR-0059 (a PWA continua como backup offline), ADR-0055 (tier), ADR-0024 (migração com Designer)

## Contexto

A banca revisora de 09/10 (#1509) deixou três achados que pediam decisão de modelo antes de
código. Fatos medidos no master `d0a4cdb6`, cada um com teste que falhava antes da correção:

| # | O que acontecia | Medida |
|---|---|---|
| 1 | Pedido "pronto": o PWA desconta o saldo local e manda o saldo absoluto no `product.upsert`; o servidor grava e o `ApplyStockRule` desconta de novo | aparelho 9, servidor **8** |
| 1 | Lote novo: o PWA soma e manda o absoluto; `ApplyBatch` soma de novo | aparelho 15, servidor **20** |
| 1 | Produto com vínculo e dois lotes: o saldo de **um** `ItemEstoque` era copiado para o produto | aparelho 9, servidor **4** |
| 2 | Lançamento de caixa que já existe: `if (existing != null) return; // imutável`, e responde "aceito" | edição perdida |
| 2 | Exclusão no PWA: `deleteCashEntry` só tira do array; o diff do `sync.js` não enxerga ausência | exclusão nunca sai |
| 3 | Pull filtra por `UpdatedAt` (pedido) e `CreatedAt` (lote, lançamento), que vêm do relógio do aparelho | edição offline antiga não chega; relógio adiantado gera conflito falso |

Por que o `ApplyStockRule` existe: ele está no primeiro commit do módulo (`fe43ddfc`, 23/04), que
já gravava o saldo absoluto **e** recontava. A contagem em dobro nasceu ali. O único cliente do
sync é o PWA (o app MAUI saiu no #1189; o wrapper Capacitor carrega o mesmo PWA). Desde a Onda 2 a
regra ganhou uma segunda função, que é a única que continua: levar a baixa ao estoque do ERP
(`MobileStockReconciler`).

Produção tem 0 linhas nas tabelas `mobile_*` (medido em 10/10): não há legado a migrar.

## Decisão

### D1. Estoque: cada movimento é uma mutation, contada uma vez

O saldo do espelho (`mobile_products.Stock`) só muda por dois caminhos:

1. **`stock.delta`** `{ id, productId, qty }`: um movimento com sinal. O servidor soma. A soma não
   depende da ordem de chegada, e o reenvio é barrado pelo `MutationId` (dedup que já existia).
2. **Saldo absoluto no cadastro** (`product.upsert` sem `stockByDelta`): aparelho com PWA antigo e
   o botão "Sincronizar tudo". O servidor grava como veio.

Pedido e lote **não mexem mais** no espelho. `ApplyStockRule` fica só com a ponte para o ERP, e o
`MobileStockReconciler` não copia mais o saldo de um `ItemEstoque` para o produto.

No PWA o movimento sai do diff do snapshot (`computeMutations`): toda mudança de `p.stock` vira
`stock.delta`, qualquer que seja a tela. Por isso venda, cancelamento, pedido retroativo,
produção, `appendProductionToBatch` e descarte e exclusão de lote (`ajustarSaldoDoLote`) chegam
sem uma linha alterada no `index.html`. O PWA não tem ajuste manual de saldo hoje; se ganhar, sobe
pelo mesmo caminho. O cadastro leva `stockByDelta: true`.

Regras que fecham os cantos:

- O servidor aplica os `stock.delta` **por último** no lote. A fila do aparelho reordena o cadastro
  (dedup por id), então o movimento pode chegar antes do produto. Por serem os últimos, entram no
  mesmo `SaveChanges` que o registro do `MutationId`.
- Produto que **nasce** no servidor usa o saldo que veio no cadastro, e os movimentos do mesmo
  lote não são somados (já estão nele).
- O que chega pelo pull atualiza o snapshot do `sync.js`: saldo vindo do servidor não vira
  movimento do aparelho.
- "Sincronizar tudo" manda o absoluto e tira da fila os movimentos pendentes (já estão nele). Um
  cadastro por movimento não tira da fila um absoluto que ainda não subiu.
- Durante a atualização do PWA (bloqueio de OTA) o movimento passa, com o cadastro do produto.

### D2. Caixa: edição atualiza, exclusão é explícita e vira estorno

- **Edição** (`cashEntry.upsert` de lançamento que já existe): atualiza tipo, valor, descrição e
  forma no lançamento e no `MovimentoCaixa` vinculado. Reenvio sem mudança não faz nada. Movimento
  já estornado no ERP não é alterado.
- **Exclusão** é a mutation `cashEntry.delete`, emitida pela ação do operador depois que o
  "Desfazer" expira. A pendência fica gravada; se o app fechar antes, sobe no próximo boot.
  **Nunca por ausência**: `purgeOldData` e `clearTestData` tiram lançamentos do aparelho.
- No servidor a linha fica (`deleted_at`, `deleted_by`) e o movimento do ERP é **estornado** pelo
  `EstornarMovimentoCaixaUseCase`, o mesmo do console. Nenhuma linha é apagada.
- **Dia com caixa fechado**: recusa com o motivo. A recusa cai na dead-letter com aviso (#1516).
- Lançamento excluído não volta por reenvio de outro aparelho, não é promovido ao ERP e desce no
  pull com `estornado: true`, que o PWA já trata.

### D3. Pull e conflito usam o carimbo do servidor

- Coluna `server_updated_at` em `mobile_products`, `mobile_clients`, `mobile_orders`,
  `mobile_batches` e `mobile_cash_entries`. Quem grava é o `AuditTimestampsInterceptor`, em toda
  inserção e alteração, por qualquer caminho (sync, linkers, painel). O aparelho não tem como
  informar esse valor.
- O pull filtra por `server_updated_at > since` e devolve o carimbo como `ts`. O cursor continua
  sendo o `serverTime` lido antes das consultas (#1474) e o filtro por empresa (#1522) fica.
- A detecção de conflito compara o carimbo do servidor com o `ts` da mutation.
- Com o carimbo, o descarte e a exclusão de lote e a edição e a exclusão de lançamento passam a
  descer para os outros aparelhos (antes o filtro era a data de criação). Duas regras acompanham:
  quem marcou o lote ou excluiu o lançamento vira o autor (`LastDeviceId`) e não recebe de volta; e
  o PWA aplica **só as marcas** no lote que já tem, como o servidor faz, porque trocar o lote
  inteiro apagava a foto e o status locais.
- Contrato do pull inalterado: aparelho com PWA antigo continua sincronizando.
- Migração `AddCarimboServidorMobile`: coluna `NOT NULL DEFAULT now()`, backfill pelo valor que o
  pull usava (`updated_at` ou `created_at`, cortado em `now()`) e índice `(empresa_id,
  server_updated_at)` por tabela. Tem `Down`.

## Alternativas descartadas

### Estoque

| Opção | Por que não |
|---|---|
| **Só parar de recontar** (absoluto vence) | Resolve um aparelho. Com dois, o último absoluto apaga o movimento do outro, ou o conflito recusa o cadastro e a venda some. O teste `DoisAparelhosVendemOMesmoProduto` não passa nesse modelo. Fica como compatibilidade, não como modelo |
| **Servidor conta, a partir de pedido e lote**, e ignora o saldo do cadastro | `appendProductionToBatch` e o descarte de lote só chegam pelo saldo do produto; deixariam de chegar |
| **Movimento emitido em cada tela do `index.html`** | São nove atribuições de `p.stock` num arquivo de 23 mil linhas com outra sessão mexendo, e o próximo caminho novo esqueceria de emitir |
| **`stock.delta` sem o saldo no cadastro** | Um rollback do servidor leria saldo 0 em todo cadastro. Com o saldo junto, o servidor antigo continua funcionando como antes |

### Caixa

| Opção | Por que não |
|---|---|
| Exclusão por ausência no diff | O PWA apaga lançamentos antigos sozinho; viraria estorno em massa |
| Apagar a linha do lançamento | Os outros aparelhos nunca saberiam, e o reenvio de um deles recriaria o lançamento |
| Emitir a exclusão na hora do clique | O "Desfazer" exigiria desfazer um estorno já feito no ERP |
| Regra de estorno própria no sync | Já existe caso de uso com as travas (dia fechado, tipo, devolução) |

### Carimbo

| Opção | Por que não |
|---|---|
| Carimbar dentro de cada `Apply*` | Linkers e painel também gravam essas tabelas; o que esquecesse sumiria do pull |
| Backfill com `now()` | Todo aparelho receberia tudo de novo no primeiro pull |
| Margem de sobreposição no cursor | O SSE dispara o pull logo após o commit; todo registro cairia na margem e o PWA recarregaria duas vezes |
| Versão-base enviada pelo PWA, já agora | É o desenho do ADR-0059 D3 e pede spec própria. O carimbo é a peça que faltava para ela |

## Consequências

- O servidor conta certo com mais de um aparelho e em qualquer ordem. O espelho e o estoque do ERP
  passam a ser contas separadas: a divergência entre eles aparece no painel que já existe, e a
  reconciliação manual continua valendo.
- **Aparelho que fez o movimento não recebe de volta o saldo do servidor** (o pull pula o que ele
  mesmo gravou). Com dois aparelhos mexendo no mesmo produto, o servidor fica certo e a tela de um
  deles pode ficar atrasada até o outro mexer de novo. Já era assim; agora o servidor não é
  corrompido por isso.
- PWA antigo e novo misturados: o absoluto do antigo ainda pode apagar movimento do novo. Dura até
  o service worker atualizar.
- A edição recusada por caixa fechado fica aplicada só no aparelho, com aviso na dead-letter.
- Entre o carimbo (no `SaveChanges`) e o commit há milissegundos em que um pull concorrente pode
  pular a linha. Ela volta na próxima alteração ou num pull completo.
- Toda gravação do servidor numa linha `mobile_*` (vínculo com o ERP, painel de reconciliação)
  carimba a linha: ela desce de novo para os aparelhos e conta como "alteração do servidor" na
  detecção de conflito de produto e pedido. É o preço de não depender de cada gravador lembrar.
- O snapshot do EF foi regenerado e perdeu um `ValueGeneratedOnAdd` de `CardapioItemVariacao.Id`
  que já divergia do modelo desde a #1529. Não gera DDL.

## Rollback

| Parte | Como desfazer | Efeito |
|---|---|---|
| D1 | Reverter o commit. Sem migração | PWA novo em cache continua mandando o saldo no cadastro, então o servidor antigo funciona (com a contagem em dobro de antes) e recusa `stock.delta`, que vai para a dead-letter |
| D2 | Reverter o commit e a migração `AddExclusaoLancamentoCaixaMobile` (`Down` tira as duas colunas) | `cashEntry.delete` de PWA novo é tratado como upsert pelo servidor antigo, sem quebrar o lote. Estornos já feitos no ERP ficam |
| D3 | Reverter o commit e a migração `AddCarimboServidorMobile` (`Down` tira índices e colunas) | O pull volta a filtrar pela hora do aparelho. Nenhum dado é perdido |

As migrações são aditivas: dá para reverter só o código e deixar as colunas.

## Pendências

1. **Módulo de merge (ADR-0059 D3)**: o PWA passar a enviar a versão-base (o `ts` que recebeu no
   pull) e o servidor estacionar o conflito em vez de recusar.
2. **Devolver ao aparelho o saldo do servidor** quando ele divergir do local, sem recarregar a tela
   a cada venda.
3. **Chave de `mobile_products`**: a chave é só o `Id`; dois tenants com o mesmo id colidem. Hoje
   há um tenant (ADR-0056).
