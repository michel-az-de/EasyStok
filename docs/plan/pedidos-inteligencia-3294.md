# Pedidos com números de backoffice (#3294)

- Data: 2026-11-24
- Base medida: `origin/master` `624193fde`
- Issue: #3294. Referência de padrão: Leads e clientes com relação (#3237, #3281).
- Status: plano em `~/.claude/planos` passou por banca independente (Opus, sem o contexto da sessão) em 24/11/2026 às 22:35, aprovado com 9 ajustes obrigatórios, todos incorporados abaixo. A sugestão de dividir em duas PRs foi recusada: API e tela só fazem sentido juntas.
- Tier: alto pela natureza (feat com endpoint novo), com delegação da ADR-0070.
- Atlas: busca por EasyStok voltou vazia (lacuna declarada). Fonte: repositório.

## 1. Problema

Cadastros > Pedidos era um CRUD: lista com Pedido, Cliente, Situação, Entrega e Total. Não respondia às perguntas da dona ao abrir a tela: quanto vendi, quanto falta receber, o que pede ação agora, quem voltou a comprar e o que mais sai. A crítica do impeccable em 1440 mediu ainda: coluna Pedido com 26% e quase vazia, Cliente e Situação quebrando palavra no meio, o motivo de "Cancelar pedido" desligado repetido em toda linha, linha de cerca de 150 px (5 pedidos por tela).

## 2. O que entra

API (sem migração):

- `GET api/pedidos/resumo` (`PedidosResumoController`, Operador, módulo Atendimento, empresa do token). Recebe os mesmos filtros da lista menos situação, página e ordem: `clienteId`, `desde`, `ate`, `search`, `entregaDesde`, `entregaAte`.
- O filtro da lista saiu do `PedidoRepository` para `PedidoFiltroDaLista`, usado pela lista e pelo resumo. A faixa e as linhas medem o mesmo recorte.
- Somas no banco (`PedidosResumoQuery`), sempre a partir da empresa.
- A lista ganha campos opcionais na linha: `itensResumo` (3 primeiros itens na ordem em que entraram), `compraDoCliente`, `situacaoDesde` e `atrasado`. Os sinais vêm em uma leitura só por página.

Console:

- `FaixaDePedidos`: 4 grupos lado a lado (Vendido, A receber, Pede ação agora, Clientes e itens) com divisor neutro de 1px, legenda das definições e estado de erro que não derruba a lista. No celular começa recolhida numa linha.
- Grid: Pedido, Cliente (com selo da compra), Itens, Situação (com tempo e "Atrasado"), Entrega, Total (com saldo), Ações. "Cancelar pedido" foi para o menu do pedido, como na Ficha; o motivo de não poder aparece ao abrir o menu.

## 3. Definições

| Número | Regra |
|---|---|
| Pedidos | Todos do recorte, qualquer situação. |
| Vendido, vendas | Soma e quantidade das compras (`RegraDeCompra`: fora cancelado, rascunho e aguardando pagamento). |
| Ticket médio | Vendido dividido por vendas, 2 casas. |
| A receber | Compras com `QuitadoHistoricoEm` nulo e total menos pagamentos maior que zero. Difere do Fluxo de caixa, que conta por vencimento; a legenda diz isso. |
| Atrasado | `RegraDeAtraso`: hora marcada passou e o pedido não está entregue, cancelado nem rascunho. Pedido "para agora" (sem hora marcada) nunca atrasa. |
| Período anterior | Só com "Feito em" fechado (hoje, 7 ou 30 dias) e sem filtro de entrega nem data final: janela de mesmo tamanho que termina na mesma hora (hoje compara com ontem até agora). |
| Clicáveis | Aguardando pagamento, aguardando aprovação e cancelados filtram a lista pela situação; clicar de novo tira o filtro. O resumo ignora a situação. |
| Compra do cliente | 1 mais as compras anteriores do mesmo cliente na mesma empresa, por `CriadoEm` e `Id`. Pedido que não é compra não tem ordem. |
| Na situação desde | Último evento com `StatusNovo` igual à situação atual; sem evento, a criação. Entregue e cancelado não contam tempo. |
| Mais pedidos | 3 itens com mais quantidade nas compras, sem a linha de frete. |
| Voltaram | Compras com cliente que já tinha comprado antes, sobre todas as compras com cliente. |

## 4. Prova

- Unidade: domínio (`RegraDeAtrasoTests`), aplicação (`ResumoPedidosUseCaseTests`, `ListarPedidosSinaisDaLinhaTests`), API (`PedidosResumoControllerTests`: política, módulo, 403 de A pedindo B).
- Integração Postgres (`PedidosResumoQueryIntegrationTests`): duas empresas, pedido alheio apontando o mesmo cliente, e o resumo igual à soma das linhas em 3 páginas.
- Console: `prova-3294-dominio.mjs` (regras puras) e `prova-3294-pedidos-inteligencia.mjs` (navegador com HTTP controlado, 1920, 1440 e 390, claro e escuro).

## 5. Fora

Ficha, Atendimento, regras de cancelamento e pagamento e o `PedidosController` ficaram como estavam.
