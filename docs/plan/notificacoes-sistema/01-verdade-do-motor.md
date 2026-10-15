# M0 · Verdade do motor (S0, N0, N2, N1)

O M0 faz o motor de notificações parar de mentir e voltar a ler. Hoje nada passa por ele em produção e
eventos se perdem sem erro ([00-diagnostico.md](00-diagnostico.md)). As quatro specs abaixo foram
conferidas contra o código em `c2eb85ca` (.NET 10): cada achado cita `caminho:linha`, e o que o código
desmentiu está marcado. Decisão: [ADR-0057](../../adr/0057-notificacoes-de-plataforma.md), emenda da
ADR-0055. Issue da S0: #1344; as demais abrem a própria issue ao iniciar.

**Como ler as tabelas.** Prova: **código** = lido na linha citada; **teste** = teste que já existe no
repo e prova o mecanismo; **medir** = só a S0 fecha. **Corrigido** = premissa do plano que o código
desmente. **Novo** = achado desta conferência. Caminhos abreviados: `Domain/`, `App/`, `Api/`,
`Postgre/`, `Notifications/`, `Async/`, `Worker/` valem `EasyStock.Domain/`, `EasyStock.Application/`,
`EasyStock.Api/`, `EasyStock.Infra.Postgre/`, `EasyStock.Infra.Notifications/`, `EasyStock.Infra.Async/`
e `EasyStock.Worker/`. Teste leva o caminho inteiro.

## Ordem

```
S0 medição + ADR-0057 + guarda do enum
 └─► N0 fecha o /register anônimo           (antes de qualquer Smtp__* na API)
      └─► N2 verdade do envio               (Simulado, Indeterminado, falha permanente, categoria Seguranca)
           └─► N1 destrava o motor          (bypass pela porta, claim, escopo por item, quarentena, health)
                └─► M1 (N3, e-mail real)
S0 também alimenta a N1: os prazos da quarentena saem da medição.
```

---

## S0 · Medição e plano · issue aberta ao iniciar

**Problema.**

| # | Achado | Onde | Prova |
|---|---|---|---|
| 1 | O diagnóstico de produção apoia-se em fatos fora do repo: o papel `easystock` ser `NOBYPASSRLS` (compose da VPS) e o backlog real. A única medição de papel no repo é de 22/05, na Render, com outro papel (`easystok_user`: sem superuser, sem BYPASSRLS, 60 tabelas com FORCE) | `docs/dev/incidentes/2026-05-22-rls-prod-role-status.md`; `docs/plan/notificacoes-sistema/00-diagnostico.md:17` | código |
| 2 | O script de medição já está versionado: 8 seções em transação `READ ONLY`, com `SET LOCAL app.bypass_rls` para ver todas as empresas pelo mesmo mecanismo do app. As colunas citadas existem no modelo EF | `scripts/diagnostico/notificacoes-s0.sql:11-83`; colunas conferidas em `Postgre/Data/Configurations/Notifications/*` | código |
| 3 | Lacunas do script: (a) outbox `Pendente` por tipo e idade (o backlog de campanha mora no outbox: o evento nasce `Processado` e a mensagem com `ProximaTentativaEm` futuro); (b) outbox antigo ainda não anonimizado (inferência: o UPDATE do anonimizador sob RLS afeta 0 linhas); (c) linha global de canal com `CredenciaisCifradas` (pré-condição da policy da N1); (d) bloqueios ativos; (e) opcional: `outbox_evento_integracao` pendente (tabela anterior à RLS e loop do Worker sem bypass: mesmo risco, só medir) | `App/Services/Campanhas/EnfileiradorMensagensCampanha.cs:93-96,119`; `Postgre/Notifications/Maintenance/AnonimizarLogsAntigosService.cs:62-72`; `Domain/Entities/Notifications/ConfiguracaoCanal.cs:11`; `Worker/BackgroundServices/IntegrationOutboxBackgroundService.cs:89-94` | código |
| 4 | **Corrigido:** o enum não vai ao banco como int. `Tipo`, `TipoEvento`, `Status`, `Categoria` e `Canal` são texto (`varchar(40)` e `varchar(20)`) | `Postgre/Data/Configurations/Notifications/EventoNotificacaoConfiguration.cs:12,18`; `RotinaNotificacaoConfiguration.cs:14,20`; `TemplateNotificacaoConfiguration.cs:16`; `VariavelTemplateCatalogoConfiguration.cs:12`; `OutboxMensagemNotificacaoConfiguration.cs:21,31` | código |
| 5 | Efeito da correção: valor repetido compila (alias) e o EF grava `ToString()`, que devolve um só dos nomes (inferência pela documentação de `Enum.ToString`), então um tipo vira outro no banco; e renomear membro quebra a leitura das linhas antigas. Hoje são 47 membros, de 1 a 47, sem buraco nem repetição; o maior nome tem 28 de 40 caracteres | `Domain/Enums/Notifications/TipoEventoNotificacao.cs:3-76` | código, contagem por script |
| 6 | Status ou categoria novos não exigem migration: colunas `varchar(20)` sem CHECK nem índice parcial por status, e o trigger de NOTIFY só testa `'Pendente'` | `Postgre/Migrations/20260506221516_AddNotificationsCore.cs:111,266,474`; `OutboxMensagemNotificacaoConfiguration.cs:21` | código |
| 7 | O canário anti-skip dos testes de integração já existe: sem Docker no CI ele falha em vez de pular | `EasyStock.Infra.Postgre.IntegrationTests/HarnessCanaryTests.cs:19-31` | código |

**Abordagem.**
- **Medir antes de mexer.** O Felipe roda o script na VPS (`docker exec -i shared-postgres psql -U easystock -d easystock -v ON_ERROR_STOP=1 < notificacoes-s0.sql`), cola a saída na #1344 (só contagens, tipos e idades, sem dado pessoal) e roda no host os dois greps do rodapé (`42501` e as linhas do motor no `ez-worker`).
- **Fechar as lacunas** com as seções 9 a 12 (outbox `Pendente` por tipo do evento e faixa de idade; outbox com mais de 90 dias sem anonimizar; globais de canal com `CredenciaisCifradas`; bloqueios ativos) e a 13, opcional (`outbox_evento_integracao`).
- **Decidir pela medida.** A saída fecha estas decisões antes da N2 e da N1:

| Medida | Esperado | Se vier diferente |
|---|---|---|
| `rolsuper` e `rolbypassrls` do `easystock` | `f` e `f` | `t`: a hipótese central da N1 cai. PARE (R10) e reabra o diagnóstico |
| `relrowsecurity` e `relforcerowsecurity` das `notif_*` com `EmpresaId` | `t` e `t` | `f`: a RLS não vale ali e a policy da N1 seria inócua |
| Pendentes por tipo e idade (seções 3, 4 e 9) | backlog antigo | dimensiona os prazos da N1 e quanto expira na primeira rodada |
| `Processado` sem outbox (seção 5) | maior que 0 em `CaixaAbertoEsquecido` e nos de conta | confirma a cegueira dos produtores |
| `Enviado` com provider `stub` ou `smtp` sem `Smtp__*` (seções 6 e 7) | existe | confirma o falso sucesso da N2 |
| `42501` no log do `ez-worker` | maior que 0 se houver pedido agendado | 0 não refuta: o erro só nasce com pedido agendado elegível em `mobile_orders` |

- **Reservar valores do enum.** O comentário de reserva já é a convenção do arquivo (`TipoEventoNotificacao.cs:55-56`). Faixas novas, a partir de 48, no comentário do enum e no README:

| Faixa | Dono |
|---|---|
| 48 a 63 | N13 (catálogo: convite, incidente, prazo estourado, rotina agendada) |
| 64 a 79 | N10, N11 e N12 (correm em paralelo) |
| 80 em diante | livre: maior valor mais 1, nunca reaproveitar |

- **Guardar o contrato do enum** com três testes (valores únicos, nome cabe em 40 caracteres, membros publicados não mudam de nome nem de valor) e com a execução do script contra o schema real.
- ADR-0057 e README entram nesta PR (a ADR está como Proposto e passa a Aceito no merge com `aprovado`), mais `changelog.d/1344.md`.

**Leitura mínima.** `scripts/diagnostico/notificacoes-s0.sql`; `docs/plan/notificacoes-sistema/00-diagnostico.md` e `README.md`; `docs/adr/0055-tier-baixo-para-spec-de-plano-aprovado.md`; `Domain/Enums/Notifications/TipoEventoNotificacao.cs`; `Postgre/Data/Configurations/Notifications/EventoNotificacaoConfiguration.cs` e `OutboxMensagemNotificacaoConfiguration.cs`; `EasyStock.Infra.Postgre.IntegrationTests/PostgreSqlDatabaseFixture.cs` e `HarnessCanaryTests.cs`.

