-- ============================================================
-- Onda 4 — Comandos remotos pra dispositivos pareados.
--
-- Idempotente: CREATE TABLE IF NOT EXISTS + indexes IF NOT EXISTS.
--
-- Caso de uso: gestor no painel /operacao clica "Forçar sync" num
-- celular específico. Servidor enfileira comando aqui. Device pega
-- na próxima requisição (push/pull) e executa. Sem WebSocket por
-- ora — Onda 5 evolui pra realtime.
--
-- Tipos de comando suportados:
--   - flush_now: app força flush imediato da fila de mutations
--   - pull_now: app força pull do servidor
--   - reload: app dispara location.reload (apos flush)
--   - message: notifica operador via toast (texto em payload.text)
-- ============================================================

CREATE TABLE IF NOT EXISTS mobile_device_commands (
    "Id"                  uuid         PRIMARY KEY,
    device_id             varchar(64)  NOT NULL,
    empresa_id            uuid         NOT NULL,
    command_type          varchar(32)  NOT NULL,
    payload_json          text         NULL,
    created_at            timestamp    NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    created_by_user_id    uuid         NULL,
    delivered_at          timestamp    NULL,
    executed_at           timestamp    NULL,
    expires_at            timestamp    NULL
);

-- Pull rapido pelo device (only pending = not delivered & not expired)
CREATE INDEX IF NOT EXISTS ix_mobile_device_commands_pending
    ON mobile_device_commands(device_id, delivered_at)
    WHERE delivered_at IS NULL;

-- Listagem no painel /operacao
CREATE INDEX IF NOT EXISTS ix_mobile_device_commands_empresa
    ON mobile_device_commands(empresa_id, created_at DESC);
