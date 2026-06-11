# Issues pendentes — criar no GitHub quando o `gh` reautenticar

> O token do `gh` está inválido no keyring (`gh auth login` é interativo). A rede ao
> GitHub funciona (git push/pull ok). Conforme §4.5 P2 e o GO de 2026-06-10, registro
> aqui as issues que precisam ser abertas e referencio nos commits assim que existirem.
>
> Trabalho em andamento: plano ADR-0031 (cardápio do tenant no Web + estabilização).
> Para criar: `gh auth login -h github.com` e depois `gh issue create ...`.

## Incidente (já resolvido)
- **#567** (já fechado pelo commit `18c135da`) — API em crash loop por migration do cardápio
  (snake_case vs PascalCase no SQL cru). Postmortem em
  `docs/dev/incidentes/2026-06-10-cardapio-migration-crashloop.md`.

## A criar

### 1. security(api): escopo de empresaId no cardápio admin — IDOR cross-tenant `priority:p0` `nfe`/`web-api`/`storefront`
**Contexto:** `AdminStorefrontCardapioController` (`[Authorize(Policy="Admin")]`) não verificava
se o storefront pertence à empresa do usuário. Um Admin tenant da empresa A podia listar/criar/
editar/togglar o cardápio de qualquer storefront por GUID.
**Entrega:** `ICardapioItemRepository.GetByIdAndScopeAsync` + escopo `EmpresaId` nos use cases admin +
controller resolve empresaId do claim (SuperAdmin = null). 404 (não 403) em storefront alheio.
**Aceite:** Admin de outra empresa recebe 404; SuperAdmin segue cross-tenant. (Fase 1)

### 2. feat(web): gestão de cardápio do tenant no EasyStock.Web `enhancement` `web-api`/`storefront`
**Contexto:** a gestão de cardápio existe no EasyStock.Admin (SuperAdmin). O tenant (ex: Casa da Babá)
precisa gerenciar o próprio cardápio no produto (EasyStock.Web), não no painel interno.
**Entrega:** `TenantVitrineCardapioController` (`/api/minha-vitrine/cardapio`) + `CardapioController`/
`CardapioService` + Views no Web (espelha LojasController). Item no menu lateral.
**Aceite:** tenant cria item avulso, publica, vê no site público; isolamento por empresa. (Fases 1+4)

### 3. test: cobertura avulso do cardápio (unit + integração) `enhancement` `web-api`/`storefront`
**Contexto:** faltam testes do modo avulso (Application) e testes de integração que apliquem a
migration contra Postgres real — lacuna que causou o #567.
**Entrega:** unit avulso (Adicionar + conversão centavos no Listar), integração no
`EasyStock.Api.IntegrationTests` (GET menu avulso, mix order sem NRE, POST nome duplicado 400, scope 404).
**Decisão registrada:** manter `EasyStok.CI.slnf` sem os `*IntegrationTests`; o pipeline roda
integração por path (`azure-pipelines.yml`). Documentar, não alterar. (Fase 2)

### 4. ux: design-system + ux-copy do cardápio no Web `enhancement` `web-api`
**Contexto:** entrega premium SaaS exige UX de ponta e copy PT-BR pensada para a Thatiane.
**Entrega:** `/design-system` (auditar tokens/componentes do Web p/ a superfície do cardápio) +
`/ux-copy` (copy-deck: labels, erros 23505/23514, empty states, CTAs, onboarding). Docs em
`docs/design/cardapio-web-*.md`. (Fase 3)
