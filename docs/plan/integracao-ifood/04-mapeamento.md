# 4. Dados do iFood × base do EasyStok

Fonte EasyStok: [fontes/easystok-base.md](fontes/easystok-base.md) (§ indicado) e os arquivos citados lá. Fonte iFood: [03](03-dicionario-dados.md).
Legenda: ✅ existe; 🟡 cabe com transformação; ❌ lacuna (vira requisito em [05](05-requisitos.md)).

## 4.1 Estados do pedido

Estados do EasyStok: `StatusPedido` e `PedidoStateMachine.cs:38-65` ([base §3.6](fontes/easystok-base.md)).

| Evento ou ação iFood | Estado EasyStok | Direção | Observação |
|---|---|---|---|
| PLC (PLACED) | `Aguardando` (está em `Entradas`) | iFood → EasyStok | ❌ L08: não há prazo de aceite nem marca "aguardando aceite do canal" |
| `confirm` / CFM | `Aguardando` (não muda) | EasyStok → iFood | Aceite é registro (`ConfirmadoNoCanalEm`), não estado. `Preparando` só depois do aceite (UC14 E.2) |
| `startPreparation` / PRS | `Preparando` | EasyStok → iFood | Opcional na API |
| `readyToPickup` / RTP | `Pronto` | EasyStok → iFood | Dispara a baixa de estoque atual (`AtualizarStatusPedidoUseCase.cs` ~126-132) |
| `dispatch` / DSP | `SaiuParaEntrega` | EasyStok → iFood | Só `deliveredBy=MERCHANT` |
| CLT (entregador iFood coletou) | `SaiuParaEntrega` | iFood → EasyStok | Só `deliveredBy=IFOOD` |
| CON (CONCLUDED) | `Entregue` | iFood → EasyStok | Também por timeout do iFood |
| CAN (CANCELLED) | `Cancelado` | iFood → EasyStok | ❌ L19: com pagamento registrado, o cancelamento exige nível Gerente (`EfeitosCancelamentoPedido.cs:25-29`, usado em `CancelarPedidoUseCase.cs:60-63`) e gera `devolucao_a_fazer` (`:115-134`); pedido iFood pré-pago cairia nas duas regras |
| `requestCancellation` | `Cancelado` só após CAN | EasyStok → iFood | Até o CAN, manter estado e sinalizar "cancelamento pedido" |
| CARF | volta ao estado anterior | iFood → EasyStok | Alertar a operação |
| OPA (itens alterados) | mesmo estado, itens recalculados | iFood → EasyStok | ❌ L21: sem fluxo de alteração vinda de fora |
| HSD e HSS | sem estado; pendência com prazo | iFood ↔ EasyStok | ❌ L20: sem entidade de disputa |

`Rascunho`, `AguardandoPagamento`, `AguardandoAprovacaoBaba` e `AprovadoBaba` não se aplicam: o iFood já cobra e aprova. **Inferência** a partir de `payments.prepaid` (U/order/details).

## 4.2 Pedido: campos

