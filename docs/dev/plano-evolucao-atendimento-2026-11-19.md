# Plano de evolução do atendimento, Casa da Baba (19/11/2026)

Versão 2, DEPOIS da banca (3 revisores, todos "aprova com ressalvas"). Medido em produção
(banco, leitura) e no `master` em `5a9c013c5`. Sem nome de cliente nem trecho de conversa.

## 1. O que está medido

| Medida | Valor |
|---|---|
| Pedidos válidos desde abr/2026 | 71, cerca de 10 por mês, ticket R$ 89, perto de R$ 890 por mês |
| Clientes: total / com pedido / com 2 ou mais pedidos | 35 / 24 / 12 |
| Clientes com pedido E telefone gravado | 13 de 24 |
| Clientes que consentiram marketing | 0 de 35 |
| Pedido nascido do atendimento ou do Assistente | 0 |
| Conversas desde 01/10 / de cliente com pedido | 21 / 1 |
| Automáticas ligadas / respostas prontas cadastradas | 1 de 5 / 0 |
| Avaliações / modelos da Meta enviados | 0 / 0 |
| Textos ao cliente fora do guia de voz da marca | 12, com arquivo e linha (seção 5) |

## 2. O que é hipótese, não fato

| Hipótese | Por que não está provada | Como provar |
|---|---|---|
| A venda acontece no WhatsApp pessoal da dona | `Origem = mobile` mede o aparelho que digitou o pedido, não o canal da cliente (`MobileOrdersController.cs:120`) | Campo "de onde veio" no pedido do APK, 1 toque, 30 dias de coleta |
| A dona respondeu pelo celular em 14/11 e o sistema não gravou | É 1 caso. O código grava o eco, mas só com coexistência e campo `smb_message_echoes` assinado, ou pela ponte sem contato `@lid` | Ler a configuração do número e o log daquele dia |
| Qual gateway está no ar | A tabela de credenciais não é visível ao papel de leitura | Felipe confirma, ou consulta com papel de admin |

## 3. Direção

TODO PEDIDO DEVOLVE À CLIENTE UM CANHOTO DA CASA NO WHATSAPP, e tudo o que a loja escreve fala
com a mesma voz. O Assistente fica como apoio.

Honestidade sobre o resultado: com 10 pedidos por mês, o plano inteiro move de R$ 100 a 300 por
mês em receita (inferência da banca, premissa de 1 pedido a mais por fase). O ganho certo é
TEMPO DA DONA, menos erro de pedido e a cliente reconhecer a Casa. Só a recompra mexe em receita.

## 4. Plano revisado

| Ordem | Entrega | O que a cliente ou a dona vê | Número (hoje → alvo) | Tier | Depende de |
|---|---|---|---|---|---|
| A | Textos na voz da Casa: 12 correções, 5 automáticas com texto, 3 respostas prontas que a dona usa de fato | Uma voz só, sem emoji no lugar de texto, "Boa massa!" no fim | Textos fora do guia 12 → 0; respostas prontas 0 → 3 | Baixo no que é dado; código para roteiro e apresentação do Assistente | Decisão 2 |
| B | Duas medições baratas: campo "de onde veio" no APK e causa do eco de 14/11 | Dona marca 1 toque ao lançar o pedido | Pedidos com canal conhecido 0% → 100% | Alto (campo novo no pedido) | Nada |
| C | Canhoto da Casa para a cliente, em imagem vertical, com Lora e Nunito Sans, logo, itens, total, entrega, "Pago" e "Boa massa!" | Cliente recebe o canhoto; dona para de digitar o resumo | Pedidos com canhoto enviado 0 → todos os que têm telefone (13 de 24 clientes hoje) | Alto | Decisões 1 e 4 |
| D | Recompra, primeiro MANUAL: lista dos 12 recorrentes para a dona chamar; automação só se o piloto der pedido | Cliente antiga recebe um convite da dona | Recompra em 30 dias, base 12 de 24 | Baixo (lista) e depois alto | Decisão 1 |
| E | Mesma cara em tudo: 6 cores e 2 fontes da marca copiadas para o EasyStok, com teste que acusa divergência; assinatura de e-mail (PR 14 do repositório de marca) | Canhoto, e-mail e Console com a cor da Casa | Consumidores com token da marca 1 → 3 | Misto | Fase C no ar |

Correções que a banca impôs ao plano anterior:

