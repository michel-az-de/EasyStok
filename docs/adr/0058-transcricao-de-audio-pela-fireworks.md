# ADR-0058 · Transcrição de áudio do WhatsApp pela Fireworks, reaproveitando a chave do agente

- Status: Aceito
- Data: 2026-10-06
- Relacionados: ADR-0050 e ADR-0051 (Meta e canais), issues #1398 (PR #1405), #1397 (PR #1403).

## Contexto

Clientes da Casa da Baba mandam áudio no WhatsApp. Até 02/10 o agente recebia só o marcador
`[áudio recebido]` e a dona precisava ouvir cada áudio no console. A produção já tinha uma chave da
Fireworks (`EZ_FIREWORKS_API_KEY` → `Anthropic__ApiKeyAgente`), usada pelo agente de atendimento.

## Decisão

- Transcrever o áudio recebido depois que a mídia é armazenada, pela API de transcrição da Fireworks
  (Whisper `whisper-v3-turbo`, idioma `pt`), por trás da porta `ITranscritorAudio`.
- Configuração na seção `Transcricao` (`Enabled`, `ApiKey`, `BaseUrl`, `Modelo`, `Idioma`,
  `TimeoutSegundos`). Sem `Transcricao:ApiKey`, reaproveita `Anthropic:ApiKeyAgente` quando
  `Anthropic:AutenticacaoBearer=true`: um fornecedor e uma chave a menos para gerir.
- Falha na transcrição só registra em log; nunca derruba o armazenamento da mídia.
- Sem chave ou `Transcricao:Enabled=false`, o comportamento anterior é mantido.

## Alternativas descartadas

- OpenAI Whisper: exigiria conta e chave novas, sem ganho para áudio curto em pt-BR.
- Transcrição local (faster-whisper): a VPS não tem GPU; custo de CPU e de manutenção maior.

## Consequências

- A transcrição liga sozinha onde a chave do agente estiver configurada.
- Custo por duração de áudio na conta Fireworks.
- O host `audio-prod.api.fireworks.ai` responde 401 sem credencial (medido em 06/10); o nome do modelo
  só se confirma com o primeiro áudio real. `BaseUrl` e `Modelo` são configuráveis para troca sem deploy de código.
- O agente pode processar o turno antes de a transcrição chegar; tratado em issue própria.