**Testes Red.** Não há Red de comportamento: as guardas passam sobre o estado de hoje, que é o ponto. Refutação (D08): mutar `LembreteVencido = 47` para `46` e ver `Valores_sao_unicos` falhar; renomear um membro e ver o golden falhar. O Red real é o do script, que ganha quatro seções.

| Teste | Falha hoje porque |
|---|---|
| `EasyStock.Domain.Tests/Enums/TipoEventoNotificacaoTests.cs::Valores_sao_unicos`<br>`::Nomes_cabem_na_coluna_de_40_caracteres`<br>`::Membros_publicados_nao_mudam_de_nome_nem_de_valor` | guardas: passam hoje |
| `EasyStock.Infra.Postgre.IntegrationTests/Notifications/ScriptS0Tests.cs::Script_roda_inteiro_no_schema_atual_e_nao_escreve` | o script devolve 8 conjuntos de resultado e a spec pede 12; e um `INSERT` acrescentado ao texto deve ser recusado pelo `READ ONLY` (25006) |

**Aceite.**
- [ ] **Dado** o script, **quando** o Felipe o roda na VPS, **então** a saída traz os 12 blocos, termina em `ROLLBACK` e nada é escrito.
- [ ] **Dado** a saída colada na #1344, **quando** a tabela de decisão é preenchida, **então** cada linha mostra "esperado" ou "diferente mais ação", e os prazos da N1 saem dela.
- [ ] **Dado** o enum, **quando** um PR repete valor, renomeia membro ou passa de 40 caracteres, **então** o teste falha.
- [ ] **Dado** README e ADR-0057, **quando** a PR é mergeada com `aprovado`, **então** a ADR vira Aceito e o README aponta as specs 01 a 06.

**Fora.** Qualquer mudança de comportamento do motor; os valores dos prazos (saem da medição e ficam na N1); corrigir o `IntegrationOutboxBackgroundService` (só medir).

