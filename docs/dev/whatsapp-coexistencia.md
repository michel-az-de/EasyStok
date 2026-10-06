# WhatsApp por coexistência (Embedded Signup v4)

Issue #1417. O número da loja continua no app **WhatsApp Business** do celular e passa a falar
também pela Cloud API. O EasyStok recebe as mensagens dos clientes, o agente responde, e o que a
loja escreve pelo celular aparece na conversa como mensagem de saída da dona.

## Fluxo

```
Console (Gestão › Entregas e integrações › WhatsApp)
  GET  /api/integracoes/whatsapp/coexistencia/config   → appId, configId, graphVersion, habilitado
  FB.login(config_id, response_type=code, extras={})    → code (vale ~30 s)
  postMessage WA_EMBEDDED_SIGNUP (FINISH...)            → waba_id, phone_number_id
  POST /api/integracoes/whatsapp/coexistencia           { code, wabaId, phoneNumberId }

API
  1. oauth/access_token (client_id + client_secret + code) → business token
  2. POST {waba}/subscribed_apps
  3. GET  {phone}?fields=display_phone_number,verified_name,is_on_biz_app,platform_type
  4. vincula o phone_number_id à empresa (outra empresa ou número de plataforma: 409)
  5. grava o token cifrado em credencial_integracao (provider meta-whatsapp)
  6. POST {phone}/smb_app_data  sync_type=smb_app_state_sync e history
```

Respostas: 400 quando a Meta recusa o `code` (refazer o fluxo), 502 quando recusa os passos 2 e 3,
409 no conflito de número. `is_on_biz_app` diferente de `true` conecta, mas o console avisa.
Sincronização recusada não derruba a conexão. O `/register` não é chamado: não se aplica à
coexistência.

Depois de conectado, envio, "marcar como lida" e download de mídia da empresa usam o token dela.
Empresa sem credencial segue no token global (`Notifications:WhatsApp:Meta:AccessToken`). O número
de plataforma (N6) continua sempre no global.

## Na Meta (painel, fora do código)

1. **Tech Provider** aprovado para o app (Business Settings › app › WhatsApp).
2. **Advanced Access** em `whatsapp_business_management` e `whatsapp_business_messaging`.
3. **Facebook Login for Business › Configurations › Create**, variante **WhatsApp Embedded Signup**,
   versão **v4** (v2 e v3 saem do ar em 15/10/2026). Copie o **Configuration ID** da lista: é o
   `EmbeddedSignupConfigId`.
4. Em Facebook Login for Business › Settings: domínio do console (`app.easystok.online`) em
   *Allowed Domains for the JavaScript SDK* e *Login with the JavaScript SDK* ligado.
5. **Webhook** do app (Callback já usado pelo atendimento: `/api/webhooks/whatsapp`), campos
   assinados: `messages`, `smb_message_echoes`, `history`, `smb_app_state_sync` e `account_update`.
6. No celular da loja: **WhatsApp Business 2.24.17 ou mais novo**.

## Variáveis

| Configuração | Variável de ambiente | Observação |
|---|---|---|
| `Notifications:WhatsApp:Meta:AppId` | `Notifications__WhatsApp__Meta__AppId` | público; id do app (897859909924304) |
| `Notifications:WhatsApp:Meta:EmbeddedSignupConfigId` | `Notifications__WhatsApp__Meta__EmbeddedSignupConfigId` | público; do passo 3 |
| `Notifications:WhatsApp:Meta:AppSecret` | `Notifications__WhatsApp__Meta__AppSecret` | já existe (assinatura do webhook); também troca o `code` |
| `Crypto:CurrentKekId` e `Crypto:Keks:<id>` | `Crypto__CurrentKekId` e `Crypto__Keks__<id>` | KEK de 32 bytes em Base64; sem ela o token não é gravado |

`habilitado` no `/config` só fica `true` com AppId, ConfigId e AppSecret preenchidos. Na VPS, o
compose é o de `/opt/stacks/easystok` (não o do repositório): acrescente no serviço da API, com os
valores no `.env` (nunca pelo chat):

```yaml
      Notifications__WhatsApp__Meta__AppId: "${EZ_META_APP_ID}"
      Notifications__WhatsApp__Meta__EmbeddedSignupConfigId: "${EZ_META_EMBEDDED_SIGNUP_CONFIG_ID}"
```

## Limites

- **20 mensagens por segundo** por número em coexistência.
- **Sem grupos**: só conversas individuais passam pela API.
- **Sincronização em até 24 h** depois da conexão; perdeu o prazo, refaça o Embedded Signup.
- O histórico (`history`) e o estado do app (`smb_app_state_sync`) são aceitos e só contados no
  log; a importação dos 180 dias fica para outra issue.
- Mídia que a loja manda pelo celular entra com o tipo e um aviso, sem baixar o arquivo.

## Desfazer

Admin › Tenants › WhatsApp desvincula o número (o webhook deixa de rotear para a empresa). Sem
número vinculado o envio da empresa é recusado antes da Meta, com ou sem token gravado.
