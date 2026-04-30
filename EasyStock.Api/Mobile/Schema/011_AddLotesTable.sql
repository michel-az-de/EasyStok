-- ============================================================
-- Onda P5.A — Lotes ERP (paridade com mobile_batches + etiquetas
-- granulares pra conferência scanner-by-scanner).
--
-- Idempotente: CREATE TABLE IF NOT EXISTS + ADD COLUMN IF NOT EXISTS.
--
-- Tabelas:
--   lotes            — entidade raiz (codigo, data, operador, status)
--   lote_itens       — produto + qty + peso/un + validade
--   lote_etiquetas   — uma por unidade produzida (sequencial 1..N),
--                      conferencia individual (pendente/conferida/divergente)
--
-- mobile_batches.erp_lote_id liga ao ERP.
-- ============================================================

CREATE TABLE IF NOT EXISTS lotes (
    "Id"             uuid          PRIMARY KEY,
    "EmpresaId"      uuid          NOT NULL,
    "LojaId"         uuid          NULL,
    "Codigo"         varchar(40)   NOT NULL,
    "Status"         varchar(20)   NOT NULL DEFAULT 'em_producao',
    "DataProducao"   timestamp     NOT NULL,
    "OperadorUserId" uuid          NULL,
    "OperadorNome"   varchar(120)  NULL,
    "Observacoes"    text          NULL,
    "FotoUrl"        text          NULL,
    "Origem"         varchar(20)   NULL,
    "MobileBatchId"  varchar(64)   NULL,
    "CriadoEm"       timestamp     NOT NULL DEFAULT now(),
    "AlteradoEm"     timestamp     NOT NULL DEFAULT now(),
    "FinalizadoEm"   timestamp     NULL,
    CONSTRAINT fk_lotes_empresa FOREIGN KEY ("EmpresaId") REFERENCES empresas("Id") ON DELETE RESTRICT,
    CONSTRAINT fk_lotes_loja    FOREIGN KEY ("LojaId")    REFERENCES lojas("Id")    ON DELETE SET NULL,
    CONSTRAINT uq_lotes_codigo  UNIQUE ("EmpresaId", "Codigo")
);

CREATE INDEX IF NOT EXISTS ix_lotes_empresa_data    ON lotes ("EmpresaId", "DataProducao" DESC);
CREATE INDEX IF NOT EXISTS ix_lotes_empresa_status  ON lotes ("EmpresaId", "Status");
CREATE INDEX IF NOT EXISTS ix_lotes_mobile_id       ON lotes ("MobileBatchId") WHERE "MobileBatchId" IS NOT NULL;

CREATE TABLE IF NOT EXISTS lote_itens (
    "Id"            uuid          PRIMARY KEY,
    "LoteId"        uuid          NOT NULL,
    "ProdutoId"     uuid          NULL,
    "Nome"          varchar(150)  NOT NULL,
    "Emoji"         varchar(16)   NULL,
    "Unidade"       varchar(32)   NULL,
    "Quantidade"    int           NOT NULL,
    "PesoG"         int           NULL,
    "ValidadeDias"  int           NULL,
    "ExpiraEm"      timestamp     NULL,
    "FotoUrl"       text          NULL,
    "CriadoEm"      timestamp     NOT NULL DEFAULT now(),
    CONSTRAINT fk_lote_item_lote    FOREIGN KEY ("LoteId")    REFERENCES lotes("Id")    ON DELETE CASCADE,
    CONSTRAINT fk_lote_item_produto FOREIGN KEY ("ProdutoId") REFERENCES produtos("Id") ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS ix_lote_itens_lote     ON lote_itens ("LoteId");
CREATE INDEX IF NOT EXISTS ix_lote_itens_validade ON lote_itens ("ExpiraEm") WHERE "ExpiraEm" IS NOT NULL;

CREATE TABLE IF NOT EXISTS lote_etiquetas (
    "Id"                    uuid          PRIMARY KEY,
    "LoteId"                uuid          NOT NULL,
    "LoteItemId"            uuid          NOT NULL,
    "Sequencial"            int           NOT NULL,
    "Codigo"                varchar(60)   NOT NULL,
    "Status"                varchar(20)   NOT NULL DEFAULT 'pendente',
    "ConferidaEm"           timestamp     NULL,
    "ConferidaPorUserId"    uuid          NULL,
    "ConferidaPorNome"      varchar(120)  NULL,
    "ObservacaoConferencia" text          NULL,
    "CriadoEm"              timestamp     NOT NULL DEFAULT now(),
    CONSTRAINT fk_etiq_lote      FOREIGN KEY ("LoteId")     REFERENCES lotes("Id")      ON DELETE CASCADE,
    CONSTRAINT fk_etiq_lote_item FOREIGN KEY ("LoteItemId") REFERENCES lote_itens("Id") ON DELETE CASCADE,
    CONSTRAINT uq_etiq_codigo    UNIQUE ("Codigo")
);

CREATE INDEX IF NOT EXISTS ix_etiq_lote_status ON lote_etiquetas ("LoteId", "Status");

ALTER TABLE mobile_batches ADD COLUMN IF NOT EXISTS erp_lote_id uuid NULL;
CREATE INDEX IF NOT EXISTS ix_mobile_batches_erp_lote ON mobile_batches (erp_lote_id) WHERE erp_lote_id IS NOT NULL;
