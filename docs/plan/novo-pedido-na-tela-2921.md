# Novo pedido na própria tela (#2921)

- Data: 2026-11-19
- Base medida: `origin/master` `1e3e414c3`
- Issue: #2921. Relacionada: #2919 (furos 2 e 3 são de outra sessão; aqui entram o furo 4 e, na fase 2, o 3).
- Status: proposta. Tier alto (feat sem spec aprovada). PR aberta até a label `aprovado`.
- Revoga: a decisão da #2871/#2873 (pedido montado na comanda do Atendimento).

## 1. Problema

Hoje, "Novo pedido" em Pedidos, Clientes e Ficha chama `useNovoPedidoDoCliente` (`EasyStock.Console/src/aplicacao/novoPedidoDoCliente.js`): abre a conversa do WhatsApp da loja, seleciona, troca `window.location.hash` para o Atendimento e anuncia a abertura. A equipe cai numa conversa onde pode mandar mensagem sem querer, sem saber de quem é o pedido nem como voltar.

Felipe, 19/11, 21:24, com print de Pedidos > Novo pedido: "TA ERRADO EU FAZER O PEDIDO INDO PRO ATENDIMENTO E ESTA CONFUSO".

## 2. Decisão

Às 21:35, entre "na própria tela" e "Atendimento em modo pedido", Felipe escolheu **na própria tela**. O pedido é montado numa janela aberta sobre a tela atual. O módulo e a hash não mudam.

## 3. Princípios

1. Não troca a hash. Pedidos, Clientes ou Ficha continuam por baixo.
2. A conversa é detalhe interno: existe só como chave do motor, nunca aparece, nunca recebe digitação.
3. Reaproveita o motor que já funciona (comanda, cotação, geração, resumo à cliente). Nenhuma regra de preço, frete ou janela reescrita.
4. Nada de mensagem acidental: a janela não tem campo de texto. A única mensagem que sai é o resumo do pedido, e só no Registrar, com o aviso visível antes.
5. Ao registrar, volta à tela de origem com o pedido novo visível.

## 4. Qual endpoint (decisão registrada)

