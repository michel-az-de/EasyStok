# ADR 0016 — Hash SHA-256 e retenção de 5 anos para `FechamentoCaixa`

**Status:** Proposed (2026-05-16)
**Contexto do plano:** Caixa Conciliado + Pagamentos Múltiplos por Pedido — `docs/plan/`.

## Decisão

**Cada `FechamentoCaixa` gera um PDF + snapshot canonical-JSON cujo `SHA-256`
combinado é persistido na coluna `HashSha256` e exibido publicamente na
página de verificação `/caixa/verificar/{codigo}` (rota anônima).**

**Retenção mínima de 5 anos** (1825 dias) garantida via reuso da
infraestrutura `EntityAlteracao` existente (`EntityAlteracaoRetentionService`
já purga registros após retenção configurada — adicionar `FechamentoCaixa`
e `SessaoCaixa` à lista com retention 1825).

**Ordem de geração**: PDF rendering + upload para `IFileStorage` acontecem
**ANTES** do commit SQL. Falha em qualquer etapa = rollback total. Sem
janela inconsistente em que o fechamento existe mas o PDF que aquele hash
representa não existe no storage.

## Opções consideradas

### Opção A — Hash + retenção via `EntityAlteracao` (escolhida)

**Mecanismo de hash**:
```
HashSha256 = SHA256(canonical_json_snapshot || pdf_bytes)
```
- `canonical_json_snapshot`: serialização do `SnapshotJson` com chaves
  ordenadas, sem whitespace, datas em ISO-8601 Z.
- `pdf_bytes`: bytes do PDF gerado pelo QuestPDF.
- Hash em hex (64 chars), persistido em `fechamentos_caixa.hash_sha256`.
- Exibido no PDF (texto + QR code) e na página pública.

**Retenção**:
- `EntityAlteracaoRetentionService` (existente em
  `EasyStock.Infra.Postgre/Hosting/`) recebe configuração:
  `{ "FechamentoCaixa": 1825, "SessaoCaixa": 1825, "PedidoPagamento": 1825 }`.
- Garantia: registros de mudança/criação dessas entidades não são purgados
  antes de 5 anos.
- PDF físico no `IFileStorage` (Fly volume / S3 / Azure) tem ciclo de vida
  separado — política de retenção do storage também configurada para 5 anos
  (premissa N.7).

**Imutabilidade**:
- `FechamentoCaixaImutavelInterceptor` rejeita `Update`/`Delete` em
  `FechamentoCaixa` existente.
- Erros pós-fechamento se materializam como `MovimentoCaixa` de ajuste na
  próxima sessão (com `Categoria='ajuste_sessao_anterior'`).

**Prós**:
- Zero infraestrutura nova de retenção — reusa serviço já testado.
- Hash determinístico permite verificação externa por contador/fiscal sem
  consultar EasyStock.
- Página pública anônima permite auditoria sem login.

**Contras**:
- Mudança no `EntityAlteracaoRetentionService` exige atenção: se o config
  for sobrescrito em deploy, retention default volta a vigorar.
- QR code aponta para URL `easystok.fly.dev/caixa/verificar/{codigo}` —
  deprecação do domínio quebra QR antigo. Mitigação: domínio próprio em F+1.

### Opção B — Assinatura digital eIDAS / ICP-Brasil

**Prós**:
- Reconhecimento jurídico no Brasil (Lei 14.063/2020).

**Contras**:
- Certificado ICP-Brasil A1 custa ~R$150-300/ano por empresa, exige cadeia
  de confiança AC raiz, e implementação consome ~2 semanas só para gerenciar
  certificados.
- Casa da Babá não pediu (validado em premissa N.10 — contador aceita SHA-256
  com hash visível).
- Fora de escopo F0; revisitar quando primeiro fiscal cobrar.

### Opção C — Hash + retenção via `Outbox` ou tabela `audit_fechamentos` própria

**Prós**:
- Separação de concerns mais limpa (audit em tabela dedicada).

**Contras**:
- `EntityAlteracao` já existe e já tem retention configurável. Criar tabela
  paralela é duplicação. NotInventedHere syndrome.
- `OutboxEventoIntegracao` é para integração externa (webhook, email), não
  para audit log.

## Análise de trade-off

Para o caso de uso real (Casa da Babá + fiscais brasileiros padrão), SHA-256
+ hash visível + retenção 5 anos é o **mínimo viável regulatório** sem
custo de certificados. Contador validou em F0 (premissa N.10).

Reuso de `EntityAlteracao` é decisão de coerência arquitetural — toda
auditoria sensível do projeto já passa por ele.

## Consequências

**Becomes easier**:
- Verificação por terceiro (contador, fiscal): abre PDF, lê hash,
  acessa URL pública, confere igual.
- Implementação: ~3 dias em F7.
- Custo operacional zero — sem certificados, sem renovação.

**Becomes harder**:
- Mudar layout do PDF futuramente invalida hashes históricos. **Hashes são
  imutáveis após fechamento** — não regenera-se. Se layout precisar mudar,
  PDFs antigos permanecem com layout antigo + hash antigo. F+1 muda layout
  só para novos.
- Migração de domínio (fly.dev → easystok.com.br) precisa redirect
  permanente para QR codes antigos continuarem funcionando.

**To revisit**:
- Se cliente regulado mais exigente (farmácia, distribuidor de medicamentos)
  aparecer, considerar Opção B (ICP-Brasil A1). Trigger: pedido explícito
  de cliente OU fiscal rejeitando SHA-256 simples.
- Custo: implementação ~2 semanas + R$300/ano por empresa.

## Action items

1. [ ] Coluna `HashSha256 varchar(64)` em `FechamentoCaixa`.
2. [ ] Coluna `VerificacaoCodigo varchar(32)` (slug opaco URL-safe).
3. [ ] `CalculadoraHashFechamento` em
       `EasyStock.Application/UseCases/CaixaSessoes/Common/`.
4. [ ] `GeradorCodigoVerificacaoFechamento` (16 chars base62 via
       `RandomNumberGenerator` + verificação de unicidade).
5. [ ] `FechamentoCaixaPdfRenderer` em `EasyStock.Infra.Async/Pdf/`
       (espelha `FaturaPdfRenderer`).
6. [ ] Rota pública `/caixa/verificar/{codigo}` (Razor + JSON via
       `?format=json`), rate-limited 10 req/min/IP.
7. [ ] `EntityAlteracaoRetentionService` configurado com `FechamentoCaixa`,
       `SessaoCaixa`, `PedidoPagamento` em 1825 dias.
8. [ ] Política de retenção do `IFileStorage` para `fechamentos/*.pdf`
       configurada para 5 anos (Fly: documentado; S3/Azure: lifecycle
       policy).
9. [ ] Smoke test em F6: PDF gerado em staging → hash visível →
       URL pública abre → hash bate.

Detalhes técnicos em
[`../plan/01-dominio.md`](../plan/01-dominio.md) B.1.d e
[`../plan/03-api.md`](../plan/03-api.md) D.2.6.
