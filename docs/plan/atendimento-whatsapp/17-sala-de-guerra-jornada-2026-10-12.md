# Sala de guerra da jornada de pedido, 12/10/2026

Issue #1704. Laboratório isolado `guerra` (API e Worker do master `9aeffa86`, PostgreSQL 17, Meta e Mercado Pago falsos de `scripts/homologacao/`, agente pelo proxy com modelo real). Cada cenário foi executado por HTTP e conferido no banco. Provedores controlados: não equivale a WhatsApp, pagamento ou transportadora reais.

## Matriz executada no master `9aeffa86`

| ID | Cenário | Resultado | Evidência |
|---|---|---|---|
| W0 | Primeira mensagem de WhatsApp com a loja fechada | Passou com ressalva | Conversa e cliente criados; mensagem de fora do horário enviada. O agente ainda assim escalou (achado 1) |
| W1 | Pedido pelo WhatsApp no automático | **Falhou** | "Quero pedir 2 lasanhas" recebeu "Vou encaminhar sua mensagem para a equipe" e a conversa foi para humano. Nenhum pedido criado |
| W2 | Agente com ferramenta: status do pedido e janelas | Passou | Resposta de `consultar_pedido` e de `listar_janelas` enviadas |
| W3 | Agente em pergunta simples ("aceitam cartão na entrega?") | **Falhou** | Encaminhou e escalou (achado 1) |
| M1 | Manual: assumir, responder, gerar link da comanda | Passou, 13 de 13 | Mensagem do atendente entregue na Meta falsa; link com número da comanda |
| M2 | Comanda em tempo real: cliente salva, atendente altera, cliente recebe por SSE | Passou | Evento `comanda` chegou em menos de 5 s; revisão velha recebeu 409 |
| M3 | Cliente confirma pela comanda, paga, cozinha, viagem, entrega | Passou | Pedido R$ 130,00 (2 itens e frete R$ 9); 1 cobrança; pagamento pelo webhook; caixa do dia somou R$ 130,00; status até `entregue`; avisos ao cliente pelo outbox |
| M4 | Corrida manual com custo na viagem | Passou | `corridas_externas` com R$ 12,50. Não lança no caixa, por desenho (#1694) |
| M5 | Sugestão para a atendente e devolver ao automático | Passou | `sugestao` 200; `liberar-automatico` voltou a `Automatica` |
| S1 | Pedido pelo site sem login: cardápio, janelas, frete, checkout, pagamento, acompanhamento, cozinha, entrega | Passou com defeito | 21 de 25 no roteiro; 3 falhas eram do roteiro (nome do campo do token). Defeito real: achado 2 |
| S2 | Site: repetir o envio, CEP fora da área, carrinho vazio | Passou | Mesmo pedido devolvido; 422 e 4xx sem pedido novo |
| S3 | Acompanhamento com token de outro pedido | Passou | 404 |

## Achados

| # | Sev. | O que acontece | Onde | Destino |
|---|---|---|---|---|
| 1 | S1 | O agente do WhatsApp descarta todo texto próprio. Só sai resposta montada por ferramenta; qualquer conversa vira "Vou encaminhar" e escala. O pedido automático pelo WhatsApp fica impossível. Veio do commit `257cc008` | `AgenteAtendimentoService.cs`, `GeracaoAgenteAtendimento.cs` | Corrigido nesta PR |
| 2 | S1 | O pedido do site guarda o endereço de entrega sem rua nem complemento quando o CEP não resolve a rua ("50 - CEP 05422-030"); a rota e a parada da viagem saem assim. O que o cliente digitou ia só para a observação. O cadastro do visitante ficava sem endereço | `CheckoutSiteUseCase.cs`, `IniciarCheckoutGuestUseCase.cs`, `IniciarCheckoutUseCase.cs` | Corrigido nesta PR |
| 3 | S2 | O acompanhamento do site devolve `initPointUrl` nulo com cobrança pendente: quem fecha a aba do pagamento não consegue pagar de novo pelo acompanhamento | `ListarPedidosClienteUseCase.cs:206` | Aberto, depende do site usar o campo |
| 4 | S3 | Checkout do site recusado (CEP fora, carrinho vazio) deixa uma conversa "Visitante do site" vazia e assumida na caixa de entrada | `CheckoutSiteUseCase.SessaoAsync` | Aberto |
| 5 | S3 | Avisos de pagamento e de preparo saem no mesmo lote do outbox, sem ordem garantida: o cliente pode ler "em preparo" antes de "recebemos seu pagamento" | outbox de notificações | Aberto |
| 6 | S3 | A resposta de status do agente é contábil demais para o cliente ("Reservado para devolução: R$ 0,00") | `ConsultarPedidoFerramenta.cs` | Aberto |

Varredura de perda entre merges: 104 commits desde 11/10; 45 pares de arquivo em que uma PR removeu linhas de outra da janela. Todos são retrabalho do mesmo tema. Nenhuma sobrescrita acidental encontrada.

## Correções

**Agente conversa com guarda (decisão do Felipe em 12/10).** O texto do agente volta a ir ao cliente. A `GuardaRespostaAgente` barra, e a conversa vai para a equipe, quando o texto:

- afirma situação de pedido, pagamento ou devolução que a ferramenta não confirmou no turno;
- promete frete grátis ou desconto;
- traz valor em reais que não está em resultado de ferramenta do turno, no prompt da loja nem no histórico (aceita múltiplo inteiro de um preço conhecido, para "duas ficam R$ 76,00");
- traz link que não está nessas fontes;
- contém chamada de ferramenta no lugar da resposta.

Ferramenta que falha no turno também barra o texto. A resposta verificada das ferramentas continua tendo precedência. O motivo da barragem vai para a escalada, para a equipe saber o que conferir.

**Endereço do pedido do site.** Rua, número, complemento, bairro e cidade digitados no site vão para o endereço de entrega do pedido nos quatro caminhos (com e sem login, com e sem chat) e para o cadastro do visitante.

## Limites

A guarda é uma lista de padrões: reduz o risco de invenção, não o elimina. Afirmação fora dos padrões (por exemplo "aceitamos cartão na entrega") passa. A regra de alérgenos e de dinheiro continua no prompt e nas ferramentas.