**Depende de.** Nada: abre a cadeia (#1344).

**Tamanho.** P.

**Tier.** ALTO: ADR é policy e processo (`docs/adr/0055-tier-baixo-para-spec-de-plano-aprovado.md:24`). A R5 (`CLAUDE.md:69-71`) não se aplica: a PR só leva comentário no enum, testes, script e documentos.

**Rollback.** Reverter a PR. O script é somente leitura e o enum só ganha comentário e testes.

---

## N0 · Fechar /api/auth/register anônimo · issue aberta ao iniciar

**Problema.**

| # | Achado | Onde | Prova |
|---|---|---|---|
| 1 | A rota é anônima: sem `[Authorize]` e sem política de fallback no projeto. É criação de conta aberta à internet | `Api/Controllers/AuthController.cs:207-213`; `FallbackPolicy` ausente em `EasyStock.Api` (grep) | código |
| 2 | Enumera conta: e-mail já cadastrado responde 409 `BUSINESS_RULE_VIOLATION` com "Email ja cadastrado."; e-mail novo responde 200 com o id | `App/UseCases/CadastrarUsuario/CadastrarUsuarioUseCase.cs:21-26,79`; `Api/Observability/GlobalExceptionHandler.cs:169-174` | código |
| 3 | Manda e-mail a qualquer endereço quando o `BaseUrl` do corpo bate com a allowlist, que é `Auth:TrustedLinkOrigins` mais `Cors:AllowedOrigins`: basta o SMTP real (Onda 0) para a rota passar a enviar | `CadastrarUsuarioUseCase.cs:51-70`; `App/UseCases/Common/LinkBaseUrlResolver.cs:23-28` | código |
| 4 | O bucket `auth` permite 20 por minuto por IP: um IP sozinho pede mil envios (a cota diária de uma caixa Hostinger) em 50 minutos, e cada pedido deixa uma linha em `usuarios` sem empresa | `Api/Configuration/ApiServiceCollectionExtensions.cs:201-213`; `CadastrarUsuarioUseCase.cs:28-31`; cota em `docs/plan/notificacoes-sistema/00-diagnostico.md:48` | código |
| 5 | Nenhum cliente do repo chama a rota: Console, Web, PWA, Contracts, design, scripts, tests, k8s e workflows não a citam. O Swagger já manda criar a empresa e o primeiro usuário pelo suporte | grep em `EasyStock.Console/src`, `EasyStock.Web`, `EasyStock.Api/wwwroot`, `EasyStock.Contracts`, `design`, `scripts`, `tests`, `k8s` e `.github`; `Api/Configuration/SwaggerDocumentConfiguration.cs:52-55` | código |
| 6 | Só o `CadastrarUsuarioUseCase` emite `EmailConfirmationToken`: sem ele, `confirmar-email` fica sem emissor | `CadastrarUsuarioUseCase.cs:45`; `Api/Controllers/AuthController.cs:288-289` | código |
| 7 | **Novo:** a prova do 429 do `register` mora em `Api.IntegrationTests`, que está fora do `EasyStok.CI.slnf`, e está velha: espera bloqueio na 11ª chamada e o bucket permite 20 | `EasyStock.Api.IntegrationTests/AuthRateLimitTests.cs:76-126`; `EasyStok.CI.slnf` (sem o projeto); commit `9fb543a8` | código |

**Abordagem.**
- **Remover a rota e o código morto:** a ação `Register` e o parâmetro do construtor (`Api/Controllers/AuthController.cs:47,207-213`), `CadastrarUsuarioUseCase`, `CadastrarUsuarioCommand`, `CadastrarUsuarioCommandValidator` e o registro (`App/DependencyInjection/ServiceCollectionExtensions.Auth.cs:33`). A rota passa a responder como qualquer rota inexistente (404). Descartados: 410 e a flag `Auth:RegistroAnonimo:Habilitado`, que mantêm código morto e deixam a rota a um erro de digitação no `.env` de voltar quando o SMTP real entrar.
- **Ajustar os testes que a usam** (R8): `EasyStock.Api.UnitTests/Controllers/AuthControllerTests.cs:52,64` (monta o use case), `EasyStock.Application.Tests/Validators/ValidatorsCoverageTests.cs:88-114` (validador) e `AuthRateLimitTests`. O par login e register parte do limite antigo: a prova do 429 passa a usar `forgot-password` com 20, e roda local porque o projeto está fora da CI.
- **Travar a superfície anônima por reflexão** (molde `EasyStock.Api.UnitTests/Controllers/DiagnosticoAuthorizationTests.cs`): a lista de rotas sem `[Authorize]` do `AuthController` é fechada; rota anônima nova exige editar a lista, e isso aparece no diff.
- **Manter:** `Usuario.Criar`, `EmailConfirmationToken`, `ConfirmEmailUseCase` e `confirmar-email` (sem emissor depois da N0; a limpeza fica para a N9 ou a poda). `docs/api/openapi.json` (linhas 5 e 8447) é snapshot gerado e já defasado: regerar só se a PR mexer nele.
- **Operação:** a N0 entra no master e é publicada antes de qualquer `Smtp__*` na API (Onda 0, item 3).

**Leitura mínima.** `Api/Controllers/AuthController.cs`; `App/UseCases/CadastrarUsuario/*`; `App/DependencyInjection/ServiceCollectionExtensions.Auth.cs`; `App/UseCases/Common/LinkBaseUrlResolver.cs`; `Api/Configuration/ApiServiceCollectionExtensions.cs` (política `auth`); `EasyStock.Api.UnitTests/Controllers/AuthControllerTests.cs` e `DiagnosticoAuthorizationTests.cs`; `EasyStock.Application.Tests/Validators/ValidatorsCoverageTests.cs`; `EasyStock.Api.IntegrationTests/AuthRateLimitTests.cs`.

**Testes Red.**

| Teste | Falha hoje porque |
|---|---|
| `EasyStock.Api.UnitTests/Controllers/AuthControllerRotasTests.cs::Superficie_anonima_do_AuthController_e_a_lista_conhecida` (roda na CI) | `Api/Controllers/AuthController.cs:211` expõe `register` e a lista conhecida, de nove rotas, não o inclui |
| `EasyStock.Api.IntegrationTests/AuthRegisterRemovidoTests.cs::Post_register_responde_404_e_nao_cria_usuario` (local, fora da CI) | hoje responde 200 e cria a linha em `usuarios`, ou 409 com e-mail repetido |
| `EasyStock.Api.IntegrationTests/AuthRateLimitTests.cs::Forgot_password_apos_20_tentativas_no_mesmo_IP_retorna_429` (local, fora da CI) | substitui o par `Login_` e `Register_` atual, que espera bloqueio na 11ª chamada com o bucket em 20 |

**Aceite.**
- [ ] **Dado** o `AuthController`, **quando** o teste lista as rotas sem `[Authorize]`, **então** são as nove conhecidas (`login`, `google/config`, `google/login`, `lista-empresas`, `refresh`, `logout`, `forgot-password`, `reset-password`, `confirmar-email`) e `register` não está entre elas.
- [ ] **Dado** um POST anônimo em `/api/auth/register` com e-mail novo ou já cadastrado, **quando** a API responde, **então** a resposta é 404 nos dois casos, não sai e-mail e `usuarios` não ganha linha (local).
- [ ] **Dado** `login`, `lista-empresas`, `forgot-password`, `reset-password`, `refresh` e `google/login`, **quando** os testes existentes rodam, **então** seguem verdes.
- [ ] **Dado** o deploy, **quando** o Felipe confere em produção `POST /api/auth/register` com `{}`, **então** a resposta é 404, antes de escrever `Smtp__*` no `.env`.

**Fora.** `forgot-password` (enumeração, limites e tokens: N7 e N8); `confirmar-email` e `EmailConfirmationToken`; convite por link (N9); regerar o `openapi.json`.

**Depende de.** S0 (ADR-0057 e README entram nela; a N0 só os cita).

**Tamanho.** P.

**Tier.** ALTO: autenticação (`docs/adr/0055-tier-baixo-para-spec-de-plano-aprovado.md:23`) e R5 (`CLAUDE.md:69-71`: mais de 5 arquivos).

**Rollback.** `git revert` devolve a rota. Só vale enquanto a API não tiver SMTP real: com ele, a rota volta a ser o canhão de spam.

---

## N2 · Verdade do envio · issue aberta ao iniciar

**Problema.**

| # | Achado | Onde | Prova |
|---|---|---|---|
| 1 | Os stubs de WhatsApp e SMS devolvem sucesso e o outbox vira `Enviado` sem nada sair. O padrão dos dois providers é `stub` | `Notifications/WhatsApp/StubWhatsAppProvider.cs:26`; `Notifications/Sms/StubSmsProvider.cs:30`; `Postgre/Notifications/Dispatcher/NotificacoesDispatcherOrchestrator.cs:137-139`; `Worker/appsettings.json:12-19`; `Notifications/DependencyInjection/NotificationsInfraServiceCollectionExtensions.cs:44,64` | código |
| 2 | E-mail: sem `Smtp__*` o Worker usa o `ConsoleEmailService`, que devolve `Task.CompletedTask`, e o canal registra `Sucesso` com `ProviderUsado = "smtp"` fixo | `Worker/Program.cs:69-84`; `Async/DependencyInjection/ServiceCollectionExtensions.cs:201-229`; `Notifications/Email/SmtpEmailCanal.cs:54-57` | código |
| 3 | Stub e console vazam dado pessoal no log: telefone e 50 caracteres do corpo em Information, destinatário em Debug nos canais, e-mail no console. O teste LGPD (#1292) não cobre nenhum deles | `StubWhatsAppProvider.cs:17,23-24`; `StubSmsProvider.cs:21,27-28`; `Notifications/Sms/SmsCanal.cs:16-18`; `Notifications/WhatsApp/WhatsAppCanal.cs:16-18`; `Async/DependencyInjection/ServiceCollectionExtensions.cs:208`; `EasyStock.Api.UnitTests/Notifications/LogsDeEnvioSemDadoPessoalTests.cs:35-86` | código |
| 4 | `ResultadoEnvio` já é tipado e tem `FalhaPermanente`, mas só o Meta o preenche. SMTP, Twilio e WebPush devolvem sempre falha transitória: 550, 401 e "sem inscrição" são retentados 3 vezes, com backoff de 1, 5 e 30 min | `App/Ports/Output/Notifications/ICanalNotificacao.cs:23-30`; `Notifications/WhatsApp/MetaCloudWhatsAppProvider.cs:82-95`; `SmtpEmailCanal.cs:66-70`; `Notifications/Sms/TwilioSmsProvider.cs:52-58`; `Notifications/Push/WebPushCanal.cs:41,48,79-91`; `NotificacoesDispatcherOrchestrator.cs:156-163`; `Domain/Entities/Notifications/OutboxMensagemNotificacao.cs:22,123-129` | código |
| 5 | Retentativa aninhada no e-mail: Polly do canal (3 retentativas em 421, 221, Socket e IO) vezes o laço do serviço (3 tentativas) vezes o outbox (`MaxTentativas = 3`). Um 421 chega a 12 tentativas SMTP por rodada e 36 por mensagem; um 550, tratado como transitório, faz 9 em cerca de 6 minutos. **Corrigido:** o Polly de `NotificationResiliencePipelineFactory` não é o do e-mail, só embrulha o Twilio | `SmtpEmailCanal.cs:18-32`; `Async/SmtpEmailService.cs:17,42-61,103-108`; `Notifications/Resilience/NotificationResiliencePipelineFactory.cs:8-17`, usado só em `TwilioSmsProvider.cs:20` e `TwilioWhatsAppProvider.cs:20` | código |
| 6 | **Novo:** no Twilio, `EnsureSuccessStatusCode` fica dentro do pipeline: todo 4xx vira `HttpRequestException` e o Polly retenta 3 vezes (1, 2 e 4 s) antes de o outbox retentar de novo. Num 5xx o SMS pode já ter saído: duplica | `TwilioSmsProvider.cs:29-47`; `Notifications/WhatsApp/TwilioWhatsAppProvider.cs:29-47`; `NotificationResiliencePipelineFactory.cs:12-15` | código |
| 7 | **Novo:** timeout do Meta sobe como `TaskCanceledException`, que o filtro do provider não captura, e a mensagem volta a `Pendente`. O cliente já evita retry no POST para não duplicar (#1292), e o dispatcher o reintroduz | `MetaCloudWhatsAppProvider.cs:92`; `Integrations/WhatsApp/WhatsAppCloudClient.cs:11-17` | código |
| 8 | O segredo da categoria de segurança fica em claro: corpo e metadados no outbox, payload no evento. O anonimizador (90 dias) só troca destinatário e corpo, ignora payload e metadados, e filtra `Status != Pendente`. A categoria `Seguranca` não existe | `AnonimizarLogsAntigosService.cs:62-72`; `App/Services/Notifications/NotificationsHostingOptions.cs:47`; `EventoNotificacaoConfiguration.cs:15`; `OutboxMensagemNotificacao.cs:39`; `Domain/Enums/Notifications/CategoriaConteudoNotificacao.cs:3-8` | código |
| 9 | Status e categoria são texto: `Simulado`, `Indeterminado` e `Seguranca` não pedem migration. Mas a conciliação da campanha manda qualquer status desconhecido para `Falhou` | `OutboxMensagemNotificacaoConfiguration.cs:21,31`; `App/UseCases/Campanhas/ProcessarCampanhaUseCase.cs:71-84` | código |
| 10 | O nome `ConsoleEmailService` é contrato: o diagnóstico decide "SMTP configurado?" pelo nome da classe | `Api/BackgroundServices/DiagnosticoEmailReportJob.cs:60`; `Api/Controllers/DiagnosticoController.cs:237` | código |
| 11 | `MarcarEmEnvio()` existe e ninguém o chama, e `ResultadoEnvio` não carrega o id do provider: o Meta tem o `wamid` e o descarta | `OutboxMensagemNotificacao.cs:106-109`; `MetaCloudWhatsAppProvider.cs:62-80` | código |

**Abordagem.**
- **Um desfecho tipado, aditivo.** `ResultadoEnvio` ganha `Desfecho` (`Enviado`, `Simulado`, `FalhaTransitoria`, `FalhaPermanente`, `Indeterminado`) e `IdExterno`. O construtor atual continua valendo (R8: 14 arquivos o usam): `Desfecho` deriva de `Sucesso` e `FalhaPermanente`, e as fábricas `Simulado` e `Indeterminado` o fixam. Para quem está fora do motor, `Simulado` segue como `Sucesso` (`Notifications/Atendimento/CanalSms.cs:25`, `App/Events/Storefront/Handlers/EnviarLinkAvaliacaoWhatsAppHandler.cs:42`); descartado tratá-lo como falha, o que quebraria o desenvolvimento e o S37. Só o dispatcher grava `Simulado`.
- **Status e categoria, sem migration.** `StatusOutbox.Simulado` e `.Indeterminado`; `CategoriaConteudoNotificacao.Seguranca`. `OutboxMensagemNotificacao` ganha `MarcarSimulado`, `MarcarIndeterminado` e `PurgarSegredos`; `MarcarEmEnvio` fica para a N1. `Expirado` também fica para a N1.
- **Stub e console dizem a verdade.** Os stubs de WhatsApp e SMS, e o canal de e-mail quando o serviço é o console, devolvem `Simulado` com o provider real (`stub`, `console`), nunca `smtp`. O canal reconhece o simulado por uma interface marcadora que o `ConsoleEmailService` implementa, não pelo nome da classe, que não pode mudar. O log de envio grava o provider real e `Sucesso = false` com `ErroDetalhado = "simulado"`, sem coluna nova. Nenhum deles, nem `SmsCanal` e `WhatsAppCanal`, loga destinatário ou corpo: só o `OutboxId`.
- **Classificar por protocolo.** Permanentes: HTTP 4xx exceto 408 e 429, e SMTP 5xx (`SmtpException.StatusCode >= 500`). Transitórios: HTTP 5xx, 408, 429, rede e SMTP 4xx, para e-mail, push e in-app. **Corrigido:** o plano diz "4xx/5xx permanente"; vale por protocolo, e não vale para 5xx HTTP nem para 4xx SMTP. WhatsApp e SMS: timeout, queda de conexão e 5xx viram `Indeterminado` (o `TaskCanceledException` por timeout, que não é cancelamento do chamador, é capturado no provider). WebPush: 400, 401, 403 e 413 permanentes; 404 e 410 desativam a inscrição como hoje; "sem inscrição ativa" e chave VAPID ausente são permanentes.
- **Uma única camada de retentativa: o outbox** (backoff de 1, 5 e 30 min, `MaxTentativas = 3`, uma linha em `notif_logs_envio` por tentativa). Saem o Polly do `SmtpEmailCanal`, o laço do `SmtpEmailService` e o Polly do Twilio (a fábrica deixa de existir e `EnsureSuccessStatusCode` vira leitura do status). `MailboxUnavailable` (550) sai da lista de transitórios. Os defeitos do `SmtpClient` (sem 465, singleton, sem timeout) são da N3.
- **Categoria Seguranca.** `ResolvedorCanal` a trata como `Transacional` no consentimento (`App/Services/Notifications/ResolvedorCanal.cs:72`) e o dispatcher marca `BypassConsentimento` (`NotificacoesDispatcherOrchestrator.cs:150,172`). Ao ficar terminal (qualquer status fora de `Pendente` e `EmEnvio`), a transição do domínio chama `PurgarSegredos`: o corpo vira `[apagado]` e assunto e metadados zeram. O `PayloadJson` do evento vira `{}` no mesmo commit em que termina a última mensagem aberta dele, porque o fallback de canal ainda o relê (`NotificacoesDispatcherOrchestrator.cs:226`). O destinatário fica até o anonimizador.
- **Conciliação da campanha.** `Simulado`, `Expirado` e `Indeterminado` caem no `default` de `ProcessarCampanhaUseCase` e contam como falha, o que é o desejado (nada saiu, ou não há como confirmar). O teste fixa isso.
- **Dispatcher.** Mapeia o desfecho: `Enviado`, `Simulado`, falha transitória (backoff), falha permanente (sem reagendar, com fallback de canal) e `Indeterminado` (terminal, sem fallback, para não duplicar entre canais).

**Leitura mínima.** `App/Ports/Output/Notifications/ICanalNotificacao.cs`; `Domain/Entities/Notifications/OutboxMensagemNotificacao.cs` e `EventoNotificacao.cs`; `Domain/Enums/Notifications/StatusOutbox.cs` e `CategoriaConteudoNotificacao.cs`; `Postgre/Notifications/Dispatcher/NotificacoesDispatcherOrchestrator.cs`; em `Notifications/`: `Email/SmtpEmailCanal.cs`, `Sms/StubSmsProvider.cs`, `Sms/SmsCanal.cs`, `Sms/TwilioSmsProvider.cs`, `WhatsApp/StubWhatsAppProvider.cs`, `WhatsApp/WhatsAppCanal.cs`, `WhatsApp/TwilioWhatsAppProvider.cs`, `WhatsApp/MetaCloudWhatsAppProvider.cs`, `Push/WebPushCanal.cs`, `Resilience/NotificationResiliencePipelineFactory.cs`; `Async/SmtpEmailService.cs` e `Async/DependencyInjection/ServiceCollectionExtensions.cs` (linhas 195-229); `App/Services/Notifications/ResolvedorCanal.cs`; `App/UseCases/Campanhas/ProcessarCampanhaUseCase.cs` (61-88); `Postgre/Notifications/Maintenance/AnonimizarLogsAntigosService.cs`; `EasyStock.Api.UnitTests/Notifications/LogsDeEnvioSemDadoPessoalTests.cs`.

**Testes Red.** Quando o Red é de compilação, o executor cria só a assinatura (corpo `NotImplementedException`) para a falha ser de comportamento. Os de dispatcher usam a conexão de superusuário (`fixture.ConnectionString`), para falharem pelo motivo próprio, sem depender da N1.

| Teste | Falha hoje porque |
|---|---|
| `EasyStock.Domain.Tests/Entities/Notifications/OutboxMensagemNotificacaoTests.cs::MarcarSimulado_guarda_o_provider_e_nao_preenche_EnviadoEm`<br>`::Seguranca_ao_terminar_apaga_corpo_assunto_e_metadados`<br>`::Operacional_ao_terminar_nao_apaga_nada` | os membros e a categoria não existem |
| `EasyStock.Domain.Tests/Entities/Notifications/EventoNotificacaoTests.cs::PurgarPayload_troca_o_json_por_objeto_vazio` | o membro não existe |
| `EasyStock.Application.Tests/Services/Notifications/ResolvedorCanalTests.cs::Seguranca_ignora_consentimento_como_transacional` | a categoria não existe; `App/Services/Notifications/ResolvedorCanal.cs:72` só conhece `Transacional` |
| `EasyStock.Application.Tests/UseCases/Campanhas/ProcessarCampanhaUseCaseTests.cs::Simulado_expirado_e_indeterminado_contam_como_falha` | os status não existem (guarda do `default` de `:71-84`) |
| `EasyStock.Api.UnitTests/Notifications/LogsDeEnvioSemDadoPessoalTests.cs::StubWhatsAppNaoLogaTelefoneNemCorpo`<br>`::StubSmsNaoLogaTelefone`<br>`::SmsCanalNaoLogaTelefone`<br>`::WhatsAppCanalNaoLogaTelefone`<br>`::ConsoleEmailNaoLogaDestinatario` | os logs de `StubWhatsAppProvider.cs:23-24`, `StubSmsProvider.cs:27-28`, `SmsCanal.cs:16-18`, `WhatsAppCanal.cs:16-18` e `Async/DependencyInjection/ServiceCollectionExtensions.cs:208` levam telefone, corpo ou e-mail, e a linha não traz o `OutboxId` |
| `EasyStock.Api.UnitTests/Notifications/EnvioSimuladoTests.cs::Stub_de_whatsapp_devolve_Simulado_com_provider_stub`<br>`::Stub_de_sms_devolve_Simulado_com_provider_stub`<br>`::Canal_de_email_sobre_o_console_devolve_Simulado_e_nao_smtp` | os stubs devolvem sucesso e o canal grava `smtp` |
| `EasyStock.Api.UnitTests/Notifications/ClassificacaoDeFalhaTests.cs::Smtp_550_e_falha_permanente`<br>`::Smtp_421_e_transitoria_e_chama_o_servico_uma_vez`<br>`::Twilio_sms_400_e_permanente_e_chama_a_API_uma_vez`<br>`::Twilio_whatsapp_503_e_indeterminado`<br>`::WebPush_sem_inscricao_ativa_e_permanente`<br>`::WebPush_400_e_permanente` | hoje tudo é transitório; o 421 chama o serviço 4 vezes (cerca de 14 s de backoff) e o 400 do Twilio chama a API 4 vezes |
| `EasyStock.Infra.Integrations.UnitTests/Notifications/MetaCloudWhatsAppProviderTests.cs::Timeout_de_http_vira_Indeterminado`<br>`::Sucesso_devolve_o_wamid_em_IdExterno` | o `TaskCanceledException` escapa pelo filtro de `:92`; `:80` descarta o `wamid` |
| `EasyStock.Infra.Async.UnitTests/Email/SmtpEmailServiceTests.cs::Smtp_550_e_tentado_uma_vez` (SMTP falso em loopback) | o serviço faz 3 `RCPT TO` |
| `EasyStock.Infra.Postgre.IntegrationTests/Notifications/DispatcherDesfechoTests.cs::Desfecho_simulado_grava_Simulado_e_o_log_do_provider_real`<br>`::Falha_permanente_nao_reagenda_e_cai_no_fallback_de_canal`<br>`::Indeterminado_nao_reenvia_nem_cai_no_fallback`<br>`::Seguranca_enviada_apaga_corpo_metadados_e_o_payload_so_quando_a_ultima_termina` | hoje o status é `Enviado`, a falha permanente depende do provider, e nada é apagado |

**Aceite.**
- [ ] **Dado** `Notifications:WhatsApp:Provider=stub`, **quando** o dispatcher processa uma mensagem de WhatsApp, **então** o outbox fica `Simulado`, sem `EnviadoEm`, com `ProviderUsado = "stub"`, e o log de envio guarda `stub` com `Sucesso = false`.
- [ ] **Dado** o Worker sem `Smtp__*`, **quando** sai um e-mail, **então** o desfecho é `Simulado` com provider `console`, nunca `smtp`, e o diagnóstico ainda detecta "SMTP não configurado" pelo nome da classe.
- [ ] **Dado** qualquer log de stub, console e canal, **quando** o teste varre as linhas, **então** nenhuma contém telefone, e-mail ou corpo, e todas levam o `OutboxId`.
- [ ] **Dado** SMTP 550, Twilio 400 ou WebPush 400, **quando** o canal responde, **então** o desfecho é `FalhaPermanente`, com uma chamada só, e a mensagem termina `Falhado` sem reagendar.
- [ ] **Dado** timeout, queda de conexão ou 5xx no WhatsApp ou no SMS, **quando** o provider responde, **então** o desfecho é `Indeterminado`, sem retentativa dentro do provider e sem fallback de canal.
- [ ] **Dado** uma mensagem `Seguranca`, **quando** ela termina, **então** o corpo é `[apagado]`, os metadados são nulos, e o payload do evento vira `{}` só quando a última mensagem aberta dele termina.
- [ ] **Dado** uma campanha com mensagens `Simulado`, `Expirado` e `Indeterminado`, **quando** a conciliação roda, **então** nenhuma conta como enviada.

**Fora.** Quarentena, claim e `EmEnvio` (N1); MailKit, remetente por categoria e configuração única de SMTP (N3); template por código e canal, e kill switch por empresa no Avaliador (N5); webhook que fecha o `Indeterminado` (N6); o alerta "stub em Production" (health da N1); SMS e Zenvia (poda na #783); `CanalSms` e `CanalEmail` do atendimento seguem tratando `Simulado` como sucesso (registrar na #783).

**Depende de.** S0 (a medição confirma quantos `Enviado` falsos existem).

**Tamanho.** G, em 4 commits verdes: domínio (status, categoria, resultado), canais (stubs, console, classificação, retentativa), dispatcher e purga, limpeza.

**Tier.** ALTO. Sem migration, a ADR-0055 daria baixo, mas a R5 (`CLAUDE.md:69-71`) manda ALTO: cerca de 20 arquivos de produção, mais de 100 linhas e contrato `ResultadoEnvio` alterado. A ADR-0057 (`docs/adr/0057-notificacoes-de-plataforma.md:68-69`) manda valer a regra mais restritiva. **Corrigido:** o plano marca N2 como baixo.

**Rollback.** `git revert` da PR. Antes de reverter, converter o que o código novo gravou, porque o enum antigo lança ao ler texto que não conhece: `UPDATE notif_outbox_mensagens SET "Status" = 'Suprimido' WHERE "Status" IN ('Simulado','Indeterminado')` e `UPDATE notif_outbox_mensagens SET "Categoria" = 'Transacional' WHERE "Categoria" = 'Seguranca'`. Sem migration, não há `Down`.

---

## N1 · Destravar o motor · issue aberta ao iniciar

**Problema.**

| # | Achado | Onde | Prova |
|---|---|---|---|
| 1 | O papel é `NOBYPASSRLS` por decisão (ADR-0010) e a policy só admite o tenant da sessão ou o bypass: sem os dois devolve 0 linhas e nenhum erro. A S0 reconfirma o papel | `Postgre/Migrations/20260511120000_AddRowLevelSecurity.cs:88-98`; `docs/plan/notificacoes-sistema/00-diagnostico.md:17` (compose da VPS, fora do repo) | código, medir |
| 2 | Nenhum loop liga o bypass nem fixa tenant: o Dispatcher lê o outbox dentro do lock, o Avaliador lê os eventos, o Coletor varre os lotes. O único bypass do módulo é o do template global | `Postgre/Notifications/Dispatcher/NotificacoesDispatcherOrchestrator.cs:63-80`; `App/Services/Notifications/Orchestrators/NotificacoesAvaliadorOrchestrator.cs:36`; `Postgre/Notifications/Collectors/ColetorProdutosVencendo.cs:34-41`; `Postgre/Repositories/Notifications/TemplateNotificacaoRepository.cs:36` | código |
| 3 | O advisory lock abre a conexão antes da action e o interceptor só emite `SET app.empresa_id` e `app.bypass_rls` na abertura: ligar o bypass dentro do lock não vale. Já provado | `Postgre/Concurrency/PostgresAdvisoryLock.cs:30`; `Postgre/Data/Interceptors/SetTenantOnConnectionInterceptor.cs:94,176-183`; `EasyStock.Infra.Postgre.IntegrationTests/Tenancy/CaixaEsquecidoCrossTenantRlsTests.cs:61-78` | teste |
| 4 | No Worker o usuário é SuperAdmin: o filtro global do EF fica desligado e só a RLS isola. No escopo por item, a RLS será a única camada | `Async/Reporting/WorkerCurrentUserAccessor.cs:38,55-57,94-95`; `Postgre/Data/EasyStockDbContext.cs:86,538` | código |
| 5 | Catálogo global invisível: linha com `EmpresaId` nulo não passa na policy nem no filtro do EF. Em escopo de empresa a rotina global some, o kill switch global não vale e, sem canal visível, `CanalAtivo` devolve falso e o evento fecha `Processado` sem outbox | `Postgre/Repositories/Notifications/RotinaNotificacaoRepository.cs:20`; `BloqueioNotificacaoRepository.cs:21`; `ConfiguracaoCanalRepository.cs:20`; `TemplateNotificacaoRepository.cs:27-36`; `App/Services/Notifications/ResolvedorCanal.cs:58-64`; `NotificadorService.cs:162-184`; `Api/Data/NotificacoesGlobaisSeed.cs:79-80`; `EasyStock.Infra.Postgre.IntegrationTests/Repositories/DisparoCampanhaIntegrationTests.cs:189-190` | teste |
| 6 | `AgendamentoNotificacaoService` publica dentro do lock, numa conexão sem tenant: o INSERT em `notif_eventos` viola o WITH CHECK (42501) no commit, a exceção aborta o laço de candidatos e se repete a cada tick | `Worker/BackgroundServices/AgendamentoNotificacaoService.cs:63-94,139`; `App/Services/Notifications/NotificadorService.cs:51` | código |
| 7 | `CaixaEsquecidoJob`: o gate enxerga a rotina global (`IgnoreQueryFilters` sob bypass) e o `PublicarEventoAsync` não (filtro do EF na API): evento `Processado` sem outbox e dedup carimbado | `Api/BackgroundServices/CaixaEsquecidoJob.cs:73,86-92,127-141`; `RotinaNotificacaoRepository.cs:20`; `NotificadorService.cs:137-148` | código |
| 8 | `ContaFinanceiraVencimentoJob`: mesma cegueira, e o `catch` engole a falha e o chamador carimba assim mesmo | `Api/BackgroundServices/ContaFinanceiraVencimentoJob.cs:133-134,264-282,295-311` | código |
| 9 | **Novo:** `ReportRunnerBackgroundService` (RelatorioPronto e RelatorioFalhou) usa o mesmo `PublicarEventoAsync` em escopo de empresa: global invisível, evento `Processado` sem outbox | `Worker/BackgroundServices/ReportRunnerBackgroundService.cs:306,359` | código |
| 10 | **Novo:** o anonimizador também não liga bypass: o UPDATE do outbox (destinatário e corpo com mais de 90 dias) deve afetar 0 linhas sob RLS (inferência pela policy; a S0 mede) | `Postgre/Notifications/Maintenance/AnonimizarLogsAntigosService.cs:62-72` | código, medir |
| 11 | A API também roda os loops: o padrão é `Hosted`, não há seção no appsettings e o comentário do módulo diz o contrário; o aviso só dispara com `Hosted` explícito. Avaliador e Coletor não têm lock (só o Dispatcher tem chave), então com API e Worker de pé avaliam o mesmo evento e o índice único do outbox barra a segunda linha com 23505 | `App/Services/Notifications/NotificationsHostingOptions.cs:36`; `Api/DependencyInjection/NotificationsModuleExtensions.cs:13-16,24-27,34-40`; `Api/Program.cs:129`; grep de `Notifications` em `EasyStock.Api/appsettings*.json` = 0; `Postgre/Concurrency/LockKeys.cs:37`; `OutboxMensagemNotificacaoConfiguration.cs:36` | código |
| 12 | Coletor registrado duas vezes no Worker: `AddEasyStockPostgreInfrastructure` já chama o registro de repositórios e o Worker chama de novo. Roda duas vezes por rodada; a dedup por `CorrelationId` evita evento duplicado | `Postgre/DependencyInjection/ServiceCollectionExtensions.cs:258`; `Worker/Program.cs:64-66`; `Postgre/DependencyInjection/ServiceCollectionExtensions.Notifications.cs:39`; `ColetorProdutosVencendo.cs:53-56` | código |
| 13 | Veneno no Avaliador: um escopo e um DbContext para até 200 eventos. Se um commit falha, a entidade inválida segue rastreada, o catch tenta marcar `Falhado` com o mesmo INSERT pendente e todos os commits seguintes falham. `IUnitOfWork.DescartarAlteracoesPendentes` existe para isso e o catch não o usa | `NotificadorService.cs:93-114`; `Notifications/Hosting/AvaliadorLoopHostedService.cs:30`; `NotificacoesAvaliadorOrchestrator.cs:36-47`; `App/Ports/Output/Persistence/IUnitOfWork.cs:7-12` | código |
| 14 | Veneno no Dispatcher: sem try/catch por mensagem nem por shard. Uma exceção no commit aborta o lote e os shards seguintes, a mensagem volta ao topo da fila e, se o envio já ocorreu, sai de novo a cada rodada | `NotificacoesDispatcherOrchestrator.cs:46-50,82-86,134,181` | código |
| 15 | `ShardKey` é fixo `% 4` e `ShardCount` é configurável: com 2, os shards 2 e 3 nunca rodam. O único leitor da coluna é o repositório do Dispatcher | `Domain/Entities/Notifications/OutboxMensagemNotificacao.cs:91`; `NotificationsHostingOptions.cs:39`; `NotificacoesDispatcherOrchestrator.cs:46`; `Postgre/Repositories/Notifications/OutboxNotificacaoRepository.cs:17` | código |
| 16 | **Novo:** o Dispatcher não consulta kill switch: bloqueio ativado depois do enfileiramento não segura o que já está no outbox (campanha enfileirada inteira) | `NotificacoesDispatcherOrchestrator.cs` (nenhuma referência a bloqueio); `App/Services/Campanhas/EnfileiradorMensagensCampanha.cs:82,128-137` | código |
| 17 | O backlog sem prazo existe em dois lugares: evento `Pendente` (aviso S13) e outbox `Pendente` de campanha (o evento nasce `Processado` e a mensagem leva `ProximaTentativaEm` futuro) | `App/Services/Atendimento/AvisoStatusPedidoCliente.cs:101`; `EnfileiradorMensagensCampanha.cs:93-96,119` | código, medir |
| 18 | At-most-once não existe: `EmEnvio` nunca é gravado e o resultado só é gravado depois do envio | `OutboxMensagemNotificacao.cs:106-109`; `NotificacoesDispatcherOrchestrator.cs:134-181` | código |
| 19 | `/health/dispatcher` só confere heartbeat e responde saudável quando o host é `Disabled`: com a API desligada, Worker parado e backlog crescendo não aparecem | `Api/Hosting/PipelineExtensions.cs:268-275`; `Notifications/Hosting/NotificationsHostingHealthCheck.cs:31-33` | código |
| 20 | A guarda de bypass não varre `Infra.Notifications` e a regex só casa a porta `IRowLevelSecurityBypass`, que nenhum arquivo usa. As 27 chamadas diretas a `UseRowLevelSecurityBypass()` (em 16 arquivos) passam sem a lista | `EasyStock.ArchitectureTests/RlsBypassAllowlistTests.cs:30,56-66,72-82` | código |

**Abordagem.**

```
Rodada do Worker (sinal LISTEN ou 10 s)
 1. escopo A, bypass pela porta, transação curta:
    a) expirar    : evento e outbox Pendentes além do prazo do tipo viram Expirado
    b) reclamar   : EmEnvio com lease vencido volta a Pendente (e-mail, in-app, push) ou vira Indeterminado (WhatsApp, SMS)
    c) reservar   : Pendente elegível vira EmEnvio com lease, FOR UPDATE SKIP LOCKED, devolve (Id, EmpresaId)
 2. por item, escopo novo + SetCurrentTenant(EmpresaId) antes da 1ª conexão + try/catch próprio:
    kill switch (global, empresa, canal) -> Suprimido | canal.EnviarAsync -> Enviado, Simulado, Falhado, Pendente (backoff) ou Indeterminado
    (WhatsApp e SMS já estão em EmEnvio antes da chamada; id do provider gravado no mesmo commit)
```

- **Bypass só pela porta.** Os loops reservam trabalho num escopo com `IRowLevelSecurityBypass.Begin()` antes de qualquer conexão (o interceptor lê a flag na abertura), em transação curta sem retentativa (`ExecuteInTransactionSemRetryAsync`), como a S39 (`Api/BackgroundServices/MensagensProgramadasBackgroundService.cs:48-63`, `App/UseCases/Atendimento/Programadas/DisparoMensagensProgramadas.cs:16-29`, `Postgre/Repositories/Atendimento/MensagemProgramadaRepository.cs:25-38`). Entram na `Allowlist` (arquivo, #1344, motivo): o claim do Dispatcher, a leitura do Avaliador, a rodada do Coletor (varredura cross-tenant por natureza), o anonimizador e o health de backlog.
- **Escopo de DI por item.** Cada mensagem ou evento roda em escopo novo, com tenant fixado antes da primeira conexão. Falha vira `Falhado` com motivo (ou `Indeterminado`, se estourar depois de o WhatsApp ou o SMS ter sido chamado), gravada num escopo limpo, e nunca para o lote: isso elimina o DbContext sujo do Avaliador (13) e o veneno do Dispatcher (14). O Dispatcher larga o advisory lock: `SKIP LOCKED` mais o lease dão a exclusão entre réplicas. Como o filtro do EF não vale no Worker (4), toda query nova leva `EmpresaId` no WHERE além da RLS (ADR-0010).
- **Catálogo global legível e imutável.** Uma migration aditiva cria `FOR SELECT USING ("EmpresaId" IS NULL)` em `notif_rotinas`, `notif_templates`, `notif_configuracoes_canal` e `notif_bloqueios`. Policies permissivas somam: o tenant lê o global, e UPDATE, DELETE e INSERT seguem sob a `tenant_isolation`. Só os métodos de leitura do motor (`ListarAtivasAsync`, `GetAtivoAsync`, `ListarAtivosAsync` e a listagem de canais) passam a ler com `IgnoreQueryFilters()` e predicado `EmpresaId == empresa ou nulo` (a empresa vira parâmetro: R8 nos chamadores); o CRUD das telas não muda. `GetGlobalAtivoAsync` perde o bypass direto. O teste que afirma o contrário (`DisparoCampanhaIntegrationTests.cs:189-190,197-198`) é invertido no mesmo commit. Descartadas: ler o global sob bypass e repassar em memória (mexe em toda a cadeia do `NotificadorService`); mover o catálogo para a empresa padrão (muda a semântica de `EmpresaId` nulo no seed e nas telas); alargar a `tenant_isolation` (altera a policy auditada). Custo aceito: no banco, uma sessão de empresa passa a ler o texto dos templates globais, o `Motivo` e o `AtivadoPor` dos bloqueios globais e a configuração de canal global; nenhum é segredo, e o filtro do EF e a checagem de dono (`App/UseCases/Notifications/EscopoEmpresaNotificacao.cs:6-12`) seguem escondendo o global das telas da empresa. `CredenciaisCifradas` sairia junto na linha global, mas ninguém lê nem grava a coluna (`Domain/Entities/Notifications/ConfiguracaoCanal.cs:11,32,40`; `TrocarProvider` não tem chamador) e a S0 mede que o global não tem credencial.
- **Quarentena por prazo, antes de ligar.** Prazos por tipo em código (`PoliticaValidadeNotificacao`, sobrescrevíveis em `Notifications:Quarentena:Prazos:<Tipo>`, minutos), valores iniciais propostos, a confirmar com a medição da S0:

| Grupo | Tipos | Prazo |
|---|---|---|
| Segurança | `ResetSenha`, `ConfirmacaoEmail` | 30 min |
| Aviso do pedido e pós-venda ao cliente (S13) | `PedidoPagoConfirmado`, `PedidoEmPreparo`, `PedidoSaiuParaEntrega`, `PedidoEntregue`, `AvaliacaoSolicitada`, `ReembolsoEfetuado` | 2 h |
| Campanha | `CampanhaMarketing`, `CampanhaLembreteEncerramento` | 1 h |
| Atendimento interno | `ConversaEscalada`, `LembreteVencido` | 1 h |
| Demais | todos os outros | 24 h |

  A expiração roda antes da reserva, na mesma rodada: não há janela em que o backlog velho seja elegível. No evento, o prazo conta de `OcorridoEm`. No outbox, só `Pendente` com `Tentativas = 0`, contado de `ProximaTentativaEm` (a avaliação nasce agendada e a campanha leva o horário da onda), com o tipo lido pelo evento (`EventoId` é obrigatório). Entram `StatusEventoNotificacao.Expirado` e `StatusOutbox.Expirado`, texto sem migration de coluna.
- **Entrega por canal.** E-mail, in-app e push, ao menos uma vez: o claim grava `EmEnvio` com lease em `ProximaTentativaEm` (5 min, como `Domain/Integration/OutboxEventoIntegracao.cs:146-161`) e lease vencido volta a `Pendente`. WhatsApp e SMS, no máximo uma vez: `EmEnvio` é commitado antes da chamada, `Indeterminado` (N2) é terminal e sem fallback, lease vencido vira `Indeterminado` e nunca `Pendente`, e o `IdExterno` do provider vai para a coluna nova `ProviderMensagemId` (varchar(128), nula, índice parcial), que a N6 usa no webhook de status.
- **Kill switch no envio.** Antes de chamar o canal, o item confere bloqueio ativo (global, da empresa, do canal): casando, vira `Suprimido` com o motivo.
- **Avaliador e Coletor.** O Avaliador expira, lista até 200 `(Id, EmpresaId)` sob bypass numa leitura curta e processa cada evento em escopo próprio: 23505 na `IdempotencyKey` conta como já enfileirado (evento `Processado`), outra exceção marca `Falhado` num escopo limpo. O registro do coletor vira idempotente (`TryAddEnumerable`) e a chamada do Worker sai (`Worker/Program.cs:64-66`).
- **Produtores.** `AgendamentoNotificacaoService`: o tick vira classe testável e cada candidato roda em escopo próprio com o tenant do pedido, e o carimbo de dedup só vem depois do commit do evento. `CaixaEsquecidoJob`, `ContaFinanceiraVencimentoJob` e `ReportRunner` ficam como estão, porque a visibilidade do catálogo corrige a cegueira na raiz; o job de contas passa a carimbar só quando a publicação deu certo (a publicação devolve o resultado em vez de engolir a exceção).
- **Um host só.** A API força `Mode=Disabled` por código no módulo (`PostConfigure<NotificationsHostingOptions>`) e não registra loops, sinalizador nem anonimizador, ignorando `Hosted` da configuração, com log de aviso; o comentário errado é corrigido. Os gatilhos `api/internal/notif-jobs/*` ficam e `?shard=` continua aceito e ignorado (`Api/Controllers/Internal/NotificacoesJobsController.cs:60-62`). `ShardKey`, `ShardCount` e o índice ficam sem uso e sem migration. Sem `[Obsolete]`: o build trata aviso como erro.
- **Ver e avisar.** `NotificacoesBacklogHealthCheck` mede a idade do `Pendente` elegível mais antigo, `EmEnvio` além do lease, `Falhado`, `Simulado`, `Indeterminado` e `Expirado` por hora, `Processado` sem outbox em 24 h, e provider ativo `stub` ou console em Production (Degraded). É consulta agregada sob bypass pela porta, sem trazer linha, com limites em `Notifications:Health:*` (padrões propostos: pendente elegível acima de 5 min, 5 `Falhado` por hora, qualquer `Indeterminado`, qualquer `Simulado` em Production). Vai para `/health/notificacoes`, onde Degraded e Unhealthy respondem 503 (o UptimeRobot só vê status HTTP), fora de `/health` e `/health/ready` para um backlog ruim não tirar a API do balanceador. No Worker, um serviço pinga o Healthchecks.io (`Notifications:Monitoring:PingUrl`, no `.env`) a cada minuto enquanto heartbeats e backlog estão saudáveis, e chama `/fail` quando não: "a cada rodada" vira no máximo um ping por minuto, para não martelar o serviço a cada 10 s.
- **Guarda.** `RlsBypassAllowlistTests` passa a varrer `EasyStock.Infra.Notifications`, ganha as entradas novas e um teste que proíbe `UseRowLevelSecurityBypass(` direto nos diretórios do módulo.
- **Migration única e aditiva** (`dotnet ef migrations add` com `.Designer.cs`, ADR-0024): as quatro policies, a coluna e o índice. `Down` remove os três.

**Leitura mínima.** `Postgre/Notifications/Dispatcher/NotificacoesDispatcherOrchestrator.cs`; `Postgre/Concurrency/PostgresAdvisoryLock.cs`; `Postgre/Data/Interceptors/SetTenantOnConnectionInterceptor.cs`; `Postgre/Data/EasyStockDbContext.cs` (linhas 86-145 e 511-543); `Postgre/Repositories/Notifications/*` (Outbox, Evento, Rotina, Template, Bloqueio, ConfiguracaoCanal); `Postgre/Notifications/Collectors/ColetorProdutosVencendo.cs` e `Maintenance/AnonimizarLogsAntigosService.cs`; `Postgre/DependencyInjection/ServiceCollectionExtensions.Notifications.cs`; `Postgre/Migrations/20260511120000_AddRowLevelSecurity.cs` e `20261001012521_AddRlsOcorrenciasEParadaUnicaPorPedido.cs` (molde); `App/Services/Notifications/NotificadorService.cs`, `Orchestrators/NotificacoesAvaliadorOrchestrator.cs` e `ResolvedorCanal.cs`; `App/Ports/Output/Security/IRowLevelSecurityBypass.cs`; `Async/Reporting/WorkerCurrentUserAccessor.cs`; `Notifications/Hosting/*`; `Api/DependencyInjection/NotificationsModuleExtensions.cs`; `Api/Hosting/PipelineExtensions.cs` (241-290); `Worker/Program.cs`; `Worker/BackgroundServices/AgendamentoNotificacaoService.cs`; `Api/BackgroundServices/CaixaEsquecidoJob.cs` e `ContaFinanceiraVencimentoJob.cs`; padrão S39 (`MensagensProgramadasBackgroundService.cs`, `DisparoMensagensProgramadas.cs`, `MensagemProgramadaRepository.cs`); `Domain/Integration/OutboxEventoIntegracao.cs` (lease); `EasyStock.ArchitectureTests/RlsBypassAllowlistTests.cs`; moldes de teste `CaixaEsquecidoCrossTenantRlsTests.cs`, `TenantContextAccessorIntegrationTests.cs` e `PostgreSqlDatabaseFixture.cs`.

**Testes Red.** Moldes: `CaixaEsquecidoCrossTenantRlsTests` (DbContext com `RlsClientConnectionString` e `SetTenantOnConnectionInterceptor`) e `TenantContextAccessorIntegrationTests` (DI real com `AddEasyStockPostgreInfrastructure`, canal trocado por um falso que conta chamadas). O canário `HarnessCanaryTests` cobre o projeto contra skip silencioso. Os de isolamento rodam com o papel `rls_test_client`; os de comportamento rodam com superusuário, para falharem pelo motivo próprio e não pela RLS. Na ordem dos commits, cada teste só falha pelo motivo listado quando o anterior está verde.

| Teste (`EasyStock.Infra.Postgre.IntegrationTests/Notifications/MotorNotificacoesRlsTests.cs`) | Falha hoje porque |
|---|---|
| `Dispatcher_sob_papel_NOBYPASSRLS_envia_a_pendente_de_cada_empresa` | o lock abre a conexão sem bypass nem tenant: 0 linhas, 0 envios |
| `Dispatcher_escopo_por_item_so_enxerga_as_linhas_da_empresa_da_mensagem` | nada é processado |
| `Avaliador_sob_papel_NOBYPASSRLS_enfileira_o_evento_pendente_com_a_rotina_global` | a leitura dos eventos devolve 0 linhas e, com tenant, a rotina global é invisível |
| `Coletor_sob_papel_NOBYPASSRLS_enxerga_os_lotes_de_todas_as_empresas` | a varredura devolve 0 linhas |
| `Anonimizador_sob_papel_NOBYPASSRLS_anonimiza_o_outbox_antigo` | o UPDATE afeta 0 linhas |
| `Catalogo_global_e_legivel_no_escopo_da_empresa_e_imutavel_por_ela` | o global é invisível (`DisparoCampanhaIntegrationTests.cs:189-190` prova); UPDATE e DELETE devem afetar 0 linhas e INSERT com `EmpresaId` nulo deve dar 42501 |
| `Publicar_no_escopo_da_empresa_acha_a_rotina_global` | `RotinaNotificacaoRepository.cs:20` não vê a global: evento `Processado` sem outbox |

| Teste (`.../Notifications/ProdutoresNotificacaoRlsTests.cs`) | Falha hoje porque |
|---|---|
| `Lembrete_de_pedido_agendado_grava_o_evento_sob_RLS_sem_42501` | `PostgresException` 42501 no commit (`AgendamentoNotificacaoService.cs:63-94,139`) |
| `Caixa_esquecido_enfileira_antes_de_carimbar_o_dedup` | evento `Processado` sem outbox e carimbo gravado (`CaixaEsquecidoJob.cs:127-141`) |
| `Contas_vencendo_nao_carimba_o_dedup_quando_a_publicacao_falha` | carimba sempre (`ContaFinanceiraVencimentoJob.cs:133-134,279-282`) |

| Teste (`.../Notifications/MotorNotificacoesComportamentoTests.cs`) | Falha hoje porque |
|---|---|
| `Dispatcher_mensagem_venenosa_nao_trava_as_demais_nem_os_outros_shards` | o commit que falha (provider com 60 caracteres para `varchar(40)`) aborta o lote e os shards seguintes (`:46-50,82-86`) |
| `Avaliador_evento_cujo_commit_falha_nao_impede_o_proximo` (inclui 23505 = já enfileirado) | o ChangeTracker sujo derruba os commits seguintes (`NotificadorService.cs:93-114`) |
| `Dispatcher_ignora_ShardKey_e_processa_mensagem_de_qualquer_shard` | com `ShardCount = 2`, o shard 3 nunca é lido |
| `Dispatcher_whatsapp_grava_EmEnvio_e_o_id_do_provider` | o status é `Pendente` durante o envio e não há coluna do id |
| `Dispatcher_whatsapp_com_timeout_vira_Indeterminado_e_nao_reenvia` | volta a `Pendente` com backoff e reenvia |
| `Dispatcher_lease_vencido_volta_a_Pendente_no_email_e_vira_Indeterminado_no_whatsapp` | `EmEnvio` nunca é gravado |
| `Quarentena_expira_evento_e_outbox_alem_do_prazo_do_tipo_antes_da_reserva` | tudo `Pendente` velho é enviado; o canal falso não deve ser chamado |
| `Dispatcher_respeita_kill_switch_global_e_da_empresa_no_envio` | o Dispatcher não consulta bloqueio |
| `Dispatcher_duas_instancias_nao_enviam_a_mesma_mensagem` | guarda do claim: passa hoje pelo lock e deve seguir passando com `SKIP LOCKED` |

| Teste (unitário, roda na CI) | Falha hoje porque |
|---|---|
| `EasyStock.Api.UnitTests/Notifications/NotificationsModuleExtensionsTests.cs::Api_nunca_registra_os_loops_mesmo_com_Mode_Hosted_na_configuracao` | o padrão `Hosted` registra Dispatcher, Avaliador, Coletor, sinalizador e anonimizador |
| `EasyStock.Api.UnitTests/Notifications/ColetoresRegistradosTests.cs::Coletor_fica_registrado_uma_vez_mesmo_chamando_o_registro_duas_vezes` | dois descritores |
| `EasyStock.ArchitectureTests/RlsBypassAllowlistTests.cs::Notificacoes_nao_chamam_UseRowLevelSecurityBypass_direto` | `TemplateNotificacaoRepository.cs:36` |
| `EasyStock.Api.UnitTests/Notifications/NotificacoesBacklogHealthCheckTests.cs::Pendente_elegivel_acima_do_limite_deixa_Unhealthy`<br>`::Simulado_em_Production_deixa_Degraded`<br>`::Fora_de_Production_Simulado_nao_degrada` | a classe não existe |
| `EasyStock.Api.UnitTests/Notifications/HealthchecksPingTests.cs::Pinga_so_com_os_checks_saudaveis_e_chama_fail_quando_nao` | a classe não existe |
| `EasyStock.Application.Tests/Services/Notifications/PoliticaValidadeNotificacaoTests.cs::Todo_tipo_tem_prazo_e_o_aviso_ao_cliente_e_mais_curto_que_o_padrao` | a classe não existe |

**Aceite.**
- [ ] **Dado** o papel `rls_test_client` (NOBYPASSRLS) e mensagens `Pendente` de duas empresas, **quando** uma rodada do Worker roda, **então** as duas saem uma vez cada, com o tenant certo e sem 42501.
- [ ] **Dado** um evento `Pendente` e uma rotina global ativa, sem rotina da empresa, **quando** o Avaliador roda, **então** nasce o outbox da empresa do evento e o evento fica `Processado`.
- [ ] **Dado** backlog velho (aviso S13 há 3 h, campanha 2 h depois do horário, demais há 2 dias), **quando** a primeira rodada roda, **então** tudo vira `Expirado`, o canal não é chamado e a contagem por tipo bate com a S0.
- [ ] **Dado** timeout no WhatsApp, **quando** o canal estoura, **então** a mensagem vira `Indeterminado`, com uma chamada e sem reenvio; e um e-mail preso em `EmEnvio` volta a `Pendente` depois do lease.
- [ ] **Dado** uma mensagem venenosa, **quando** a rodada roda, **então** as outras saem e a venenosa termina `Falhado` com o motivo.
- [ ] **Dado** kill switch global ou da empresa ativo, **quando** a rodada roda, **então** o que está no outbox daquele canal vira `Suprimido` e nada sai.
- [ ] **Dado** a API em produção, **quando** sobe com `Notifications:Hosting:Mode=Hosted` na configuração, **então** nenhum loop é registrado e `/health/dispatcher` diz "disabled neste host".
- [ ] **Dado** o Worker, **quando** uma rodada fecha, **então** o ping segue ao Healthchecks.io; com o loop parado há mais de 5 min, o ping para e `/health/notificacoes` responde 503.
- [ ] **Dado** `RlsBypassAllowlistTests`, **quando** roda, **então** só os arquivos listados (com issue e motivo) tocam a porta, `Infra.Notifications` é varrido e nenhum arquivo do módulo chama `UseRowLevelSecurityBypass()` direto.
- [ ] **Dado** o catálogo global, **quando** uma empresa tenta alterá-lo ou criar linha global, **então** a RLS afeta 0 linhas ou devolve 42501.
- [ ] **Dado** o deploy, **quando** o Felipe roda de novo as seções de pendentes da S0, **então** não há `Pendente` além do prazo e o `42501` some do log do `ez-worker`.

**Fora.** Ordem tenant antes de global, kill switch por empresa no Avaliador, destinatário e fallback certos (N5); MailKit e configuração de SMTP (N3); webhook que fecha `Indeterminado` e remetente por mensagem (N6); cron do Avaliador e `HorarioBrasil` (N12); migrar Caixa e contas para `EnfileirarEventoAsync` (ADR-0030); remover a coluna `ShardKey` e seu índice; corrigir o `IntegrationOutboxBackgroundService` (só medir na S0).

**Depende de.** S0 (os prazos saem da medição) e N2 (`Simulado`, `Indeterminado`, `IdExterno` e `Seguranca` precisam existir). Precede a N3.

**Tamanho.** G, em 5 commits verdes dentro da branch: (1) porta, policy, repositórios e allowlist; (2) claim e escopo por item no Dispatcher; (3) Avaliador, Coletor e produtores; (4) quarentena, kill switch, lease e `EmEnvio`; (5) hosting, health e ping.

**Tier.** ALTO: migração EF e RLS (`docs/adr/0055-tier-baixo-para-spec-de-plano-aprovado.md:22-23`) e R5 (`CLAUDE.md:69-71`: toca `Program.cs` do Worker e uma migration, com mais de 5 arquivos e mais de 100 linhas). Merge só com a label `aprovado`.

**Rollback.**
- **Imediato, sem código:** `docker stop ez-worker` devolve ao estado de hoje (nada flui) sem perder dado; e o kill switch global, que passa a valer com esta spec, segura o envio com o Worker de pé.
- **De código:** `git revert` da PR mais `Down` da migration, que remove as policies, a coluna e o índice. Antes, converter o que o código novo gravou: `UPDATE notif_outbox_mensagens SET "Status" = 'Suprimido' WHERE "Status" = 'Expirado'` e `UPDATE notif_eventos SET "Status" = 'Falhado' WHERE "Status" = 'Expirado'`, porque o enum antigo lança ao ler texto que não conhece.
- **Sem volta:** mensagem que já saiu não se desfaz. Por isso a quarentena e a N2 vêm antes de ligar.

---

## Rollback do marco

| Ordem | Spec | O que desfaz | Cuidado |
|---|---|---|---|
| 1 | N1 | `git revert` mais `Down` da migration; `docker stop ez-worker` e o kill switch global como freio imediato | converter `Expirado` (outbox para `Suprimido`, evento para `Falhado`) antes de reverter |
| 2 | N2 | `git revert`; sem migration | converter `Simulado` e `Indeterminado` para `Suprimido`, e `Seguranca` para `Transacional`; o corpo já apagado não volta |
| 3 | N0 | `git revert` devolve a rota | só enquanto a API não tiver `Smtp__*` reais: com SMTP, a rota é um canhão de spam |
| 4 | S0 | `git revert` | nada de dado; o script é somente leitura |

O marco não perde dado de negócio em nenhum passo. As únicas perdas irreversíveis são mensagens já enviadas e corpos de categoria `Seguranca` já apagados, e ambas são intencionais.
