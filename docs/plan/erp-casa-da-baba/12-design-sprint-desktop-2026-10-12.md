# Design sprint do Console no computador, 12/10/2026

Pedido do Felipe e issue #1683. Só `EasyStock.Console` em tela de computador. Celular e PWA fora.

## Sala

| Papel | Como participou |
|---|---|
| Dona da Casa da Baba, nervosa com o sistema, e psicóloga | Percorreram a jornada do dia no código do master `fa307f98`, com a entrevista de 21/09 como base da voz |
| Designer sênior e direção de arte | Estrutura, hierarquia de ações, estados e vocabulário |
| QA | Fronteira entre comanda e pedido, tentando refutar cada suspeita |
| Desenvolvimento e QA de pagamento | Cobrança, confirmação, baixa manual e caixa |
| Três frentes de desenvolvimento | Correção com prova que falha antes |

As pessoas são simuladas. O que vale como evidência é o código citado, a prova que falhava e a tela do laboratório (API do master em compose isolado, Console do worktree em modo API, Mercado Pago em stub).

## O que a dona disse e o que era

| Fala | Causa na tela |
|---|---|
| "Qual das duas é o pedido?" | Dois blocos "Comanda N" na Ficha, com duas listas e dois totais |
| "A cliente pagou no meu Pix e não tenho onde marcar" | Link vencido escondia o "Marcar pago" |
| "Cliquei e não aconteceu nada" | Erro do "Gerar cobrança e enviar" só no rodapé |
| "O sistema disse que mandou e a cliente não recebeu" | "Enviada ao cliente" aparecia só por existir cobrança |
| "Só fechei a conversa e sumiu o pedido" | Encerrar atendimento cancelava o pedido sem perguntar |
| "Fechei sem querer e agora a loja não abre?" | Fechar caixa não avisava a consequência |

## Votação

28 achados únicos. Critério: dor da dona, risco de dinheiro ou pedido errado, e caber em correção pequena só no Console.

| # | Corrigido nesta rodada | Prova |
|---|---|---|
| 1 | "Manter minha edição" regravava endereço, janela e cupom antigos sobre os do cliente | `prova-1683-ficha-comanda` |
| 2 | Item do pedido sumia da Ficha quando o prato saía do cardápio; observação do pedido não aparecia | `prova-1683-ficha-comanda` |
| 3 | Uma comanda só: "Escolha do cliente pelo link" tem nome próprio e trava a montagem paralela enquanto o cliente escolhe; total sem frete diz que é sem frete | `prova-1683-ficha-comanda` |
| 4 | Link vencido mantém "Marcar pago" e não promete link novo nem cancelamento que o sistema não faz | `prova-1683-cobranca` |
| 5 | Erro do "Gerar cobrança e enviar" aparece ao lado do botão | `prova-1683-cobranca` |
| 6 | Mensagem que não saiu vira "Cobrança criada, mas a mensagem não saiu", com "Copiar link"; a atualização da tela não apaga o aviso | `prova-1683-cobranca` |
| 7 | "Alterar forma" só oferece a troca que o EasyStok executa | `prova-1683-cobranca` |
| 8 | "Desfazer pagamento" não aparece para sinal pago online | `prova-1683-cobranca` |
| 9 | Estorno e voltar etapa esperam a resposta e mostram o erro | `prova-1683-cobranca` |
| 10 | Cozinha e painel do hall dizem "Na fila"; "Aguardando" fica só para pedido não pago | `prova-1683-operacao` |
| 11 | Histórico mostra o status por extenso | `prova-1683-ficha-comanda` |
| 12 | Encerrar atendimento só cancela o pedido com a caixa marcada; cancelamento que falha não encerra | `prova-1683-operacao`, `prova-1586` |
| 13 | Fechar caixa avisa que não reabre e que a loja depende dele; "Venda no balcão" como sugestão de entrada; grupo "Fora da gaveta (Pix, cartão e outros)" | `prova-1683-operacao` |
| 14 | Cancelar e estornar com variante de perigo; confirmações e baixa à mão com Enter e Esc; um rótulo só para receber: "Marcar pago" | `prova-1683-cobranca` |

Adiados com issue: regra de dinheiro no backend em #1685 e restos de comanda, operação e layout em #1686.

Refutado pela sala: cobrança ou pedido duplicado por clique duplo não acontece (uma ação por conversa, trava da conversa no servidor, chave de idempotência).

## Evidência

- Provas: 83 de 83 (as 80 do CI mais as três novas) e `npm run qualidade` com build aprovados no worktree.
- Tela do laboratório, 1440 de largura: título "Escolha do cliente pelo link"; "Pedido não criado: Não entregamos neste CEP." ao lado do botão; um único "Marcar pago" e "Copiar link" na cobrança; Esc fecha e Enter registra a baixa; modal de encerrar com "Cancelar também o pedido Nº 1, que ainda não foi pago" desmarcada; coluna "NA FILA" na Cozinha; "Em preparo ou na fila" no hall.
- A tela reproduziu o item 2 da #1685: baixa do sinal por Pix responde "Encomenda futura exige sinal de 50% por link antes da entrega", depois de avisar que o link seria cancelado.

## Banca final

| Banca | Veredito | O que entrou por causa dela |
|---|---|---|
| A sala (dona, psicóloga, designer, direção de arte) | Aprovada com ressalvas, 4 de 4 | Enter no campo de motivo quebra a linha e só Ctrl+Enter confirma, para não cancelar pedido sem querer; cinco textos reescritos sem jargão e com o nome do botão |
| Revisão de código | Aprova com ressalvas, nenhum achado crítico | A marca de "mensagem não saiu" cai quando a troca de forma manda link novo; pedido sem link em aberto nasce com a caixa de cancelar marcada ao encerrar, porque nada o fecharia depois |

O que a dona ainda sentiria amanhã, segundo a sala: o sinal de encomenda pago por fora (#1685), a segunda compra e a troca de item de pedido criado (#1686), e os limites abaixo.

## Limites

- Não vistos na tela, só por prova: aviso do modal de fechar caixa, rótulo do grupo fora da gaveta, link vencido, mensagem que não saiu, histórico, item fora do cardápio.
- As três provas novas ainda não rodam no CI: a linha no workflow é mudança de processo e vai em PR própria.
- "Desfazer pagamento" usa uma regra aproximada: com sinal na reserva, o primeiro pagamento é o online. A regra exata pede a origem do pagamento na API.
- A marca de "mensagem não saiu" vive na tela: recarregar a página ou abrir em outro computador volta a "Enviada ao cliente". A correção de verdade é a API devolver esse dado na leitura do pedido.
- "Adicionar itens" ainda aparece no bloco da cobrança com comanda vazia enquanto o cliente escolhe pelo link.
- Com item já no rascunho local e o cliente escolhendo pelo link, as duas listas ainda aparecem.
- Estorno e voltar etapa não estão ligados no modo API; a correção vale para a demonstração e para quando forem ligados.
