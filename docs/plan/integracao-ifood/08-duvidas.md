# 8. Dúvidas em aberto

Cada dúvida tem alternativas e a recomendada marcada com ✅. As de negócio vêm primeiro. Por último, os pontos `[NÃO CONFIRMADO]` que só o iFood ou a loja de teste resolvem.

## 8.1 Negócio e escopo

**D01 · O iFood entra agora?** (reabre a D3-02, `docs/plan/erp-casa-da-baba/README.md:120`)
- A) Manter fora até a casa ter conta e volume; o estudo fica pronto para quando houver.
- B) ✅ Confirmar com a casa se vai operar no iFood e em que prazo; só abrir o desenvolvimento com conta, CNPJ e data.
- C) Desenvolver já contra a loja de teste, sem a casa operar.

**D02 · Escopo da primeira entrega**
- A) ✅ Só pedidos (Order e Events): importar, aceitar, estados, cancelamento, disputa e baixa de estoque.
- B) Pedidos mais disponibilidade de item e status da loja.
- C) Pedidos mais catálogo completo.

**D14 · Quem entrega os pedidos do iFood?**
- A) Entregador do iFood.
- B) Frota própria da casa.
- C) ✅ As duas, decidido por pedido em `deliveredBy`: o custo extra é pequeno e a homologação cobra o despacho próprio.

**D15 · O iFood respeita a capacidade de produção (vagas e janelas)?**
- A) ✅ Na fase 1, não: alerta quando a fila passar de um limite e a dona pausa a loja.
- B) Pedido do iFood consome vaga e o EasyStok pausa a loja no iFood quando esgotar (depende da fase 2).
- C) Ignorar.

**D18 · Conciliação financeira (módulo Financial)**
- A) ✅ Fora das fases 1 a 3; os valores do pedido bastam para o Extrato.
- B) Junto com a fase 1.

## 8.2 Operação

**D05 · Como o pedido é aceito?**
- A) ✅ Manual no console com alerta e contagem de 8 min, e aceite automático como opção por loja.
- B) Sempre automático.
- C) Sempre manual.

**D06 · Item do iFood sem correspondência no cardápio do EasyStok**
- A) ✅ Entra como item avulso sem baixa de estoque e gera alerta.
- B) Recusar o pedido (gera cancelamento e penalidade).

**D08 · Quando baixar o estoque?**
- A) ✅ Em `Pronto`, como hoje.
- B) No aceite.

**D10 · Cliente do iFood vira cadastro?**
- A) ✅ Não; nome, telefone com localizador e endereço ficam só no pedido.
- B) Criar ou associar `Cliente` pelo telefone.

## 8.3 Técnica

**D03 · Tipo de app**
- A) ✅ Centralizado: o EasyStok é SaaS multi-loja; permite webhook.
- B) Distribuído: uma instalação por loja, com userCode.

**D04 · Recebimento de eventos**
- A) ✅ Polling de 30 s: mantém a loja online, segue o molde do Lalamove, sem exposição de entrada.
- B) Webhook (não tem ack e pode perder evento; a doc indica o polling como contingência).
- C) Webhook com polling de contingência.

**D07 · Complementos do item**
- A) ✅ Tabela filha do item com nome, grupo, quantidade, preço e `externalCode`; opção mapeada a produto baixa estoque.
- B) JSON no item, sem baixa.
- C) Só texto em `Observacao`.

**D09 · Valores do pedido**
- A) ✅ Colunas novas no pedido (desconto da loja, subsídio do canal, taxas do canal, pago online, troco) mais o JSON bruto.
- B) Só o total e o JSON bruto.

**D11 · Quem manda no cardápio do iFood?**
- A) ✅ A casa monta no Portal do Parceiro; o EasyStok só vincula por `externalCode` e empurra disponibilidade (e preço, na fase 3).
- B) O EasyStok cria e mantém tudo por `PUT /items`.
- C) Ninguém sincroniza; só mapeamento para baixa de estoque.

**D12 · Como informar disponibilidade?**
- A) ✅ Status AVAILABLE ou UNAVAILABLE a partir de `CardapioItem.Disponivel`.
- B) Saldo em `/inventory`.

**D13 · Fechar a loja no EasyStok fecha no iFood?**
- A) ✅ Sim: `ForcarFechada` cria interrupção (fase 2).
- B) Não.

**D16 · Onde guardar o id do pedido iFood?**
- A) ✅ Colunas novas de canal e id externo, com índice único.
- B) Reusar `MobileOrderId` (dispara devolução de estoque por outro caminho).

**D17 · Pedido agendado (SCHEDULED)**
- A) ✅ Aceitar e mapear para `AgendadoParaEm`; ler a página de pedidos agendados antes de codificar.
- B) Recusar agendados na fase 1.

## 8.4 A confirmar com o iFood ou na loja de teste

| Id | Ponto `[NÃO CONFIRMADO]` | Como fechar |
|---|---|---|
| N01 | Custo de uso da API | Perguntar ao iFood (P12) |
| N02 | Caminho do polling e do ack (`/events` ou `/order`) | Spike na loja de teste |
| N03 | Verbo de `readyToPickup` e `dispatch` (POST ou PUT) | Spike |
| N04 | Limite do ack (2000 ou 10000) | Usar 2000 até provar |
| N05 | Timeout do webhook (5 s ou 30 s) | Responder em 5 s |
| N06 | `externalCode` duplicado no catálogo (409 ou reuso) | Spike |
| N07 | Página de pedidos agendados e de retirada | Ler antes da fase 1 |
| N08 | Guia de implementação da Plataforma de Negociação | Ler antes da fase 1 |
| N09 | Rate limit de endpoints não listados (`PATCH /items/{id}`, imagem, `myPreparationTime`) | Perguntar ou medir 429 |
| N10 | Critérios de homologação do Merchant | Chamado ao iFood |
| N11 | SLA de propagação de preço e status até o app | Perguntar ao iFood |
| N12 | Escopos finos por módulo | Ler ao criar o app |
| N13 | Termos de uso do desenvolvedor | Ler antes do contrato (P13) |
| N14 | Se `/orders/{id}` devolve `status` | Spike |
| N15 | Se o iFood baixa o `amount` do `/inventory` a cada pedido | Spike (só se D12=B) |
| N16 | Chat com o cliente pela API | A página de detalhes do pedido manda usar o chat para falar com o cliente, mas não há endpoint de chat na referência; perguntar ao iFood |
| N17 | Tamanho máximo do token | Ler na resposta real do spike |
| N18 | Página de erros do módulo Order | Ler antes da fase 1 |
| N19 | Changelog e versão da doc | Acompanhar antes de cada fase |
| N20 | Registro e IPs de origem do webhook (D04, P10) | Só se D04 for webhook |
| N21 | Valores de `{operation}` em `/status` (UC40) | Spike |
| N22 | Tamanho máximo do lote de preço e status (RF23) | Ler ou medir na fase 3 |
| N23 | Multisetup no catálogo v2 e guia `modifiers-management` | Ler antes da fase 3 |
| N24 | Se o DAR (mudança de endereço) exige resposta da loja | Ler e provar no spike |
| N25 | Como chega o aceite parcial da disputa (OPA ou outro evento) | Ler N08 e provar no spike |
| N26 | Nomes de `sponsorshipValues` (IFOOD e MERCHANT ou OWN e PARTNER) | Spike com pedido com cupom |
| N27 | Início dos 8 min em pedido agendado | Spike com pedido SCHEDULED |
