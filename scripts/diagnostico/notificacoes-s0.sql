-- S0 · Medição do motor de notificações em produção (somente leitura). Issue #1344.
-- Plano: docs/plan/notificacoes-sistema/01-verdade-do-motor.md (S0).
--
-- Como rodar na VPS (papel da aplicação, que é NOBYPASSRLS):
--   docker exec -i shared-postgres psql -U easystock -d easystock -v ON_ERROR_STOP=1 < notificacoes-s0.sql
--
-- A transação é READ ONLY: qualquer escrita falha. O SET LOCAL do app.bypass_rls não escreve nada;
-- ele só deixa este script ver as linhas de todas as empresas, pelo mesmo mecanismo que a aplicação usa
-- (ADR-0010). Não há dado pessoal na saída: só contagens, tipos, status e idades.

BEGIN TRANSACTION READ ONLY;
SET LOCAL app.bypass_rls = 'true';

\echo '== 1. Papel da aplicação (esperado: rolsuper=f, rolbypassrls=f)'
SELECT rolname, rolsuper, rolbypassrls
FROM pg_roles
WHERE rolname IN (current_user, 'easystock');

\echo '== 2. RLS nas tabelas do motor (esperado: as duas colunas t)'
SELECT c.relname, c.relrowsecurity, c.relforcerowsecurity
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public' AND c.relname LIKE 'notif\_%' AND c.relkind = 'r'
ORDER BY c.relname;

\echo '== 3. Eventos por tipo, status e idade'
SELECT "Tipo", "Status",
       CASE
         WHEN now() - "OcorridoEm" < interval '1 hour'  THEN '1. < 1 h'
         WHEN now() - "OcorridoEm" < interval '1 day'   THEN '2. < 1 dia'
         WHEN now() - "OcorridoEm" < interval '7 days'  THEN '3. < 7 dias'
         WHEN now() - "OcorridoEm" < interval '30 days' THEN '4. < 30 dias'
         ELSE '5. >= 30 dias'
       END AS idade,
       count(*) AS total
FROM notif_eventos
GROUP BY 1, 2, 3
ORDER BY 1, 2, 3;

\echo '== 4. Pendentes mais antigos (o backlog que a N1 vai destravar)'
SELECT "Tipo", count(*) AS pendentes,
       min("OcorridoEm") AS mais_antigo,
       date_trunc('minute', now() - min("OcorridoEm")) AS idade_maxima
FROM notif_eventos
WHERE "Status" = 'Pendente'
GROUP BY 1
ORDER BY pendentes DESC;

\echo '== 5. Eventos Processado sem mensagem no outbox (perda silenciosa), últimos 30 dias'
SELECT e."Tipo", count(*) AS processados_sem_outbox
FROM notif_eventos e
LEFT JOIN notif_outbox_mensagens o ON o."EventoId" = e."Id"
WHERE e."Status" = 'Processado'
  AND e."OcorridoEm" >= now() - interval '30 days'
  AND o."Id" IS NULL
GROUP BY 1
ORDER BY 2 DESC;

\echo '== 6. Outbox por status, canal e provider'
SELECT "Status", "Canal", coalesce("ProviderUsado", '(nenhum)') AS provider,
       count(*) AS total,
       min("CriadoEm") AS mais_antiga,
       max("CriadoEm") AS mais_recente
FROM notif_outbox_mensagens
GROUP BY 1, 2, 3
ORDER BY 1, 2, 3;

\echo '== 7. Log de envio por provider nos últimos 30 dias (stub/console = nada saiu de verdade)'
SELECT "Canal", "Provider", "Sucesso", count(*) AS tentativas, max("OcorridoEm") AS ultima
FROM notif_logs_envio
WHERE "OcorridoEm" >= now() - interval '30 days'
GROUP BY 1, 2, 3
ORDER BY 1, 2, 3;

\echo '== 8. Catálogo: rotinas e templates globais e por empresa'
SELECT 'rotina' AS item, ("EmpresaId" IS NULL) AS global, "Ativa" AS ativo, count(*) AS total
FROM notif_rotinas GROUP BY 2, 3
UNION ALL
SELECT 'template', ("EmpresaId" IS NULL), "Ativo", count(*)
FROM notif_templates GROUP BY 2, 3
ORDER BY 1, 2, 3;

\echo '== 9. Outbox Pendente por tipo do evento e idade (o que sairia ao destravar)'
SELECT e."Tipo", o."Canal",
       CASE
         WHEN now() - o."CriadoEm" < interval '1 hour'  THEN '1. < 1 h'
         WHEN now() - o."CriadoEm" < interval '1 day'   THEN '2. < 1 dia'
         WHEN now() - o."CriadoEm" < interval '7 days'  THEN '3. < 7 dias'
         ELSE '4. >= 7 dias'
       END AS idade,
       count(*) AS total
FROM notif_outbox_mensagens o
JOIN notif_eventos e ON e."Id" = o."EventoId"
WHERE o."Status" = 'Pendente'
GROUP BY 1, 2, 3
ORDER BY 1, 2, 3;

\echo '== 10. Outbox com mais de 90 dias ainda com destinatário (anonimização parada)'
SELECT count(*) AS sem_anonimizar
FROM notif_outbox_mensagens
WHERE "CriadoEm" < now() - interval '90 days'
  AND "Destinatario" <> '[anonimizado]';

\echo '== 11. Configurações de canal (globais e por empresa; só presença de credencial)'
SELECT ("EmpresaId" IS NULL) AS global, "Canal", "ProviderAtivo", "AtivoNoTenant",
       ("CredenciaisCifradas" IS NOT NULL) AS tem_credencial
FROM notif_configuracoes_canal
ORDER BY 1 DESC, 2;

\echo '== 12. Bloqueios ativos (kill switch)'
SELECT ("EmpresaId" IS NULL) AS global, coalesce("Canal", '(todos)') AS canal,
       "AtivadoEm", "ExpiraEm"
FROM notif_bloqueios
WHERE "RemovidoEm" IS NULL AND ("ExpiraEm" IS NULL OR "ExpiraEm" > now())
ORDER BY "AtivadoEm";

\echo '== 13. (opcional) Outbox de integração por status e idade'
SELECT status, count(*) AS total, min(criado_em) AS mais_antigo
FROM outbox_evento_integracao
GROUP BY 1
ORDER BY 1;

ROLLBACK;

-- Também medir (fora do SQL), no host da VPS:
--   docker logs --since 72h ez-worker 2>&1 | grep -c "42501"
--   docker logs --since 72h ez-worker 2>&1 | grep -E "Notific|Dispatcher|Avaliador" | tail -50
