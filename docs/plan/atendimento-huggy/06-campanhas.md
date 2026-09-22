# Onda 6 — Campanhas e relacionamento (S28–S31)

Objetivo: a dona filtra quem avisar, anexa a arte, agenda o disparo, o sistema exclui quem não deve
receber e nunca manda mais de uma campanha por semana para o mesmo cliente. Segunda onda só por
decisão dela.
Cobre US-052 a US-059, RN-39 a RN-43, UC-07.

Dependência externa: onda 0.8 (templates de marketing aprovados). Fora da janela de 24 h, a Meta só
aceita template; o custo por conversa de marketing é maior que o de utilidade. O provider é o de S09.

---

### S28 · `Campanha` e `CampanhaDestinatario`

**Problema.** Não existe campanha. `Cupom` é de plano SaaS (sai em P02) e não serve.
**Abordagem.** Duas entidades; o envio real reusa o outbox de notificações (retry, log, janela) com um `OutboxMensagemNotificacao` por destinatário e `ProximaTentativaEm = DisparoEm` (mesmo desenho do adiamento por janela, decisão de 08/08/2026).
**Escopo.**
- Criar `Domain/Entities/Campanhas/Campanha.cs`: `Id`, `EmpresaId`, `Nome`, `Mensagem` (Scriban com `{{nome}}`), `ImagemUrl?`, `TemplateHuggyId?` (HSM), `FiltroJson` (tags incluir, tags excluir, comprou item X nos últimos N dias, todos), `TagsRestricaoExcluidas` (csv), `Status` (`Rascunho`, `Agendada`, `Enviando`, `Enviada`, `Encerrada`, `Cancelada`), `DisparoEm?`, `EncerramentoEm?`, `EnviarLembreteEncerramento` (bool), `TamanhoOnda?` (int; null = todos), `OndaAtual`, `CriadaEm`, `CriadaPorUsuarioId`. Métodos `Agendar`, `IniciarOnda`, `Encerrar`, `Cancelar`.
- Criar `CampanhaDestinatario.cs`: `Id`, `EmpresaId`, `CampanhaId`, `ClienteId`, `Onda`, `Status` (`Pendente`, `Excluido`, `Enfileirado`, `Enviado`, `Falhou`, `Pediu`), `MotivoExclusao?` (`restricao`, `limite_semanal`, `sem_consentimento`, `bloqueado`, `sem_telefone`), `OutboxMensagemId?`, `EnviadoEm?`, `PedidoId?`. Único por `(CampanhaId, ClienteId)`.
- Migration `AddCampanhas`. Índices `(EmpresaId, Status)`, `(ClienteId, EnviadoEm)` para o limite semanal.
- `Api/Controllers/CampanhasController.cs`: `GET|POST api/campanhas`, `GET|PUT api/campanhas/{id}`, `POST api/campanhas/{id}/cancelar`. Upload da arte usa `UploadsController` existente.
**Fora.** Cálculo de público (S29); disparo (S30).
**Aceite.**
- [ ] `Agendar` exige `DisparoEm` futuro e `Mensagem` não vazia; com `TamanhoOnda` exige `> 0`.
- [ ] `Cancelar` em `Enviando` marca destinatários `Pendente` como `Excluido(motivo="cancelada")` sem tocar nos `Enviado`.
- [ ] Filtro de tenant ativo nas duas tabelas.
**Testes (Red).** `CampanhaTests.AgendarValida`, `CampanhaTests.CancelarPreservaEnviados`, `CampanhaRepositoryTests.IsolamentoDeTenant`.
**Rollback.** Migration `Down`.
**Depende de.** S24 (tags).
**Leitura mínima.** `Domain/Entities/Storefront/PedidoAvaliacao.cs` (padrão); `Domain/Entities/Notifications/OutboxMensagemNotificacao.cs`; `Api/Controllers/UploadsController.cs` (só rotas).
**Tamanho.** M. **Tier.** alto (migration).

---

### S29 · Público, exclusões e limite semanal

