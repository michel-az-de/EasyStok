# Ponte WPPConnect muda: conectada e sem entregar mensagem (#3117)

**Data:** 2026-11-21
**Severidade:** SEV2 (mensagens de cliente não entraram no atendimento por cerca de cinco horas)
**Status:** Mitigado (ponte reiniciada às 23:40 com GO do Felipe); causa raiz NÃO provada
**Formato:** blameless

## Resumo

Entre 18:32 e 23:40 a ponte `easystok-wppconnect-1` respondeu `CONNECTED` e não chamou o webhook
`POST /api/webhooks/wppconnect`. O healthcheck do contêiner mede só o `/healthz` do servidor HTTP, então
tudo aparecia verde. No mesmo dia, 3 chamadas do webhook receberam 200 sem gravar nada e sem deixar log.
A ligação com o deploy das 20:13 é só de horário: a parada começou antes e a API não chama a ponte ao subir.

## O que mudou

| Sinal | Onde | O que diz |
|---|---|---|
| Log `Webhook sem efeito: motivo ..., evento ..., origem ...` | API | todo webhook aceito e não gravado, com o motivo |
| Contador `webhook_sem_efeito_total{motivo}` | métricas da API | volume por motivo |
| Incidente `Integracao` (`PONTE_MUDA`) | Worker, coletor `ColetorPonteWppConnectMuda` | a ponte enxerga mensagem recebida que o webhook não recebeu |
| `AVISO: ponte WPPConnect ...` | fim do `vps-deploy.sh` | contêiner fora de `running healthy` ou loja com incidente aberto |

O vigia compara a última mensagem recebida de cada conversa individual (na ponte) com `webhooks_recebidos`.
Não mede silêncio: madrugada sem tráfego não abre incidente. Estado em `endpoint_health_state`, nome
`wppconnect/<empresa>`. Abre na primeira rodada muda (até 10 min depois da mensagem) e resolve depois de duas
rodadas boas.

Limites conhecidos:

- Se a página do WhatsApp dentro da ponte congelar inteira, a listagem congela junto e o vigia não vê nada.
- O aviso vai para a empresa padrão (dono da plataforma), não para o dono da loja.
- Nada aqui recupera as mensagens da parada: isso está em issue própria.

## Ligar o vigia (nasce DESLIGADO)

O formato do `list-chats` da ponte foi lido no código do `wppconnect-server` 2.10.81, não na ponte real.
Antes de ligar, confira na VPS, com a sessão de uma loja, sem colar a saída em lugar nenhum (ela tem telefone):

```bash
# imprime só tipos e nomes de campo, nunca o conteúdo
curl -s -X POST "http://localhost:21465/api/<sessao>/list-chats" \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"count":1,"onlyUsers":true}' \
  | jq 'if type=="array" then .[0] else .response[0] end | {id: (.id|type), ultima: (.lastReceivedKey|keys?)}'
```

Esperado: `ultima` contém `_serialized` e `fromMe`. Se não contiver, NÃO ligue: o cliente devolve lista vazia e o
vigia nunca abriria incidente (falso verde). Abra issue com os nomes de campo vistos.

Conferido, ligue no Worker com `Atendimento__WppConnect__VigiaPonteMuda__Habilitado=true` (mudança de
produção: exige GO do Felipe) e acompanhe por um dia o log `Ponte WPPConnect muda`.

## Quando o aviso aparece

1. Contêiner: `docker inspect -f '{{.State.Status}} {{.State.Health.Status}}' easystok-wppconnect-1`.
2. Entrega: no log da API, procure `POST /api/webhooks/wppconnect` nos últimos minutos e os motivos em
   `Webhook sem efeito`. Requisição chegando com motivo `evento_ignorado` ou `duplicata` não é ponte muda.
3. Ponte: no log dela, conte `Error calling Webhook.` no intervalo.
4. Estado do vigia: `select endpoint_name, last_alerted_at from endpoint_health_state where endpoint_name like 'wppconnect/%'`.
5. Se a ponte está conectada e nada chega, o remédio usado em 21/11 foi `docker restart easystok-wppconnect-1`.
   Reiniciar sessão de WhatsApp arrisca QR e bloqueio do número: só com GO do Felipe, nunca pelo deploy.

Nunca cole telefone, texto de mensagem ou token em issue, PR ou log (#2590).
