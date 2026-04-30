-- ============================================================
-- Onda P5.B — Lista de compras no ERP.
--
-- App tem lista de compras local (DOM-based, não persistida em
-- mobile_*). Aqui formalizamos no ERP pra que gestor possa criar/
-- compartilhar listas entre operadores.
-- ============================================================

CREATE TABLE IF NOT EXISTS listas_compras (
    "Id"               uuid          PRIMARY KEY,
    "EmpresaId"        uuid          NOT NULL,
    "LojaId"           uuid          NULL,
    "Nome"             varchar(120)  NOT NULL,
    "Status"           varchar(20)   NOT NULL DEFAULT 'aberta',
    "Observacoes"      text          NULL,
    "CriadaPorUserId"  uuid          NULL,
    "CriadaPorNome"    varchar(120)  NULL,
    "Origem"           varchar(20)   NULL,
    "CriadoEm"         timestamp     NOT NULL DEFAULT now(),
    "AlteradoEm"       timestamp     NOT NULL DEFAULT now(),
    "ArquivadoEm"      timestamp     NULL,
    CONSTRAINT fk_lista_compras_empresa FOREIGN KEY ("EmpresaId") REFERENCES empresas("Id") ON DELETE RESTRICT,
    CONSTRAINT fk_lista_compras_loja    FOREIGN KEY ("LojaId")    REFERENCES lojas("Id")    ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS ix_lista_compras_empresa_status ON listas_compras ("EmpresaId", "Status");
CREATE INDEX IF NOT EXISTS ix_lista_compras_empresa_data   ON listas_compras ("EmpresaId", "CriadoEm" DESC);

CREATE TABLE IF NOT EXISTS itens_lista_compras (
    "Id"             uuid          PRIMARY KEY,
    "ListaComprasId" uuid          NOT NULL,
    "Texto"          varchar(255)  NOT NULL,
    "Quantidade"     numeric(14,3) NULL,
    "Unidade"        varchar(32)   NULL,
    "Observacao"     text          NULL,
    "Categoria"      varchar(60)   NULL,
    "Done"           boolean       NOT NULL DEFAULT false,
    "DoneEm"         timestamp     NULL,
    "DonePorUserId"  uuid          NULL,
    "DonePorNome"    varchar(120)  NULL,
    "CriadoEm"       timestamp     NOT NULL DEFAULT now(),
    "AlteradoEm"     timestamp     NOT NULL DEFAULT now(),
    CONSTRAINT fk_item_lista FOREIGN KEY ("ListaComprasId") REFERENCES listas_compras("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_item_lista_lista_done ON itens_lista_compras ("ListaComprasId", "Done");
