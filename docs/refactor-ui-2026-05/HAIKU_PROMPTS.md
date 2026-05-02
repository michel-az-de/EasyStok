# Prompts para Haiku — Tarefas restantes da refatoração de UI

> **Contexto.** A refatoração da identidade visual EasyStock foi concluída em 8 commits (`7934047` → `f3c2011`). Tokens, build local Tailwind, componentes DS, paleta navy+orange, JS quality e auditoria estão prontos. As 5 tarefas abaixo ficaram como follow-up: cada prompt é **atômico, self-contained, e seguro para Haiku 4.5 executar isoladamente**.
>
> Cada prompt foi escrito assumindo o agente NÃO viu a conversa anterior. Inclui paths absolutos, comandos de verificação e acceptance criteria.
>
> **Modelo recomendado:** `claude-haiku-4-5-20251001` em todas as tarefas (são repetitivas/mecânicas, baixa exigência de raciocínio).

---

## Prompt 1 — Migração de páginas Web para componentes DS

**Escopo.** Substituir wrappers ad-hoc `<div class="bg-white shadow ...">` pelas classes `.panel` / `.card` do DS em páginas do EasyStock.Web. Tabelas ad-hoc viram `.tbl` envolvidas em `.tbl-wrap`. Status badges ad-hoc viram `.badge-{ok,warn,crit,info,accent}` ou `.stp.{aguardando,preparando,pronto,entregue,vencendo,critico}`.

**Páginas alvo (em ordem):** `EasyStock.Web/Views/Dashboard/Index.cshtml`, `Produtos/Index.cshtml`, `Produtos/Detail.cshtml`, `Estoque/Index.cshtml`, `Estoque/Detail.cshtml`, `Lotes/Index.cshtml`, `Caixa/Index.cshtml`, `Caixa/Historico.cshtml`, `Pedidos/Index.cshtml`, `Pedidos/Recibo.cshtml`, `Entradas/Historico.cshtml`, `Saidas/Historico.cshtml`.

**Restrições.**
- **NÃO mexer em controllers, services, models, viewmodels.** Só `.cshtml`.
- **NÃO remover** classes Tailwind utilitárias (`flex`, `gap-*`, `mt-*`); só substituir o wrapper de containers visuais.
- **Uma página = um commit.** Mensagem `refactor(ui-pages): aplica .panel/.card/.tbl em <Página> (DS)`.
- **Build verde antes de cada commit:** `dotnet build C:/rep/EasyStok/EasyStock.Web/EasyStock.Web.csproj`.

**Como mapear:**
- `<div class="bg-white rounded-xl shadow p-6">` → `<div class="panel">`
- Header de painel `<h2 class="text-lg font-semibold mb-4">X</h2>` → `<div class="panel-h"><h3>X</h3></div>` no topo do `.panel`.
- `<table class="...">...</table>` → `<div class="tbl-wrap"><table class="tbl">...</table></div>`.
- Cards de KPI no Dashboard: usar `.card` + `.t-mono` para "label" superior (eyebrow) + `.t-display` para o valor.
- Pills de status pedido (`Aguardando`, `Pronto`, etc.): `<span class="stp aguardando"><span class="ind"></span>Aguardando</span>`.

**Verificar visualmente:** rodar `cd C:/rep/EasyStok && dotnet run --project EasyStock.Web` em paralelo (porta 5000) e abrir cada página migrada antes/depois.

**Definição de pronto:** todas as 12 páginas migradas, build verde, console sem erros, layouts respiram (não amassados), KPIs e tabelas legíveis em desktop e mobile (DevTools 375px).

---

## Prompt 2 — Reescrita do mobile.css para 7 breakpoints DS

**Escopo.** O arquivo `C:/rep/EasyStok/EasyStock.Web/wwwroot/css/mobile.css` (~26KB, ~841 LOC) hoje tem só `@media (max-width: 768px)` e foi escrito antes do DS. Reescrever alinhando aos 7 breakpoints do DS (vide `docs/refactor-ui-2026-05/DS_BREAKPOINTS.md` se existir, senão verificar `app-shell.css` linhas finais):

