# Onda 15 · Processo de atendimento do Assistente (#2533)

Issue #2533. Conduta corrigida em #2531 e #2532. Configuração por loja na Onda 14
([19-engrenagem-do-assistente.md](19-engrenagem-do-assistente.md), S67 a S70). Tier alto: o código só começa depois
de o Felipe aprovar esta spec.

## Problema

No teste em produção (conversa `96ca9f9e`), o Assistente abriu com o link da comanda e parou de conduzir. Saíram 3 links,
"a Baba vai combinar" e "faço por aqui" sem criar o pedido. As palavras do Felipe: "a comanda é uma opção pra ajudar a
pessoa, o assistente tem que antes identificar com a pessoa e tocar sozinho". A sequência é foto do produto, PDF da
comanda para confirmar e link de pagamento. Também falta um script de atendimento.

Origem de "Baba": o prompt diz "atendente virtual da Casa da Baba" (`PromptAtendimento.cs:31`), e o modelo leu
o nome da loja como se fosse uma pessoa. A S68 já troca esse texto pelo nome da loja e pelo `NomeDona`. Esta onda escreve as regras
de como falar da dona.

## Processo

```
Acolher ─► Entender ─► Mostrar ─► Montar ─► Entrega ─► Confirmar ─► Pagar ─► Acompanhar
                          │                              │            │
                    foto do produto               resumo (PDF)   link Pix/cartão
                          └── recurso opcional: comanda pelo link, se a pessoa quiser escolher sozinha
```

## Script padrão (conteúdo de `RoteiroAtendimento.Padrao`, formato da S68)

| # | Etapa | O que o Assistente faz | Ferramenta |
|---|---|---|---|
| 1 | Acolher | Cumprimenta pelo nome, se apresenta como assistente da loja e pergunta como pode ajudar. Não manda link. | nenhuma |
| 2 | Entender | Ocasião, data, quantas pessoas, restrição. Uma pergunta por mensagem e nada que já foi dito. | `consultar_pedido` se citar pedido |
| 3 | Mostrar | Sugere 2 ou 3 opções do cardápio e oferece a foto ("quer ver como fica?"). Se a pessoa quer escolher sozinha, oferece a comanda. | `consultar_cardapio`, `enviar_foto_produto`, `enviar_cardapio_imagem` |
| 4 | Montar | Confirma item, sabor, tamanho e quantidade. O preço vem só da ferramenta. | `consultar_cardapio` |
| 5 | Entrega | Endereço com CEP e número, depois data e janela. Fora da área, salva e chama a dona pelo nome. | `validar_endereco`, `confirmar_endereco`, `listar_janelas`, `escalar_para_dona` |
| 6 | Confirmar | Cria o pedido, manda o resumo e pede o "ok". | `criar_pedido`, `enviar_resumo_pedido` |
| 7 | Pagar | Com o "ok", manda o link de pagamento. | `enviar_link_pagamento` |
| 8 | Acompanhar | Status, entrega e pagamento só pelo que a ferramenta devolver. | `consultar_pedido`, `consultar_entrega` |

Tom, que vale para todas as etapas:
- Mensagens curtas, um assunto por vez.
- O recurso vai como oferta, nunca despejado: pergunta antes de mandar foto, comanda ou cardápio.
- Fala da dona pelo `NomeDona`. Sem ele, diz "a dona". Nunca usa o nome da loja como se fosse pessoa.

O formato do script não muda para a S68: `RoteiroJson` = `[{"nome":"...","instrucao":"...","ativo":true}]`, que
desserializa em `EtapaRoteiroAtendimento(Nome, Instrucao, Ativo)`. A S71 troca só o conteúdo padrão.

## O que já existe e é reaproveitado

| Peça | Onde | Falta |
|---|---|---|
| Foto do item | `CardapioItem.FotoUrl` (capa e galeria) | Expor em `consultar_cardapio` e mandar |
| Envio de imagem | `EnviarImagemAsync` nos 5 clientes (Cloud, WPPConnect, Waha, Roteador, Stub) | Nada |
| Envio de documento PDF | Não existe em nenhum cliente | Porta nova, se D2 = b |
| Gerador de PDF | QuestPDF 2024.12 em `Infra.Async/Pdf` (ficha, canhoto, fatura) | Renderer do resumo do pedido |
| Upload com URL pública | `AnexadorMensagem` (canhoto) | Reusar para o resumo |
| Link de pagamento | `GerarCobrancaPedidoUseCase` (Mercado Pago, 30 min) via `criar_pedido` | Ferramenta própria, depois do "ok" |

