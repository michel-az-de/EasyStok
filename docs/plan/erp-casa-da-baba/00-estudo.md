# Estudo: ERP da Casa da Baba sobre o EasyStok

Data: 01/10/2026. Base: master `0a70eb56`. Leitura somente, nada alterado.
Fontes: código (23 projetos), `docs/plan/atendimento-whatsapp/`, ADRs 0040–0055, protótipo
`casa-da-baba-atendimento/prototipo-omni`, entrevista da Thati (Downloads 01–04), design-system-v1.

## 1. Veredito

**NÃO É REFAZER DO ZERO. O BACKEND JÁ COBRE ~75% DO DESENHO; O QUE FALTA É O FRONT ÚNICO.**

- Backend: M3, M4 e M5 vivos de ponta a ponta. M1 vivo sem combos. M2, M6 e M7 parciais.
- O front novo já tem semente: `EasyStock.Console` (o protótipo trazido para o repo, ~33,6 mil
  linhas, F01–F05 ligados na API). Mas ele é "uma tela só" de atendimento, sem sala de módulos,
  sem permissão por perfil e com a paleta tomate do protótipo, não a da marca.
- Hoje existem **4 fronts vivos em paralelo** que o front novo precisa aposentar.

## 2. Cobertura do desenho M1–M7

Legenda: ✅ vivo · 🟡 parcial · ⬜ ausente

| Módulo | Sub-item | Backend | Protótipo/Console | Legado (Web/PWA) |
|---|---|---|---|---|
| **M1 Cardápio** | Produtos | ✅ `Produto`, `CardapioItem` | ✅ | ✅ Web |
| | Categorias | 🟡 `Categoria` é de estoque; `CardapioSecao` sem CRUD | ⬜ (só "linha") | ✅ Web |
| | Preços | 🟡 dois preços (`PrecoReferencia` × `PrecoStorefront`) | 🟡 preço único | ✅ Web |
| | Combos | ⬜ | ⬜ | ⬜ |
| | Disponibilidade | ✅ disponível, expediente, janelas | ✅ | — |
| **M2 Produção** | Insumos | ✅ `Produto.EhInsumo` | 🟡 marcação | ✅ Web |
| | Fichas técnicas | ✅ `ProdutoComposicao` (BOM), `PUT` sem policy | ⬜ | ⬜ (a "ficha técnica" do Web é a nutricional) |
| | Planejamento | 🟡 calculadora só na rota mobile | ⬜ | 🟡 PWA |
| | Produção do dia | 🟡 gera lote e estoque, **não baixa insumo pela ficha** | ✅ lote em porções, FIFO | ✅ PWA |
| | Perdas | 🟡 só natureza de saída, sem relatório | 🟡 ajuste com motivo | ✅ Web |
| **M3 Atendimento** | Pedidos | ✅ `Pedido` + máquina de estados | ✅ | ✅ Web/PWA |
| | Clientes | ✅ dossiê, tags, notas, bloqueio | ✅ | ✅ PWA |
| | Canais | ✅ WhatsApp, IG, Messenger, chat do site; ⬜ **iFood** | ✅ (sem iFood) | — |
| | Status | ✅ com aviso ao cliente | ✅ esteira | ✅ |
| | Histórico | ✅ | ✅ | ✅ |
| **M4 Cozinha** | Fila de preparo | ✅ KDS sobre `Pedido` (S19) | ✅ tablet de parede | ✅ Web KDS |
| | Prioridades | 🟡 fila ordenada por criação (`KdsPedidoQueries.cs:41`), sem campo | 🟡 implícita pela janela | — |
| | Itens em produção | 🟡 por pedido, não por item | 🟡 | — |
| | Tempo de preparo | 🟡 previsto, real não medido | 🟡 | — |
| | Expedição | ✅ canhoto, comanda, viagens | ✅ | — |
| **M5 Caixa** | Abertura/fechamento | ✅ `MovimentoCaixa` | ✅ (F14 não ligada) | ✅ **PWA, uso diário** |
| | Pagamentos | ✅ MP, Pix, manual, webhooks | ✅ | ✅ |
| | Sangrias/movimentações | ✅ | ✅ | ✅ PWA |
| | Relatórios | 🟡 só fechamentos; extrato PDF ocioso | 🟡 resumo do dia | 🟡 |
| **M6 Campanhas** | Promoções | ⬜ | ⬜ | ⬜ |
| | Cupons | ⬜ (o `Cupom` atual é do plano SaaS) | ✅ (front só) | ⬜ |
| | CRM | ✅ tags, interesse, consentimento | ✅ | — |
| | Mensagens | ✅ campanha em ondas, limite 7 dias, outbox | 🟡 | — |
| | Resultados | 🟡 atribuição em 7 dias, sem painel | ⬜ | — |
| **M7 Configurações** | Usuários | ✅ | ⬜ (atendente fixa "Thatiane") | ✅ Web |
| | Permissões | 🟡 só por nível (Operador/Gerente/Admin); **perfil × módulo não existe vivo** | ⬜ | 🟡 |
| | Integrações | 🟡 núcleo de credencial ocioso, sem KEK (F16 em PR) | 🟡 | — |
| | Impressão | 🟡 modelos térmicos (S49/S52) e fila `ImpressaoPendente`, mas **a fila não tem consumidor** (ver 08-m7); fiscal fora de propósito (RN-28) | ✅ canhoto | — |
| | Parâmetros gerais | ✅ loja, atendimento, vitrine | ✅ | ✅ |