| Breakpoint | Comportamento |
|---|---|
| ≤1280px | Sidebar de produtos (ficha 380px) vira full-width abaixo da tabela |
| ≤1180px | Sidebar app vira icon-rail 72px (texto oculto, tooltip on hover) |
| ≤1100px | Topbar wraps, search full-width |
| ≤980px | Op-grid vai a 2 cols |
| ≤920px | Hero/type-grid vai a 1 col |
| ≤820px | Logo-rules e type-grid 1 col |
| ≤760px | Mobile real: sidebar horizontal scrollable como chips, tabelas com scroll-x via `.tbl-wrap` |

**Restrições.**
- **Manter compat de classes** `.app-sidebar`, `.app-topbar`, `.app-main`, `.ni`, `.tbl-full` etc. — só ajustar comportamento por breakpoint.
- **Não remover** customizações específicas de mobile (touch targets, `-webkit-overflow-scrolling`).
- **Um commit final** com mensagem `refactor(mobile-css): alinha aos 7 breakpoints do DS`.

**Verificar:**
- Build verde + tailwind.dist.css regenera.
- Em DevTools, redimensionar em todos os breakpoints e checar se sidebar colapsa, topbar wraps, tabelas têm scroll.

**Definição de pronto:** mobile.css reescrito (LOC pode crescer ~10–20%), build verde, smoke manual em 5 viewports (375, 760, 980, 1180, 1280) sem layout quebrado.

---

## Prompt 3 — DTOs tipados no EasyStock.Admin

**Escopo.** O `EasyStock.Admin/Services/AdminApiClient.cs` retorna `JsonElement` cru e as Pages parseiam com `.TryGetProperty(...)`. Criar DTOs tipados em `EasyStock.Admin/Services/Models/` para os 5 endpoints mais usados:

1. `Dashboard` — `api/admin/dashboard` (badges).
2. `Tenants` — `api/admin/tenants` (lista) e `api/admin/tenants/{id}` (detail).
3. `Tickets` — `api/admin/tickets` (lista) e `api/admin/tickets/{id}` (detail).
4. `Planos` — `api/admin/planos`.
5. `Cupons` — `api/admin/cupons`.

**Como descobrir os shapes:**
1. Rodar a API local: `cd C:/rep/EasyStok && dotnet run --project EasyStock.Api`.
2. Ler `https://localhost:7100/swagger/v1/swagger.json` ou inspecionar os controllers em `EasyStock.Api/Controllers/Admin*.cs`.
3. Para cada endpoint, criar o `record` correspondente em `EasyStock.Admin/Services/Models/<Nome>Dto.cs`.

**Como aplicar:**
- Adicionar métodos genéricos `GetDtoAsync<T>(string path)` em `AdminApiClient` que use `JsonSerializer.Deserialize<T>(stream)` com `JsonSerializerOptions.PropertyNameCaseInsensitive = true`.
- Substituir nas Pages (`Pages/Index.cshtml.cs`, `Pages/Tenants/Index.cshtml.cs`, etc.) o uso de `JsonElement` pelos DTOs.
- **Manter** o método antigo `GetAsync<JsonElement>` para chamadas que ainda não migraram (zero breaking change).

**Restrições.**
- **NÃO alterar a API** (producer). Só consumer.
- **Um commit por DTO** (5 commits no total). Mensagem `refactor(admin-dtos): tipa contrato <Endpoint>`.
- **Build + testes verdes a cada commit.**

**Verificação manual:** abrir `/`, `/Tenants`, `/Tickets`, `/Planos`, `/Cupons` e confirmar que os dados continuam aparecendo.

**Definição de pronto:** 5 DTOs criados, 5 Pages migradas, suite de testes (`dotnet test EasyStok.sln`) verde, smoke manual ok.

---

