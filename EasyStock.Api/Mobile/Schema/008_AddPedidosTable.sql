-- ============================================================
-- Onda P2 — Pedidos (encomendas) do ERP, paridade com app mobile.
--
-- Idempotente: CREATE TABLE IF NOT EXISTS + ADD COLUMN IF NOT EXISTS.
--
-- Pedido é a fase PRÉ-Venda: status aguardando → preparando →
-- pronto → entregue. Quando entregue+cobrado, pode gerar uma Venda
-- no ERP (VendaId aponta).
--
-- Estrutura expansível como Cliente/Produto:
--   pedidos             — entidade raiz (snapshot cliente + total + status)
--   pedido_itens        — produtos do pedido (snapshot nome/preço)
--   pedido_eventos      — trail completo de mudanças
--   pedido_pagamentos   — N pagamentos por pedido (metade entrega + metade PIX)
--
-- mobile_orders.erp_pedido_id liga opcionalmente (padrão Onda 2.1).
-- ============================================================

CREATE TABLE IF NOT EXISTS pedidos (
    "Id"               uuid          PRIMARY KEY,
    "EmpresaId"        uuid          NOT NULL,
    "LojaId"           uuid          NULL,
    "ClienteId"        uuid          NULL,
    -- Snapshot do cliente no momento da criação (preserva histórico mesmo se cliente mudar)
    "ClienteNome"      varchar(150)  NULL,
    "ClienteApt"       varchar(32)   NULL,
    "ClienteTelefone"  varchar(32)   NULL,
    "Status"           varchar(20)   NOT NULL DEFAULT 'aguardando',
    "Total"            numeric(14,2) NOT NULL DEFAULT 0,
    "Observacoes"      text          NULL,
    "Origem"           varchar(20)   NULL,
    "MobileOrderId"    varchar(64)   NULL,
    "VendaId"          uuid          NULL,
    "CriadoEm"         timestamp     NOT NULL DEFAULT now(),
    "AlteradoEm"       timestamp     NOT NULL DEFAULT now(),
    "EntreguEm"        timestamp     NULL,
    "CanceladoEm"      timestamp     NULL,
    CONSTRAINT fk_pedidos_empresa  FOREIGN KEY ("EmpresaId")  REFERENCES empresas("Id")  ON DELETE RESTRICT,
    CONSTRAINT fk_pedidos_cliente  FOREIGN KEY ("ClienteId")  REFERENCES clientes("Id")  ON DELETE SET NULL,
    CONSTRAINT fk_pedidos_loja     FOREIGN KEY ("LojaId")     REFERENCES lojas("Id")     ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS ix_pedidos_empresa_status   ON pedidos ("EmpresaId", "Status");
CREATE INDEX IF NOT EXISTS ix_pedidos_empresa_cliente  ON pedidos ("EmpresaId", "ClienteId") WHERE "ClienteId" IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_pedidos_empresa_criado   ON pedidos ("EmpresaId", "CriadoEm" DESC);
CREATE INDEX IF NOT EXISTS ix_pedidos_mobile_id        ON pedidos ("MobileOrderId") WHERE "MobileOrderId" IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_pedidos_venda_id         ON pedidos ("VendaId") WHERE "VendaId" IS NOT NULL;

CREATE TABLE IF NOT EXISTS pedido_itens (
    "Id"             uuid          PRIMARY KEY,
    "PedidoId"       uuid          NOT NULL,
    "ProdutoId"      uuid          NULL,
    -- Snapshot do produto no momento (nome pode mudar depois no catálogo)
    "Nome"           varchar(150)  NOT NULL,
    "Emoji"          varchar(16)   NULL,
    "Unidade"        varchar(32)   NULL,
    "Quantidade"     numeric(14,3) NOT NULL DEFAULT 1,
    "PrecoUnitario"  numeric(14,2) NOT NULL DEFAULT 0,
    "Subtotal"       numeric(14,2) NOT NULL DEFAULT 0,
    "Observacao"     text          NULL,
    "CriadoEm"       timestamp     NOT NULL DEFAULT now(),
    CONSTRAINT fk_ped_item_pedido  FOREIGN KEY ("PedidoId")  REFERENCES pedidos("Id")   ON DELETE CASCADE,
    CONSTRAINT fk_ped_item_produto FOREIGN KEY ("ProdutoId") REFERENCES produtos("Id")  ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS ix_pedido_itens_pedido  ON pedido_itens ("PedidoId");
CREATE INDEX IF NOT EXISTS ix_pedido_itens_produto ON pedido_itens ("ProdutoId") WHERE "ProdutoId" IS NOT NULL;

CREATE TABLE IF NOT EXISTS pedido_eventos (
    "Id"             uuid          PRIMARY KEY,
    "PedidoId"       uuid          NOT NULL,
    "Tipo"           varchar(40)   NOT NULL,
    "StatusAntigo"   varchar(20)   NULL,
    "StatusNovo"     varchar(20)   NULL,
    "Detalhes"       text          NULL,
    "UsuarioId"      uuid          NULL,
    "UsuarioNome"    varchar(120)  NULL,
    "Origem"         varchar(20)   NULL,
    "OcorridoEm"     timestamp     NOT NULL DEFAULT now(),
    CONSTRAINT fk_ped_evt_pedido FOREIGN KEY ("PedidoId") REFERENCES pedidos("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_pedido_eventos_pedido_data ON pedido_eventos ("PedidoId", "OcorridoEm" DESC);

CREATE TABLE IF NOT EXISTS pedido_pagamentos (
    "Id"                    uuid          PRIMARY KEY,
    "PedidoId"              uuid          NOT NULL,
    "Metodo"                varchar(20)   NOT NULL DEFAULT 'outro',
    "Valor"                 numeric(14,2) NOT NULL,
    "Referencia"            varchar(120)  NULL,
    "Observacao"            text          NULL,
    "PagoEm"                timestamp     NOT NULL DEFAULT now(),
    "RegistradoPorUserId"   uuid          NULL,
    "RegistradoPorNome"     varchar(120)  NULL,
    CONSTRAINT fk_ped_pag_pedido FOREIGN KEY ("PedidoId") REFERENCES pedidos("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_pedido_pagamentos_pedido ON pedido_pagamentos ("PedidoId");

-- mobile_orders já existe (criado em 001). Adiciona linkagem reversa pro ERP.
ALTER TABLE mobile_orders ADD COLUMN IF NOT EXISTS erp_pedido_id uuid NULL;
CREATE INDEX IF NOT EXISTS ix_mobile_orders_erp_pedido ON mobile_orders (erp_pedido_id) WHERE erp_pedido_id IS NOT NULL;
