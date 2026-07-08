# ADR-0042 — Esteira de Pedidos canônica: estado, vocabulário e integração E2E

- Status: Proposto
- Data: 2026-07-07
- Issue: #868 (épico) — ondas #862 a #867
- Relacionados: ADR-0030 (outbox transacional), ADR-0014 (pagamento aditivo em PedidoPagamento), ADR-0035 (migration antes de config em prod), `docs/plan/` (Caixa Conciliado — eixo financeiro), épico #831 (integração Hiram), #824 (Api.IntegrationTests fora do CI)

## Contexto

A esteira de pedidos hoje é **três pipelines sobrepostos numa única coluna
`Pedido.Status` (varchar 32)**, e **cada superfície tem seu próprio dicionário
de tradução de status**, sem uma projeção canônica. Isso é a raiz estrutural de
uma classe inteira de bugs — inclusive os dois reportados pela operação da Casa
da Babá — e bloqueia a integração com marketplaces e com o Hiram.

Tudo abaixo foi medido no código em 2026-07-07 (arquivo:linha).

### Modelo atual

- Enum `StatusPedido` com 9 valores — `EasyStock.Domain/Sales/StatusPedido.cs:17`.
- Máquina de estados explícita e boa (ponto de partida): `PedidoStateMachine.cs:20`
  (transições válidas + conjuntos `Abertos`/`Finais`/`ComEstoqueDescontado`).
- Fluxo storefront: `rascunho → aguardando_pagamento → aguardando_aprovacao_baba
  → aprovado_baba → preparando → pronto → entregue`.
- Fluxo ERP: `aguardando → preparando → pronto → entregue`.
- **Três mapas de vocabulário independentes**, sem fonte única:
  - Domínio: `StatusPedidoMapper` (9 strings canônicas).
  - Lojista/Web: `StatusHelper.Map` — `EasyStock.Web/Helpers/StatusHelper.cs:15`.
  - Cliente/storefront: `StatusToContract` — `ListarPedidosClienteUseCase.cs:216`,
    que **colapsa `aguardando`+`preparando` em "EmPreparo"** e `pronto` em
    "SaiuParaEntrega" (linhas 224-226).

### Insight central

A coluna `Status` mistura **dois eixos ortogonais**: *fulfillment* (aguardando →
preparando → pronto → entregue) e *gate financeiro/aprovação* (aguardando_pagamento,
aguardando_aprovacao_baba). O `docs/plan/` de Caixa já modela o eixo financeiro
como **derivado** (`EstadoFinanceiroPedido`, `docs/plan/02-estados-e-eventos.md:12`).
A esteira correta reconhece os dois eixos e tem **uma projeção única por audiência**.

### Bug A — "recebo e volta a aparecer pra receber de novo"

Verificado: o grid `/pedidos` carrega **todos os status sem filtro** e ordena por
urgência — `EasyStock.Web/Controllers/PedidosController.cs:82` (`status: null`) +
`PedidoRepository.cs:56` (`PorUrgencia`). A ação de status via form **engole o erro
de transição sem toast** — `PedidosController.cs:211`
(`if (HasError(result)) return RedirectToAction(nameof(Detail))`). Transição inválida
vira 400 no use case — `AtualizarStatusPedidoUseCase.cs:65-72`. A máquina não deixa
pular etapas (`aguardando_aprovacao_baba` só vai para `aprovado_baba`).

Hipótese (não reproduzida — Onda 0 crava): a ação chamada de "receber" ou (a) foi
rejeitada por transição inválida com erro engolido, ou (b) é registrar pagamento —
que **não altera o `Status` de fulfillment** — então o pedido segue no mesmo bucket
e "reaparece".

### Bug B — "no acompanhamento diz aguardando confirmação, mas não aparece lá"

Verificado: o cliente vê `aguardando_aprovacao_baba` → contrato "AguardandoAprovacaoBaba"
→ rótulo "Aguardando aprovação" (`ListarPedidosClienteUseCase.cs:222`). A **aprovação
não acontece no grid `/pedidos`** — vive numa tela separada (Operação Mobile /
`AprovacaoPedidoController`; "aprov" aparece em `Views/OperacaoMobile/Index.cshtml`,
não nas ações do grid). O rótulo que o cliente lê e a ação que o lojista precisa
tomar vivem em fontes/telas diferentes.

## Decisão

Estabelecer uma **esteira de pedidos canônica** com quatro pilares:

1. **Dois eixos de estado explícitos.** `StatusFulfillment` (máquina única,
   `PedidoStateMachine` estendida como fonte da verdade) + `EstadoFinanceiro`
   derivado (não persistido), alinhado ao plano de Caixa. Nada de misturar eixos
   numa string.
