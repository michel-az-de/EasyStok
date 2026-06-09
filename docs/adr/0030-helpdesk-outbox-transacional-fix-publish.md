# ADR-0030 — Helpdesk/notificacoes: fix do publish (Add-then-Update) + outbox transacional

**Status:** Aceito
**Data:** 2026-06-08
**Relacionado:** #547 (epico Helpdesk solucao definitiva), #548 (P0-1a incidente), ADR-0028 (nao chamar Update em raiz rastreada), ADR-0010 (RLS), ADR-0029 (poka-yoke)

## Contexto

QA 2026-06-08 no `/Tickets` (Admin): toda mutacao (criar/assumir/encaminhar/gerar bug-fix/resolver) retornava erro ao operador (409 "Os dados foram alterados por outro processo"; 500 na criacao) MAS persistia. Hipotese do QA (concorrencia otimista/rowversion obsoleto no DOM entre acoes AJAX): REFUTADA por medicao — `AdminTicket` nao tem token de concorrencia e todas as acoes da tela sao full-POST com `RedirectToPage` (nao ha mutacao AJAX nem rowversion no DOM).

Medicao em producao (read-only, Postgres da VM, 2026-06-08): `admin_tickets`=2, `ticket_historico`=10 (do dia), `notif_eventos`=0, `notif_outbox_mensagens`=0. O negocio e o historico persistem em cada mutacao, mas o EVENTO de notificacao NUNCA grava. A tabela esta universalmente vazia: nao e so helpdesk — os jobs (fatura/assinatura) que tambem chamam `PublicarEventoAsync` estao com notificacao MORTA. Apagao de notificacoes de todo o sistema (nao "lentidao").

Causa-raiz, 2 camadas:

1. **Mecanismo (instancia do ADR-0028).** `NotificadorService.PublicarEventoAsync` faz `eventoRepository.AddAsync(evento)` (estado `Added`) e, dentro de `ProcessarEventoInternoAsync`, `eventoRepository.UpdateAsync(evento)` (= `db.NotifEventos.Update`, rebaixa a `Modified`) no MESMO evento ainda nao inserido (`EventoNotificacao.Criar` seta `Id` client-side). No `CommitAsync` -> `UPDATE ... WHERE Id=@novo` -> **0 linhas** -> `DbUpdateConcurrencyException` -> `GlobalExceptionHandler` -> 409. E o anti-padrao do ADR-0028 ponto 2 ("nao chamar `Update` em entidade ja rastreada"), aqui numa raiz `Added` diretamente. O avaliador (`AvaliarEventoAsync`) NAO sofre: le o evento via `AsNoTracking` (Detached), entao o `Update` acerta 1 linha.

2. **Estrutural.** `PublicarEventoAsync` roda AGUARDADO APOS o `db.CommitAsync()` do negocio, no MESMO `EasyStockDbContext` (que e o `IUnitOfWork`), com um segundo commit. Qualquer throw no publish (a camada 1, ou um render Scriban quebrado, ou a escrita do outbox) corrompe a resposta de um write ja-commitado. E `PublicarEventoAsync` NAO e poison-safe (so `AvaliarEventoAsync` envolve `ProcessarEventoInternoAsync` em try/catch). Na criacao ha ainda um 3o efeito pos-commit (`audit.LogAsync` no controller).

O gap passou porque nenhum teste instanciava o `NotificadorService` real (os testes de ticket mockam `INotificadorService`), entao o caminho `PublicarEventoAsync` ponta-a-ponta nunca rodou contra Postgres.

## Decisao