| Critério | `POST /api/atendimento/conversas/{id}/pedido` (+ `/pedido/simulacao`) | `POST /api/pedidos` (`CriarPedidoCommand`) |
|---|---|---|
| Cliente | `ClienteId` + `VinculoRevisao` obrigatórios | `ClienteId` ou ad hoc |
| Itens | `CardapioItemId`, `VariacaoId`, observação por item (cardápio do dia) | `CriarPedidoItemInput` de produto, sem cardápio do dia |
| Janela de entrega e data | `JanelaId`, `DataEntrega` | só `AgendadoParaEm` |
| Endereço e frete | `EnderecoId`, cotação, `FreteCombinado`; área fora vira "a combinar" | endereço copiado do cliente, sem cotação |
| Cobrança | `Forma`; sinal e restante na encomenda | nenhuma |
| Resumo à cliente | `EnviarAoCliente`, inclusive fora das 24 h pelo modelo `pedido_atualizado` (#2901/#2905) | nenhum. `NotificarClienteStatusPedidoHandler.TipoDoAviso` cobre Preparando, SaiuParaEntrega, Entregue, Pronto e Cancelado, não "pedido recebido" |
| Permissão | `AtenderConversas` | `Operador` |

Escolha: **o endpoint da conversa, com a conversa por baixo**. `POST /api/pedidos` é o pedido cru (origem `web`): não tem cardápio do dia, janela, frete, cobrança nem resumo. Usá-lo exigiria portar essas regras para a API e criar o aviso de "pedido recebido", com risco de divergir do que a Tatiana já vê na comanda. O aviso de status por telefone sem conversa (`AvisoStatusPedidoCliente.cs`) continua valendo depois da criação, mas não cobre o "recebido". Fica como alternativa futura na pergunta 3.

## 5. Jornada da Tatiana

1. Em Pedidos, Clientes ou Ficha, clica em **Novo pedido**. A janela abre sobre a tela; a hash não muda. Se essa cliente tem rascunho guardado, o botão diz **Continuar pedido** e a janela reabre nele.
2. **Cliente**: na Ficha e em Clientes já vem preenchida; em Pedidos escolhe por busca (nome, telefone, documento). Fase 2: "Cliente nova" com cadastro rápido.
3. **Itens**: cardápio do dia dentro da janela, "+" soma no pedido da janela, observação por item.
4. **Entrega**: retirada ou entrega, endereço da cliente, janela e data. Cotação de frete ao vivo (`/pedido/simulacao`).
5. **Pagamento**: forma de pagamento; em encomenda mostra sinal e restante.
6. **Revisar e Registrar**: resumo com itens, frete, total e a linha "Vamos enviar o resumo para a cliente no WhatsApp" (com opção de desligar, como o `enviarAoCliente` de hoje). Um clique registra.
7. A janela fecha. A lista recarrega e destaca o pedido novo; em Clientes e na Ficha o pedido aparece na lista da cliente, com o aviso "Pedido registrado".

**Fechar no meio** (decisão do Felipe, 19/11, 23:05: guarda rascunho). Fechar a janela, trocar de tela ou recarregar guarda a montagem (cliente, itens com observação, retirada ou entrega, endereço, janela, data e forma). Na reabertura para a mesma cliente, a janela volta no passo em que parou e refaz a cotação pela `/pedido/simulacao`; item que saiu do cardápio do dia some da montagem com o aviso "Bolo de pote saiu do cardápio e foi tirado do pedido". Registrar apaga o rascunho. Na janela há **Descartar rascunho**, com confirmação.

## 6. Wireframe

```
+-- Novo pedido ------------------------------------------------[x]--+
| 1 Cliente   2 Itens   3 Entrega   4 Pagamento   5 Revisar          |
|--------------------------------------------------------------------|
| Cliente: [ Maria Souza  (11) 9....  ]   [trocar]                   |
|                                                                    |
| Cardápio do dia            | Pedido                                |
| [busca.........]           | 2x Bolo de pote  (sem nozes)  R$ 24   |
|  Bolo de pote      [+]     | 1x Brigadeiro 12un            R$ 36   |
|  Brigadeiro 12un   [+]     |--------------------------------------|
|  Torta             [+]     | Entrega: Qui 21/11, 14h-16h           |
|                            | Frete (cotado)                R$ 8    |
|                            | Forma: Pix        Total       R$ 68   |
|--------------------------------------------------------------------|
| [x] Enviar o resumo à Maria no WhatsApp     [Voltar] [Registrar]   |
+--------------------------------------------------------------------+
```

## 7. O que se reaproveita e o que é novo

| Arquivo | Situação |
|---|---|
| `src/aplicacao/novoPedidoDoCliente.js` | Muda: deixa de trocar a hash e de anunciar; passa a abrir a janela. Mantém `disponivel` (permissão `atenderConversas`) |
| `src/aplicacao/api/comanda.js` e `acoes/*` (`gerarPedido`, cotação, `abrirConversaDoCliente`) | Reaproveita sem mudar contrato |
| `src/features/ficha-cliente/PainelFicha.jsx` (`BlocoPedido`, `BlocoEntrega`, `BlocoCobranca`) | Extrair a montagem para componentes que recebem `conversaId` e o pedido por props, sem ler `selecionada`. A Ficha do Atendimento passa a consumi-los, sem mudar o que mostra |
| `src/features/cardapio/PainelCardapio.jsx` (~linha 552, `selecionada`) | Principal acoplamento (#1587). O "+" ganha um alvo explícito (`conversaId` por prop); sem a prop, mantém o comportamento atual |
| `src/features/pedidos/NovoPedidoJanela.jsx` (novo) | Janela em 5 passos |
| `src/features/pedidos/useMontagemDoPedido.js` (novo) | Estado da montagem (itens, janela, endereço, forma), cotação e geração por `conversaId` |
| `src/infra/api/rascunhosDoPedido.js` (novo) | Rascunho da montagem por cliente, no mesmo padrão de `infra/api/rascunhosDaSessao.js`: `sessionStorage`, chave por empresa e usuário, apagado no "Sair" (`esquecerRascunhos`) e ao Registrar. Chave por `clienteId`, não por conversa: a conversa é detalhe interno |
| `PainelPedidos.jsx:53`, `PainelCadastros.jsx:73`, `EditorCadastro.jsx` (prop `novoPedido`) | Trocam "iniciar e navegar" por "abrir a janela" |
| `app/App.jsx` (~466) | A janela precisa do `AtendimentoProvider`, hoje só nas telas de ajuste. Conferir se Pedidos, Clientes e Ficha estão sob ele; senão, provider mínimo para a janela |
| `EasyStock.Infra.Postgre/Repositories/Atendimento/ConversaRepository.cs` (`ListarInboxAsync` e contadores) | Muda: esconde da fila, sem busca, a conversa em que a cliente nunca escreveu (seção 8) |
| `ferramentas/prova-2921-*.mjs` (novos) | Seção 10 |

## 8. API

Uma mudança pequena na fase 1, sem migração. Decisão do Felipe (19/11, 22:08): a conversa criada por baixo **fica escondida** da fila do Atendimento até a cliente responder. Regra: sem busca, `ConversaRepository.ListarInboxAsync` não lista conversa com `UltimaMensagemEntradaEm` nulo (a cliente nunca escreveu); com busca, lista. A primeira mensagem da cliente preenche o campo e a conversa volta à fila. Contadores da fila seguem a mesma regra. Medido em produção (19/11, 22:10): 21 conversas, 0 sem mensagem da cliente; hoje a regra não esconde nada.

Contrato usado como está: `POST /api/atendimento/conversas/{id}/pedido`, `/pedido/simulacao` e a rota `do-cliente` que abre a conversa.

Fase 2 (cliente nova): usa a criação de cliente que o Console já tem. Se o cliente sem celular (furo 2 da #2919) não puder abrir conversa, a fase 2 espera a correção de lá; não se resolve aqui.

## 9. Fases

| Fase | Entrega | Gate objetivo | Abandono |
|---|---|---|---|
| 1 | Janela em Pedidos, Clientes e Ficha, cliente já cadastrada. Sem mudar hash. Registrar fecha e mostra o pedido | Provas Red/Green verdes; prova de tela nos 3 pontos de entrada; resumo sai dentro e fora das 24 h; a conversa por baixo não aparece na fila até a cliente responder | Se o desacoplamento do cardápio e da comanda passar de 2 PRs, ou a Ficha do Atendimento regredir, parar e voltar ao fluxo atual |
| 2 | "Cliente nova" com cadastro rápido na janela | Cliente nova registra pedido sem sair da tela; prova de tela | Se depender do furo 2 ainda aberto na #2919, esperar; não duplicar a correção |

Cada fase é uma PR de tier alto (feat), até `aprovado`.

## 10. Testes

Provas no padrão `EasyStock.Console/ferramentas/prova-*.mjs` (Red antes, Green depois), `prova-2921-*`:

1. `novoPedidoDoCliente` não altera `window.location.hash` e não chama `selecionar` nem `anunciarAberturaDeConversa`.
2. O "+" do cardápio soma no `conversaId` da janela, mesmo com outra conversa `selecionada` no provider.
3. Registrar chama `POST .../conversas/{id}/pedido` com `ClienteId`, `VinculoRevisao`, itens, janela, data, endereço, forma e `enviarAoCliente`; falha mantém a janela aberta com a montagem.
4. Sem `atenderConversas` o botão não aparece.
5. Resumo fora das 24 h: segue coberto pelos testes da #2905 (não pode regredir).
6. Rascunho: fechar com itens guarda; reabrir a mesma cliente restaura no mesmo passo; item fora do cardápio do dia sai com aviso; Registrar e "Sair" apagam; rascunho de um usuário não aparece para outro.
7. Teste de repositório: sem busca, `ListarInboxAsync` não traz conversa com `UltimaMensagemEntradaEm` nulo; com busca, traz; depois da primeira mensagem da cliente, volta à fila.

Prova de tela no Console real (build do worktree) nos 3 pontos de entrada, Pedidos, Clientes e Ficha. Em cada um: abrir a janela, hash inalterada, montar, registrar, janela fecha, pedido visível na tela de origem, nenhum campo de mensagem na janela.

## 11. Riscos

| Risco | Mitigação |
|---|---|
| Estado `selecionada` do provider: abrir a conversa por baixo muda a seleção, e a Ficha e o cardápio do Atendimento passam a mostrar outra cliente | A janela usa `conversaId` próprio e não chama `selecionar`; prova 2 |
| Comanda em montagem perdida ao fechar | Rascunho guardado ao fechar (seção 5). Limite: o `sessionStorage` some ao fechar a aba ou o navegador, como os rascunhos de mensagem de hoje |
| Rascunho com preço ou cardápio velho | Na reabertura, cotação refeita e item fora do cardápio do dia tirado com aviso; nada é registrado sem passar pelo Revisar |
| A mesma conversa aberta no Atendimento por outra pessoa | A comanda é da conversa: a outra pessoa vê os itens aparecerem. Aviso na janela se houver outra pessoa atendendo; não bloquear |
| Permissão `atenderConversas` | Botão só aparece com a permissão; a API responde 403 sem ela; erro claro na janela |
| Conversa criada por baixo polui a fila do Atendimento | Escondida da fila até a cliente responder (seção 8). Efeito colateral: a conversa aberta pela equipe em "Iniciar" também some da fila até a resposta; 0 casos em produção hoje, e segue achável pela busca |
| Mensagem acidental | Sem campo de texto; único envio é o resumo, com aviso e opção de desligar |

## 12. Rollback

Reverter a PR da fase: o botão volta ao fluxo atual pelo Atendimento e a fila volta a listar todas as conversas. Sem migração, não há dado a desfazer.

## 13. Perguntas abertas para o Felipe

1. ~~A conversa criada por baixo pode aparecer na fila do Atendimento, ou fica escondida até a cliente responder?~~ Respondida 19/11, 22:08: **fica escondida** (seção 8).
2. ~~Fechar a janela no meio da montagem: perde tudo (com confirmação) ou guarda rascunho para retomar?~~ Respondida 19/11, 23:05: **guarda rascunho** (seção 5).
3. O resumo continua saindo pelo WhatsApp da loja no Registrar (escolha desta spec). No futuro, prefere pedido sem conversa por `POST /api/pedidos` mais um aviso novo de "pedido recebido"?