2. **Projeção de vocabulário única (SoT no Domínio).** Uma projeção
   `status → { rótulo_lojista, rótulo_cliente, ação, badge, bucket, é_terminal }`.
   Web e Storefront **derivam** dela; os três mapas manuais somem. Teste de drift
   cobre os três consumidores.
3. **Eventos de transição como único ponto de integração.** Toda mudança de status
   publica `PedidoMudouStatusEvent` (outbox, padrão ADR-0030). Hiram, marketplaces
   e notificações **consomem esse evento**, não observam a coluna.
4. **Suíte E2E nas 4 superfícies rodando no CI.** Criar (cada canal) → transições →
   asserta as projeções de cada audiência em cada passo. Pré-requisito: destravar
   `Api.IntegrationTests` no CI (hoje fora — #824).

Decisões de fundação aprovadas por Felipe em 2026-07-07: **eixos ortogonais**
(D1-B) e **aprovação unificada no grid** (D3-B).

## Alternativas rejeitadas

- **D1-A — Um eixo de status formalizado.** Mais barato (sem migration), mas não
  resolve a mistura fulfillment×financeiro que ajuda a causar os bugs e diverge do
  plano de Caixa. Rejeitado.
- **D2-A — Manter os 3 mapas + só ampliar o teste de drift.** É o status quo; o
  drift já produziu BUG-003/014/015. Trata sintoma, não causa. Rejeitado em favor
  da projeção única (D2-B).
- **D3-A — Manter a aprovação só na tela Operação Mobile.** Preserva a fragmentação
  que originou o Bug B (lojista não age onde vê). Rejeitado em favor de unificar no
  grid (D3-B).
- **Integração via observação da coluna `Status` (polling).** Rejeitada: acopla
  cada integração ao schema; ADR-0030 já estabeleceu o outbox transacional como
  padrão. Reusamos.

## Consequências

- **Fica mais fácil:** adicionar canal (marketplace) ou status vira um lugar só;
  Hiram/notificações plugam num evento estável; bugs de rótulo órfão somem por
  construção; o lojista age onde enxerga.
- **Fica mais difícil:** exige migration + backfill do eixo financeiro (R5 + ADR-0035);
  refactor coordenado das três superfícies no mesmo commit (R8 — todos os call-sites).
- **A revisitar:** o mobile MAUI/PWA valida transições no seu lado (duplicação);
  convergir depois. Coordenar `EstadoFinanceiro` com o plano de Caixa para não
  duplicar a derivação.

## Plano de fatiamento (cada onda = commits build-verdes, 1 issue)

- **Onda 0 — Estabilizar (#862):** reproduzir A e B com teste que falha; corrigir o
  engolir-erro do grid e expor a ação de aprovar/recusar onde falta. Fecha o incidente.
- **Onda 1 — Vocabulário único (#863):** projeção canônica no Domínio; Web+Storefront
  derivam; teste de drift amplia para 3 consumidores.
- **Onda 2 — Dois eixos (#864):** `StatusFulfillment` + `EstadoFinanceiro` derivado;
  migration + backfill; guardas completos.
- **Onda 3 — Cockpit do lojista (#865):** bucket "Novos do cardápio", ação
  aprovar/recusar unificada, filtros por status storefront.
- **Onda 4 — Eventos + E2E no CI (#866):** `PedidoMudouStatusEvent` no outbox;
  destravar `Api.IntegrationTests` no CI; E2E das 4 superfícies (webhook MP fakeado).
- **Onda 5 — Extensão (#867):** consumidores Hiram + marketplace sobre o evento.

## Estratégia de testes E2E ("roda tudo no CI")

- **Domínio:** ampliar `PedidoStateMachineTests` para 100% das transições + rejeições.
- **Contrato de vocabulário:** teste que falha se qualquer superfície tiver status sem
  projeção (generaliza `StatusHelperCobreTodosOsStatusPedido`).
- **Por canal:** integração criar-via-{web, balcão, storefront, mobile, API} → asserta
  status inicial.
- **Jornada E2E:** `checkout → webhook pagamento → aprovação → preparo → pronto →
  entregue`, validando as duas projeções (cliente e lojista) em cada passo — é o teste
  que teria pego os dois bugs.
- **CI:** roda como `Api.IntegrationTests` com Postgres efêmero. Bloqueador real: #824
  (a suíte sobe o app e tem contenção de banco) — a Onda 4 trata isso como infra antes
  do E2E.
