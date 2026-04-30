-- ============================================================
-- Onda P3 — Caixa formalizado no ERP (paridade com app + audit).
--
-- Idempotente: CREATE TABLE IF NOT EXISTS + ADD COLUMN IF NOT EXISTS.
--
-- Tabelas:
--   movimentos_caixa    — entrada/saida/abertura/fechamento (raiz)
--   fechamentos_caixa   — snapshot consolidado do dia
--
-- mobile_cash_entries.erp_movimento_caixa_id liga ao ERP.
-- ============================================================

CREATE TABLE IF NOT EXISTS movimentos_caixa (
    "Id"                   uuid          PRIMARY KEY,
    "EmpresaId"            uuid          NOT NULL,
    "LojaId"               uuid          NULL,
    "Tipo"                 varchar(20)   NOT NULL DEFAULT 'entrada',
    "Valor"                numeric(14,2) NOT NULL,
    "Descricao"            text          NULL,
    "Metodo"               varchar(20)   NULL,
    "Categoria"            varchar(60)   NULL,
    "Referencia"           varchar(120)  NULL,
    "DataMovimento"        timestamp     NOT NULL,
    "RegistradoPorUserId"  uuid          NULL,
    "RegistradoPorNome"    varchar(120)  NULL,
    "Origem"               varchar(20)   NULL,
    "EstornadoEm"          timestamp     NULL,
    "EstornadoPorUserId"   uuid          NULL,
    "EstornadoPorNome"     varchar(120)  NULL,
    "MotivoEstorno"        text          NULL,
    "CriadoEm"             timestamp     NOT NULL DEFAULT now(),
    CONSTRAINT fk_mov_caixa_empresa FOREIGN KEY ("EmpresaId") REFERENCES empresas("Id") ON DELETE RESTRICT,
    CONSTRAINT fk_mov_caixa_loja    FOREIGN KEY ("LojaId")    REFERENCES lojas("Id")    ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS ix_mov_caixa_empresa_data ON movimentos_caixa ("EmpresaId", "DataMovimento" DESC);
CREATE INDEX IF NOT EXISTS ix_mov_caixa_empresa_tipo ON movimentos_caixa ("EmpresaId", "Tipo");
CREATE INDEX IF NOT EXISTS ix_mov_caixa_ativo ON movimentos_caixa ("EmpresaId", "DataMovimento") WHERE "EstornadoEm" IS NULL;

CREATE TABLE IF NOT EXISTS fechamentos_caixa (
    "Id"                       uuid          PRIMARY KEY,
    "EmpresaId"                uuid          NOT NULL,
    "LojaId"                   uuid          NULL,
    "Data"                     date          NOT NULL,
    "SaldoInicial"             numeric(14,2) NOT NULL DEFAULT 0,
    "TotalVendas"              numeric(14,2) NOT NULL DEFAULT 0,
    "TotalPagamentosPedidos"   numeric(14,2) NOT NULL DEFAULT 0,
    "TotalEntradasExtras"      numeric(14,2) NOT NULL DEFAULT 0,
    "TotalSaidasExtras"        numeric(14,2) NOT NULL DEFAULT 0,
    "SaldoFinal"               numeric(14,2) NOT NULL DEFAULT 0,
    "FechadoPorUserId"         uuid          NULL,
    "FechadoPorNome"           varchar(120)  NULL,
    "Observacoes"              text          NULL,
    "FechadoEm"                timestamp     NOT NULL DEFAULT now(),
    CONSTRAINT fk_fech_caixa_empresa FOREIGN KEY ("EmpresaId") REFERENCES empresas("Id") ON DELETE RESTRICT,
    CONSTRAINT fk_fech_caixa_loja    FOREIGN KEY ("LojaId")    REFERENCES lojas("Id")    ON DELETE SET NULL,
    CONSTRAINT uq_fech_caixa_dia UNIQUE ("EmpresaId", "LojaId", "Data")
);

CREATE INDEX IF NOT EXISTS ix_fech_caixa_empresa_data ON fechamentos_caixa ("EmpresaId", "Data" DESC);

-- mobile_cash_entries já existe (criado em 001). Adiciona link reverso.
ALTER TABLE mobile_cash_entries ADD COLUMN IF NOT EXISTS erp_movimento_caixa_id uuid NULL;
CREATE INDEX IF NOT EXISTS ix_mobile_cash_erp_movimento ON mobile_cash_entries (erp_movimento_caixa_id) WHERE erp_movimento_caixa_id IS NOT NULL;
