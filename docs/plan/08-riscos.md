# 08 — Riscos e Decisões Abertas

> Parte do [Plano](README.md). Anterior: [07-faseamento.md](07-faseamento.md). Próximo: nenhum.

### I.1 Riscos identificados

| ID | Risco | Prob. | Impacto | Mitigação ativa | Gatilho para plano B |
|---|---|---|---|---|---|
| R-01 | Backfill de SessaoCaixa retroativa cria links errados (movimento de dia X linkado a sessão de dia Y por timezone) | Média | Relatório histórico errado por 1+ dia | Script F.3 usa `DATE(data_movimento AT TIME ZONE 'UTC')` consistente; validação 5 (F.5) detecta duplicatas; testes unit cobrem caso de movimento meia-noite UTC | Validação retorna >0 → reverter backfill (deletar SessaoCaixa criadas pela query) e refazer com filtros refinados |
| R-02 | Mobile MAUI quebra com novas colunas em `pedido_pagamentos` | Baixa | Casa da Babá APK não sincroniza | Todas as colunas novas têm DEFAULT; APK lê apenas campos antigos que continuam intactos | APK não sincroniza → publicar nova versão APK que ignora colunas desconhecidas |
| R-03 | PDF rendering em produção (Fly) falha por dependência nativa do QuestPDF | Baixa | Fechamento não conclui | QuestPDF é managed, sem libs nativas — já testado em `FaturaPdfRenderer` | PDF falha → rollback flag, geração via fallback HTML simples |
| R-04 | Idempotency-Key body hash quebra retries existentes do PWA | Média | Operadora vê erro 409 em retry legítimo | Migration M01 adiciona coluna nullable; PWA atualizado para enviar mesma key apenas com mesmo body; legacy clients sem hash continuam funcionando (campo nullable) | Erro frequente → desligar body_hash check (config) |
| R-05 | Contador rejeita PDF (falta campo, formato ruim) | Média | Atraso 1-2 semanas para refazer template | F0 inclui validação ANTES de F6; iterar 1x antes de codificar | Contador pede refactor grande → reduz escopo do PDF para "extrato simples" (sem QR/hash visível) e move o resto para F+1 |
| R-06 | Casa da Babá fecha caixa errado durante validação em produção | Média | Operadora perde confiança, volta pra planilha | Felipe acompanha primeiros 3 fechamentos ao vivo (telefone); banner com "Suporte ao vivo das 14h-22h hoje" | Operadora reclama → desliga flag, Felipe revisa fluxo, repete em 1 semana |
| R-07 | GitHub Actions continua bloqueado por billing (memory) | Alta | Sem CI gates, deploy manual | Build local + checksum + testes locais antes de deploy fly; documentar processo manual em `docs/dev/deploy-sem-ci.md` | Critical bug em produção → rollback flag imediato |
| R-08 | RLS Postgres (branch separado) faz merge primeiro e quebra queries dos novos UseCases | Média | UseCases retornam 0 linhas em produção | F1 inclui revisar branch RLS; queries dos novos UseCases usam `EmpresaId` filter explícito (não dependem só de query filter); adicionar testes que executam com policies ativas | Merge primeiro do RLS → mergear policies novas das tabelas SessaoCaixa/etc. no PR de RLS antes de F2 |
| R-09 | Cronograma estoura 8 semanas | Alta | Casa da Babá fica sem ferramenta | Buffer 25% já incluído; F+1 pode adiar polimento (F8) sem afetar core; comunicar Casa da Babá honestamente | Atraso >2 semanas → cortar F7 (auditoria/hash visíveis) para F+1, manter apenas fechamento básico |
| R-10 | Fechamento + PDF + storage não cabe em uma transação (timeout) | Baixa | Fechamento parcial → estado incoerente | **Decisão: opção (a) — render + upload ANTES do commit.** Sequência: (1) abre TX, (2) calcula snapshot in-memory + canonical JSON, (3) gera PDF em memória (QuestPDF retorna `byte[]`), (4) calcula `HashSha256(pdf_bytes \|\| canonical_json)`, (5) `IFileStorage.UploadAsync(key, pdf_bytes)` — chamada async com timeout de 25s, (6) se upload OK, popula `PdfStorageKey + HashSha256` na entidade, (7) `uow.CommitAsync()`. **Falha em qualquer etapa = rollback total**. Sem janela inconsistente, sem job de retry, fiscal nunca encontra fechamento órfão | Upload >25s OU storage indisponível → operador vê erro 503, refaz. Métrica `fechamento_pdf_render_seconds` p95 < 5s; alerta se > 10s |
| R-11 | EntityAlteracao retention 1825 dias não é aplicado automaticamente para FechamentoCaixa | Média | Após 6 meses, audit log purgado e fiscal cobra | F7 explicitamente configura retention; teste unit verifica que `RetentionConfig["FechamentoCaixa"] = 1825` | Verificação manual: query SQL após 60 dias mostra rows preservadas |
| R-12 | Operadora confunde "estorno" com "deletar pagamento" e estorna pagamento legítimo | Média | Confusão financeira | Microcopy clara no modal de estorno (E.1.2); sugestões pré-definidas; campo motivo obrigatório (mín 10 chars) cria fricção; banner pós-estorno mostra ação para desfazer (criar novo pagamento) | Reclamação repetida → modal de confirmação dupla "Tem certeza? Estorno de R$ X — digite ESTORNAR para confirmar" |
| R-13 | Voucher/crédito-cliente entra como pedido urgente do cliente durante F2-F6 | Baixa | Reescopo no meio | Documentar que voucher é F+1 (B.2.4); preservar enum `nao_conciliavel` deixa porta aberta | Cliente realmente pede → F+1 imediato após go-live |
| R-14 | `pg_advisory_xact_lock(hashtext(uuid))` tem colisão de hash | Muito Baixa hoje / Média no futuro SaaS | 2 operadores em pedidos diferentes serializados desnecessariamente | hashtext é 32-bit (~4B buckets) — para Casa da Babá (1 loja, 1-3 ops simultâneos), colisão = zero prático. **Registrado como dívida técnica explícita**: criar item em backlog "Migrar advisory lock para xxhash64 (8 bytes)" com **gatilho objetivo: `lock_wait_seconds` p99 > 100ms OU ≥ 20 empresas distintas operando simultaneamente em janela de 1h** (qualquer um). Métrica obrigatória desde F5. | Métrica dispara → migrar lock para `pg_advisory_xact_lock(int4, int4)` com (hi, lo) de xxhash64 do UUID + scope key (custo: ~2h de trabalho, migration sem schema, deploy hot) |

