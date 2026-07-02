# Consolidação v1 — diagnóstico canônico

> **Este doc APONTA, não explica.** Conteúdo vive nas issues e ADRs. Se um parágrafo aqui
> explica em vez de linkar, ele está errado — mova para a issue.

**Épico:** [#786](https://github.com/michel-az-de/EasyStok/issues/786) · **Plano origem:** aprovado 2026-07-01 (9 frentes medidas no código, evidência arquivo:linha).

## Protocolo de sessão (3 passos)

1. Leia este doc + a issue da fatia que vai executar.
2. Commit Conventional com `closes #N` (ou `refs #N` se fatiado).
3. Marque o checkbox no épico [#786](https://github.com/michel-az-de/EasyStok/issues/786) e, se um achado mudou de status, atualize a tabela abaixo.

Prompt de retomada suficiente: *"leia docs/plan/consolidacao-v1/README.md e execute a issue #N"*.

## Vereditos por frente (o QA acertou nos sintomas, errou em 3 diagnósticos)

| Frente | Veredito (1 linha) | Issue | Status |
|---|---|---|---|
| XSS nome de produto | **REFUTADO** — Razor escapa; nome já tem `EnsureSemTagsHtml`; PWA usa `escapeHtml` | #612 | fechado |
| XSS metadata de cardápio → NF-e/PDF | **CONFIRMADO P0** — 6 campos sem guard, sink fiscal sem escaping | #658 | **FEITO** (2a294daa+dca57b34) |
| Preço R$142mi sem teto | **REFUTADO** — teto R$99.999.999,99 existe; visto é dado legado | #771 (auditoria) | aberto |
| Produto ativo sem preço em pedido | **CONFIRMADO P1** — gate no Create, falta no Update e no pedido | #656 #561 | aberto |
| SKU duplicado "ZFZW" | **INDETERMINADO** — índice único existe; exige query no banco | #771 #454 | aberto |
| Receita divergente R$60 | **CONFIRMADO P1** — 4 implementações paralelas (fonte/fuso/período) | #774 | aberto |
| Valor estoque custo vs venda | Bases legítimas, mal rotuladas (fix é rótulo de UI) | #774 (fatia) | aberto |
| Caixa aberto 10 dias | **BY-DESIGN** — #641/ADR-0034 notify-only; `CaixaEsquecidoJob` roda | #640 #641 | #640 aguarda GO |
| Pedidos parados 24 dias | **CONFIRMADO P2** — `AguardandoAprovacaoBaba` sem timeout/job | #775 #724 | aberto |
| Pagamento "Verificar" | **INDETERMINADO** — não existe no enum; provável rótulo de UI | #776 | triagem |
| FEFO | **FUNCIONA** — `ValidadeEm NULLS LAST`, configurável | — | ok |
| Concorrência de estoque | **SEGURA** — FOR UPDATE + retry + xmin; falta CHECK no banco | #772 | aberto |
| NFC-e meio construído | Backend real (~80%), mas botão sem gate + rota inexistente | #770 (gate) #558 | aberto |
| ~35 JS sem bundle + Chart.js CDN | **CONFIRMADO P1** — 24 scripts globais, CDN que já derrubou Alpine | #777 #778 | aberto |

## Achados extras (o QA não viu)

| Achado | Issue | Status |
|---|---|---|
| CSRF ausente nos POST `/api-proxy/*` do Admin | #357 | aberto |
| Security headers ausentes no Admin | #354 | aberto |
| CSP ausente (Web+Admin) | #355 | aberto |
| Unsubscribe: sem rate-limit + fallback de secret inseguro | #773 | aberto |
| JWT 8h em localStorage no PWA sem refresh | #785 | adiada |
| `Program.cs` Api 221 linhas (alvo 200) | #779 | aberto |
| `Infra.MongoDb.IntegrationTests` vazio + comentários Fly/Render | #780 | aberto |
| 32 TODOs sem issue | #781 | aberto |
| PWA drift 4 linhas vs mirror Mobile | #414 | aberto |
| BrazilTime/FormatHelper duplicados | #782 | aberto |
| Providers SMS/WhatsApp inertes | #783 | aberto |
| IntegrationTests fora do CI.slnf | #784 | aberto |
| 22 controllers com DbContext direto | #349 | registrar |
| Monolito PWA / code-split | #401 | registrar |

## Decisões vigentes (não reabrir)

- **Caixa esquecido:** notify-only, sem auto-fechar — `docs/adr/0034-*` / #641.
- **Fiscal fora da v1.0:** `docs/plan/v1.0/SCOPE.md` linha 33; gate esconde até homologação (#770/#558).
- **Fonte única (precedente):** `docs/adr/0039-*` (reposição, #748) — receita replica o padrão em ADR-0040 (#774).
- **PWA/mirror são gerados:** não editar `EasyStok.Mobile/Resources/Raw/pwa/**` nem `EasyStock.Web/wwwroot/etiqueta/**` (hook poka-yoke bloqueia).

## Ordem de execução

F0 (#769) → Onda 1 (segurança/integridade) → Onda 2 (números) → Onda 3 (assets) → Onda 4 (dívida). Fatias **[BANCO VIVO]** (#771) são pré-condição dura de qualquer migration (#772 e índice nome-CI de #561).
