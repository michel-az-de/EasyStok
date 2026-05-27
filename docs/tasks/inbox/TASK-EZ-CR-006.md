# TASK-EZ-CR-006 — Splittar AdminNotificacoesController (20 deps)

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-8)
**Prioridade:** P2
**Esforco:** M
**Status:** inbox

## Objetivo

Quebrar `AdminNotificacoesController` (249 linhas, 20 dependencias no construtor) em 3 controllers especializados respeitando SRP.

## Numeros (validados por leitura)

Construtor atual: 6 repositorios + 1 `ICurrentUserAccessor` + 13 use cases = **20 deps**.

## Escopo

[EasyStock.Api/Controllers/AdminNotificacoesController.cs](../../../EasyStock.Api/Controllers/AdminNotificacoesController.cs)

## Plano

Dividir em 3 controllers, todos com rota base `/api/admin/notificacoes/...` para nao quebrar clientes:

### `AdminNotificacoesTemplatesController` — `/api/admin/notificacoes/templates/*`
- Deps: `ITemplateRepository`, `IVariavelTemplateCatalogoRepository`, `ICurrentUserAccessor`
- UseCases: `CriarTemplateUseCase`, `AtualizarTemplateUseCase`, `AprovarTemplateUseCase`, `PreviewTemplateUseCase`, `PreviewDraftTemplateUseCase`

### `AdminNotificacoesRotinasController` — `/api/admin/notificacoes/rotinas/*`
- Deps: `IRotinaRepository`, `IBloqueioNotificacaoRepository`, `ICurrentUserAccessor`
- UseCases: `CriarRotinaUseCase`, `AtualizarRotinaUseCase`, `AtivarRotinaUseCase`, `DesativarRotinaUseCase`, `AtivarKillSwitchUseCase`, `RemoverKillSwitchUseCase`

### `AdminNotificacoesBroadcastController` — `/api/admin/notificacoes/broadcast/*`, `/canais/*`, `/consentimentos/*`, `/logs/*`
- Deps: `IConfiguracaoCanalRepository`, `IConsentimentoRepository`, `ICurrentUserAccessor`
- UseCases: `ListarLogsEnvioUseCase`, `EnviarNotificacaoManualUseCase`

## Definicao de Pronto

- [ ] 3 novos controllers criados, cada um com <=8 deps
- [ ] `AdminNotificacoesController` original deletado OU virou agregador minimo
- [ ] Rotas REST mantidas (zero breaking change para clientes)
- [ ] `dotnet build` verde
- [ ] Tests passando + novos tests por controller
- [ ] PR mergeado

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-8)