## Prompt 4 — Lighthouse a11y audit

**Escopo.** Rodar Lighthouse a11y em 5 páginas do Web e gerar relatório em `docs/refactor-ui-2026-05/LIGHTHOUSE_A11Y.md`.

**Páginas:** `/dashboard`, `/produtos`, `/caixa`, `/auth/login`, `/admin/tenants`.

**Setup:**
1. `cd C:/rep/EasyStok && dotnet run --project EasyStock.Web` e `dotnet run --project EasyStock.Admin` (terminais separados).
2. Garantir que o usuário de teste consegue logar (criar via `/auth/registrar` se necessário).
3. Instalar Lighthouse CLI: `npm i -g lighthouse@^11`.
4. Para cada página: `lighthouse http://localhost:5000/dashboard --only-categories=accessibility --output=json --output-path=./report-dashboard.json`.

**Como reportar.** Em `docs/refactor-ui-2026-05/LIGHTHOUSE_A11Y.md`:

```markdown
# Lighthouse a11y — relatório

| Página | Score | Falhas | Avisos |
|---|---|---|---|
| /dashboard | 95 | … | … |
…

## Falhas a corrigir
1. **<id-da-regra>** em <página>: descrição + arquivo:linha + sugestão de fix.
…
```

**Restrições.**
- **Apenas relatar**, NÃO alterar código nesse prompt — gerar uma lista priorizada para um futuro PR.
- Score-alvo: ≥95 em todas as páginas. Marcar páginas <90 como prioridade alta.

**Definição de pronto:** relatório markdown com 5 scores, lista de issues priorizada, e arquivo `lighthouse-reports/*.json` salvos para auditoria.

---

## Prompt 5 — Content-Security-Policy (CSP) baseline

**Escopo.** Adicionar header CSP em `EasyStock.Web/Program.cs` (e `EasyStock.Admin/Program.cs` se houver pipeline equivalente), permitindo:

- `default-src 'self'`
- `script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net` (Alpine.js + Chart.js)
- `style-src 'self' 'unsafe-inline' https://fonts.googleapis.com` (Tailwind/CSS inline + fontes)
- `font-src 'self' https://fonts.gstatic.com data:`
- `img-src 'self' data: blob:`
- `connect-src 'self' <ApiSettings:BaseUrl>` (precisa ler config)
- `frame-ancestors 'self'` (já coberto por X-Frame-Options)

**Restrições.**
- **Modo `report-only` no primeiro deploy** (`Content-Security-Policy-Report-Only`) por 1 semana antes de virar bloqueante. Adicionar config flag `Csp:Mode = "ReportOnly" | "Enforce"` em `appsettings.json`.
- **Endpoint de report:** criar `/csp-report` que loga violações em ILogger (não persiste, evita storage explosion).
- **NÃO tocar** em outros middlewares.
- **Um commit:** `feat(security): adiciona CSP baseline em modo report-only`.

**Verificação:**
1. Abrir DevTools → Network → ver header `Content-Security-Policy-Report-Only` em response.
2. Console não deve ter blocked-by-CSP em nenhuma página migrada.
3. Verificar que Alpine, Chart.js, fonts carregam normalmente.

**Definição de pronto:** header presente, configurável, endpoint de report funcional, smoke em 3 páginas sem violations.

---

## Como executar uma tarefa via Agent SDK

```bash
# Exemplo para Haiku rodando o Prompt 1, página por página em loop
for page in Dashboard Produtos Estoque Lotes Caixa Pedidos; do
  claude code --model claude-haiku-4-5-20251001 \
    --prompt "Aplique o Prompt 1 do arquivo docs/refactor-ui-2026-05/HAIKU_PROMPTS.md \
              especificamente para a página $page. Trabalhe em isolation=worktree \
              e abra um PR com o nome refactor(ui-pages): $page DS."
done
```

Ou via UI: cada prompt pode ser colado como nova tarefa do Claude Code, o agente vai ter todo o contexto necessário inline.
