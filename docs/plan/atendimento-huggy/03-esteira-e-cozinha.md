# Onda 3 — Esteira e cozinha (S17–S21)

Objetivo: o pedido pago aparece na cozinha em tempo real, imprime sozinho, muda de status em um toque
pela API e sinaliza atraso. O estoque avisa e não trava a esteira.
Cobre US-035 a US-038, US-041, US-042, US-063 (parte), RN-27 a RN-33, RN-48, D6, D7.

Dependências externas: onda 0.6 (modelo da impressora) decide o consumidor da fila em S20.

---

### S17 · Estoque avisa e não trava (pré-requisito da esteira)

**Problema.** `PedidoEstoqueIntegrationService.cs:108-118` lança `EstoqueInsuficienteException` na transição para `Pronto` quando falta saldo e aborta a mudança de status. Com `PermiteEstoqueNegativo=true` só desconta o que há e não grava o descoberto. RN-48: saldo zerado gera alerta persistente, nunca bloqueio. Sem isto, o KDS trava no primeiro pedido sem produção lançada.
**Abordagem.** Trocar o clamp pelo método de domínio que já existe para a saída manual (`ItemEstoque.RegistrarSaidaPermitindoDescoberto`, #540), gravando `QuantidadeDescoberta` no último lote FEFO. Tornar o comportamento o padrão (a opção `PermiteEstoqueNegativo` passa a default `true`; manter a opção para teste).
**Escopo.**
- `App/Services/PedidoEstoqueIntegrationService.cs`: no ramo `atual < qtd`, chamar `RegistrarSaidaPermitindoDescoberto(qtd, ...)` em vez de `qtd = atual`; `MovimentacaoEstoque` com descrição "pedido {id}: {falta} un a descoberto".
- `PedidoEstoqueOptions.PermiteEstoqueNegativo` default `true`; `appsettings.json` da Api sem override.
- Publicar `EstoqueDesacertadoEvent` (novo, outbox de integração ou evento em memória) com `ProdutoId`, `PedidoId`, `Falta` para S22 e S18 (SSE `estoque.desacerto`).
**Fora.** Tela; ajuste (S22).
**Aceite.**
- [ ] Pedido com item sem saldo transita para `Pronto`, saldo vai a 0, `QuantidadeDescoberta` recebe a falta, `MovimentacaoEstoque` registrada com referência ao pedido.
- [ ] Item sem `ItemEstoque` continua sendo ignorado com warning (comportamento atual).
- [ ] Testes de concorrência existentes (`EstoqueConcurrencyTests`) continuam verdes: dois pedidos simultâneos não deixam saldo negativo.
**Testes (Red).** `PedidoEstoqueIntegrationServiceTests.SemSaldoRegistraDescobertoENaoLanca`, `...PublicaEstoqueDesacertadoEvent`.
**Rollback.** `Pedidos:PermiteEstoqueNegativo=false` volta ao comportamento anterior (lança).
**Depende de.** Nada.
**Leitura mínima.** `App/Services/PedidoEstoqueIntegrationService.cs`; `Domain/Entities/ItemEstoque.cs` (linhas 150-260); `App/UseCases/RegistrarSaidaEstoque/RegistrarSaidaEstoqueUseCase.cs` (linhas 240-275, uso do método); `EasyStock.Application.Tests/**/EstoqueConcurrencyTests.cs` (nome exato via `git grep -l EstoqueConcurrency`).
**Tamanho.** P. **Tier.** alto (domínio).

---

### S18 · `PedidoPagoEvent` e SSE de operação com autenticação JWT

**Problema.** US-010, US-033, US-037: som e sinal verde quando o pagamento cai; a tela de cozinha atualiza sem piscar. O SSE existente (`Api/Mobile/Services/MobileEventBroker.cs` + `OperationController` em `api/mobile/operation`) publica `order.upsert`/`order.ready` a partir do sync mobile e exige `X-Mobile-Api-Key`. O console novo usa JWT e precisa de eventos do `Pedido`.
**Abordagem.** Manter o broker in-memory (uma instância de API; aceitável). Novo endpoint `GET api/operacao/eventos` `[Authorize]` (SSE, filtro por empresa da claim) publicando eventos nomeados; os use cases publicam pelo `IOperacaoEventPublisher` (porta nova) após o commit.
**Eventos.** `pedido.pago {pedidoId, numero, cliente, total, janela}`, `pedido.mudou_status {pedidoId, statusAntigo, statusNovo}`, `pedido.atrasado {pedidoId}` (S21), `conversa.mensagem_recebida {conversaId, clienteNome}` (S03), `estoque.desacerto {produtoId, falta}` (S17), `impressao.pendente {impressaoId}` (S20).
**Escopo.**
- Criar `App/Ports/Output/Operacao/IOperacaoEventPublisher.cs` e a implementação sobre `MobileEventBroker` (mover o broker para `Api/Services/Operacao/OperacaoEventBroker.cs`; o controller mobile continua funcionando apontando para a mesma instância até P05).
- Criar `Api/Controllers/OperacaoEventosController.cs` (`GET api/operacao/eventos`, `text/event-stream`, heartbeat a cada 25 s, `Last-Event-ID` ignorado nesta versão).
- Publicar em: `ConfirmarPagamentoPixPedidoUseCase` (S11) e no handler do Mercado Pago (`pedido.pago`); `AtualizarStatusPedidoUseCase` (`pedido.mudou_status`); S03 (`conversa.mensagem_recebida`); S17 (`estoque.desacerto`).
- Publicação **após** o commit (evento de UI, não de negócio): usar o padrão de "seam pós-commit" já discutido em #702 ou simplesmente chamar o publisher depois do `CommitAsync` no use case.
**Fora.** Redis pub/sub multi-instância; replay.
**Aceite.**
- [ ] Cliente SSE autenticado da empresa A recebe `pedido.pago` de A e não recebe de B.
- [ ] Sem JWT → 401. Heartbeat chega a cada 25 s.
- [ ] Publicação acontece só quando o commit teve sucesso (teste: exceção no commit → nenhum evento).
**Testes (Red).** `OperacaoEventBrokerTests.FiltraPorEmpresa`, `OperacaoEventosControllerTests.SemJwt401`, `ConfirmarPagamentoPixPedidoUseCaseTests.PublicaPedidoPagoAposCommit`.
**Rollback.** Remover controller e porta; mobile inalterado.
**Depende de.** S11.
**Leitura mínima.** `Api/Mobile/Services/MobileEventBroker.cs`; `Api/Mobile/Controllers/OperationController.cs` (linhas 380-420); `App/UseCases/AtualizarStatusPedido/AtualizarStatusPedidoUseCase.cs` (linhas 100-140).
**Tamanho.** M. **Tier.** alto.

---

### S19 · KDS sobre `Pedido` (não sobre o espelho mobile)

**Problema.** `Api/Mobile/Controllers/KdsController.cs` consulta `Domain/Entities/Mobile/Order` (espelho do MAUI, que sai em P05) com `X-Mobile-Api-Key`. US-037, US-038, US-041, US-042, RN-30, RN-31: cards por status com rótulo escrito, um toque muda o status, filtro por linha, marcação em lote quando a internet volta.
**Abordagem.** Controller novo `api/kds` (o antigo passa a `api/mobile/kds` até P05) sobre `Pedido`, JWT, delegando a `AtualizarStatusPedidoUseCase`.
**Escopo.**
- Criar `Api/Controllers/KdsController.cs`: `GET api/kds/pedidos?status=&linha=&data=` (padrão: `aguardando,preparando,pronto,saiu_para_entrega` de hoje e atrasados de ontem), `PATCH api/kds/pedidos/{id}/status {status}`, `POST api/kds/pedidos/status-lote [{id, status, ocorridoEm?}]` (aplica em sequência, devolve resultado por item, não para no primeiro erro).
- DTO `KdsPedidoDto`: `id`, `numeroCurto` (últimos 6 do id ou sequência do dia se existir), `clienteNome`, `clienteApt`, `janela {label, data, inicio, fim}`, `agendadoParaEm`, `status`, `statusRotulo` (de `StatusPedidoVocabulario`), `linhas[]` (distintas dos itens), `itens[] {nome, variacao, qtd, observacao, linha, molho}`, `inicioPrevistoEm`, `atrasado`, `pagoEm`, `criadoEm`, `observacoes`.
- `Permissao` existente para pedidos (`git grep "Permissao\." Api/Controllers/PedidosController.cs`), sem permissão nova.
- Renomear o controller antigo para `MobileKdsController` com rota `api/mobile/kds` (R8: `grep -rn "api/kds"` na PWA e no Web; ajustar os call-sites ou deixar quebrado com issue, já que `/pwa/#kds` não existe).
**Fora.** Drag and drop (front); som (front).
**Aceite.**
- [ ] `GET` devolve só pedidos da empresa do JWT, com `statusRotulo` e `linhas`.
- [ ] `PATCH` com transição inválida devolve 400 com a mensagem da `TransicaoInvalidaException`, e o status não muda.
- [ ] `status-lote` com 3 itens onde o 2º é inválido aplica o 1º e o 3º e reporta o 2º.
- [ ] Filtro `linha=preparar_em_casa` devolve só pedidos com ao menos um item dessa linha.
**Testes (Red).** `KdsControllerTests.ListaPorEmpresaComRotulo`, `...PatchInvalido400`, `...LoteContinuaAposErro`, `...FiltraPorLinha`.
**Rollback.** Remover o controller novo; o antigo volta ao caminho `api/kds`.
**Depende de.** S12, S15, S18.
**Leitura mínima.** `Api/Mobile/Controllers/KdsController.cs`; `Api/Controllers/PedidosController.cs` (só atributos de rota e policies); `App/UseCases/AtualizarStatusPedido/AtualizarStatusPedidoCommand.cs` (ou o record de comando no `UseCase.cs`); `Domain/Sales/StatusPedidoVocabulario.cs`; `App/Ports/Output/Persistence/IPedidoRepository.cs` (assinaturas de listagem).
**Tamanho.** M. **Tier.** alto.

---

### S20 · Canhoto e fila de impressão

**Problema.** US-035, US-036, US-042, RN-27 a RN-29, RN-33: pedido pago imprime sozinho; o canhoto agrupa por linha e traz porção, molho e observação por item; o papel basta para produzir sem sistema. A API está na nuvem e não alcança a impressora.
**Abordagem.** Backend agnóstico do dispositivo: uma fila persistida e dois formatos do canhoto. Quem consome a fila é decidido pela onda 0.6: bridge local (mini PC polling + ESC/POS), impressora com polling em nuvem, ou a aba do console imprimindo via navegador. Todos leem o mesmo endpoint.
**Escopo.**
- Criar `Domain/Entities/Operacao/ImpressaoPendente.cs`: `Id`, `EmpresaId`, `LojaId?`, `PedidoId`, `Tipo` (`canhoto`), `Status` (`Pendente`, `Impressa`, `Falhou`), `CriadaEm`, `ImpressaEm?`, `Tentativas`, `Erro?`. Migration `AddImpressaoPendente`. Índice `(EmpresaId, Status, CriadaEm)`.
- Enfileirar em `ConfirmarPagamentoPixPedidoUseCase` e no handler do Mercado Pago (S11/S12), na mesma transação do pedido pago; publicar `impressao.pendente` no SSE (S18) após commit.
- Criar `App/UseCases/Operacao/Impressao/MontarCanhotoUseCase.cs` → modelo `CanhotoDto` {cabeçalho: nome da casa, nº curto, cliente, telefone, endereço completo, janela e data, pago em; grupos por linha com itens (nome, variação/porção, qtd, molho (`SugestaoMolho` ou observação), observação); observações do pedido; rodapé: "imprima este canhoto: ele basta para produzir"}.
- `Api/Controllers/ImpressaoController.cs`: `GET api/pedidos/{id}/canhoto?formato=html|texto` (`html` = página 80 mm com `@media print`, reaproveitando `EasyStock.Web/wwwroot/css/recibo.css` copiado para a Api; `texto` = 42 colunas, sem acentos, pronto para ESC/POS); `GET api/impressao/pendentes?limite=10` (policy `Operador` **ou** header `X-Impressao-Api-Key` para o bridge, config `Impressao:ApiKey`); `POST api/impressao/{id}/impressa`; `POST api/impressao/{id}/falhou {erro}`; `POST api/pedidos/{id}/reimprimir`.
- Job `ImpressaoPendenteAlertaJob` (a cada 2 min): pendente há mais de 3 min → SSE `impressao.atrasada` (o console avisa a dona).
**Fora.** Driver ESC/POS no servidor; CloudPRNT/Server Direct Print (protocolos próprios; se a onda 0.6 escolher, vira spec própria).
**Aceite.**
- [ ] Pedido pago cria `ImpressaoPendente` na mesma transação (rollback do pagamento não deixa impressão órfã).
- [ ] `canhoto?formato=texto` agrupa por linha, um item por linha com qtd, porção, molho e observação; cabe em 42 colunas.
- [ ] `pendentes` com API key do bridge devolve só da empresa configurada; sem credencial → 401.
- [ ] `impressa` é idempotente.
**Testes (Red).** `MontarCanhotoUseCaseTests.AgrupaPorLinhaComObservacao`, `ImpressaoControllerTests.TextoCabeEm42Colunas`, `ConfirmarPagamentoPixPedidoUseCaseTests.EnfileiraImpressaoNaMesmaTransacao`, `ImpressaoControllerTests.ImpressaIdempotente`.
**Rollback.** Migration `Down`; sem consumidor a fila só acumula.
**Depende de.** S11, S15, S18.
**Leitura mínima.** `EasyStock.Web/Views/Pedidos/Recibo.cshtml` (só a estrutura do canhoto) e `EasyStock.Web/wwwroot/css/recibo.css`; `App/UseCases/ObterPedidoDetalhes/*.cs`; `Api/Middleware/IdempotencyMiddleware.cs` (linhas 200-240, `IdempotencyOptions.Add`).
**Tamanho.** M. **Tier.** alto (migration).

---

### S21 · Início previsto e atraso

**Problema.** US-037 e UC-04 E2: card muda para atraso quando o horário calculado de início passa e o preparo não começou. RN-30: rótulo escrito junto da cor.
**Abordagem.** Calcular e persistir `InicioPrevistoEm` no pedido quando ele é pago (S11/S12): `inicio da janela na data − PrazoMinimo(itens)` (S15). `Atrasado` é derivado: `Status == Aguardando && agora > InicioPrevistoEm`. Um avaliador leve publica `pedido.atrasado` uma vez por pedido.
**Escopo.**
- `Pedido.InicioPrevistoEm` (DateTime?) + `Pedido.AtrasoNotificadoEm` (DateTime?); migration `AddInicioPrevistoPedido`.
- Preenchido em `ConfirmarPagamentoPixPedidoUseCase` e no handler do Mercado Pago, lendo `VagaOcupada → JanelaEntrega.HoraInicio` e `CalculadoraPrazoPedido`.
- `KdsPedidoDto.inicioPrevistoEm` e `atrasado` (S19).
- `Api/BackgroundServices/PedidoAtrasoJob.cs` (a cada 60 s): pedidos `Aguardando` com `InicioPrevistoEm < agora` e `AtrasoNotificadoEm == null` → marca e publica `pedido.atrasado` (S18).
- Recalcular quando `AlterarAgendamentoPedidoUseCase` mudar a janela.
**Fora.** Aviso ao cliente sobre atraso (decisão da dona, D2).
**Aceite.**
- [ ] Pedido pago para janela 12:00 com prazo 100 min → `InicioPrevistoEm = 10:20` no fuso da loja.
- [ ] Job publica `pedido.atrasado` uma única vez por pedido.
- [ ] Reagendar recalcula e zera `AtrasoNotificadoEm`.
**Testes (Red).** `ConfirmarPagamentoPixPedidoUseCaseTests.CalculaInicioPrevisto`, `PedidoAtrasoJobTests.NotificaUmaVez`, `AlterarAgendamentoPedidoUseCaseTests.RecalculaInicioPrevisto`.
**Rollback.** Migration `Down`; job desligado por flag.
**Depende de.** S15, S18, S19.
**Leitura mínima.** `App/UseCases/AlterarAgendamentoPedido/*.cs`; `Domain/Entities/Storefront/JanelaEntrega.cs`; `App/Common/HorarioBrasil.cs`; `Api/BackgroundServices/BackgroundJobServiceCollectionExtensions.cs` (registro de job).
**Tamanho.** P. **Tier.** alto (migration).
