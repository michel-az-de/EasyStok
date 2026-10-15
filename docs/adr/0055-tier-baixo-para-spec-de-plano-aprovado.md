# ADR-0055 — Spec de plano aprovado entra no verde; CHANGELOG por fragmento

- Status: Aceito
- Data: 2026-09-30
- Amplia: ADR-0043 (policy v4.0), no item de tiers de merge. O resto da ADR-0043 continua valendo.
- Relacionados: issue #1175; `docs/plan/atendimento-whatsapp/README.md`

## Contexto

Medido em 2026-09-30: 15 PRs abertas. 11 esperavam a label `aprovado`, porque toda `feat` é tier
ALTO. Das 5 conflitantes, 3 conflitavam só no topo do `CHANGELOG.md` e 2 no registro de serviços do
atendimento. Enquanto a PR espera a aprovação, o master anda, a PR fica velha e alguém precisa
trazer o master de novo. A aprovação que importa já aconteceu antes, quando o Felipe aprovou o plano
e a spec, com escopo, testes e aceite escritos.

## Decisão

1. **Tier BAIXO para spec de plano aprovado.** PR que implementa uma spec com S-número (ou P-número)
   de um plano mergeado em `docs/plan/**`, dentro do escopo da spec, entra por squash quando o CI e o
   review ficam verdes, sem esperar `aprovado`. O título ou o corpo da PR cita a spec.
2. **Continua tier ALTO** (espera `aprovado`), mesmo dentro de spec:
   - migração EF (`Migrations/`);
   - RLS, autenticação e autorização (policies, permissões, JWT, `SetTenantOnConnectionInterceptor`);
   - policy e processo (`CLAUDE.md`, `AGENTS.md`, ADR, `.github/workflows/`);
   - `feat` ou `refactor` sem spec.
3. **CHANGELOG por fragmento.** Cada PR cria `changelog.d/<issue>.md` em vez de editar o topo do
   `CHANGELOG.md`. No release, `scripts/changelog/juntar.ps1` junta os fragmentos na seção da versão
   e apaga os arquivos. O formato fica em `changelog.d/README.md`.

## Alternativas descartadas

- **Manter tudo ALTO e aprovar em lote.** Foi o que gerou a fila: cada lote atrasado deixa as PRs
  conflitantes de novo.
- **Tier BAIXO para toda `feat`.** Perde o portão humano justamente onde o erro custa caro:
  migração, RLS e autenticação.
- **Gerar o CHANGELOG pelos commits.** Os títulos de commit não carregam o "o que muda para quem
  usa" que o CHANGELOG deste repositório registra.

## Consequências

- A fila de aprovação passa a ter só o que precisa de olho humano.
- PRs abertas antes desta ADR que editam `CHANGELOG.md` continuam válidas. Quem trouxer o master e
  conflitar no CHANGELOG move a entrada para `changelog.d/<issue>.md`.
- O merge automático continua dependendo de CI verde e da conferência de mergeabilidade no instante
  do merge (R20).
