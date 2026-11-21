# Segurança de logging — redaction de dados sensíveis

**Status:** vigente desde 2026-05-30 (B3.0b commit). B3.0a complementa o lado read-time, ver §3.

## 1. Threat model: dois vetores

Logs podem vazar segredos (senhas, conn strings, tokens JWT) por dois caminhos distintos:

- **Vetor 1 — Exibição (read-time).** Operador SuperAdmin/Admin abre a tela `/diagnostico` no Admin SPA, que consome `/api/diagnostico/logs/*` e renderiza logs no browser. Daí o segredo entra na network tab do dev tools, screen share, screenshots de bug report. Mitigado por **B3.0a** (read-time masker no `DiagnosticoLogAnalyzer`, próxima feature).
- **Vetor 2 — At-rest e shipping (write-time).** Serilog escreve o log no arquivo `logs/easystock-{date}.log` no disco. Um log shipper (Datadog Agent, fluentbit, vector — depende do deploy) lê o arquivo e envia para SaaS observability (Datadog/Sentry/etc) ou backup. Daí o segredo está em texto plano em N sistemas onde "mais gente tem acesso". Mitigado por **B3.0b** (este documento — `RedactingTextFormatter` no sink File).

Os dois vetores são complementares. Resolver só um deixa o outro aberto.

## 2. Vetor 2 — write-time redaction (B3.0b)

Implementação:

- **`EasyStock.Api/Observability/Logging/SensitivePatterns.cs`** — fonte de verdade única das regex de redação. 4 padrões: chave=valor (`password=...`), conn string Postgres (`Host=...;Password=...`), JWT cru (`eyJ...`), e Bearer token (`Bearer <token>`). Todas pré-compiladas (`Compiled` flag) por performance.
- **`RedactingTextFormatter : ITextFormatter`** — envolve o `MessageTemplateTextFormatter` padrão do Serilog, renderiza o output completo (incluindo `{Exception}` → `Exception.ToString()`), aplica `SensitivePatterns.Redact()` na string, escreve no sink.

  > **Por que TextFormatter e não Enricher?** `ILogEventEnricher` permite mexer em `LogEvent.Properties`, mas **não** em `LogEvent.Exception` (set-once, imutável). Como `DbUpdateException` coloca conn string no `.Message` da exception, e o sink File serializa `Exception.ToString()` no placeholder `{Exception}` do template, a única forma de interceptar é no resultado final — daí TextFormatter, não Enricher.

