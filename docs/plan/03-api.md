# 03 — Contratos de API

> Parte do [Plano](README.md). Anterior: [02-estados-e-eventos.md](02-estados-e-eventos.md). Próximo: [04-ux.md](04-ux.md).

### D.0 Padrões transversais

- **Prefixo**: `/api/` (padrão do projeto — `PedidosController` usa
  `/api/pedidos`). Sem versionamento de URL — versionamento via header
  `Api-Version: 1` quando necessário (não necessário neste módulo).
- **Auth**: header `Authorization: Bearer <jwt>` obrigatório. Claims:
  `empresaId`, `sub` (usuarioId), `nivel`, `permissao`.
- **Tenant**: derivado de `empresaId` claim. Não aceita override por header.
- **Erros**: ProblemDetails RFC 7807 (já em uso). Estrutura:
  ```json
  {
    "type": "https://easystok.com/erros/validacao",
    "title": "Valor inválido",
    "status": 422,
    "detail": "Valor do pagamento excede o saldo devedor do pedido (R$ 80,00).",
    "instance": "/api/pedidos/abc.../pagamentos",
    "codigo": "PAGAMENTO_EXCEDE_TOTAL",
    "campos": [ { "campo": "valor", "mensagem": "deve ser <= 80,00" } ]
  }
  ```
  Campo `codigo` (custom) permite traduções no PWA; `detail` em PT-BR para
  operador final.
- **Status codes**:
  - `400` validação de input (schema, tipos)
  - `401` JWT inválido/expirado
  - `403` permissão insuficiente (`Permissao` no JWT não cobre operação)
  - `404` recurso não existe ou não pertence à empresa
  - `409` conflito de estado (ex: sessão já fechada)
  - `410` endpoint deprecated (após F+1)
  - `422` regra de negócio violada (excedente, transição inválida)
  - `423` locked (ex: sessão em conferência)
  - `500` erro inesperado
- **Idempotency-Key**: obrigatório em POST que cria estado. Whitelist
  estendida em `IdempotencyOptions.Add()` (ver D.4).
- **Datas**: ISO 8601 UTC (`2026-05-16T14:30:00Z`).
- **Money**: number JSON (decimal). Ex: `"valor": 49.90`.

### D.1 Pagamentos

#### D.1.1 `GET /api/pedidos/{pedidoId}/pagamentos`

- **Auth**: requerido
- **Query params**: `?incluirEstornados=false` (default `false`)
- **Response 200**:
  ```json
  {
    "items": [
      {
        "id": "uuid",
        "pedidoId": "uuid",
        "metodo": "pix",
        "valor": 49.90,
        "status": "confirmado",
        "conciliacaoTipo": "adquirente",
        "referencia": "E12345...",
        "observacao": null,
        "pagoEm": "2026-05-16T14:30:00Z",
        "registradoPorUserId": "uuid",
        "registradoPorNome": "Thatiane",
        "estornadoEm": null,
        "motivoEstorno": null,
        "pagamentoOriginalId": null,
        "movimentoCaixaId": "uuid"
      }
    ],
    "totalConfirmado": 49.90,
    "totalEstornado": 0.00,
    "estadoFinanceiro": "ParcialmentePago"
  }
  ```
- **Response 404**: pedido não existe ou não pertence à empresa
- **Validação**: nenhuma além de auth/tenant
- **Side effects**: nenhum

#### D.1.2 `POST /api/pedidos/{pedidoId}/pagamentos`

- **Auth**: requerido + permissão `pedidos.pagamentos.registrar`
- **Headers**: `Idempotency-Key: <uuid>` **obrigatório**
- **Body**:
  ```json
  {
    "metodo": "pix",
    "valor": 49.90,
    "referencia": "E12345abc",
    "observacao": null,
    "pagoEm": "2026-05-16T14:30:00Z"
  }
  ```
- **Validação server-side**:
  - `metodo ∈ {pix, dinheiro, credito, debito, transferencia, outro}` → 422 `METODO_INVALIDO`
  - `valor > 0` → 422 `VALOR_INVALIDO`
  - `valor + pedido.TotalPagoConfirmado <= pedido.Total` → 422 `PAGAMENTO_EXCEDE_TOTAL`
  - `pedido.Status != "cancelado"` → 422 `PEDIDO_CANCELADO`
  - Sessão de hoje (`DataOperacional = pagoEm.Date`) NÃO está em `em_conferencia` → 423 `SESSAO_EM_CONFERENCIA`
  - `pagoEm <= now + 1min` (rejeita futuro óbvio) → 422 `DATA_FUTURA`