| Antes | Depois |
|---|---|
| Canhoto pelas respostas automáticas | Pelo aviso de status do pedido (`AvisoStatusPedidoCliente`), que acha o telefone sem precisar de conversa |
| "Qualquer origem, inclusive APK" como coisa simples | Pedido do APK não publica evento ao nascer; o gancho possível é a mudança de status e o pagamento |
| Canhoto fora da janela de 24 h | Exige MODELO NOVO aprovado pela Meta, com imagem no cabeçalho. A ponte WPPConnect não envia modelo |
| Fase de avaliação e pós-entrega | Já existem no código. Sobra só o lembrete de recompra |
| "Resumo em imagem só sai com o Assistente" | Também sai pelo novo pedido de outro canal (#2871), pelo modelo `pedido_atualizado` |
| Repositório de marca como fonte única | Abstração prematura. Cópia de 6 cores e 2 fontes resolve |
| Lembrete aos 24 clientes | 0 consentiram marketing e só 13 têm telefone. Sem envio em massa: piloto de 5, 1 lembrete por pessoa, saída por palavra |
| `canhoto.css` navy "fora da marca" | Esse é o canhoto da EQUIPE. O da cliente é peça nova |

## 5. Os 12 textos (fase A)

| # | Onde | Problema | Proposta |
|---|---|---|---|
| 1 | `ConfiguracaoAtendimento.cs:29` | Emoji e "bem-vindo(a)" | "Oi! Aqui é a Casa da Baba." |
| 2 | `ConfiguracaoAtendimento.cs:30` | Calor forçado | "Oi, {nome}. Como a Casa ajuda hoje?" |
| 3 | `ConfiguracaoAtendimento.cs:33` | Prazo vago | "Um instante, a Casa já responde." |
| 4 | `RoteiroAtendimento.cs:61` | "assistente da loja" | Apresentar-se como a Casa |
| 5 | `automacao.js:54` | Primeira pessoa do plural | "A Casa fechou por hoje e volta {abre}." |
| 6 | `automacao.js:63` | Idem | "A Casa está fechada agora." |
| 7 | `automacao.js:78`, `RegraAutomatica.cs:52` | Bordão | "Pagamento recebido. Seu pedido está confirmado para {faixa}." |
| 8 | `automacao.js:86`, `catalogo.js:153` | Primeira pessoa | "Conta para a Casa o que achou. Boa massa!" |
| 9 | `automacao.js:94` | Despedida fora da marca | "Por hoje é só, {nome}. Boa massa!" |
| 10 | `automacao.js:38-40` | Primeira pessoa | "Segue o cardápio de hoje: {link}. Escolha e a Casa anota." |
| 11 | `ConfiguracaoAtendimento.cs:42,44` | "Vou encaminhar" | "A Casa vai conferir e responde por aqui." |
| 12 | `catalogo.js:141,153` | Primeira pessoa | "Esse hoje esgotou. A Casa sugere outra massa?" |

Atenção: `{abre}` e `{link}` não são preenchidos pelo servidor hoje (só `{nome}`, `{pedido}`,
`{faixa}`). Os textos 5 e 10 precisam dessas variáveis ou saem sem elas.

## 6. Colisões com o que já está aberto

| PR | Colide com |
|---|---|
| 2881 (aviso de Pronto) | Fases C e D, no `NotificarClienteStatusPedidoHandler` |
| 2883 (aviso não entregue) | Fase C, no provedor da Meta |
| 2848 | Fase C, em `IWhatsAppCloudClient` |
| 2887 (Assistente no chat do site) | Fase A, se mexer em `PromptAtendimento` ou `RoteiroAtendimento` |
| 2882 e issue 2885 (saudação sem IA) | Fase A, no texto da saudação |

A fase C só começa depois de 2881, 2883 e 2848 mergearem.

## 7. Decisões do dono

1. A loja ganha número próprio? Pré-requisito das fases C e D.
2. Voz: a banca recomenda automáticas e canhoto em nome da Casa, e respostas prontas em primeira pessoa (fala da dona).
3. Qual gateway está no ar: API oficial da Meta ou ponte?
4. A sessão "Otimizar fluxo de atendimento" segue só com a S75 e pausa S76 a S78?

## 8. Não medido

- Gateway ativo em produção.
- Causa do eco não gravado em 14/11.
- Preço por mensagem da Meta no Brasil (fontes de terceiros divergem).
- O visual do canhoto: o design system da marca ainda é rascunho e CNPJ, endereço e telefone estão "A PREENCHER". O canhoto sai sem esse rodapé até o preenchimento.

## 9. Estado em 20/11/2026

| Fase | Estado | PRs |
|---|---|---|
| A | No ar | 2898 |
| B | No ar | 2914 |
| C | No ar, fatias 1 e 2. Falta ver o canhoto num pedido pago real | 2933, 2939 |
| D | No ar, lista manual. Piloto mede pedidos depois do convite | 2947, 2952 |
| E | No ar no canhoto. E-mail ficou fora; assinatura espera os dados da empresa | 2959 |

Decisões 1 e 4 seguem abertas. A fase C saiu pela ponte provisória, só dentro da janela de 24 h.
