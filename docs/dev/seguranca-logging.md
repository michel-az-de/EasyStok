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
- **`appsettings.json`** — sink de arquivo trocou de `"Name": "File"` para `"Name": "RedactedFile"`. Console fica sem redaction (dev local; logs de produção vão pelo File sink).

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
