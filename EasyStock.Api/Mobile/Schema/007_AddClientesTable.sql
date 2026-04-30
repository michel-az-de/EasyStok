-- ============================================================
-- Onda P1 — Cadastro de clientes do ERP (paridade com app + expansível).
--
-- Idempotente: CREATE TABLE IF NOT EXISTS + ADD COLUMN IF NOT EXISTS.
--
-- Estrutura espelha Produto (entidade rica):
--   clientes              — entidade raiz (campos primários + métricas)
--   cliente_enderecos     — múltiplos endereços (casa/trabalho/entrega)
--   cliente_telefones     — múltiplos números (celular/fixo/whatsapp)
--   cliente_documentos    — CPF, CNPJ, RG, passaporte, CNH, etc
--   cliente_alteracoes    — audit de mudanças (espelho ProdutoAlteracao)
--
-- mobile_clients.erp_cliente_id ligação opcional (Onda 2.1 padrão).
-- ============================================================

CREATE TABLE IF NOT EXISTS clientes (
    "Id"           uuid          PRIMARY KEY,
    "EmpresaId"    uuid          NOT NULL,
    "Nome"         varchar(150)  NOT NULL,
    -- Campos primários (snapshot rápido pro app)
    "Apt"          varchar(32)   NULL,
    "Endereco"     varchar(255)  NULL,
    "Telefone"     varchar(32)   NULL,
    "Email"        varchar(255)  NULL,
    "Documento"    varchar(30)   NULL,
    "Observacoes"  text          NULL,
    -- Métricas operacionais
    "OrderCount"   integer       NOT NULL DEFAULT 0,
    "LastOrderAt"  timestamp     NULL,
    "Ativo"        boolean       NOT NULL DEFAULT TRUE,
    "CriadoEm"     timestamp     NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    "AlteradoEm"   timestamp     NOT NULL DEFAULT (now() AT TIME ZONE 'utc')
);
CREATE INDEX IF NOT EXISTS ix_clientes_empresa_nome     ON clientes("EmpresaId", lower("Nome"));
CREATE INDEX IF NOT EXISTS ix_clientes_empresa_telefone ON clientes("EmpresaId", "Telefone");
CREATE INDEX IF NOT EXISTS ix_clientes_empresa_documento ON clientes("EmpresaId", "Documento");

-- ── Endereços extras ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS cliente_enderecos (
    "Id"           uuid          PRIMARY KEY,
    "ClienteId"    uuid          NOT NULL REFERENCES clientes("Id") ON DELETE CASCADE,
    "Tipo"         varchar(32)   NULL,
    "Logradouro"   varchar(255)  NULL,
    "Numero"       varchar(32)   NULL,
    "Complemento"  varchar(120)  NULL,
    "Bairro"       varchar(120)  NULL,
    "Cidade"       varchar(120)  NULL,
    "Estado"       varchar(60)   NULL,
    "Cep"          varchar(20)   NULL,
    "Pais"         varchar(60)   NULL,
    "Referencia"   varchar(255)  NULL,
    "Padrao"       boolean       NOT NULL DEFAULT FALSE,
    "CriadoEm"     timestamp     NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    "AlteradoEm"   timestamp     NOT NULL DEFAULT (now() AT TIME ZONE 'utc')
);
CREATE INDEX IF NOT EXISTS ix_cliente_enderecos_cliente ON cliente_enderecos("ClienteId");

-- ── Telefones extras ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS cliente_telefones (
    "Id"           uuid          PRIMARY KEY,
    "ClienteId"    uuid          NOT NULL REFERENCES clientes("Id") ON DELETE CASCADE,
    "Tipo"         varchar(32)   NULL,
    "Numero"       varchar(32)   NOT NULL,
    "Whatsapp"     boolean       NOT NULL DEFAULT FALSE,
    "Principal"    boolean       NOT NULL DEFAULT FALSE,
    "Observacao"   varchar(255)  NULL,
    "CriadoEm"     timestamp     NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    "AlteradoEm"   timestamp     NOT NULL DEFAULT (now() AT TIME ZONE 'utc')
);
CREATE INDEX IF NOT EXISTS ix_cliente_telefones_cliente ON cliente_telefones("ClienteId");
CREATE INDEX IF NOT EXISTS ix_cliente_telefones_numero  ON cliente_telefones("Numero");

-- ── Documentos extras ──────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS cliente_documentos (
    "Id"           uuid          PRIMARY KEY,
    "ClienteId"    uuid          NOT NULL REFERENCES clientes("Id") ON DELETE CASCADE,
    "Tipo"         varchar(32)   NOT NULL,
    "Valor"        varchar(60)   NOT NULL,
    "Emissor"      varchar(120)  NULL,
    "EmitidoEm"    timestamp     NULL,
    "ValidoAte"    timestamp     NULL,
    "Principal"    boolean       NOT NULL DEFAULT FALSE,
    "CriadoEm"     timestamp     NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    "AlteradoEm"   timestamp     NOT NULL DEFAULT (now() AT TIME ZONE 'utc')
);
CREATE INDEX IF NOT EXISTS ix_cliente_documentos_cliente ON cliente_documentos("ClienteId");
CREATE INDEX IF NOT EXISTS ix_cliente_documentos_valor   ON cliente_documentos("Valor");

-- ── Audit de alterações ────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS cliente_alteracoes (
    "Id"                 uuid          PRIMARY KEY,
    "ClienteId"          uuid          NOT NULL REFERENCES clientes("Id") ON DELETE CASCADE,
    "AlteradoPorUserId"  uuid          NULL,
    "AlteradoPorNome"    varchar(120)  NULL,
    "Campo"              varchar(60)   NOT NULL,
    "ValorAntigo"        text          NULL,
    "ValorNovo"          text          NULL,
    "AlteradoEm"         timestamp     NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    "Origem"             varchar(20)   NULL
);
CREATE INDEX IF NOT EXISTS ix_cliente_alteracoes_cliente ON cliente_alteracoes("ClienteId", "AlteradoEm" DESC);

-- ── Linkagem mobile_clients ↔ clientes ERP ─────────────────────────
ALTER TABLE mobile_clients
    ADD COLUMN IF NOT EXISTS erp_cliente_id uuid NULL,
    ADD COLUMN IF NOT EXISTS approved_at    timestamp NULL,
    ADD COLUMN IF NOT EXISTS approved_by_user_id uuid NULL;

CREATE INDEX IF NOT EXISTS ix_mobile_clients_erp_cliente
    ON mobile_clients(erp_cliente_id) WHERE erp_cliente_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_mobile_clients_pending_review
    ON mobile_clients(empresa_id)
    WHERE erp_cliente_id IS NULL;
