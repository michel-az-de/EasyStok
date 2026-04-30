-- ============================================================
-- Onda P4 — Audit expandido: tabelas de alteração para Fornecedor + Venda.
--
-- Idempotente: CREATE TABLE IF NOT EXISTS.
--
-- Estrutura espelha ProdutoAlteracao + ClienteAlteracao:
--   fornecedor_alteracoes  — diff campo-a-campo do cadastro de fornecedor
--   venda_alteracoes       — skeleton (sem use case ainda; preparada
--                            pra estornos/ajustes formalizados futuros)
-- ============================================================

CREATE TABLE IF NOT EXISTS fornecedor_alteracoes (
    "Id"                 uuid          PRIMARY KEY,
    "FornecedorId"       uuid          NOT NULL,
    "AlteradoPorUserId"  uuid          NULL,
    "AlteradoPorNome"    varchar(120)  NULL,
    "Campo"              varchar(60)   NOT NULL,
    "ValorAntigo"        text          NULL,
    "ValorNovo"          text          NULL,
    "AlteradoEm"         timestamp     NOT NULL DEFAULT now(),
    "Origem"             varchar(20)   NULL,
    CONSTRAINT fk_forn_alt_fornecedor FOREIGN KEY ("FornecedorId") REFERENCES fornecedores("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_forn_alt_fornecedor_data ON fornecedor_alteracoes ("FornecedorId", "AlteradoEm" DESC);
CREATE INDEX IF NOT EXISTS ix_forn_alt_user             ON fornecedor_alteracoes ("AlteradoPorUserId") WHERE "AlteradoPorUserId" IS NOT NULL;

CREATE TABLE IF NOT EXISTS venda_alteracoes (
    "Id"                 uuid          PRIMARY KEY,
    "VendaId"            uuid          NOT NULL,
    "AlteradoPorUserId"  uuid          NULL,
    "AlteradoPorNome"    varchar(120)  NULL,
    "Campo"              varchar(60)   NOT NULL,
    "ValorAntigo"        text          NULL,
    "ValorNovo"          text          NULL,
    "AlteradoEm"         timestamp     NOT NULL DEFAULT now(),
    "Origem"             varchar(20)   NULL,
    CONSTRAINT fk_venda_alt_venda FOREIGN KEY ("VendaId") REFERENCES vendas("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_venda_alt_venda_data ON venda_alteracoes ("VendaId", "AlteradoEm" DESC);