**Problema.** US-052, US-056, US-058, RN-39, RN-40: filtrar por tag e histórico; excluir restrição incompatível; no máximo uma campanha a cada 7 dias por cliente; mostrar à dona quantos saíram e por quê antes de enviar.
**Abordagem.** Um use case puro de cálculo que materializa `CampanhaDestinatario` com `Pendente` ou `Excluido(motivo)`, idempotente (recalcular substitui os `Pendente`/`Excluido`, preserva `Enviado`).
**Escopo.**
- Criar `App/UseCases/Campanhas/CalcularPublicoCampanhaUseCase.cs`: base = clientes ativos da empresa com telefone; filtros de `FiltroJson`: tags incluídas (todas presentes), tags excluídas, "comprou item X" (`PedidoItem.CardapioItemId` ou `ProdutoId` em pedidos `Entregue` nos últimos N dias), "todos". Exclusões em ordem, com motivo: `bloqueado` (`Cliente.Bloqueado`), `sem_consentimento` (`!ConsentiuMarketing`), `restricao` (interseção com `TagsRestricaoExcluidas`), `limite_semanal` (existe `CampanhaDestinatario.Enviado` do cliente com `EnviadoEm > agora − 7 dias`), `sem_telefone`.
- Resultado: `{ Total, Pendentes, ExcluidosPorMotivo {motivo: n}, Amostra (10 nomes) }`.
- `POST api/campanhas/{id}/publico` recalcula e devolve o resumo; `GET api/campanhas/{id}/destinatarios?status=`.
- Query em `Postgre/Queries/CampanhaPublicoQueries.cs` com `EmpresaId` no `WHERE`.
**Fora.** Segmentação por valor gasto; RFM.
**Aceite.**
- [ ] Cliente com tag `intolerante_lactose` e campanha com `TagsRestricaoExcluidas=intolerante_lactose` → `Excluido(restricao)`.
- [ ] Cliente que recebeu campanha há 5 dias → `Excluido(limite_semanal)`; há 8 dias → `Pendente`.
- [ ] Recalcular após um envio preserva os `Enviado` e refaz os demais.
- [ ] Resumo devolve contagem por motivo igual à soma dos destinatários excluídos.
**Testes (Red).** `CalcularPublicoCampanhaUseCaseTests.ExcluiRestricao`, `...LimiteSemanal7Dias`, `...RecalculoPreservaEnviados`, `...FiltroComprouItem`.
**Rollback.** Remover use case e endpoints.
**Depende de.** S28, S24.
**Leitura mínima.** `Domain/Entities/Campanhas/*.cs` (S28); `Domain/Entities/ClienteTag.cs` (S24); `Domain/Entities/Pedido.cs` (linhas 236-270, `PedidoItem`); `Postgre/Queries/` (um arquivo existente como padrão, ex.: `EstoqueAnalyticsQueries.cs`, só a assinatura).
**Tamanho.** M. **Tier.** alto.

---

### S30 · Disparo, ondas, encerramento e lembrete

