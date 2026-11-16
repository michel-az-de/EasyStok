# Integração iFood → EasyStok: estudo

Data: 2026-11-16. Só estudo, sem código. Base do repositório: `origin/master` em `71be60ba7`. Documentação do iFood lida em 2026-11-16. Revisado por banca independente de 3 revisores ([09](09-banca.md)).

## Resumo

**O iFood cabe no EasyStok, mas a integração não é só um adaptador.** A API é madura e bem documentada: OAuth, polling de 30 s,
eventos de pedido, ações de estado, catálogo com `externalCode`. O EasyStok já tem o outbox, o cofre, o molde de webhook e polling
do Lalamove, o KDS e a baixa de estoque. **O que falta está no modelo do pedido**: id externo, valores separados (desconto,
subsídio, taxas, troco), complementos, tipo de entrega e cancelamento iniciado pelo canal. Isso exige migração (tier alto).

**O portão é de negócio, não técnico:** a D3-02 deixou o iFood fora até a casa ter conta e volume, e a homologação exige CNPJ
e a aplicação completa. Recomendação: confirmar com a casa (D01) e, se sim, entregar em três fases, começando só por pedidos.

| Fase | Entrega | Módulos iFood |
|---|---|---|
| 1 | Receber, aceitar, preparar, pronto, despachar, cancelar, disputa, baixa de estoque, contingência | Authentication, Events, Order; Merchant e Catalog só leitura (status da loja, lista de itens para mapear) |
| 2 | Disponibilidade de item, pausa e horário da loja | Catalog (status), Merchant |
| 3 | Preço gerido pelo EasyStok; cardápio completo só se D11=B | Catalog (preço; itens se D11=B) |

## Índice

| # | Arquivo | Responde |
|---|---|---|
| 1 | [01-preparacao.md](01-preparacao.md) | O que providenciar: conta, CNPJ, app, credenciais, loja de teste, homologação |
| 2 | [02-api.md](02-api.md) | Como a API funciona: auth, eventos, pedido, loja, catálogo, limites, erros, divergências da doc |
| 3 | [03-dicionario-dados.md](03-dicionario-dados.md) | Entidades e enums do iFood |
| 4 | [04-mapeamento.md](04-mapeamento.md) | iFood × EasyStok: estados, campos, lacunas L01 a L24 |
| 5 | [05-requisitos.md](05-requisitos.md) | RF01 a RF32, RNF01 a RNF19 |
| 6 | [06-casos-de-uso.md](06-casos-de-uso.md) | UC01 a UC53 com exceções |
| 7 | [07-analise.md](07-analise.md) | Analista sênior e desenvolvedor sênior |
| 8 | [08-duvidas.md](08-duvidas.md) | D01 a D18 em múltipla escolha; N01 a N27 a confirmar |
| 9 | [09-banca.md](09-banca.md) | Achados da banca revisora e correções; relatórios em [banca/](banca/) |
| — | [fontes/](fontes/) | Leituras brutas: [pedidos](fontes/api-ifood-pedidos.md), [catálogo e loja](fontes/api-ifood-catalogo-merchant.md), [base EasyStok](fontes/easystok-base.md) |

## Hipóteses adotadas (o pedido veio com o contexto em branco)

| Id | Hipótese | Base |
|---|---|---|
| H1 | "EAS Stok" é o EasyStok deste repositório: .NET 10, PostgreSQL, EF Core, deploy em VPS | `AGENTS.md`; [base §1](fontes/easystok-base.md) |
| H2 | Objetivo: receber pedidos e baixar estoque; sincronizar disponibilidade, preço e status da loja | Exemplo do próprio pedido; M3.9 em `docs/plan/erp-casa-da-baba/04-m3-atendimento.md:293-300` |
| H3 | Primeira loja: Casa da Baba, restaurante (categoria FOOD) | `docs/plan/erp-casa-da-baba/` |
| H4 | App centralizado (SaaS multi-loja) | D03 |

## Escopo

| Dentro | Fora | Por quê |
|---|---|---|
| Authentication, Events, Order, Merchant, Catalog v2 | Shipping | Contrata entregador iFood para pedidos de outros canais; não é receber pedido |
| | Logistics | Para integradoras de logística |
| | Financial | Conciliação de repasse; útil depois (D18) |
| | Review, Analytics, Promotion | Não tocam pedido, estoque nem cardápio |
| | Item, Picking, Groceries (SPS, SPE, virtual bag) | Só mercado e farmácia |
| | Catalog v1 | Descontinuado em 2024 |
| | Chat com o cliente | Sem endpoint na referência; a doc cita o chat do iFood (N16) |
| | Emissão de NF-e | O estudo só garante exibir o documento do cliente; emissão é do módulo fiscal |

## Siglas de URL

Todas as afirmações sobre a API citam uma destas origens oficiais (acesso em 2026-11-16, lidas no navegador; o portal é SPA).

| Sigla | URL |
|---|---|
| U/… | https://developer.ifood.com.br/pt-BR/docs/food/guides/modules/… (ex.: U/order/details) |
| G/… | https://developer.ifood.com.br/pt-BR/docs/getting-started/… (ex.: G/first-steps/create-app) |
| G-cat/… | https://developer.ifood.com.br/pt-BR/docs/food/guides/modules/catalog/… |
| G-mer | https://developer.ifood.com.br/pt-BR/docs/food/guides/modules/merchant/workflow |
| OA | https://developer.ifood.com.br/page-data/pt-BR/docs/references/page-data.json (OpenAPI, renderizado em https://developer.ifood.com.br/pt-BR/docs/references) |
| RL | https://developer.ifood.com.br/pt-BR/docs/getting-started/documentation/rate-limit |
| HP | https://developer.ifood.com.br/pt-BR/docs/getting-started/homologation/categories |
| HC | https://developer.ifood.com.br/pt-BR/docs/food/guides/modules/catalog/homologation |
| improper-use | https://developer.ifood.com.br/pt-BR/docs/getting-started/documentation/improper-use |
| best-practices | https://developer.ifood.com.br/pt-BR/docs/getting-started/documentation/best-practices |

## Dúvidas mais críticas

| Id | Pergunta | Recomendação |
|---|---|---|
| D01 | O iFood entra agora? | Confirmar com a casa conta, CNPJ e data antes de abrir desenvolvimento |
| D02 | Escopo da primeira entrega | Só pedidos |
| D05 | Aceite manual ou automático | Manual com alerta de 8 min, automático opcional |
| D15 | iFood respeita a capacidade de produção? | Fase 1 com alerta e pausa manual |
| N01 | Custo da API | Perguntar ao iFood |

## Próximo passo

1. Felipe decide D01 com a casa. 2. Se sim: spike de um dia na loja de teste para fechar N02 a N06, N14, N21 e N24 a N27, gravando respostas reais como fixtures. 3. Issue da fase 1 com spec, tier alto.
