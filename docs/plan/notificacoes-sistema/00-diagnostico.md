# Diagnóstico · 2026-10-01

Medido no master `c2eb85ca`, no compose e nos logs do stack `easystok` na VPS, e no DNS e nas
assinaturas da Hostinger. Tudo foi lido sem escrever nada. Os segredos do `.env` não estão
transcritos aqui.

## Veredito

**O motor de notificações existe, mas nada passa por ele em produção, e eventos se perdem sem
deixar erro.** O backlog parado não tem prazo de validade. Destravar sem quarentena mandaria
mensagem velha a cliente real.

## Produção (VPS, stack `/opt/stacks/easystok`)

| Fato | Fonte |
|---|---|
| O papel `easystock` é `NOBYPASSRLS` por decisão (ADR-0010) | comentário do `compose.yaml` |
| O Worker sobe Avaliador, Coletor e Dispatcher (`shards=4 batch=50`), e o `LISTEN notif_outbox` fica ativo | log do `ez-worker` às 23:41 |
| Não há `Notifications__Hosting__Mode`, então a API também sobe os loops (o padrão é Hosted) | `compose.yaml`; `NotificationsHostingOptions.cs:36` |
| O Worker não tem `Smtp__*` nem `Notifications__*__Provider`: e-mail cai no console, SMS e WhatsApp no stub | `compose.yaml`; `EasyStock.Worker/appsettings.json:12-19` |
| Na API, `Atendimento__WhatsApp__Cliente=meta` vale só para o atendimento; o provider de notificação continua `stub` | `compose.yaml` |
| Não há `Auth__TrustedLinkOrigins` nem `PUBLIC_BASE_URL` | `compose.yaml` |

## Código

| # | Achado | Prova |
|---|---|---|
| 1 | Nenhum loop do motor liga o bypass de RLS. O advisory lock abre a conexão antes e a policy devolve 0 linhas | `PostgresAdvisoryLock.cs:30`; `NotificacoesDispatcherOrchestrator.cs:63-80`; `AddRowLevelSecurity.cs:88-98`; o único bypass do módulo está em `TemplateNotificacaoRepository.cs:36` |
| 2 | Os produtores perdem evento: `AgendamentoNotificacaoService` viola o WITH CHECK, e `CaixaEsquecidoJob`/`ContaFinanceiraVencimentoJob` não veem a rotina global, marcam Processado e carimbam o dedup | `CaixaEsquecidoJob.cs:127-141`; `RotinaNotificacaoRepository.cs:20` |
| 3 | Sucesso falso: stub e console devolvem sucesso, e o canal grava `ProviderUsado="smtp"` fixo | `StubWhatsAppProvider.cs:26`; `SmtpEmailCanal.cs:54-57` |
| 4 | O destinatário vem só do payload; `usuarioDestinoId` não busca o contato; `Usuario` não tem telefone | `NotificadorService.cs:364-392`; `Usuario.cs` |
| 5 | Fallback de canal quebrado; rotina tenant×global sem ordem; kill switch por empresa ignorado | `NotificadorService.cs:129,137-138,189`; `NotificacoesDispatcherOrchestrator.cs:212-270` |
| 6 | Evento veneno trava a rodada (change tracker sujo); o Dispatcher não isola exceção por mensagem | `NotificadorService.cs:100-113`; `NotificacoesDispatcherOrchestrator.cs:46-50,82-86` |
| 7 | `SmtpClient`: não aceita 465, o singleton não suporta concorrência, não tem timeout, há 3×3 retentativas e o 550 é tratado como transitório. O remetente padrão `noreply@easystock.com` é de domínio alheio | `SmtpEmailService.cs`; `Worker/Program.cs:77` |
| 8 | O provider da Meta depende da `Conversa`, há um remetente só por canal, a Graph usada é a v19.0 (expirada) e não há botão de código | `MetaCloudWhatsAppProvider.cs:28-33`; `WhatsAppCanal.cs:9`; `WhatsAppCloudClient.cs:93-144` |
| 9 | O ramo cron do Avaliador é stub; janela e limite diário existem só como campos | `NotificacoesAvaliadorOrchestrator.cs:55-68`; `ConfiguracaoCanal.cs:12-14` |
| 10 | Reset de senha por fora do motor; tokens antigos seguem válidos; corrida no uso único; o JWT não é revogado | `EsqueciSenhaUseCase.cs`; `ResetarSenhaUseCase.cs`; `ApiServiceCollectionExtensions.cs:84-104` |
| 11 | Trocar o e-mail não pede senha nem verificação, e o Admin troca o e-mail de usuário de outra empresa: tomada de conta pelo reset ou pelo Google | `AtualizarUsuarioAtualUseCase.cs:25-39`; `AtualizarUsuarioUseCase.cs:27-40`; `IdentificarUsuarioGoogleUseCase.cs:20-21` |
| 12 | `lista-empresas` confere a senha sem contar falha, e o bloqueio de 5 tentativas é contornável | `ListarEmpresasParaLoginUseCase.cs:38-43` |
| 13 | `/api/auth/register` é anônimo, enumera conta e manda e-mail a qualquer endereço | `CadastrarUsuarioUseCase.cs:21-63` |
| 14 | O helpdesk com SLA foi removido (#1121, #1211). Os prazos vivos são lembrete de 10 min, pedido atrasado, impressão acima de 3 min e caixa esquecido | `AvaliarLembretesUseCase.cs:36-37`; `PedidoAtrasoJob`; `ImpressaoPendenteAlertaJob` |
| 15 | O `EndpointHealthMonitorService` faz POST para uma rota apagada em `6b0a6f98` | `EndpointHealthMonitorService.cs:195-205` |

## Infra externa

| Fato | Fonte |
|---|---|
| O *Starter Business Email* da Hostinger (1.000 envios por dia, por caixa) está ativo, com DNS completo em `rigorsistemas.com.br` | MCP Hostinger, assinaturas e DNS |
| `easystok.online` só tem A, CNAME e a verificação do Facebook, sem MX, SPF, DKIM nem DMARC | MCP Hostinger, DNS |
| `fmasoftware.com.br` está no Google Workspace; `casadababa.com` fica fora da Hostinger | DNS |
| A Meta mudou a cobrança em 01/10/2026: mensagens de serviço e utility dentro da janela passam a ser cobradas | docs Meta, mudanças de preço |
| Portfólio da Meta sem verificação: até 2 números e 250 conversas iniciadas por dia, contando o portfólio inteiro | docs Meta, números e limites |

## Premissas corrigidas pela banca

- **O enum vai para o banco como texto, não como inteiro.** `Tipo` e `Status` usam
  `HasConversion<string>()` (`EventoNotificacaoConfiguration.cs:12,18`). Por isso, colisão de
  valor numérico entre specs paralelas não corrompe dado. A reserva de faixas na S0 continua por
  higiene.
- **A stack é .NET 10**, não 8 (`global.json`).
- **O tenant do WhatsApp não vaza entre mensagens por cache.** O risco real está na mensagem sem
  empresa e no commit no meio do lote (`RemetenteWhatsAppDoTenant.cs:25-33`;
  `MetaCloudWhatsAppProvider.cs:132`).

## O que falta medir (S0)

Rodar `scripts/diagnostico/notificacoes-s0.sql`. Ele mede o papel, o RLS das tabelas, o backlog por
tipo e idade, os eventos Processado sem outbox, o outbox por provider e o catálogo global. Contar
também os `42501` no log do Worker.
