# Onda 11 — Reenvio de mensagens que falharam (S57)

Issue: #1355 · Origem: pergunta do Felipe em 02/10/2026 ("como vamos fazer quando o WhatsApp ficar fora e
precisarmos reenviar uma mensagem que não foi?"). Decisão dele no mesmo dia: itens 1, 2, 3 e 6 da proposta.

## Decisões (Felipe, 02/10/2026)

| Falha | Exemplo | O que acontece |
|---|---|---|
| **Temporária** | Meta fora, rede, limite de taxa (`EhPermanente = false`, `HttpRequestException`) | Reenvio automático em **1, 5, 15 e 60 min**, até **6 h** do primeiro envio |
| **Permanente** | número fora da lista, janela de 24 h vencida, erro desconhecido | Sem reenvio automático. Botão "Reenviar" no console |
| **Incerta** | timeout (sem resposta da Meta) | Sem reenvio automático: a mensagem pode ter saído e reenviar duplicaria (#1292). O erro começa com "Envio incerto". Botão "Reenviar" no console |

Fora da janela de 24 h o reenvio (manual ou automático) não envia: registra o motivo e encerra o agendamento.
Ficam para depois: modelo aprovado quando a janela venceu (item 4), fila "Não entregues" (item 5) e SMS de
reserva (item 7).

## Arquitetura

```
envio falha ──► Mensagem.RegistrarFalhaEnvio(erro, ClassificadorFalhaEnvio.Classificar(ex), agora)
                    │ temporária e dentro do prazo → ProximoReenvioEm = agora + espera[tentativa]
                    ▼
ReenvioMensagensBackgroundService (30 s)
  1. ReservarReenviosUseCase  (bypass de RLS, FOR UPDATE SKIP LOCKED, ProximoReenvioEm = null)
  2. ReenviarMensagemUseCase  (escopo do tenant; janela 24 h; envia pelo canal da conversa)

Console ── POST api/atendimento/conversas/{id}/mensagens/{mensagemId}/reenviar ──► ReenviarMensagemUseCase
```

---

### S57 · Reenvio automático e manual

**Aceite.**
- [x] Falha temporária agenda reenvio com espera crescente e para depois de 4 reenvios ou 6 h.
- [x] Falha permanente e incerta não agendam; a incerta avisa no erro.
- [x] Reenvio que dá certo vira `Enviada` com o novo `wamid` e limpa o erro.
- [x] Fora da janela de 24 h o reenvio não envia e encerra o agendamento.
- [x] Dois processos da API não reenviam a mesma mensagem (reserva com `SKIP LOCKED`).
- [x] Resposta do agente, saudação, confirmação de opt-out e resposta da avaliação usam o classificador.
- [x] Endpoint de reenvio (permissão de atender) e botão "Reenviar" no balão que falhou.

**Banco.** `atendimento_mensagens.TentativasEnvio` (int, 0) e `ProximoReenvioEm` (timestamptz, nulo) com
índice parcial `ix_atendimento_mensagens_proximo_reenvio`.

**Rollback.** `BackgroundJobs:EnableReenvioMensagens=false` desliga o automático; o botão continua. A migração
é aditiva (colunas novas com default), o `Down` remove.