1. **Fix do publish no locus correto (repositorio).** A camada Application (`NotificadorService`) nao enxerga estado EF; a decisao pertence ao repositorio, que tem o `DbContext`. `EventoNotificacaoRepository.UpdateAsync` so faz `Update` quando a entidade esta `Detached` (caminho do avaliador); quando ja rastreada (`Added`/`Modified`, caminho do publish) e no-op — a mutacao in-place (`MarcarComoProcessado`/`MarcarComoFalhado`, feita ANTES do Update) persiste no proximo `SaveChanges` como INSERT. Ressuscita a notificacao de TODO o sistema (helpdesk + jobs). Unico caller real do `UpdateAsync` e `ProcessarEventoInternoAsync` (2 paths). NB: diferente de `FaturaRepository.UpdateAsync` (ADR-0028 ponto 2), que faz fail-fast em `Detached` — aqui o caminho `Detached` e LEGITIMO (avaliador le AsNoTracking), entao o contrato e inverso: `Update` so em `Detached`.

2. **Outbox transacional nos fluxos de helpdesk/storefront (endurecimento estrutural, necessario).** Como o publish nao e poison-safe e roda pos-commit, um novo `EnfileirarEventoAsync` (so `AddAsync` do evento `Pendente`, sem processar/enviar, sem commit proprio) e chamado ANTES do unico `CommitAsync` do negocio. O evento grava ATOMICO com o negocio; nada aguardado roda apos o commit. O avaliador (timer ~60s, poison-safe) processa fora de banda. O `IOutboxSignaler` (LISTEN/NOTIFY) acorda o dispatcher de MENSAGENS, nao o avaliador — enqueue nao precisa sinalizar.

3. **Auditoria pos-commit best-effort onde cabe, NAO blanket.** `AdminAuditService` ganha um caminho best-effort (`TryLogAsync`) usado nas mutacoes de ticket (auditoria de operacao nao pode derrubar a operacao). A auditoria de REVELACAO de PII (`HelpdeskClienteService.RevelarAsync`, LGPD) mantem `LogAsync` que LANCA — o registro de quem revelou documento deve ser garantido.

4. **Nao enfileirar evento condenado a `Falhado`.** `TicketCriado` e `TicketEncaminhadoNivel` so teriam destinatario de "fila/nivel", que `ResolverDestinatario` nao resolve hoje (retorna vazio -> `Falhado`). Enfileira-los poluiria `notif_eventos` permanentemente e mataria o sinal de `Falhado`. Ficam de fora ate haver modelo de destinatario de fila (P1). Decisao explicita: don't-enqueue-yet > enqueue-then-fail.

## Consequencias

- (+) Notificacoes do sistema inteiro voltam a funcionar (helpdesk + jobs); o operador para de ver erro falso em acoes que persistem.
- (+) Evento de notificacao grava atomico com o negocio (helpdesk); falha de notificacao nunca mais corrompe a resposta nem perde evento silenciosamente.
- (+) `Falhado` em `notif_eventos` volta a ser sinal de problema REAL.
- (-) Latencia de avaliacao do evento ate ~60s (timer do avaliador); aceitavel — o processamento ja era assincrono (o publish so persistia).
- (-) RISCO customer-facing: ligar o publish REATIVA o envio real dos jobs (fatura/assinatura), cujos templates nunca renderizaram em prod. GATE pre-deploy: dry-run de `renderer.RenderizarAsync` contra os templates seedados com payloads de exemplo, SEM enviar; opcional feature-flag no envio do dispatcher p/ tipos nao-helpdesk ate validar.
- (-) `PublicarEventoAsync` segue nao-poison-safe para os callers de background (jobs); a decisao 1 os faz gravar/processar, mas torna-lo poison-safe (ou migra-los a enqueue) e follow-up.

## Faseamento

- P0-1a (hotfix, #548): decisao 1 (fix do publish). GATE pre-deploy = dry-run de render.
- P0-1b: decisoes 2/3/4 (`EnfileirarEventoAsync` nos fluxos de ticket + `TryLogAsync` + nao-enfileirar-garbage) + BUG-03/04/05/09 + storefront.
- P1+ (epico #547): state machine, identidade do solicitante, destinatario de fila (destrava TicketCriado/Encaminhado), auto-escalonamento.