## Fatias

### S71 · Script padrão como base do prompt
**Abordagem:** reescrever `RoteiroAtendimento.Padrao` com as 8 etapas. O `PromptAtendimento` passa a abrir pela identidade, depois o script e
por último as regras. A comanda deixa de ser o caminho de "Para montar um pedido" e vira um recurso. A RN-01 passa a dizer só que a saudação já foi enviada, porque o primeiro contato sai sem link (D1).
**Aceite:** prompt sem "Baba"; etapa 1 sem link; `enviar_cardapio_imagem` só aparece como recurso; snapshot revisado.
**Testes (Red):** `Prompt_AbrePeloScript`, `Roteiro_ComandaEhRecurso`, `Prompt_DonaPeloNomeOuADona`.
**Depende de:** S68 para `NomeDona`. Sem ela, usa "a dona". **Tamanho:** P.

### S72 · `enviar_foto_produto`
**Abordagem:** `consultar_cardapio` passa a devolver `temFoto` por item. A ferramenta nova recebe `itemId`, manda a `FotoUrl`
com legenda de nome e preço vindos do cardápio e grava `Mensagem(Saida, Agente, Imagem)`. Limite de 3 fotos por turno.
**Testes (Red):** item sem foto → `enviado=false, motivo=sem_foto`; item de outra empresa → não encontrado;
URL relativa vira absoluta e HTTPS; envio grava a mensagem. **Risco:** a URL precisa ser pública e HTTPS, o que exige confirmar em produção. **Tamanho:** P.

### S73 · Resumo do pedido para confirmar
**Abordagem:** `criar_pedido` cria o pedido sem link e devolve `pedidoId` e o resumo. A ferramenta nova
`enviar_resumo_pedido(pedidoId)` renderiza o resumo (itens, total, entrega e janela) com QuestPDF, sobe com URL
pública e manda no formato escolhido em D2.
**Testes (Red):** o resumo traz os itens e o total do pedido; pedido de outra conversa → recusa; falha no envio
não grava a mensagem como enviada. **Tamanho:** M. O resumo sai como imagem, gerada pelo QuestPDF e enviada por `EnviarImagemAsync` (D2).

### S74 · `enviar_link_pagamento`
**Abordagem:** a ferramenta chama `GerarCobrancaPedidoUseCase` para o pedido da conversa e manda o link (Pix ou cartão).
Serve também para "o link venceu". A guarda já barra link que não veio de ferramenta.
**Testes (Red):** sem "ok" do resumo no turno → a orientação pede confirmação; pedido pago → não gera; link
vencido → gera novo. **Tamanho:** P.

Ordem: S71, S72, S74, S73. As quatro formam um pacote: uma PR de tier alto, com um commit por fatia, porque o script só funciona com as ferramentas novas.

## Decisões do Felipe (14/11/2026 00:57 BRT: D1 a, D2 a, D3 a; spec aprovada)

| # | Pergunta | Opções (a = recomendada) |
|---|---|---|
| D1 | Saudação automática do primeiro contato | a) sem link, pergunta o que a pessoa quer · b) mantém o link do cardápio |
| D2 | Formato do resumo para confirmar | a) imagem gerada do PDF (canal já existe) · b) PDF de verdade (porta nova em 5 clientes) · c) texto |
| D3 | Quando oferecer a comanda pelo link | a) só se a pessoa quiser escolher sozinha ou pedir o cardápio · b) sempre depois de entender o pedido |

## Prova

Não há eval do agente no master, porque a S61 não foi implementada. A prova é o Red por ferramenta, o snapshot do prompt e uma
homologação: o Felipe repete a conversa, e um SELECT em `atendimento_mensagens` mostra a sequência foto, resumo e link,
sem link no primeiro contato e sem "Baba".