| iFood | EasyStok | Transformação | Situação |
|---|---|---|---|
| `id` | — | Coluna nova de id externo, única por empresa e canal | ❌ L01 |
| `displayId` | `NumeroDoDia` | Manter o `NumeroDoDia` interno e guardar o `displayId` para a comanda | ❌ L01 |
| `salesChannel`, origem | `Origem` (varchar 20, livre) | `"ifood"` | 🟡 L02 |
| `merchant.id` | `LojaId`, `EmpresaId` | Tabela de conexão merchantId → empresa e loja | ❌ L14 |
| `createdAt` | `CriadoEm` | UTC, exibir em America/Sao_Paulo | ✅ |
| `orderTiming=SCHEDULED`, `schedule.*` | `AgendadoParaEm` | Início da janela | 🟡 |
| `preparationStartDateTime` | `InicioPrevistoEm` | Direto | 🟡 |
| `orderType` | — | DELIVERY, TAKEOUT, DINE_IN | ❌ L06 |
| `customer.name` | `ClienteNome` (150) | Truncar | ✅ |
| `customer.phone.localizer` | `ClienteTelefone` (32) | Número do iFood mais localizador; não é o telefone real | 🟡 |
| `customer.documentNumber` | — | Exibir na comanda para NF quando vier | ❌ L22 |
| `delivery.deliveryAddress` | `EnderecoEntrega`, `BairroEntrega`, `CepEntrega` (8), `EntregaLat`, `EntregaLng` | `formattedAddress` + complemento + referência; CEP pode vir zerado | 🟡 |
| `delivery.observations`, `extraInfo` | `Observacoes` | Concatenar com prefixo | 🟡 |
| `delivery.deliveredBy` | — | Decide dispatch e eventos de entregador | ❌ L06 |
| `delivery.pickupCode` | — | Exibir na comanda | ❌ L22 |
| `total.subTotal` | `Total` (soma dos itens) | Conferir igualdade | 🟡 |
| `total.deliveryFee` | linha de item "Entrega — iFood" | Frete é item no EasyStok | 🟡 L04 |
| `benefits[]` com MERCHANT | — | Desconto da loja | ❌ L03 |
| `benefits[]` com IFOOD, EXTERNAL, CHAIN | — | Valor a receber do iFood (não é desconto) | ❌ L03 |
| `additionalFees` | — | Receita do iFood; fora da NF | ❌ L03 |
| `payments.prepaid` | `PedidoPagamento` | Método novo "ifood_online", já pago | ❌ L03 |
| `payments.pending` com `method` | `PedidoPagamento.Metodo` | CASH → dinheiro, CREDIT → credito, DEBIT → debito, PIX → pix; vale-refeição e outros → outro | 🟡 |
| `cash.changeFor` | — | Troco | ❌ L03 |
| `card.brand` | `PedidoPagamento.Referencia` ou `Observacao` | Texto | 🟡 |
| `isTest` | — | Marcar pedido de teste e excluir de relatórios | ❌ L01 |

## 4.3 Item e complementos

