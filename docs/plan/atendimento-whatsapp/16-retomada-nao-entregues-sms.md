# Onda 12 — Retomada, não entregues e reserva por SMS (S58–S60)

Issue: #1391 · Continuação do reenvio (S57, doc 15). Felipe pediu os três itens que ficaram para depois em 02/10/2026.
Para o SMS ele escolheu **código pronto, desligado**: a ADR-0057 (item 10) deixa o SMS fora por enquanto.

## Decisões

| Tema | Decisão |
|---|---|
| Janela vencida | Com **modelo de retomada** configurado, o modelo sai **uma vez por contato a cada 24 h** e a mensagem fica **aguardando o cliente**. Quando ele responde, ela volta para a fila de reenvio na mesma rodada (até 30 s). Sem modelo, falha permanente com o motivo, como antes. |
| Qual janela vale | A da conversa da mensagem ou, se o cliente escreveu de novo, a da conversa aberta dele. O contato casa pelo número ou pelo **cliente vinculado** (o WhatsApp grava o celular com e sem o nono dígito). |
| Modelo | Configurado pela empresa (`PUT api/atendimento/configuracao/modelo-retomada`, campo na Gestão › Atendimento). Precisa estar **aprovado na Meta** com **uma variável no corpo**: o primeiro nome do cliente. |
| Não entregues | `GET api/atendimento/conversas/nao-entregues`: saídas que falharam, mais recentes primeiro. No console, uma faixa vermelha no Balcão (só quando há alguma) abre a lista com Reenviar e Abrir conversa. |
| SMS | Quando o WhatsApp desiste (sem reenvio agendado, sem esperar o cliente, falha que **não** é incerta), o texto vai **uma vez** por SMS. Liga só com `Atendimento:ReenvioMensagens:SmsReserva=true` **e** `Notifications:Sms:Provider` real. O stub responde "enviado" sem enviar, então com ele fica desligado. |

## Arquitetura

```
reenvio (console ou serviço) ── janela? ──sim──► envia texto ── 131047 ─┐
                                   │não                                    │
                                   ▼                                       ▼
                      modelo configurado? ──não──► falha permanente ──► reserva SMS (se ligada)
                                   │sim
                                   ▼
                      modelo já saiu p/ o contato em 24 h? ──não──► envia o modelo ("[modelo x]" no histórico)
                                   │
                                   ▼
                      mensagem.AguardaClienteDesde = agora
                                   │ cliente escreve (qualquer conversa do contato ou do cliente)
                                   ▼
ReservarReenviosUseCase: libera (ProximoReenvioEm = agora) → reserva → ReenviarMensagemUseCase
```

---

### S58 · Modelo de retomada

- [x] Fora da janela, com modelo: o modelo sai e a mensagem espera o cliente.
- [x] Outro texto que falhar para o mesmo contato em 24 h só entra na espera (o modelo não repete).
- [x] A Meta recusando por janela (131047) cai no mesmo caminho.
- [x] O cliente escreveu numa conversa nova: o reenvio usa a janela dela.
- [x] O modelo que falha encerra com o motivo; sem modelo, falha permanente como antes.
- [x] O serviço de reenvio libera quem o cliente respondeu (SQL com `FOR UPDATE OF m SKIP LOCKED`).

### S59 · Não entregues

- [x] Endpoint com a mensagem, o contato e se a conversa está aberta.
- [x] Faixa e lista no console com Reenviar e Abrir conversa; o balão mostra "saiu por SMS".

### S60 · Reserva por SMS (desligada)

- [x] Só com a chave e um provedor real; nunca em falha incerta; uma vez por mensagem.
- [x] Falha do SMS só vai para o log e não muda o resultado do reenvio.

**Banco.** `atendimento_mensagens`: `UltimaFalhaEnvio`, `AguardaClienteDesde`, `ReservaSmsEm` e os índices parciais
`ix_atendimento_mensagens_aguarda_cliente` e `ix_atendimento_mensagens_nao_entregues`. `configuracoes_atendimento`:
`ModeloRetomadaNome`, `ModeloRetomadaIdioma` (padrão `pt_BR`).

**Para ligar a retomada.** Criar e aprovar na Meta um modelo de categoria Utilidade, por exemplo `retomar_conversa`:
"Oi {{1}}, aqui é a Casa da Baba. Ficou uma resposta nossa pendente para você. Responda esta mensagem para continuar."
Depois, gravar o nome na Gestão › Atendimento.

**Rollback.** Apagar o nome do modelo na configuração; manter `SmsReserva` desligada. Migração aditiva com `Down`.
