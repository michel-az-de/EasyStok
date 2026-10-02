# Code review do backend, 2026-10-02 (issue #1394)

Método: 3 revisões somente leitura (Api/auth, Application/Domain, Infra/Worker), achados lidos no código antes de listar. Cobertura parcial: não foi lido todo o repositório. Nenhum achado foi reproduzido em teste, exceto os marcados "teste".

## Corrigidos neste PR

| # | Sev. | Local | Problema | Correção |
|---|---|---|---|---|
| C1 | Alta | `AtribuirPerfilUsuarioUseCase`, `CriarUsuarioUseCase` | Admin de tenant atribuía perfil global SuperAdmin (login promove quem tem qualquer perfil SuperAdmin) | Perfil validado: recusa SuperAdmin e perfil de outra empresa; guarda também no cadastro com senha (teste) |
| C2 | Alta | `DevicePairingController` | Pareamento, listagem, comandos, broadcast e revogação só com `[Authorize]`: Visualizador criava device com API key | Policy `Gerente` nesses 5 endpoints |
| C3 | Alta | `EfiPixWebhookProcessor` | `catch` engolia a falha, webhook ficava `Sucesso` e o Pix nunca baixava (contradiz #787) | Propaga a falha após processar os demais itens; controller responde 500 e a Efi reenvia |
| C4 | Média | `EstornarMovimentoCaixaUseCase` | Permitia estornar abertura/fechamento | Só `entrada`/`saida` (teste) |
| C5 | Média | `EstornarMovimentoCaixaUseCase`, `EstornarPagamentoParcelaUseCase` | Fechamento checado pelo dia UTC, não BRT | `HorarioBrasil.DataOperacional` |
| C6 | Média | `ProcessarRecebimentoPedidoFornecedorUseCase` | Pedido virava Recebido pela soma global dos itens | `All` por item |
| C7 | Média | `CategoriaFinanceiraRepository.ExisteContaAbertaAsync` | Duas queries paralelas no mesmo DbContext (`InvalidOperationException`) | Sequencial |
| C8 | Média | `ReportRunnerBackgroundService` | Heartbeat e `Task.Run` morriam sem log; lease expirava e relatório duplicava | try/catch com log |
| C9 | Média | `Infra.Async ServiceCollectionExtensions` | `new HttpClient` por resolução, sem timeout (Efi Pix/boleto) | Handler compartilhado com `PooledConnectionLifetime` e timeout 30 s |

## Pendentes (precisam de desenho ou migração; abrir issues)

| # | Sev. | Local | Problema |
|---|---|---|---|
| P1 | Alta | `EstornarSaidaUseCase`, `CaixaRepository:175` | Estornar saída não desfaz a Venda; caixa continua somando (e Perda/Doação com preço entram como receita) |
| P2 | Alta | `ItemEstoque.RestaurarQuantidade`, `PedidoEstoqueIntegrationService` | Estorno de saída com descoberto cria estoque fantasma |
| P3 | Alta | `PedidoEstoqueIntegrationService.DescontarAsync` | Desconto de pedido escolhe lote vazio/vencido/bloqueado; usar FEFO de `RegistrarSaidaEstoqueUseCase` |
| P4 | Média | `ValidarOtpUseCase` | Contador de tentativas do OTP não atômico (brute force paralelo) |
| P5 | Média | `RefreshTokenUseCase` | Sem atomicidade nem detecção de reuso; perde empresa/nível; token fraco, 7 vs 30 dias |
| P6 | Média | `CurrentUserAccessor` | IP do primeiro `X-Forwarded-For` (forjável) vai para auditoria e LGPD |
| P7 | Média | `ClienteSessionAuthenticationHandler` | Scheme nunca executa; fingerprint é código morto |
| P8 | Média | `DevicePairingController pair-auto` | Segredo do APK sobrescreve device de outro tenant (responder 409) |
| P9 | Média | `AutenticarUsuarioUseCase` | Enumeração por mensagem "bloqueada" e por timing |
| P10 | Média | `RegistrarSaidaEstoqueUseCase` | Com descoberto e sem lote, item some em silêncio |
| P11 | Média | `RegistrarPagamentoPedidoUseCase` | Overpay por corrida, sem idempotência |
| P12 | Baixa | vários | Código de pareamento em log; replay Efi sem timestamp; `AllowUnsigned` MP sem trava Production; `frontend-error` sem teto; webhook sem limite de corpo; IdempotencyMiddleware com corpo >64 KB; comparação de token cron; guarda de saldo do caixa sem lock; projeção de ruptura; data de entrega passada; código de lote UTC; `ContinueWith(t.Result)` e sync-over-async latentes |

## Descartado como falso positivo
SQL injection (todo SQL cru parametrizado), HMAC da Meta, outbox com `SKIP LOCKED`, RLS por conexão, advisory lock dos relatórios, reset/convite atômicos, divisões por zero.