| iFood | EasyStok | Transformação | Situação |
|---|---|---|---|
| `items[].externalCode` | — | Tabela de mapeamento externalCode → `CardapioItem` ou `CardapioItemVariacao`. Basta chegar ao `CardapioItemId`: na baixa, o relink herda o produto do prato (`PedidoEstoqueIntegrationService.cs:78-106`, #1619). `ItemEstoque.CodigoMarketplace` existe (`ItemEstoque.cs:16`), mas é código único no item de estoque, não por canal | ❌ L07 |
| `name` | `PedidoItem.Nome` (150) | Direto | ✅ |
| `quantity`, `unit` | `Quantidade` (14,3), `Unidade` | g, kg, ml, L direto; UN → un | ✅ |
| `unitPrice`, `totalPrice` | `PrecoUnitario`, `Subtotal` | `totalPrice` inclui complementos | 🟡 |
| `observations` (até 1024) | `Observacao` (text) | Direto | ✅ |
| `options[]`, `customization[]` | — | Hoje só cabe em `Observacao` | ❌ L05 |
| Item sem `externalCode` ou não mapeado | `CardapioItemId` e `ProdutoId` nulos | Entra sem baixa de estoque (comportamento atual; com `CardapioItemId` o relink resolve) | 🟡 L10 |

## 4.4 Cardápio, preço e disponibilidade (EasyStok → iFood)

| EasyStok | iFood | Transformação | Situação |
|---|---|---|---|
| `CardapioSecao` | Categoria | Hierarquia do EasyStok tem níveis; o iFood tem um nível | 🟡 |
| `CardapioItem.NomePublico` | `product.name` (1 a 150) | Direto | ✅ |
| `DescricaoPublica` | `product.description` (até 2000) | Direto | ✅ |
| `PrecoStorefront` ou `Produto.PrecoReferencia` | `item.price.value` | Preço próprio do iFood é decisão (D11) | 🟡 |
| `CardapioItemVariacao` (porção) | Item próprio, ou grupo SPECIFICATION com opções | Decisão de modelagem | 🟡 |
| `Disponivel` e `EstaOfertado()` (`CardapioItem.cs:190`) | `status` AVAILABLE ou UNAVAILABLE | Empurrar quando mudar | ❌ L11 |
| Saldo de estoque | `POST /inventory` `amount` | Opcional (D12) | ❌ L11 |
| `FotoUrl` | `POST /image/upload` → `imagePath` | jpg ou png abaixo de 5 MB | 🟡 |
| `Sku`, `CodigoBarras` | `externalCode`, `ean` (13) | Usar código estável por item e porção | ❌ L07 |
| Complementos | Grupos e opções | Não existem no EasyStok | ❌ L05 |

## 4.5 Loja

| EasyStok | iFood | Transformação | Situação |
|---|---|---|---|
| `ExpedienteLoja.HorariosJson` (um turno por dia) | `opening-hours.shifts` (vários turnos) | Converter turno para start e duração | 🟡 |
| `ControleManual = ForcarFechada` | Interrupção | Criar ao forçar fechamento, remover ao voltar | ❌ L12 |
| — | `status.validations` | Mostrar no console por que o iFood não vende | ❌ L12 |
| `CardapioItem.TempoPreparoMinutos` | `myPreparationTime` (loja, 5 a 70) | Granularidade diferente | 🟡 |

## 4.6 Lacunas

Do lado do EasyStok (numeradas como em [base §9](fontes/easystok-base.md), mais as novas L21 a L24):

| Id | Lacuna |
|---|---|
| L01 | Pedido sem id externo, canal, `displayId` e marca de teste |
| L02 | `Origem` sem catálogo |
| L03 | Sem desconto, subsídio do canal, taxas do canal, pago online e troco |
| L04 | Frete é item |
| L05 | Sem complementos |
| L06 | Sem tipo (entrega, retirada, consumo no local) e sem "quem entrega" |
| L07 | Sem código externo por item de cardápio |
| L08 | Sem estado ou prazo de aceite de canal |
| L09 | Baixa ao entrar em `{Pronto, SaiuParaEntrega, Entregue}` (`PedidoStateMachine.cs:100-101`), sem reserva |
| L10 | Item sem `CardapioItemId` nem produto não baixa |
| L11 | Nada empurra cardápio, preço ou disponibilidade |
| L12 | Nada empurra status da loja |
| L13 | Catálogo de credenciais sem iFood |
| L14 | Sem conexão por loja (`conexao_integracao`) |
| L15 | `webhook_recebidos` sem `EmpresaId` |
| L16 | Sem job genérico de polling |
| L17 | Sem taxa ou recebível por canal |
| L18 | `CanalVenda` sem iFood |
| L19 | Sem cancelamento iniciado por sistema externo |
| L20 | Sem disputa (HSD) com prazo |
| L21 | Sem alteração de itens vinda de fora (OPA) |
| L22 | Comanda sem campos do iFood (código de coleta, documento para NF, cupom e quem paga) |
| L23 | Capacidade de produção (vagas e janelas do checkout, `CheckoutCoreService.cs:150-262`) não vale para pedido externo |
| L24 | Horário em si não bloqueia o checkout (`CheckoutCoreService.cs:310-315`); no iFood, o horário enviado manda |

Do lado do iFood (o que o EasyStok tem e o iFood não recebe):

| EasyStok | Situação no iFood |
|---|---|
| Ficha técnica e baixa de insumos | Sem equivalente. Baixa de insumo continua interna |
| Lote, FEFO, validade | Sem equivalente |
| Aprovação da Baba, sinal de encomenda | Sem equivalente: o iFood cobra e aprova |
| Seções em vários níveis | Um nível de categoria |
| Telefone real do cliente | O iFood entrega número com localizador, que expira |
| Fluxo de pagamento próprio (Pix, Mercado Pago) | O iFood cobra; repasse em Financial (fora de escopo) |
