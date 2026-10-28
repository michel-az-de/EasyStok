-- Avisos públicos pré-pareamento. Estas tabelas não concedem acesso a tenants.
CREATE TABLE IF NOT EXISTS mobile_push_installations (
 id varchar(64) PRIMARY KEY, public_key varchar(512) NOT NULL,
 token varchar(4096) NOT NULL, apk_version varchar(64) NOT NULL,
 issued_at bigint NOT NULL, payload_hash varchar(64) NOT NULL, registered_at bigint NOT NULL, last_seen_at bigint NOT NULL
);
CREATE TABLE IF NOT EXISTS mobile_push_messages (
 id uuid PRIMARY KEY, installation_id varchar(64) NOT NULL REFERENCES mobile_push_installations(id),
 nonce varchar(128) NOT NULL UNIQUE, created_at bigint NOT NULL, expires_at bigint NOT NULL,
 payload_base64 text NOT NULL, signature_base64 text NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_mobile_push_inbox ON mobile_push_messages(installation_id, expires_at);
CREATE TABLE IF NOT EXISTS mobile_push_receipts (
 id varchar(180) PRIMARY KEY, message_id uuid NOT NULL REFERENCES mobile_push_messages(id),
 installation_id varchar(64) NOT NULL REFERENCES mobile_push_installations(id),
 event varchar(16) NOT NULL CHECK (event IN ('received','opened')),
 source varchar(8) NOT NULL CHECK (source IN ('push','sync')),
 occurred_at bigint NOT NULL, confirmed_at bigint NOT NULL, apk_version varchar(64) NOT NULL,
 payload_base64 text NOT NULL, signature_base64 text NOT NULL,
 UNIQUE(message_id, installation_id, event, source)
);
CREATE TABLE IF NOT EXISTS mobile_push_provider_events (
 id uuid PRIMARY KEY, message_id uuid NOT NULL REFERENCES mobile_push_messages(id),
 installation_id varchar(64) NOT NULL REFERENCES mobile_push_installations(id),
 status varchar(16) NOT NULL CHECK (status IN ('accepted','failed')),
 provider_message_id varchar(512) NOT NULL, payload_hash varchar(64) NOT NULL, recorded_at bigint NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_mobile_push_provider_message ON mobile_push_provider_events(message_id);
