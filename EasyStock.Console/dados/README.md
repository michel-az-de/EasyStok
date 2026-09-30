# Massa de conversas

`massa-conversas.json` traz 31 conversas da Casa da Baba, ids `c1` a `c31`. As cinco primeiras são as da semente (`src/infra/conversasSemente.js`), mesmas pessoas, mensagens enriquecidas.

## Rodada 10 (frente histórico): massa gerada, não escrita à mão

Achado P2 (banca 10): 31 cadastros não sustentam o exemplo da própria dona na
US-052 ("filtrou lasanha, deu 50 clientes"). Em vez de crescer este JSON à mão,
`src/infra/massaClientesGerados.js` gera, por semente fixa (determinístico,
mesmo resultado toda carga), mais 60 cadastros (`g1` a `g60`): cliente da casa,
recorrente, bloqueado, fora de área que já foi cliente, com restrição
alimentar, e lead. Cada um nasce com o histórico de pedidos correspondente em
`src/infra/historicoPedidos.js` (mesma contagem, `coerencia-da-massa.mjs`
cobra os dois lados). Total hoje: 91 cadastros, 68 com lasanha no histórico.

Dois acréscimos por cima da massa de sempre, os dois por regra, nunca por
JSON maior:
- `avaliacaoCliente` (`src/infra/avaliacaoSemente.js`): preenche uma fração
  dos pedidos concluídos, por palavra-chave da nota ou hash do número do
  pedido (nunca aleatório de verdade, sempre a mesma avaliação).
- Atendimentos antigos (`src/infra/atendimentosAntigos.js`): para todo pedido
  fechado que não é o pedido de hoje, gera a troca de mensagens e o resumo de
  encerramento equivalentes, prepostos ao fio pela própria `massaConversas.js`.

Ver `auditoria/decisoes/81-r10-historico.md` para a medida completa (dist
antes/depois, contagens por tipo).

## Esquema
Mesmos campos da semente, com três trocas, porque aqui não existe relógio:

| Campo | O que é |
|---|---|
| `mensagens[].minutosAtras` | inteiro, minutos antes do `INSTANTE_INICIAL` do catálogo (22/09/2026 11:05). A mais antiga tem o maior número |
| `janelaMinutos` | positivo, janela de 24 h do WhatsApp aberta por N min. Negativo, venceu há N min. `null`, canal sem janela ou conversa encerrada |
| `ultimaMinutosAtras` | idade da última mensagem |

`dir` é `in` (cliente), `out` (casa) ou `sistema`. Só `out` tem `status` e o opcional `automatica: true`. A janela é `1440` menos a idade da última mensagem do cliente. Cada conversa carrega `esperado` com `cenario`, `intencao`, `acao` (`propor`, `passar_para_dona`, `nada`), `proibido`, `deveConter` e `porque`: é gabarito de avaliação do agente, não dado de tela.

## Cenários
Quatro leads (fora da área, fora da área que insiste, vira cliente, sumiu no cardápio), três restrições (glúten, lactose, vegano), três pedidos com adicional ou observação, duas janelas lotadas, dois de pagamento, dois de acompanhamento, duas reclamações com devolução, dois elogios, bloqueado, família no mesmo endereço, madrugada, sumiço no meio do pedido, Instagram, chat do site, item zerado, pergunta fora do cardápio, avisos automáticos desligados e resposta a campanha.

## Desvios conscientes
`ocupadas` em `catalogo.js` é retrato antigo e ficou menor que os pedidos ativos da massa; a capacidade das janelas é respeitada. `LAS-CLA` tem 5 porções comprometidas contra estoque 4, que é o desacerto de estoque que alerta e nunca bloqueia a venda.