- **Response 201**:
  ```json
  {
    "pagamento": { /* mesmo formato de D.1.1 item */ },
    "pedido": { /* PedidoResult, com totalPago atualizado */ },
    "sessaoCaixa": { /* SessaoCaixaResumida se foi aberta agora ou já existia */ }
  }
  ```
- **Response 409**: `Idempotency-Key` duplicada com body diferente
- **Side effects**:
  - Cria `PedidoPagamento` (status `confirmado`)
  - Se nenhuma sessão de hoje: abre `SessaoCaixa` automaticamente (saldo
    inicial 0)
  - Cria `MovimentoCaixa` tipo `pagamento` linkado a `pagamento.Id` e
    `sessaoCaixa.Id`
  - Emite evento `PagamentoConfirmado` (inline + outbox)
  - Registra `PedidoEvento` tipo `"pagamento"`
- **Tudo em uma única transação**

#### D.1.3 `POST /api/pagamentos/{pagamentoId}/estornar`

- **Auth**: requerido + permissão `pedidos.pagamentos.estornar`
- **Headers**: `Idempotency-Key: <uuid>` obrigatório
- **Body**:
  ```json
  {
    "motivo": "Cliente desistiu da compra"
  }
  ```
- **Validação**:
  - `motivo` obrigatório, min 10 chars, max 500 → 422 `MOTIVO_OBRIGATORIO`
  - `pagamento.Status == "confirmado"` → 422 `PAGAMENTO_JA_ESTORNADO`
  - Sessão original (do `pagamento.MovimentoCaixa.SessaoCaixa`) NÃO está
    em `em_conferencia` → 423 `SESSAO_EM_CONFERENCIA`
- **Comportamento**:
  - **NÃO altera registro original** — cria NOVO `PedidoPagamento` com:
    - `Valor = original.Valor` (positivo)
    - `Status = "estornado"` (informativo — o registro novo já nasce como contra-lançamento)
    - `PagamentoOriginalId = original.Id`
    - `Metodo = original.Metodo`
    - `MotivoEstorno = body.motivo`
  - Marca o **original** como `Status = "estornado"`, `EstornadoEm = now`,
    `EstornadoPorUserId`, `MotivoEstorno`
  - Se a sessão da venda original está aberta hoje: cria `MovimentoCaixa`
    tipo `estorno_pagamento` linkado, `MovimentoOriginalId =
    original.MovimentoCaixaId`. Se já fechada: estorno fica sem movimento
    em caixa (aparece em ajuste do dia atual).
- **Response 200**:
  ```json
  {
    "estorno": { /* novo PedidoPagamento (status estornado) */ },
    "pagamentoOriginal": { /* original atualizado */ },
    "pedido": { /* totalPago recalculado */ }
  }
  ```
- **Side effects**: emite `PagamentoEstornado` (inline + outbox)

#### D.1.4 Deprecation: `DELETE /api/pedidos/{pedidoId}/pagamentos/{pagamentoId}`

