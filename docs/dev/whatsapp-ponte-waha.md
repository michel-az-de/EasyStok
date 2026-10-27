# WhatsApp pela ponte provisória do WAHA

Issue #2072, ADR-0062. O número da loja continua no celular e o WAHA vira um aparelho vinculado a ele. O EasyStok
fala com o WAHA pela mesma porta da Cloud API, então conversa, agente, avisos e console funcionam como na Meta.

> **RISCO:** é uma conexão não oficial. O WhatsApp pode banir o número da loja sem recurso. As regras de operação
> abaixo reduzem o risco, não o zeram. A saída é a Cloud API por coexistência (ADR-0050, #1417).

```
cliente ──WhatsApp──► celular da loja ◄──aparelho vinculado──► ez-waha (VPS, rede edge)
                                                                  │ webhook HMAC sha512
                                                                  ▼
                                      ez-api /api/webhooks/waha ──► mesmo atendimento da Meta
                                      ez-api e ez-worker ──REST X-Api-Key──► ez-waha
```

## Ligar, nesta ordem

1. **Subir o WAHA** (gera os segredos na VPS, sobe o `ez-waha` e cria a sessão):

   ```bash
   scripts/deploy/waha-deploy.sh subir
   ```

2. **Vincular o celular da loja.** Gere o código e digite no celular em WhatsApp Business › Aparelhos conectados ›
   Conectar aparelho › Conectar com número de telefone:

   ```bash
   scripts/deploy/waha-deploy.sh codigo 5541999990000
   ```

   Alternativa: painel ao vivo com QR, por túnel (`ssh -L 3100:127.0.0.1:3100 hostinger`, depois
   `http://localhost:3100/dashboard`; usuário `waha`, senha `WAHA_DASHBOARD_PASSWORD` do `/opt/stacks/waha/.env`).

3. **Conferir** que a sessão está `WORKING` e o final do número é o da loja:

   ```bash
   scripts/deploy/waha-deploy.sh status
   ```

4. **Vincular a empresa ao número da loja** em Admin › Tenants › WhatsApp, só dígitos com 55 e DDD. É o mesmo campo do
   `phone_number_id` da Meta: o webhook roteia por ele e o envio só sai para a empresa vinculada a ele.

5. **Gravar as variáveis da API** no `.env` do easystok (não imprime segredo):

   ```bash
   scripts/deploy/waha-deploy.sh ligar-api 5541999990000
   ```

6. **Mapear as variáveis no compose do easystok**, nos serviços `api` e `worker`:

   ```yaml
   Atendimento__WhatsApp__Cliente: "${EZ_WHATSAPP_CLIENTE:-}"
   Atendimento__WhatsApp__Waha__BaseUrl: "${EZ_WAHA_BASE_URL:-}"
   Atendimento__WhatsApp__Waha__ApiKey: "${EZ_WAHA_API_KEY:-}"
   Atendimento__WhatsApp__Waha__HmacKey: "${EZ_WAHA_HMAC_KEY:-}"
   Atendimento__WhatsApp__Waha__PhoneNumberId: "${EZ_WAHA_PHONE_NUMBER_ID:-}"
   ```

7. **Publicar** com `scripts/deploy/vps-deploy.sh`. Com `Cliente=waha` a API não sobe sem endereço, chave, segredo e
   número (`StartupHardening`).

8. **Testar** de outro celular: mande "oi" para a loja. A conversa aparece no console e a resposta chega com
   "digitando" antes.

## Regras de operação

1. Só responder a quem escreveu. Nada de lista de transmissão, grupo, status ou campanha pela ponte.
2. O celular da loja fica ligado e com internet; abra o app ao menos uma vez a cada 14 dias.
3. Não troque a imagem do WAHA sem motivo. A versão é fixada no compose.
4. O painel e a API do WAHA nunca vão para a internet. A porta 3100 só existe no loopback da VPS.
5. Restrição do WhatsApp aparece no log da API como erro ("restringiu o número" ou "cota de conversas novas"). Nesse
   caso, pare qualquer envio que não seja resposta.

## O que muda em relação à Meta

| Na Meta | Na ponte |
|---|---|
| Botão interativo | Opções numeradas no texto. A resposta "2" ou o título vira o clique |
| Modelo aprovado fora da janela | O outbox manda o texto da própria mensagem. Modelo pelo console, programada e reenvio de modelo falham com o motivo |
| Janela de 24 h | Igual. A ponte manda o que a Meta mandaria |
| Mensagem que a loja escreve pelo celular | Igual: vira mensagem da dona e assume a conversa |
| Mídia recebida | Igual: baixada do WAHA (arquivo fica 1 h lá) e guardada no storage |
| Conversa por id oculto (`@lid`) | O WAHA dá o telefone; sem telefone, o contato fica com o id oculto |

O menu numerado fica no cache da aplicação por 24 h. Sem Redis compartilhado, a resposta a um menu enviado pelo Worker
(avaliação do pedido) chega ao agente como texto.

## Acompanhar

```bash
scripts/deploy/waha-deploy.sh status
```

No log da API, procure `Ponte WAHA`: sessão fora do ar, conta conectada a outro número e restrições do WhatsApp. Com a
sessão fora do ar as mensagens continuam chegando no celular; só o atendimento automático para.

## Desligar e voltar para a Meta

1. `EZ_WHATSAPP_CLIENTE=meta` no `.env` do easystok e `scripts/deploy/vps-deploy.sh`.
2. Vincular a empresa ao `phone_number_id` da Meta (coexistência, `docs/dev/whatsapp-coexistencia.md`).
3. No celular, desconectar o aparelho do WAHA em Aparelhos conectados.
4. `ssh hostinger "cd /opt/stacks/waha && docker compose down"`.

## Configuração

| Chave | Variável | Observação |
|---|---|---|
| `Atendimento:WhatsApp:Cliente` | `EZ_WHATSAPP_CLIENTE` | `waha` liga a ponte |
| `Atendimento:WhatsApp:Waha:BaseUrl` | `EZ_WAHA_BASE_URL` | `http://ez-waha:3000` |
| `Atendimento:WhatsApp:Waha:ApiKey` | `EZ_WAHA_API_KEY` | O WAHA guarda só o hash sha512 |
| `Atendimento:WhatsApp:Waha:HmacKey` | `EZ_WAHA_HMAC_KEY` | Mínimo 32 caracteres |
| `Atendimento:WhatsApp:Waha:PhoneNumberId` | `EZ_WAHA_PHONE_NUMBER_ID` | Número da loja, só dígitos |
| `Atendimento:WhatsApp:Waha:Sessao` | | `default` (o Core tem uma sessão) |
| `Atendimento:WhatsApp:Waha:PausaMinimaMs`, `PausaMaximaMs`, `PausaPorCaractereMs` | | 1200, 4500 e 30 |