- **`SerilogRedactionExtensions.WriteTo.RedactedFile(...)`** — extension method que registra o sink File com `RedactingTextFormatter`. Drop-in replacement do `WriteTo.File` nativo. Descoberto via reflection: requer `"EasyStock.Api"` no array `Serilog:Using` do `appsettings.json`.
- **`appsettings.json`** — sink de arquivo trocou de `"Name": "File"` para `"Name": "RedactedFile"`. Desde a I4.5 (#1698) o console também redige (ver seção 6).

## 3. Vetor 1 — read-time masker (B3.0a, próxima feature)

- **Mesmo conjunto de regex.** O `RegexSensitivePatternMasker` (Application) consome `SensitivePatterns.Patterns` direto — *não duplica*. Se a regex falhar em um caso, falha nos dois vetores juntos (failure mode coerente, sem silêncio em um caminho).
- **Onde aplica.** `DiagnosticoLogAnalyzer.ParseEnhancedLogFile()` recebe `ISensitivePatternMasker` via DI e chama `MaskInPlace(entry)` em cada `EnhancedLogEntryInfo` retornada. Mascara `Message` + `Exception` (corrige blind spot — versão antiga só mascarava `.Message`, deixando conn string no `.Exception`).

## 4. Testes

Tudo em `[Trait("Category", "SecurityRegression")]` — política nova: testes de regressão de segurança usam `[Fact]` (não `[SkippableFact]`). Em CI sem Docker = falha de build, não skip silencioso. Workflow CI pode rodar `dotnet test --filter Category=SecurityRegression` separado para visibilidade.

- **`SensitivePatternsTests`** — cada regex (chave=valor, conn string, JWT, Bearer, false-positive em UUID, input null/vazio).
- **`RedactingTextFormatterTests`** — LogEvent renderizado com template real, cobrindo `MessageTemplate` + `Exception.Message` + `Exception.StackTrace` (JWT propagado).
- **`RedactingFilePipelineTests`** — pipeline Serilog end-to-end escrevendo em arquivo temp, asserta que **arquivo no disco** não contém o segredo. Este é o teste-coração: regressão aqui = vazamento real em produção.
- **`SerilogConfigSmokeTests`** — parseia `appsettings.json`, garante que o sink de arquivo é `RedactedFile` (não `File` nativo). Regressão aqui = alguém trocou de volta e desligou silenciosamente a proteção write-time.

## 5. Manutenção das regex

Quando aparecer um padrão novo de segredo no log (audit periódico recomendado: trimestral, lendo amostra de logs recentes em staging), **adicione em `SensitivePatterns.Patterns`** e estenda a tabela de despacho em `SensitivePatterns.Redact`. Os dois vetores (write-time + read-time) ganham proteção automaticamente.

Antes de adicionar: rode um caso "false positive" (e.g. UUID, ID de pedido, timestamp formatado) contra a regex nova para confirmar que não mascara o que não deveria.

## 6. Todos os sinks, nos dois hosts (I4.5, #1698)

O console é o log do contêiner em produção e o OTLP sai da máquina; o Worker nunca teve sink de arquivo. Por isso a redação deixou de ser do arquivo e passou a ser de todo sink.

| Saída | Como redige |
|---|---|
| Console (API e Worker) | `"Name": "Redigido"` envolvendo o `Console` no `appsettings.json` |
| OTLP de logs (API e Worker) | `WriteTo.Redigido(s => s.OpenTelemetry(...))` no `Program.cs` |
| Arquivo (API) | `RedactedFile`, como antes |
| `WebhookRecebido.Erro` | `SensitivePatterns.Redact` no `WebhookGatewayController` |
| Erro do Mercado Pago (log e exceção) | `CorpoDeProvedor.ParaRegistro`: redige e só depois corta; corpo de endpoint de autenticação nunca é registrado |
| Log de requisição, erro e span de `/api/webhooks/*` | `CaminhoDoLog`: molde da rota (`/api/webhooks/{provedor}`), sem o caminho e sem a query |

`SinkQueRedige` (`EasyStock.Infra.Async/Logging`) redige o texto do template, toda propriedade (também dentro de objeto, lista e dicionário), o valor inteiro de propriedade com nome sensível (`Token`, `Senha`, `ApiKey`) e a exceção. Padrões novos em `SensitivePatterns`: chave sensível em JSON com aspas (`"access_token":"..."`) e segredo na query de URL (`?key=`, `&access_token=`).

**Sink novo.** Sempre dentro de `Redigido`. `TodoSinkRedigeTests` (arquitetura) reprova sink solto em `WriteTo`, no `appsettings` ou no `Program.cs`.

**O que isto não cobre.** Métrica e auditoria não passam pelo Serilog: não recebem corpo de provedor hoje e seguem sem redação automática. Span: ver a seção 7. Redação por regex é a última barreira, não a primeira: o código não deve registrar segredo.

## 7. Telemetria no OpenObserve (#2376)

Decisão de 12/11/2026: os logs continuam no OpenObserve (#2107 F2), sem Loki. Estudo e banca na issue #2376.

### Redação também nos traces e nos logs OTLP

| Peça | O que faz |
|---|---|
| `SpanQueRedige` (`EasyStock.Observabilidade`) | Processador de span: passa o nome e toda tag de texto (`url.path`, `url.full`, `db.query.text`...) por `SensitivePatterns.Redact` antes do exporter. Tag numérica não muda. Eventos de span ficam de fora |
| `LogQueRedige` (`EasyStock.Observabilidade`, #2390) | Processador de log do host sem Serilog (Web): redige a mensagem formatada, o corpo e todo atributo de texto. Troca a exceção por `exception.type`, `exception.message` e `exception.stacktrace` redigidos |
| API, Worker e Web | `.AddProcessor(new SpanQueRedige())` antes do `.AddOtlpExporter(...)` |
| Web | `.AddProcessor(new LogQueRedige())` antes do exporter de log e `IncludeScopes = false`: o escopo do `ILogger` não é editável e levaria o `RequestPath` cru. A correlação vem da Activity |
| `TodoSinkRedigeTests.Trace_exportado_passa_pelo_SpanQueRedige` | Reprova o `WithTracing` que exporta sem o processador antes, nos 3 hosts |
| `TodoSinkRedigeTests.Log_OTLP_do_Web_passa_pelo_LogQueRedige` | Reprova o log OTLP do Web sem o processador antes ou com `IncludeScopes = true` |
| `SensitivePatterns` | Ganhou e-mail, celular brasileiro e CPF (formatado ou 11 dígitos isolados). Não mascara chave de NF-e, unix em ms, GUID, CNPJ formatado |

O `EasyStock.Observabilidade` é leve de propósito (Application + SDK do OpenTelemetry): o console Web referencia só ele, sem o EF, o S3 e o PDF do Infra.Async (#2390).

### Coletor

| Decisão | Motivo |
|---|---|
| Rede própria `ez-telemetria` | O OTLP não tem senha. Na `edge` ele ficava ao alcance do WAHA e do Whisper, imagens de terceiros. Só api, web, worker e ez-otel estão nela; o OpenObserve fica na rede interna da stack |
| Token só de ingestão | O `observabilidade-deploy.sh subir` cria o token `coletor-otel` e troca o `OO_AUTH` do root por ele. O token escreve em `/v1/*` e recebe 401 em `_search` e `/users` (provado no v1.1.2). O root segue no `.env` só para o OpenObserve e para o `status` |
| Sem fila em disco (`sending_queue` + `file_storage`) | Com o OpenObserve fora do ar, a telemetria do intervalo se perde e o app não trava. A cópia json-file de cada container (10 MB x 5) continua na VPS. Fila persistente só se essa perda passar a importar |

Ordem do deploy: `subir` (o coletor sai da `edge`) e logo depois `ligar` (api, web e worker entram na `ez-telemetria`). Entre os dois, a telemetria não chega.

### Disco

A retenção limita só por tempo (30 dias) e o disco é o mesmo do Postgres. O `/health/disco` da API responde 503 abaixo de `Observabilidade:Disco:MinimoLivrePercentual` (padrão 15%) e o vigia externo (`scripts/uptime/alvos.txt`) abre a issue `uptime`. Não tira a API do balanceador: fica fora de `/health` e de `/health/ready`. Mede a raiz do container, que fica no disco do Docker na VPS.

### Riscos aceitos

| Risco | Decisão |
|---|---|
| Mesma VPS: se ela cair, somem a UI e o histórico | Aceito. O vigia externo (F1) roda fora e detecta a queda |
| Sem backup do volume `observabilidade_dados` | Logs e traces são descartáveis. O que precisa sobreviver vai para o banco (auditoria) |

### LGPD

- Base legal dos 30 dias: legítimo interesse (art. 7º, IX) para segurança, diagnóstico e prevenção de fraude, com a redação acima minimizando o dado pessoal (art. 6º, III). Confirmada pelo responsável pelo tratamento (Felipe) em 12/11/2026 (#2404).
- Eliminação sob demanda (art. 18): o OpenObserve v1.1 não apaga registro por titular. Resposta padrão: o dado expira sozinho em até 30 dias. Se a eliminação imediata for exigida, apague o stream `easystok` na UI (perde-se toda a telemetria, aceitável por ser descartável) e registre o pedido.
