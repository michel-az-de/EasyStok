-- Papel de leitura do MCP baba-db (#2522). Rodar como superusuario, UMA vez e de novo a cada migracao que crie tabela de negocio:
--   docker exec -i shared-postgres psql -U <superuser> -d easystock -v baba_ro_pw="$SENHA" -f - < scripts/mcp/baba_ro.sql
-- A senha vem de -v (nunca neste arquivo). Barreira real: so GRANT SELECT em tabelas de negocio listadas abaixo.
-- Sem views (view de dono superusuario ignora RLS). Segredos e sessoes ficam de fora: usuarios, *_tokens, credencial_integracao,
-- data_protection_keys, cliente_otp, cliente_session, sessoes_*, push, webhook_recebidos, idempotency_keys.
SELECT NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'baba_ro') AS criar \gset
\if :criar
  CREATE ROLE baba_ro LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
\endif
ALTER ROLE baba_ro PASSWORD :'baba_ro_pw';
ALTER ROLE baba_ro SET default_transaction_read_only = on;
ALTER ROLE baba_ro SET statement_timeout = '15s';
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM baba_ro;
GRANT USAGE ON SCHEMA public TO baba_ro;
REVOKE TEMP ON DATABASE easystock FROM baba_ro;  -- efetivo so se PUBLIC tambem perder TEMP: REVOKE TEMP ON DATABASE easystock FROM PUBLIC (confirmar que a API nao usa tabela temporaria)
-- Fora da allowlist por risco de payload/PII/dado global: audit_logs, system_error_logs, notif_eventos, notif_logs_envio, nfe_documentos, nfe_eventos, planos, endpoint_health_state.
DO $$
DECLARE t text;
BEGIN
  FOREACH t IN ARRAY ARRAY[
    'empresas','lojas','categorias','produtos','produto_variacoes','produto_caracteristicas','produto_embalagens','produtos_composicao',
    'itens_estoque','movimentacoes_estoque','ajustes_inventario','ajustes_inventario_linha','contagens','itens_contagem',
    'lotes','lote_itens','fornecedores','pedidos_fornecedor','pedidos_fornecedor_itens','listas_compras','itens_lista_compras',
    'pedidos','pedido_itens','pedido_pagamentos','pedido_eventos','pedido_avaliacao','pedido_estornos_manuais','pedido_estornos_online',
    'vendas','itens_venda','movimentos_caixa','fechamentos_caixa','metodos_pagamento_empresa','contas_bancarias',
    'contas_pagar','parcelas_pagar','contas_receber','parcelas_receber','lancamentos','lancamento_baixas','categorias_financeiras','centros_custo',
    'clientes','cliente_telefones','cliente_enderecos','cliente_notas','cliente_tags','creditos_cliente_comanda','usos_credito_cliente_comanda',
    'atendimento_conversas','atendimento_mensagens','configuracoes_atendimento','respostas_prontas','mensagens_programadas','lembretes',
    'campanhas','campanha_destinatarios','cardapio_item','cardapio_item_variacao','cardapio_secao','storefront','frete_zona','janela_entrega',
    'entregadores','viagens','viagem_paradas','corridas_externas','chamados_entregador','ocorrencias',
    'nfe_itens','faturas','fatura_itens','fatura_pagamentos','assinaturas_empresa',
    'notificacoes','report_runs'
  ] LOOP
    IF to_regclass(format('public.%I', t)) IS NOT NULL THEN
      EXECUTE format('GRANT SELECT ON public.%I TO baba_ro', t);
    END IF;
  END LOOP;
END $$;