- **Status atual**: existe, faz DELETE físico (gap A.6 #1)
- **F2**: passa a retornar `200` mas internamente **chama `EstornarPagamentoUseCase`**
  com motivo `"Removido via endpoint legado"`. Log warning. Sem migration de DB.
- **F+1 (release seguinte)**: endpoint retorna **HTTP 410 Gone** com
  ProblemDetails apontando para o novo. Cliente PWA já migrado.
- **Justificativa**: mobile MAUI usa este endpoint via sync. Quebra dura
  travaria APK existente.

### D.2 Caixa

#### D.2.1 `GET /api/caixa/sessao-atual`

- **Auth**: requerido
- **Query**: `?lojaId=<uuid>` opcional (default: matriz/null)
- **Response 200** (sessão aberta ou em conferência existe hoje):
  ```json
  {
    "sessao": {
      "id": "uuid",
      "status": "aberta",
      "dataOperacional": "2026-05-16",
      "abertaEm": "2026-05-16T08:00:00Z",
      "abertaPorNome": "Thatiane",
      "saldoInicial": 100.00,
      "iniciadaConferenciaEm": null
    },
    "resumo": {
      "totalVendas": 450.00,
      "totalPagamentosConfirmados": 850.00,
      "totalEntradasExtras": 50.00,
      "totalSaidasExtras": 30.00,
      "saldoEsperadoFisico": 720.00,
      "saldoEsperadoAdquirente": 250.00,
      "totalNaoConciliavel": 0.00
    }
  }
  ```
- **Response 404**: nenhuma sessão hoje (cliente exibe botão "Abrir caixa")

#### D.2.2 `POST /api/caixa/abrir`

- **Auth**: + permissão `caixa.abrir`
- **Headers**: `Idempotency-Key` obrigatório
- **Body**:
  ```json
  { "lojaId": null, "saldoInicial": 100.00, "observacoes": "Troco inicial" }
  ```
- **Validação**:
  - `saldoInicial >= 0` → 422
  - Nenhuma sessão `aberta`/`em_conferencia` hoje na (empresa, loja) → 409
    `SESSAO_JA_EXISTE`
- **Response 201**: `{ sessao: { ... } }`
- **Side effects**: cria `SessaoCaixa` + `MovimentoCaixa` tipo `abertura`
  com valor `saldoInicial`

#### D.2.3 `GET /api/caixa/sessoes/{sessaoId}`

- **Auth**: + permissão `caixa.ver`
- **Response 200**: sessão + agregados (mesmo formato D.2.1) + lista paginada
  de movimentos via query separada
- **404**: sessão não existe ou não pertence à empresa

#### D.2.4 `POST /api/caixa/sessoes/{sessaoId}/movimentos`

- **Auth**: + permissão `caixa.movimentos.registrar`
- **Headers**: `Idempotency-Key` obrigatório
- **Body**:
  ```json
  {
    "tipo": "sangria",
    "valor": 200.00,
    "metodo": "dinheiro",
    "categoria": "deposito_bancario",
    "descricao": "Levei pro banco às 14h",
    "referencia": null,
    "dataMovimento": "2026-05-16T14:00:00Z"
  }
  ```
- **Validação**:
  - `tipo ∈ {sangria, reforco, despesa, entrada}` → 422 (tipos `abertura`,
    `fechamento`, `pagamento`, `estorno_pagamento` rejeitados — esses são
    gerados pelo sistema)
  - `valor > 0` → 422
  - `sessao.Status == "aberta"` → 423 se em_conferencia/fechada
  - `dataMovimento` dentro do dia da sessão (ou retroativo permitido se
    `permitirRetroativo=true` querystring) → 422 `DATA_FORA_DA_SESSAO`
- **Response 201**: `{ movimento: { ... }, resumoAtualizado: { ... } }`
- **Side effects**: cria `MovimentoCaixa` linkado, emite
  `MovimentoManualRegistrado` (outbox)

#### D.2.5 `POST /api/caixa/sessoes/{sessaoId}/iniciar-fechamento`

- **Auth**: + permissão `caixa.fechar`
- **Headers**: `Idempotency-Key` obrigatório
- **Body**: vazio `{}`
- **Validação**:
  - `sessao.Status == "aberta"` → 409 `SESSAO_NAO_ESTA_ABERTA`
  - `now - sessao.AbertaEm >= 10 min` → 422 `SESSAO_RECENTE_DEMAIS` (config:
    `Caixa:MinutosMinimosParaFechar`, default 10)
  - Nenhum `PedidoPagamento` ou `MovimentoCaixa` criado nos últimos 30s
    (proteção contra fechamento de race) → 409 `MOVIMENTO_RECENTE`
- **Response 200**: `{ sessao: { ...status="em_conferencia"... }, snapshotConferencia: { ... } }`
- **Side effects**: muda status para `em_conferencia`, congela snapshot
  temporário em memória/cache, registra `IniciadaConferenciaEm`

#### D.2.6 `POST /api/caixa/sessoes/{sessaoId}/confirmar-fechamento`

- **Auth**: + permissão `caixa.fechar`
- **Headers**: `Idempotency-Key` obrigatório
- **Body**:
  ```json
  {
    "valorContadoFisico": 718.50,
    "conferenciaItens": [
      { "metodo": "dinheiro", "esperado": 720.00, "contado": 718.50, "justificativa": "Troco a menos" },
      { "metodo": "pix", "esperado": 200.00, "contado": 200.00, "justificativa": null },
      { "metodo": "credito", "esperado": 50.00, "contado": 50.00, "justificativa": null }
    ],
    "observacoes": "Operadora confirma divergência de R$1,50 — troco.",
    "enviarEmailContador": false,
    "emailContador": null
  }
  ```
- **Validação**:
  - `sessao.Status == "em_conferencia"` → 409 `FLUXO_INVALIDO`
  - Para cada item com `|esperado - contado| > 0.01`: `justificativa`
    obrigatória, min 10 chars → 422 `JUSTIFICATIVA_OBRIGATORIA`
  - Se `enviarEmailContador=true`: `emailContador` válido → 422
- **Response 201**:
  ```json
  {
    "fechamento": {
      "id": "uuid",
      "sessaoCaixaId": "uuid",
      "data": "2026-05-16",
      "saldoInicial": 100.00,
      "totalVendas": 450.00,
      "totalPagamentosPedidos": 850.00,
      "totalEntradasExtras": 50.00,
      "totalSaidasExtras": 30.00,
      "saldoFinal": 1420.00,
      "hashSha256": "a3f1...",
      "verificacaoCodigo": "k7Pq2X9wR4mN3vBs",
      "pdfUrl": "/api/caixa/sessoes/uuid/relatorio.pdf",
      "divergencias": [
        { "metodo": "dinheiro", "diferenca": -1.50, "justificativa": "Troco a menos" }
      ]
    },
    "sessao": { /* status=fechada */ }
  }
  ```
- **Side effects (sequência estrita, render+upload ANTES do commit)**:
  1. Abre TX
  2. Adquire `pg_advisory_xact_lock(hashtext(sessao_id))`
  3. Recarrega sessão, valida estado `em_conferencia` (rejeita se mudou)
  4. Constrói snapshot em memória: agregação por método (vendas + pagamentos
     confirmados + movimentos manuais), conferência itens, divergências
  5. Serializa snapshot em **canonical JSON** (chaves ordenadas, sem espaços,
     datas ISO-8601 Z)
  6. Gera `VerificacaoCodigo` (16 chars URL-safe via `RandomNumberGenerator`
     + base62; verifica unicidade — improvável colisão em 62^16)
  7. Chama `FechamentoCaixaPdfRenderer.RenderAsync(snapshot, codigoVerificacao)`
     → retorna `byte[]` em memória
  8. Calcula `HashSha256 = SHA256(canonical_json || pdf_bytes)` hex
  9. Define `pdfStorageKey = $"fechamentos/{empresaId:N}/{sessaoId:N}.pdf"`
  10. `await IFileStorage.UploadAsync(pdfStorageKey, pdf_bytes,
      ct: tokenComTimeout25s)` — se falhar, exceção → catch externo → rollback
  11. Constrói `FechamentoCaixa` (com `PdfStorageKey`, `HashSha256`,
      `SnapshotJson`, `ConferenciaItensJson`, `DivergenciasJson`,
      `VerificacaoCodigo` populados)
  12. Adiciona à TX, atualiza `SessaoCaixa.Status = "fechada"`,
      `FechadaEm`, `FechadaPorUserId`
  13. Adiciona `OutboxEventoIntegracao` "caixa.fechado"
  14. `uow.CommitAsync()` — atômico
  15. Em qualquer falha após upload: rollback SQL + **tentativa de
      `DeleteAsync(pdfStorageKey)`** best-effort (limpa órfão no storage; se
      essa exclusão falhar, log warning, próxima geração com mesmo nome
      sobrescreve — operação idempotente por design via key determinística)
- **Falha**: rollback completo — operador refaz o passo 3 do wizard
- **Métrica obrigatória**: histograma `fechamento_render_upload_seconds`
  (p95 < 5s, alerta > 10s; se > 25s, requisição já falhou por timeout)

#### D.2.7 `GET /api/caixa/sessoes/{sessaoId}/relatorio.pdf`

- **Auth**: requerido (operador da empresa)
- **Response 200**: `Content-Type: application/pdf`, stream do PDF
- **404**: sessão sem fechamento (ainda não fechada) ou PDF não persistido

#### D.2.8 `GET /api/caixa/historico`

- **Auth**: + permissão `caixa.historico.ver`
- **Query**: `?lojaId=`, `?desde=2026-05-01`, `?ate=2026-05-16`, `?page=1`, `?pageSize=20`
- **Response 200**: listagem paginada de sessões fechadas com agregados

#### D.2.9 `GET /caixa/verificar/{codigo}` (PÚBLICO — sem auth)

- **Path**: `/caixa/verificar/{codigo}` (NÃO sob `/api/` — rota pública)
- **Response 200**: HTML page (Razor) ou JSON `?format=json`:
  ```json
  {
    "valido": true,
    "empresa": "Casa da Babá",
    "loja": null,
    "data": "2026-05-16",
    "hashSha256": "a3f1...",
    "fechadoEm": "2026-05-16T22:30:00Z",
    "fechadoPorNome": "Thatiane"
  }
  ```
- **404**: código não encontrado
- **Rate limit**: 10 req/min por IP (`AspNetCoreRateLimit` já configurado
  no projeto — confirmar paths cobertos)
- **Sem exposição de IDs internos** — só código opaco

### D.3 Padrão de erro RFC 7807

Já em uso no projeto. Plano apenas adiciona códigos custom:

| Código | HTTP | Quando |
|---|---|---|
| `PAGAMENTO_EXCEDE_TOTAL` | 422 | soma de pagamentos > Total |
| `METODO_INVALIDO` | 422 | método não está no enum |
| `VALOR_INVALIDO` | 422 | <= 0 |
| `MOTIVO_OBRIGATORIO` | 422 | estorno sem motivo válido |
| `PAGAMENTO_JA_ESTORNADO` | 422 | tenta estornar 2x |
| `PEDIDO_CANCELADO` | 422 | pagar em pedido cancelado |
| `SESSAO_EM_CONFERENCIA` | 423 | tentar mexer em sessão em conferência |
| `SESSAO_JA_EXISTE` | 409 | abrir 2 sessões mesmo dia |
| `SESSAO_NAO_ESTA_ABERTA` | 409 | iniciar fechamento em sessão errada |
| `SESSAO_RECENTE_DEMAIS` | 422 | fechar < 10min após abrir |
| `MOVIMENTO_RECENTE` | 409 | fechar com movimento < 30s atrás |
| `JUSTIFICATIVA_OBRIGATORIA` | 422 | divergência sem justificativa |
| `FLUXO_INVALIDO` | 409 | transição não permitida na state machine |
| `DATA_FUTURA` | 422 | pagoEm > now+1min |
| `DATA_FORA_DA_SESSAO` | 422 | movimento fora do dia da sessão |

### D.4 Idempotency-Key

**Implementação atual**: `IdempotencyMiddleware` já existe. Whitelist em
`Program.cs` é estendida:

```csharp
app.UseIdempotency(opts =>
{
    opts.Add("/api/pedidos");                          // já existe?
    opts.Add("/api/pagamentos");                       // NOVO
    opts.Add("/api/caixa/abrir");                      // NOVO
    opts.Add("/api/caixa/sessoes");                    // NOVO (cobre todos os POST sob sessões)
});
```

**Contrato**:
- Header `Idempotency-Key`: UUID v4/v7, 1–120 chars, obrigatório em todos POSTs
  do módulo.
- Validação **hash do body** (não implementado hoje): adicionar campo
  `BodyHash varchar(64)` em `idempotency_keys`. Em retry com mesma key +
  body diferente → 409 `IDEMPOTENCY_BODY_MISMATCH`. **Esta é uma melhoria
  necessária**, descrita em F1.
- TTL 24h (já configurado).
- Replay: response 2xx retornada do cache; header `X-Idempotent-Replay: true`.

**Migration adicional**: adicionar coluna `body_hash` em `idempotency_keys`
(ADD COLUMN aditivo).

### D.5 Concorrência por endpoint

| Endpoint | Risco | Estratégia |
|---|---|---|
| POST /pagamentos | 2 operadores clicam ao mesmo tempo | **Otimista**: nenhum lock especial. Ambos inserem; constraint `soma <= total` é validada no commit. Quem perde (segundo commit) gera erro 422 `PAGAMENTO_EXCEDE_TOTAL` (porque o primeiro já consumiu o saldo). **Adicionar lock advisory Postgres** `pg_advisory_xact_lock(hashtext(pedido_id))` no início da TX para serializar — barato e mata o race window. |
| POST /pagamentos/{id}/estornar | 2 cliques rápidos | **Otimista** + check `Status == "confirmado"` no início da TX. Segundo clique pega `Status == "estornado"` (primeiro já confirmou) → 422. Reforço: `pg_advisory_xact_lock(hashtext(pagamento_id))`. |
| POST /caixa/abrir | 2 operadores abrem ao mesmo tempo | **Unique index parcial** já garante (B.1.a). Segundo recebe `unique_violation` → 409 `SESSAO_JA_EXISTE`. |
| POST /caixa/sessoes/{id}/movimentos | 2 operadores lançam manuais | **Sem lock** — ambos podem inserir. Race acceptable (não viola invariante). |
| POST /caixa/sessoes/{id}/iniciar-fechamento | 2 cliques | **Otimista** + check `Status == "aberta"`. Segundo recebe 409 `SESSAO_NAO_ESTA_ABERTA`. |
| POST /caixa/sessoes/{id}/confirmar-fechamento | 2 cliques rápidos | **`pg_advisory_xact_lock(hashtext(sessao_id))`** — serializa para garantir snapshot atômico. Segundo recebe 409. |

**Implementação do advisory lock**: helper em `EasyStockDbContext`:

```csharp
public async Task AdquirirLockExclusivoAsync(string scope, CancellationToken ct = default)
{
    var hash = (long)scope.GetHashCode(); // ou xxhash64 para evitar colisão
    await Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({hash})", ct);
}
```

Chamado no início dos UseCases críticos (1 linha cada). Lock liberado
automaticamente no commit/rollback da TX.

---