## 3. O que existe e NÃO está no desenho

| Área | Estado | Sugestão de lugar |
|---|---|---|
| **Entregas** (janelas com capacidade, área por CEP, entregadores, viagens, rota, Lalamove) | ✅ backend, ✅ console; Lalamove ⬜ (F17) | **Módulo próprio (M8 Entregas)**: é operação diária e a Thati pensa nela separada |
| Estoque, lotes, FIFO, desacerto | ✅ | operação do dia no M2 (console); gestão e histórico no Web |
| Compras, fornecedores, lista de compras | ✅ | fica no EasyStock.Web (ADR-0056 item 7) |
| Financeiro (contas a pagar/receber) | ✅ só no Web | fica no EasyStock.Web (ADR-0056 item 7) |
| Etiquetas/rotulagem de lote | ✅ só no Web | fica no EasyStock.Web (ADR-0056 item 7) |
| Relatórios e analytics (15 rotas, 21 relatórios) | ✅ | cada módulo mostra os seus + painel no Início |
| Fidelidade (pontos, recompensas, sorteio) | ⬜ backend; ✅ protótipo | M6 Campanhas (regra de pontos sem fonte na Thati) |
| Ocorrência/estorno, avaliação pós-venda | ✅ | M3 Atendimento |
| Lote de papel (contingência) | ✅ | M4 Cozinha |

## 4. Fronts vivos hoje (o que o front novo aposenta)

