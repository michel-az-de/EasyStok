### Fixed
- **Webhook WPPConnect**: a marca de entrega da ponte em `webhook_recebidos` passa a ser apagada depois de 7 dias, uma vez por dia; os registros de idempotência dos outros provedores não são tocados. (#3138)