**Problema.** US-053 a US-055, US-059, RN-41 a RN-43: disparo agendado; estoque menor que o público → onda com prioridade; segunda onda só pela dona; lembrete no encerramento para quem recebeu e não pediu; sazonal com uma semana de antecedência.
**Abordagem.** Disparar = enfileirar no outbox de notificações um `OutboxMensagemNotificacao` por destinatário da onda, canal WhatsApp, categoria `Marketing`, `ProximaTentativaEm = DisparoEm`, template renderizado com nome, e `Metadados` com `template_id` para HSM (S09). Prioridade da primeira onda: clientes que já compraram o item da campanha (ou `ItemFavorito`), depois os demais. Job de encerramento envia o lembrete.
**Escopo.**
- Criar `App/UseCases/Campanhas/DispararOndaCampanhaUseCase.cs` (onda N: seleciona até `TamanhoOnda` destinatários `Pendente` ordenados por prioridade; marca `Enfileirado` com `OutboxMensagemId`; `Campanha.OndaAtual = N`, `Status = Enviando`). Onda 1 automática quando `DisparoEm` chega (job); ondas seguintes **só** por `POST api/campanhas/{id}/ondas` (decisão da dona, RN-42).
- `TipoEventoNotificacao.CampanhaMarketing = 44`, `CampanhaLembreteEncerramento = 45`; templates seed.
- Callback do outbox: quando a mensagem é enviada, `CampanhaDestinatario.Status = Enviado`, `EnviadoEm`; falha → `Falhou`. Ponto de extensão: `NotificadorService` já grava `LogEnvioNotificacao`; adicionar um `IOutboxEnvioObserver` simples chamado no mesmo lugar (verificar se já existe hook; se não, criar).
- `Api/BackgroundServices/CampanhaJob.cs` (a cada 60 s): dispara onda 1 das `Agendada` vencidas; ao chegar `EncerramentoEm`, marca `Encerrada` e enfileira o lembrete para `Enviado` sem `PedidoId` (com `EnviarLembreteEncerramento`).
- Atribuição de pedido: ao criar pedido (S10) de cliente com destinatário `Enviado` em campanha `Enviando`/`Enviada` nos últimos 7 dias → `Status = Pediu`, `PedidoId` (métrica de conversão para US-030 futuro).
**Fora.** Relatório de conversão; Instagram.
**Aceite.**
- [ ] Campanha com 50 pendentes e `TamanhoOnda=30`: onda 1 enfileira 30 com os que já compraram o item primeiro; 20 continuam `Pendente`; nada dispara sozinho depois.
- [ ] `POST ondas` enfileira a onda 2; `DisparoEm` no passado não reativa a onda 1.
- [ ] Lembrete vai só para `Enviado` sem `PedidoId`.
- [ ] Cada destinatário tem `ProximaTentativaEm = DisparoEm` no outbox e `Categoria = Marketing` (o `ResolvedorCanal` respeita kill-switch e consentimento por categoria).
**Testes (Red).** `DispararOndaCampanhaUseCaseTests.PrimeiraOndaPrioridadeETamanho`, `...SegundaOndaSoManual`, `CampanhaJobTests.LembreteSoParaQuemNaoPediu`, `CriarPedidoAtendimentoUseCaseTests.MarcaDestinatarioComoPediu`.
**Rollback.** Job desligado por flag; campanhas ficam `Agendada`.
**Depende de.** S09, S29.
**Leitura mínima.** `App/Services/Notifications/NotificadorService.cs`; `App/Services/Notifications/ResolvedorCanal.cs`; `App/UseCases/Notifications/PublicarEventoNotificacaoCommand.cs`; `Api/BackgroundServices/ContaReceberPixReconciliacaoJob.cs` (padrão de job).
**Tamanho.** G. **Tier.** alto.

---

### S31 · Interesse em item e aviso de volta

**Problema.** US-057, UC-01 E1: cliente quis item indisponível; quando volta, a dona é lembrada de avisar.
**Abordagem.** Entidade mínima alimentada pelo agente (`registrar_interesse`) e pela recusa de item esgotado; ao republicar o item, o sistema sugere (não dispara).
**Escopo.**
- Criar `Domain/Entities/Campanhas/InteresseItem.cs`: `Id`, `EmpresaId`, `ClienteId`, `CardapioItemId?`, `Descricao` (texto livre quando não há item), `Origem` (`agente`, `dona`), `RegistradoEm`, `AtendidoEm?`. Migration `AddInteresseItem`.
- Ferramenta `registrar_interesse` (S06) e `POST api/clientes/{id}/interesses`.
- `GET api/campanhas/sugestoes/interesse?cardapioItemId=` → clientes com interesse aberto no item (para a dona montar a campanha ou avisar na mão).
- Ao `ToggleDisponibilidade(true)` ou `ToggleVisibilidade(true)` de `CardapioItem` com interesses abertos → SSE `cardapio.item_com_interesse {cardapioItemId, quantidade}` (S18). Nada é enviado ao cliente sem ação da dona (D8).
**Fora.** Disparo automático.
**Aceite.**
- [ ] `registrar_interesse` grava com `CardapioItemId` quando o agente identifica o item, senão só `Descricao`.
- [ ] Republicar item com 3 interesses abertos publica o evento SSE com `quantidade=3`.
- [ ] Sugestão lista só interesses com `AtendidoEm == null`.
**Testes (Red).** `InteresseItemTests.CriaComOuSemItem`, `ToggleDisponibilidadeTests.PublicaInteresse`, `SugestoesInteresseQueryTests.SoAbertos`.
**Rollback.** Migration `Down`.
**Depende de.** S06, S18.
**Leitura mínima.** `App/UseCases/Admin/Storefront/Cardapio/ToggleDisponibilidadeCardapioItemAdminUseCase.cs` (nome aproximado; localizar com `git grep -l ToggleDisponibilidade App/UseCases/Admin/Storefront`); `Domain/Entities/Storefront/CardapioItem.cs` (métodos de disponibilidade).
**Tamanho.** P. **Tier.** alto (migration).