| Front | Público | Estado | Tamanho |
|---|---|---|---|
| `EasyStock.Console` (React 19/Vite) | dona/operador | **NÃO está em produção** (F13 em PR #1310) | ~33,6 mil linhas |
| `EasyStock.Web` (MVC + Alpine) | back-office | **em produção**, 44 controllers | 105 .cshtml |
| PWA `Api/wwwroot/pwa` | operadora, **caixa diário** | **em produção** | `index.html` de 22.989 linhas |
| Storefront `casadababa.com` (outro repo) | cliente final | **em manutenção intencional** (503); volta refeito (SI.4) | ~140 arquivos |
| Admin, MAUI | — | removidos (P01, P05) | — |

## 5. Tenant único

| Peça | Medida | Recomendação |
|---|---|---|
| `EmpresaId` + filtro EF + RLS | 105 de 156 tabelas, ~3,7 mil refs, 20 migrations de RLS | **MANTER e fixar o tenant.** Remover custa migrations destrutivas e 61 testes, ganho zero |
| Login multi-empresa | com 1 empresa já cai no passo único | ignorar |
| `TenantFeatureFlag` | gateia atendimento/webhooks | deixar ligado |
| Resíduo SaaS (`Plano`, `AssinaturaEmpresa`, `Fatura*`, `Cupom` SaaS, `AdminTenantsController`) | ocioso | **remover** (libera o nome `Cupom` para o cupom de cliente) |
| `Loja` | dimensão separada, usada pelo caixa | manter |
| **ADR-0048 (FMA como 2º tenant)** | Accepted; só saiu Cliente PJ | **contradiz "um tenant só": decidir** |

## 6. Marca

- Front novo usa `design-system-v1`: Papel `#FEFEFE`, Creme `#FFF8E8`, Cacau `#422814` (texto),
  Caramelo `#A25803` (títulos), Trigo `#E3A542` (acento); **Lora** títulos, **Nunito Sans** texto.
- O design-system-v1 foi feito para impressos. Falta a camada de tela (estados, foco, erro, tabela,
  modo escuro, cor por canal e por status da esteira).
- O console hoje usa tomate `#B0381C` + Inter/Fraunces: **diverge da marca**.
- Os rascunhos usam azul genérico e o nome "EASY STOK": precisam vestir a marca.

## 7. Tensão de desenho a resolver

A Thati pediu **"uma tela só"** (D5: canal novo entra como canal, não como tela). O rascunho propõe
**sala de módulos**. Não conflitam se:

- a **sala de módulos** é o mapa de gestão (cardápio, produção, caixa, campanhas, configurações);
- o **Atendimento (balcão + conversa + ficha)** continua sendo o cockpit único da operação, e é a
  tela inicial do perfil "operação";
- a **Cozinha** e as **Entregas** são telas de dispositivo (tablet de parede, celular do entregador),
  abertas direto pela rota.

## 8. Decisões já tomadas que continuam valendo

| Tema | Decisão | Fonte |
|---|---|---|
| Canais | Meta direto (WhatsApp, IG, Messenger), chat do site próprio, e-mail e SMS só saída | ADR-0050/0051 |
| Regra mora na API, front liga módulo a módulo | | ADR-0054 |
| Pedido antes do pagamento; avisa, não trava; janela é capacidade | | D7, D9, RN-23, RN-48 |
| Logística | Lalamove por API; 99 fora | doc 11 |
| Impressos | térmica preta como imagem; 10×15, 58 mm, A4; sem CPF | `12-impressos.md` |
| Fiscal | fora (canhoto não é cupom fiscal) | RN-28, P04 |

## 9. Contradições para reconciliar

1. **Pix:** README/08/P06 dizem "MP gateway único"; `integracoes/README.md` D3 diz "Pix pelo PSP da loja (Efí)".
2. **ADR-0048** (FMA) × tenant único.
3. **Stack:** código em **.NET 10**; CLAUDE.md diz 8, README diz 9.
4. Legado de deploy ainda no repo (`fly.toml`, `render.yaml`, `k8s/`, Azure), P06 incompleta.
5. Storefront: resolvido em `10-site-e-impressos.md` §1.2 (VPS, em manutenção; Fly nunca existiu).
6. Índice do plano desatualizado (S48–S53, F06–F18 e doc 11 fora do README).
7. Lembrete de pedido agendado (`AgendamentoNotificacaoService`) ainda lê `mobile_orders`: pedidos do site e do WhatsApp ficam fora.

## 10. Estado do trabalho em curso

- S01–S49, S52 mergeadas; S53 em PR (#1293); **S50 e S51 sem PR**.
- Go-live mínimo do console = F06+F07+F08+F16+F18+F13. **Faltam F16 (#1304) e F13 (#1310).**
- F09–F12, F14, F15, F17 sem PR. F15 (fidelidade) não tem backend.
- PRs abertas relevantes: #1308 (P0 checkout sem janela), #1303, #1313, #1087.

## 11. Proposta de caminho

```
Fase 0  Decisões (este estudo)                                  ← agora
Fase 1  Fechar go-live do atendimento (F16, F13) no console atual  ┐ em paralelo
Fase 1' ADR-0056 + desenho: mapa M1–M8, perfis × módulos, tela a tela ┘
Fase 2  Fundação do front novo: shell (login → sala → módulo), marca,
        permissão perfil × módulo (backend), tenant fixo, poda do SaaS
Fase 3  Specs por módulo, em ordem de valor operacional:
        M5 Caixa (aposenta a PWA) → M1 Cardápio → M2 Produção/Estoque
        → M8 Entregas → M6 Campanhas → M7 Config → aposentar Web
```

Lacunas de backend que viram specs: combos; promoções; cupom de cliente; fidelidade; permissão
perfil × módulo; produção baixando insumo pela ficha; planejamento na rota web; relatório de perdas;
relatórios de caixa por período; tempo real de preparo; categorias/seções do cardápio; iFood; Lalamove.
