# Crítica de layout e UX do Atendimento no computador, 12/10/2026

Pedido do Felipe e issue #1697, depois da sprint #1683. Crítica feita na tela real, não no código: Console do master e do worktree lado a lado, no modo API, contra a mesma API de laboratório e o mesmo pedido (2 lasanhas, janela de terça, cobrança em Pix aguardando pagamento), em 1280x720, 1440x900 e 1920x1080.

## O que a tela mostrava

| # | Achado | Medida no master |
|---|---|---|
| 1 | A Ficha é do cliente, não do pedido. Durante o atendimento, cadastro, tags e histórico vêm antes da comanda e da cobrança | Em 1440x900: Ficha com 2.967 px para 595 visíveis (5 telas). Comanda 1.013 px abaixo do topo, cobrança 2.130 px abaixo |
| 2 | Blocos vazios e formulário de troca de janela sempre abertos | Devoluções 197 px e Ocorrências 134 px sem nada dentro; janela 274 px num pedido já agendado |
| 3 | O cardápio abre por cima da conversa | Em 1440 sobram 104 px de 540 da conversa; em 1280 a conversa some inteira. A atendente monta o pedido sem ler o que o cliente pediu |
| 4 | Em notebook a conversa é a menor coluna | 1280: Balcão 360, conversa 380, Ficha 420 |
| 5 | Em tela larga a Ficha continua estreita | 1920: Ficha com 420 px rolando 3,8 telas ao lado de uma conversa de 1.020 px com balões de até 595 |
| 6 | Título de seção recolhida quebra em duas linhas dentro da comanda | "Janela de" e "entrega" em linhas separadas, com o resumo espremido |

O que funciona e ficou como estava: a barra fixa de andamento no pé da Ficha, com o próximo passo sempre à vista; os cartões do Balcão que pedem ação; a trilha de etapas.

## O que mudou

1. Com pedido em andamento, "Contato e endereço", "Tags" e "Histórico" nascem recolhidos, com o resumo ao lado do título (telefone, quantidade de tags, pedidos). Cadastro incompleto continua aberto, porque é o que falta para cobrar. Sem pedido, tudo aberto como antes.
2. "Devoluções", "Ocorrências" e a troca de janela de pedido já agendado nascem recolhidas; o título mostra a janela atual.
3. Quem já abriu ou recolheu uma seção continua com a escolha dela. O padrão só vale para quem nunca mexeu.
4. Com pedido criado, o convite do link de compra fica só com o botão.
5. O cardápio abre do lado do Balcão, e o Balcão abre espaço para ele: a conversa fica inteira à vista, entre o cardápio e a comanda.
6. Larguras padrão acompanham a tela: em notebook, Balcão 320 e Ficha 360; em tela larga, Ficha 520. O que a pessoa arrastou continua valendo.
7. Título de seção recolhida não quebra; quem encurta é o resumo.

## Antes e depois, medido

Mesmo pedido, mesmo laboratório.

| Tela | Medida | Antes | Depois |
|---|---|---|---|
| 1280x720 | Largura da conversa | 380 px | 480 px |
| 1280x720 | Rolagem da Ficha | 7,1 telas | 4,5 telas |
| 1280x720 | Comanda abaixo do topo | 1.013 px | 477 px |
| 1440x900 | Rolagem da Ficha | 5,0 telas | 3,1 telas |
| 1440x900 | Comanda abaixo do topo | 1.013 px | 446 px |
| 1440x900 | Cobrança abaixo do topo | 2.130 px | 1.233 px |
| 1920x1080 | Rolagem da Ficha | 3,8 telas | 2,2 telas |
| 1920x1080 | Largura da Ficha | 420 px | 520 px |
| 1440, cardápio aberto | Conversa à vista | 104 px, cortada | 452 px, inteira |
| 1280, cardápio aberto | Conversa à vista | 0 px | 352 px, inteira |

Em 1440x900 e 1920x1080 a comanda passa a aparecer na primeira tela da Ficha.

## Limites

- Em 1280x720 a comanda ainda começa abaixo da primeira tela (477 px para 415 visíveis): o bloco do cliente e a barra de andamento ocupam o resto.
- A Ficha em duas colunas na tela larga não foi feita; segue na #1686.
- Com o cardápio aberto em 1280 a conversa fica com 352 px. Antes ficava com zero.
- A crítica cobriu o Atendimento. Cozinha, Caixa e Entregas não foram medidos nesta rodada.
- Capturas feitas no painel do navegador de desenvolvimento, reduzidas; as medidas vêm do DOM.

## Revisão do diff

Revisão independente: nada crítico, aprova com ressalvas. Entraram por causa dela:

- Seção que nasce recolhida não esconde erro: com falha na consulta, Devoluções, Ocorrências e a troca de janela nascem abertas; recolhidas por escolha, o título diz "Não carregou". Pedido sem vaga de entrega também nasce aberto.
- "Contato e endereço" não recolhe por cima da pergunta "mudar também a entrega do pedido?".
- Enter na alça de largura restaura o padrão da tela, não o padrão fixo antigo.

Ficaram para a #1686: entre 1180 e 1235 px de largura o cardápio ainda cobre de 31 a 60 px da conversa; na demonstração, a gaveta de simulações fica sob o cardápio. Observado durante a rodada: um erro de renderização num bloco da Ficha deixa a tela inteira em branco, porque não há contenção de erro por região.

Tema claro conferido na Ficha em 1440x900.
