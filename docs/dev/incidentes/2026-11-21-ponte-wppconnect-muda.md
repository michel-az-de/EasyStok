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

O vigia compara a última mensagem recebida de cada conversa individual (na ponte) com `webhook_recebidos`.
Não mede silêncio: madrugada sem tráfego não abre incidente. Estado em `endpoint_health_state`, nome
`wppconnect/<empresa>`. Abre na primeira rodada muda (até 10 min depois da mensagem) e resolve depois de duas
rodadas boas.

Limites conhecidos:

- Se a página do WhatsApp dentro da ponte congelar inteira, a listagem congela junto e o vigia não vê nada.
- O aviso vai para a empresa padrão (dono da plataforma), não para o dono da loja, e é um por rodada: não diz
  qual loja. A loja está no log `Ponte WPPConnect muda` e em `endpoint_health_state`.
- Mensagem que a ponte entregou e a API recusou (400 do parser, 503 do telefone do LID) NÃO é ponte muda: o webhook
  grava a marca de entrega (`webhook_recebidos`, provedor `wppconnect_entrega`) antes do parser.
- Loja que desliga o módulo ou perde a credencial tem o estado apagado e o incidente fechado. Ponte desconectada ou
  fora do ar numa rodada não muda o estado. Desconectada ou sem resposta por 30 minutos, em pelo menos seis leituras seguidas, sai do
  vigia: o estado é apagado, o incidente fecha e o deploy para de dizer "muda". A desconexão aparece na tela de
  integração (#3138).
- Mensagem sem registro só conta como ponte muda se nada foi entregue depois dela e se o Worker já via a ponte
  conectada quando ela chegou (#3138). Reconexão só conta depois de duas leituras desconectadas seguidas (uma só é
  oscilação); o que ficou de antes dela não segura o incidente. Ponte que perde parte das mensagens e entrega o
  resto NÃO é detectada: a entrega posterior esconde a perda.
  Depois de reiniciar o Worker (todo deploy), a suspeita anterior deixa a rodada sem conclusão: o incidente aberto
  não fecha sozinho e um novo só abre com mensagem nova.
- Queda e volta da ponte entre duas rodadas (5 min) não é vista: a mensagem sincronizada nesse intervalo conta como
  muda até a próxima entrega. A suspeita só é vista enquanto a conversa está entre as 20 mais recentes. As contagens
  vivem na memória do Worker.
- Desligar a flag com incidente aberto deixa o estado `wppconnect/<empresa>` parado e o deploy segue avisando: antes
  de desligar, espere normalizar ou apague essas linhas (mudança de produção: com GO).
- Nada aqui recupera as mensagens da parada: isso está em issue própria.

## Ligar o vigia (nasce DESLIGADO)

Medido na ponte real (2.10.81) em 22/11: `lastReceivedKey` vem sem `_serialized`, com `fromMe`, `remote`, `id` e
`$1`. O cliente aceita as duas formas e, na real, monta `fromMe_remote_id` (#3138, item i). O que NÃO foi medido é
se esse valor montado é o id que o webhook grava, nem o formato do `id` no `get-messages`. Antes de ligar, confira
na VPS, com a sessão de uma loja. Os comandos imprimem só nomes de campo e booleanos, nunca o conteúdo:

```bash
# 1. forma da chave: nomes de campo
curl -s -X POST "http://localhost:21465/api/<sessao>/list-chats" \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"count":1,"onlyUsers":true}' \
  | jq 'if type=="array" then .[0] else .response[0] end | {id: (.id|type), ultima: (.lastReceivedKey|keys?)}'

# 2. o id montado é o id da mensagem na ponte, o mesmo que vai no evento do webhook? (só true ou false)
chats=$(curl -s -X POST "http://localhost:21465/api/<sessao>/list-chats" \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"count":5,"onlyUsers":true}')
com_chave='[(if type=="array" then . else .response end)[]? | select(.lastReceivedKey != null)][0]'
chat=$(printf '%s' "$chats" | jq -r "$com_chave"' | .id | (._serialized? // .)' 2>/dev/null)
montado=$(printf '%s' "$chats" | jq -r "$com_chave"' | .lastReceivedKey
  | ._serialized // "\(.fromMe)_\(.remote | (._serialized? // .))_\(.id)"' 2>/dev/null)
curl -s "http://localhost:21465/api/<sessao>/get-messages/$chat?count=20" -H "Authorization: Bearer $TOKEN" \
  | jq --arg m "$montado" '[(.response | if type=="object" then [.[]] else . end)[]? | .id | (._serialized? // .)]
      | index($m) != null' 2>/dev/null
```

Esperado: no 1, `ultima` contém `_serialized`, ou então `fromMe`, `remote` e `id`; no 2, `true`. O 2 não imprime o
telefone (fica em variável) e mede de uma vez a forma do id montado e a do `get-messages`, que é a fonte do
`id._serialized` do evento. Não mede a marca já gravada em `webhook_recebidos` (`EventIdExterno` =
`wpp:<sessao>:<id>`), nem conversa `@lid` cujo evento chegue com `remote` em `@c.us`. `false` no 2 pode ser só a
última recebida fora das 20 mais recentes: repita trocando o `[0]` de `com_chave`. `false` de novo, saída vazia ou
campo faltando no 1: NÃO ligue, o vigia não acharia a mensagem (falso verde) ou acusaria toda conversa (falso
alarme). Abra issue com os nomes de campo vistos.

O Worker precisa da configuração da ponte. O `scripts/deploy/wppconnect/compose.yaml` versionado já a entrega ao
`api` e ao `worker` pela âncora `x-wppconnect-cliente`; confira na VPS, contando sem imprimir valor:

```bash
docker exec ez-worker printenv | grep -c '^Atendimento__WppConnect__'   # esperado: 4 ou mais
```

Sem ela o vigia não avalia nada e o Worker registra `Vigia da ponte WPPConnect ligado sem a configuração da ponte`.

Conferido, ligue no Worker com `Atendimento__WppConnect__VigiaPonteMuda__Habilitado=true` (mudança de
produção: exige GO do Felipe) e acompanhe por um dia o log `Ponte WPPConnect muda`.

## Quando o aviso aparece

1. Contêiner: `docker inspect -f '{{.State.Status}} {{.State.Health.Status}}' easystok-wppconnect-1`.
2. Entrega: no log da API, procure `POST /api/webhooks/wppconnect` nos últimos minutos e os motivos em
   `Webhook sem efeito`. Requisição chegando (mesmo com 400, 503 ou motivo `evento_ignorado`) não é ponte muda.
3. Ponte: no log dela, conte `Error calling Webhook.` no intervalo.
4. Estado do vigia: `select endpoint_name, last_alerted_at from endpoint_health_state where endpoint_name like 'wppconnect/%'`.
5. Se a ponte está conectada e nada chega, o remédio usado em 21/11 foi `docker restart easystok-wppconnect-1`.
   Reiniciar sessão de WhatsApp arrisca QR e bloqueio do número: só com GO do Felipe, nunca pelo deploy.

Nunca cole telefone, texto de mensagem ou token em issue, PR ou log (#2590).
