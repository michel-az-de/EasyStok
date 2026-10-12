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

## Adendo de 11/10/2026 (#1543): o que o sync leva ao estoque e ao pedido do ERP

Achados da banca de 10/10, todos latentes (produção sem aparelho pareado nem estoque na medição de
11/10). Sem migração.

| Tema | Antes | Agora |
|---|---|---|
| Baixa do pedido do aparelho no ERP | Um lote qualquer do produto; saldo truncado em zero, a falta sumia; só a troca de status baixava | FEFO entre os lotes da loja do pedido (a do aparelho que o criou, não a de quem cadastrou o produto); a falta vira descoberto no lote (#540); pedido que já chega "pronto" baixa; uma vez por pedido e produto do ERP |
| Cancelamento do pedido do aparelho | Somava a quantidade do pedido, tivesse saído ou não | Estorna as saídas que este pedido fez, nos lotes de onde saíram. Sem saída, não devolve |
| Pedido que nasceu no ERP e o aparelho avançou | Status gravado direto na tabela (sem máquina de estados, estoque nem outbox), e o aparelho ainda baixava por conta própria | A troca passa pelo caso de uso do ERP; o estoque é só o da esteira, pelos itens do pedido. Recusa da esteira fica no log e o ERP não muda |
| Lote excluído no aparelho | Tirava as porções do estoque e deixava insumo e embalagem baixados | Estorna a baixa de insumo; restaurar o lote refaz a baixa. Descarte não devolve (a comida foi feita) |
| Passo do vínculo que falha | A falha era engolida e o `SaveChanges` seguinte gravava a metade que ficou no contexto | Quem engole a falha descarta antes o que ficou pendente |
| Fechamento de caixa do aparelho (#954) | Casava "da loja, ou sem loja, ou qualquer": sobrescrevia o fechamento da empresa ou de outra loja | Casa pelo escopo exato: loja com loja, empresa com empresa |
| Ordem do vínculo | Pedidos antes de lotes: o pedido que chegava entregue junto com o lote não achava estoque e a baixa sumia | Lotes antes de pedidos |

**Quem é dono do quê.** Pedido nascido no aparelho: o aparelho manda, o Pedido do ERP espelha e o
estoque sai pelo `MobileStockReconciler`. Pedido nascido no ERP (`Origem` diferente de `mobile`): o
ERP manda. A pergunta é uma só para o envio e para o vínculo, e olha o Pedido do ERP ligado ao pedido
do aparelho, inclusive quando alguém fez essa ligação à mão. O pagamento automático na entrega pelo
aparelho continua valendo para os dois.

**Decisões da revisão independente (11/10).**

1. *O aparelho manda só o último status.* A fila do PWA funde os toques do mesmo pedido, então um
   pedido que o ERP tem em "aguardando" pode chegar como "pronto" ou "entregue". A esteira não aceita
   salto: o vínculo percorre o caminho passo a passo (`PedidoStateMachine.CaminhoPelaCozinha`), só
   pela linha da cozinha. Pagamento e aprovação nunca são pulados nem dados por feitos: do pedido
   pré-operacional só sai o cancelamento. Sem caminho, o ERP fica como está. Se a esteira recusa um
   passo, os passos já dados ficam e o pedido para ali; o próximo envio do pedido, ou o backfill,
   retoma. Pedido do ERP que a esteira não levou até "entregue" não recebe pagamento nem vira Venda.
2. *Cancelar no aparelho um pedido já entregue.* A entrega pelo aparelho registra o recebimento
   sozinha, e no ERP cancelar pedido pago é do gerente. Quando o único recebimento do pedido é esse
   automático (sem usuário, com a marca do vínculo, e sem cobrança paga no provedor), o cancelamento
   vai com nível de gerente: é o aparelho desfazendo a entrega que ele mesmo registrou. Como em todo
   cancelamento do ERP, o recebimento fica registrado; devolver dinheiro é outro ato. Recebimento
   lançado por alguém no ERP segue a regra do ERP, e o aparelho fica divergente até um gerente
   cancelar no console. Antes da #1543 qualquer aparelho cancelava qualquer pedido, sem regra nenhuma.
3. *Lote excluído devolve o insumo do lote inteiro*, mesmo que o FEFO já tenha atribuído venda a ele.
   Excluir é dizer que o lote foi registrado por engano: se ele não existiu, a porção vendida saiu,
   na prática, de outro lote. Lote que existiu e foi vendido em parte se resolve com descarte ou
   ajuste, não com exclusão.
4. *Lote que já chega excluído* baixa o insumo e o devolve no mesmo vínculo, como qualquer outro; é o
   que permite refazer a baixa se ele for restaurado. A baixa só é refeita quando a última baixa
   inteira foi devolvida pela exclusão (motivo do estorno). Se alguém estornou à mão no ERP, no todo
   ou em parte, ela não volta sozinha e o log avisa.
5. *A Venda do pedido do ERP é do vínculo.* No envio ela nascia (ou era marcada como cancelada) antes
   de a esteira dizer se aceitava a troca. Agora só o vínculo mexe nela, depois do aceite. No pedido
   nascido no aparelho nada muda: a Venda segue nascendo no envio.

**Limites que ficam.** O Pedido do ERP criado a partir do aparelho guarda os itens do primeiro envio;
editar itens no aparelho depois não os atualiza lá. A baixa do aparelho não ajusta quando os itens
mudam com o pedido já pronto. O destino do caixa do PWA segue na D-M5-02.

## Adendo de 11/10/2026 (#1571): uma venda, duas chaves de documento

O pedido que nasce no aparelho vira Pedido do ERP, e o console ou o KDS pode avançá-lo. O aparelho
baixa com o id do pedido de lá no documento; a esteira do ERP, com `{pedido}:{item}`. Uma
idempotência não enxergava a outra. Sem migração.

| Situação | Antes | Agora |
|---|---|---|
| Console dá o pedido como pronto e depois o aparelho também | Baixava duas vezes | O aparelho baixa só o que a esteira ainda não baixou daquele produto |
| Aparelho baixa e o console avança ou acrescenta item | A esteira baixava de novo tudo que ainda não tinha o documento dela | A esteira baixa só a diferença |
| Console cancela o pedido que o aparelho baixou | Não devolvia nada (a esteira só conhecia as saídas dela) | Devolve também as saídas do aparelho |
| Aparelho cancela o pedido que o console baixou | Não devolvia nada | Devolve também as saídas da esteira |
| Aparelho cancela depois de tirar um item do pedido | A baixa do item tirado ficava | Devolve tudo o que o pedido baixou |
| Pedido com mais de um produto | O vínculo ligava o produto ao item do ERP pela ordem do id do item, que é sorteado: o par saía trocado e o console baixava o produto errado | Casa pelo conteúdo com que o item foi criado (nome, quantidade, preço) |

**Regra.** Cada lado baixa só o que o outro ainda não baixou daquele produto por aquele pedido. O
item que o console acrescenta sai inteiro: o aparelho não sabe dele, então a baixa do aparelho não o
cobre. O cancelamento, venha de onde vier, devolve o que está aberto pelas duas chaves, e cada saída
devolvida fica marcada, então não volta duas vezes. Vale só para o pedido que nasceu no aparelho: no
pedido que nasceu no ERP a esteira segue dona única (adendo da #1543).

**O cancelamento do pedido do aparelho devolve pelo que está baixado, não pelo status.** O status do
ERP espelha o do aparelho, e um aparelho atrasado pode levá-lo de volta a "aguardando" com a baixa
já feita. A saída é relida com trava antes de voltar ao lote, para o cancelamento simultâneo dos dois
lados não devolver em dobro.

**Devolução por cancelamento não é estorno à mão.** O estorno feito pelo cancelamento leva o motivo
"Devolução automática: pedido cancelado" (com o prefixo, para não coincidir com o que alguém digita
no estorno manual). Se o pedido voltar a valer, a baixa é refeita. Isso acontece com qualquer reenvio
do pedido num status que desconta, inclusive o backfill, e não só com a entrega: no pedido que nasceu
no aparelho o cancelamento feito pelo console vale até o aparelho dizer o contrário. Estorno feito à
mão no ERP não volta com o reenvio.

**O que não foi feito, de propósito.** O console não avisa o aparelho quando avança um pedido que
nasceu lá. Espelhar o status sem espelhar o saldo deixaria o estoque do PWA uma venda acima do real
(o aparelho só conta o movimento que ele mesmo faz), e devolver o saldo do servidor ao aparelho é a
pendência 2 deste ADR. Enquanto isso o operador avança o pedido nos dois lugares e o estoque do ERP
fica certo.

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
