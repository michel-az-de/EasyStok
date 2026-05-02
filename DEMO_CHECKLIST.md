# EasyStock — Checklist de Apresentação

## Status atual

- **Build:** 0 erros em toda a solução
- **Testes:** 384/384 verdes
  - Application: 153/153
  - Domain: 111/111
  - Api.UnitTests: 120/120 (estavam 43 falhando antes do fix)
- **Commits da revisão:**
  - `c5d2ad6` — FIFO bug, DiagnosticoController auth, Produtos paginação/lógica
  - `204d286` — NRE em Busca e Anuncios

## Fixes aplicados nesta sessão

| # | Severidade | Arquivo | Problema | Solução |
|---|-----------|---------|----------|---------|
| 1 | 🔴 P0 | `RegistrarSaidaEstoqueUseCase.cs:151` | `SingleOrDefault()` estourava em FIFO com >1 lote | Restringir a `ItemEstoqueId.HasValue` |
| 2 | 🔴 P0 | `DiagnosticoController.cs` | 21x `[AllowAnonymous]` em prod | Trocado para `[Authorize]` |
| 3 | 🔴 P0 | `BuscaController.cs:17` | `Data!.Select` NRE risk | Null check antes |
| 4 | 🔴 P0 | `AnunciosController.cs:33` | `Data!.Select` NRE risk | Null check antes |
| 5 | 🟠 P1 | `ProdutosController.cs:150` | Lógica invertida ignorava falhas | `if (!result.Success)` direto |
| 6 | 🟠 P1 | `ProdutosController.cs:54` | `totalPages = 1` em busca | Cálculo correto + slice |
| 7 | 🟡 P2 | `Produtos/Form.cshtml` | Comentários "BUG A/C/D" confusos | Limpos |
| 8 | 🟡 P2 | `VendaControllerTests.cs` + `AuthControllerTests.cs` | Construtores desatualizados | Atualizados (destrava CI) |

## Já validado (não precisava mexer)

- ✅ Toast: auto-dismiss, hover-pause, progress bar, undo button (`toast.js`)
- ✅ Polling de notificações: pausa em `document.hidden`, resume em visibilitychange (45s)
- ✅ TokenRefreshHandler com retry automático em 401
- ✅ SessionRestoreMiddleware mantém login após restart
- ✅ Antiforgery automático em todos POST/PUT/PATCH/DELETE
- ✅ Headers de segurança (X-Content-Type-Options, X-Frame-Options, Permissions-Policy)
- ✅ Multi-tenancy: todos endpoints incluem `empresaId`
- ✅ Rate limiting (auth 10/min, geral 200/min)
- ✅ Health checks `/health`, `/health/live`, `/health/ready`

## Smoke Test Golden Path (executar 1h antes da demo)

Subir API e Web local (terminais separados):

```bash
# Terminal 1 — API
dotnet run --project EasyStock.Api

# Terminal 2 — Web
dotnet run --project EasyStock.Web
```

Abrir Chromium com **DevTools (F12) Console + Network ABERTOS** durante todo o fluxo. Qualquer 4xx/5xx no Network ou erro vermelho no Console = bloqueador.

### Sequência

| Passo | URL/Ação | Verificação |
|-------|----------|-------------|
| 1 | `/auth/login` | `felipe@easystock.com` / `Admin@2026!Secure` — login OK, sem erros |
| 2 | `/auth/selecionar-loja` | Dropdown lista lojas, clica numa, redirect dashboard |
| 3 | `/dashboard` | KPIs renderizam, gráficos carregam, sino com badge |
| 4 | Toggle dark/light | `/auth/theme` POST — mudança imediata, persiste no F5 |
| 5 | `/produtos` | Lista paginada, filtros funcionam |
| 6 | `/produtos?search=galaxy` | **Busca paginada** — confirma fix da linha 54 |
| 7 | `/produtos/novo` | Form abre, upload foto OK, preview com badge "Capa" |
| 8 | Editar um produto | Update salva, redirect com toast |
| 9 | `/estoque` | Lista de itens com qty/status |
| 10 | `/entradas/nova` | Cria entrada de lote (data, qty, custo) |
| 11 | `/saidas/nova` ou `/estoque/saida` (AJAX) | **Saída por produto, FIFO** — confirma fix do bug crítico, conferir que consumiu do lote mais antigo primeiro |
| 12 | `/pedidos/json` | POST cria pedido com cliente + itens |
| 13 | Mudar status do pedido | `aguardando → preparando → pronto → entregue` |
| 14 | `/caixa/abrir` | POST abre caixa |
| 15 | `/caixa/movimentos` | Adiciona despesa/receita |
| 16 | `/caixa/fechar` | Fecha caixa do dia |
| 17 | `/fornecedores` | CRUD fornecedor + endereço/telefone |
| 18 | `/fornecedores/pedidos-abertos` | Cria pedido fornecedor, marca recebido |
| 19 | `/notificacoes` | Marca como lida (badge decrementa) |
| 20 | `/auth/logout` | Logout limpa sessão |
| 21 | Re-login → `/diagnostico` | **Confirma exigência de auth** (era anônimo antes do fix) |

## Riscos conhecidos durante a demo

1. **API local vs Azure:** `appsettings.Development.json` aponta para `localhost:7039`. Se rodar com `ASPNETCORE_ENVIRONMENT=Production`, vai pra Azure (`easystock-api-dfdjgsfwaqhkgvf9...`). Confirmar antes qual usar.

2. **Mobile (Casa da Baba):** decidido **não incluir** na demo. PWA é independente, paleta laranja, sem JWT. Se inevitável: testar APK em `builds/` antes; se não funcionar 100%, omitir.

3. **DiagnosticoController autenticado:** se a demo precisar mostrar `/diagnostico` para validar conectividade da API, faça login primeiro (não é mais anônimo).

4. **Notificação polling:** roda a cada 45s. Em abas múltiplas, cada uma poll. Não é um problema, mas se Network tab encher de requisições é normal.

5. **Dados seed:** se o banco for resetado, rodar `dotnet ef database update` em `EasyStock.Infra.Postgre` ou deixar a API criar via `Program.cs` (migration automática). O seed em `EasyStock.Api/Data/SeedData.cs` cria empresa, lojas, produtos e usuários.

## Backup plan se algo quebrar na demo

- **Login falha:** verificar JWT_SECRET_KEY (min 32 chars) na env; verificar se API está respondendo em `/health`.
- **404 ao salvar produto:** verificar se Web está apontando pra API correta (Network → veja Request URL).
- **Saída FIFO retorna lista vazia:** confirmar que o produto tem ItemEstoque com `Status=Ok` e `QuantidadeAtual > 0`. Workaround: usar saída direta por `ItemEstoqueId`.
- **Tema não muda:** localStorage `easystock-theme` corrompido — abrir DevTools Application → Local Storage → limpar.
- **Caixa não abre:** verificar `/caixa/abrir` no Network — pode haver caixa aberto de dia anterior; usar "reabrir" no histórico.

## Itens P2 (post-demo)

- Mascarar PII (email/telefone/CPF) em DTOs
- Idempotency em `POST /api/pedidos`
- Reduzir Swagger cache de 1h para 5min
- Migrações em init-container (não em app startup)
- Migrar fotos do mobile (base64) para Blob Storage
- HTTPS no PWA Casa da Baba (Service Worker registra)
- Rate limiting em `/api/admin/*`
- Revisar hierarquia: `Gerente DELETE /api/lojas/{id}` deveria ser `Admin`