### I.2 Decisões abertas (não dá para decidir agora)

| ID | Decisão | O que precisa | Quando | Default sensato se faltar |
|---|---|---|---|---|
| D-01 | ~~Se tabela de feature flags por empresa existe~~ **RESOLVIDO**: `TenantFeatureFlag` (entidade em `EasyStock.Domain/Entities/TenantFeatureFlag.cs:5`, tabela `TenantFeatureFlags` da migration `20260430205554_AddGovernancaFeatures`). Factory `TenantFeatureFlag.Criar(empresaId, feature, ativo, adminEmail)`. **Reusar — sem migration nova.** | resolvido | resolvido | n/a |
| D-02 | Se PDF do fechamento deve ter logo da empresa | Perguntar à Casa da Babá em F0 | Antes de F6 | Sem logo (texto puro) — F+1 adiciona se cliente pedir |
| D-03 | Se "loja matriz" (LojaId = NULL) deve ter pseudo-loja explícita ou continuar nullable | Verificar query atual de loja em F1 | Antes de F2 | Continuar nullable (consistente com schema atual) |
| D-04 | Email contador é por empresa ou por loja? | Perguntar à Casa da Babá em F0 | Antes de F6 | Por empresa (campo `Empresa.EmailContador`); F+1 adiciona por loja se pedido |
| D-05 | Verificacao pública usa Razor (renderiza HTML) ou só JSON | UX preference Felipe | Antes de F6 | HTML simples Razor (mais profissional para fiscal) |
| D-06 | Permissions `caixa.*` e `pedidos.pagamentos.*` precisam adicionar ao enum de Permissões | Ler `Permissao` enum em F1 | Antes de F2 | Adicionar entradas (default: incluso em Nivel "Gerente" e "Operador"; "Visualizador" sem) |
| D-07 | Body hash de Idempotency-Key usa SHA-256 do JSON canonicalizado ou hash do bytes raw | Decidir em F2 implementação | Antes de finalizar F2 | SHA-256 do JSON canonicalizado (estável; bytes raw quebra com whitespace) |
| D-08 | Se sessão fechada permite anexar nota fiscal posterior | Perguntar contador em F0 | Antes de F6 | Não — FechamentoCaixa imutável. NFC-e emitida pós-fechamento aparece no próximo dia. |

### I.3 Pontos de não-volta

- **F1 mergeado em master** (entity changes + migrations M01-M03 aplicadas
  em produção): caro reverter porque colunas novas estão na tabela. Reversão
  exige migration destrutiva (DROP COLUMN) que é descartada por princípio
  aditivo. **Mitigação**: manter flag OFF até F8.

- **F6 PDF + hash em produção**: a partir do primeiro fechamento real, PDFs
  imutáveis estão arquivados. Mudança de layout em F+1 não retroage
  (fechamentos antigos mantêm formato antigo). **Critério de abort em F6**:
  contador rejeita PDF e mudanças exigem reestruturação grande → aborta F6,
  volta para F0 do escopo de PDF.

- **Backfill F3 + retroativo SessaoCaixa em F4**: dados criados em
  produção. Se backfill estava errado, refazê-lo é caro (manual).
  **Mitigação**: rodar em staging com snapshot de prod antes; validar com
  queries F.5; só liberar para produção após sucesso em staging por 48h.

- **F8 flag ligada para Casa da Babá**: a partir daqui, operadora usa em
  produção real. Volta para fluxo antigo é OK (não destrutivo) mas
  reputacional. **Critério go-live**: lista F.4 completa.

---
