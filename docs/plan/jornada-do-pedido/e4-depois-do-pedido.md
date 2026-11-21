# E4: Receber, ver a entrega e enxergar o que entra (#3076, onda 2)

- Data: 2026-11-21
- Base medida: `origin/master` `5c7e2bdb3`, mais a onda 1 da #3076 (PR #3102, correções do Console).
- Issue: #3076. Relacionadas: #2944 (plano da jornada), E2 (`e2-retirada-endereco-cliente-nova.md`).
- Status: proposta. Tier alto (feat sem spec aprovada). PR aberta até a label `aprovado` do Felipe.
- Origem: medição ao vivo de 21/11 com API local semeada, roteiro de 4 tarefas do dia a dia.

## 1. Problema

A medição da linha de base deu:

| Tarefa | Antes | Situação |
|---|---|---|
| T1 Cadastrar cliente | 5 cliques + 3 digitações | OK |
| T2 Registrar pedido | 14 cliques + 4 digitações | OK; a onda 1 levou a 6 cliques |
| T3 Receber pedido | sem caminho em Pedidos | FALHA |
| T4 Lançar conta paga | 9 cliques + 3 digitações | OK |

A onda 1 corrigiu os bugs. Ficaram três lacunas que pedem comportamento novo, e por isso vêm aqui:

| Lacuna | Onde | Evidência |
|---|---|---|
| L1 Detalhe do pedido só leitura: sem "Registrar pagamento", sem endereço e sem forma de pagamento no topo | `features/gestao/cadastros/DetalhePedido.jsx` | T3 impossível em Pedidos; hoje só pela conversa |
| L2 Lista de Pedidos sem a data da entrega | `features/gestao/cadastros/PainelPedidos.jsx` | quem separa a produção do dia abre pedido por pedido |
| L3 "A receber" da Visão geral ignora pedido pendente de pagamento | `features/gestao/financeiro/PainelFinanceiro.jsx:87` (`totalAVencer30dReceber`) | pedido de R$ 132,30 sem pagamento não aparece no card |

## 2. Proposta

1. L1: o detalhe do pedido ganha cabeçalho com entrega (janela, endereço ou retirada) e pagamento (forma, pago ou saldo), e o botão "Registrar pagamento" para pedido com saldo, reaproveitando o caso de pagamento que a conversa já usa (`aplicacao/pagamento.js`). Sem regra nova de valor.
2. L2: coluna "Entrega" na lista (dia e janela), ordenável, e filtro "Entrega hoje". Pede o campo na resposta da listagem, se ainda não vier.
3. L3: o card "A receber" soma pedidos com saldo dentro do período, com o detalhe "N contas, M pedidos". Decisão do Felipe: somar no mesmo número (A) ou mostrar em linha própria (B, recomendada: não mistura conta lançada com pedido).

## 3. Fora

- E2 (retirada, escolha de endereço, cliente nova): spec própria, já decidida, ninguém começou.
- Regra de celular: o cadastro aceita telefone fixo e celular fora do padrão que o pedido recusa para avisar no WhatsApp. É decisão de produto (exigir celular no cadastro ou só no pedido); hoje o pedido já diz o motivo.
- Cliente segue "Lead" depois do primeiro pedido: depende da regra de relacionamento da API (#2777).
- Caixa com data do dia e pagamento em dinheiro com caixa fechado: comportamento decidido (#2319, #2736), não é bug.

## 4. Aceite

- [ ] T3 em Pedidos: abrir o pedido, registrar o pagamento e ver saldo zero, em no máximo 4 cliques.
- [ ] Lista de Pedidos com a data da entrega visível em 1440 e 390 px.
- [ ] "A receber" com os pedidos pendentes conforme a decisão L3.
- [ ] Prova Red→Green por lacuna e roteiro de navegador com as 4 tarefas, antes e depois.
- [ ] `npm run qualidade` verde; CI verde; `/code-review` sem Critical.

## 5. Rollback

Reverter a PR. Sem migração nas lacunas L1 e L2; L3 só soma o que a API já devolve ou ganha campo novo opcional.
