# ADR 0013 — CancellationToken na interface `IUseCase<TCommand, TResult>`

**Status:** Deferred (2026-05-16)
**Contexto:** Code review do módulo Fiscal NFC-e (sessão 2026-05-15/16) identificou que o contrato `EasyStock.Application.UseCases.Common.IUseCase<TCommand, TResult>` não recebe `CancellationToken`, e portanto toda a cadeia de cancellation entre HTTP/jobs → use case → repositórios/gateways está quebrada.

## Decisão

**Adiar o refactor.** Manter a assinatura atual:

```csharp
public interface IUseCase<TCommand, TResult> where TCommand : ICommand
{
    Task<TResult> ExecuteAsync(TCommand command);
}
```

## Por que adiou

1. **Surface area:** ~80 use cases existentes implementam esta interface. Cada um precisa de mudança de assinatura + atualização de todos os call-sites (controllers, jobs, testes). Risco de regressão silenciosa elevado.
2. **Sem incidente real:** até hoje (2026-05-16), nenhum incidente foi reportado por timeout não-cancelado em use case do EasyStok. O custo de retrabalho não tem ROI imediato.
3. **Mitigação parcial existente:** repositórios EF Core já recebem `CancellationToken` no nível de query (`FirstOrDefaultAsync(ct)`); HTTP clients (Polly + `IHttpClientFactory`) propagam timeout via configuração. O gap fica em chamadas multi-step dentro de um use case (ex: `EmitirNfceUseCase` faz 2 transações + 1 HTTP — se o cliente desistir entre Tx1 e HTTP, o use case continua executando).
4. **Refactor é coeso:** fazer parcial (só use cases novos) cria inconsistência permanente — pior que não fazer.

## Quando entra

Refactor deve ser feito em **ondas coordenadas** (1 onda por área: Pedidos, Estoque, Fiscal, etc.) num PR de **infraestrutura dedicado**, fora de qualquer feature delivery. Critério de timing:

| Janela | Condição |
|---|---|
| Próxima janela de refactor de Application Layer | Default — feito junto com qualquer mudança grande na interface (ex: adicionar `IRequestContext` para audit centralizado) |
| Imediato | Se qualquer um dos critérios de "Quando NÃO adiar mais" abaixo for satisfeito |

## Quando NÃO adiar mais (gatilhos de promoção a bloqueante)

Qualquer um destes gera issue + PR no sprint corrente:

1. **Primeiro incidente real** de cancellation chain quebrada em produção — cliente fecha aba, mas use case continua executando, gerando estado inconsistente (NFC-e duplicada, pagamento dobrado, etc.).
2. **Use case que orquestra >2 operações de I/O remoto** (HTTP + DB + outra HTTP). Custo do timeout não-cancelado escala com nº de I/Os.
3. **APM (Application Insights) reportar >1% de requests com long-running activities** após cliente cancelar (heartbeat web socket fechado, mas trace continua).
4. **Quando o módulo Fiscal escalar para >100 NFC-e/min em produção** — gateway externo (Focus NFe) tem timeout 30s; cancellation evita acumular requests órfãos enchendo o pool de threads.

## Trade-off explícito (o que perdemos enquanto não temos)

- **Recursos desperdiçados em request cancelado:** se cliente fecha aba durante `EmitirNfceUseCase`, o use case completa todas as 2 transações + chamada HTTP ao Focus NFe (8-30s de CPU/network). Resultado fica órfão no DB (NFC-e Autorizada que ninguém viu).
- **Pior caso DoS soft:** ator malicioso pode iniciar requests pesados e fechar aba — servidor continua processando. Mitigação atual: rate limiting + auth. Suficiente para o piloto Casa da Babá.
- **Graceful shutdown ruim:** Worker que recebe SIGTERM aguarda use cases em andamento até timeout (default 30s do Kestrel/Worker host) — mas use case não sabe disso, então não tenta encerrar cedo. Casos de borda: deploy demora mais; em pior cenário, processo é kill -9.
- **Testes de stress complicados:** sem CT, testes de carga não conseguem cancelar requests para medir comportamento sob abandono — métrica de "abandoned request rate" não existe.

## Decisão para módulo Fiscal (escopo desta sessão)

Implementação atual aceita o trade-off:
- `EmitirNfceUseCase` não pode ser cancelado a partir do middleware HTTP. Cliente fecha aba? Use case completa, NFC-e é emitida, cliente nunca vê resposta — mas dado fica consistente.
- `ReprocessarContingenciaBackgroundService` recebe CT do host (graceful shutdown), mas o `ReprocessarContingenciaUseCase` por dentro não. Pior caso: shutdown demora até completar o lote em curso (max 30s × batch_size = 25min em pior teórico; típico 1-2s × 50 = 1min).

## Relacionado

- Code review 2026-05-16 — sugestão #4
- Plano `~/.claude/plans/analise-na-pasta-de-sprightly-melody.md`
- Sessão de implementação F1-F6 (2026-05-15/16)
