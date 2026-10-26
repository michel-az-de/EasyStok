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
  6. POST {phone}/smb_app_data  sync_type=smb_app_state_sync (contatos; history não é pedido)
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
- **O histórico anterior não é importado.** A conexão só pede `smb_app_state_sync` (contatos); o
  `history` (até 180 dias) não é pedido porque ainda não há importação, e a janela de 24 h para
  pedi-lo se perde. Para importar o histórico depois, será preciso reconectar o número.
- Os webhooks `history` e `smb_app_state_sync` são aceitos com 200 e só contados no log.
- Mídia que a loja manda pelo celular entra com o tipo e um aviso, sem baixar o arquivo.

## Desfazer

Admin › Tenants › WhatsApp desvincula o número (o webhook deixa de rotear para a empresa). Sem
número vinculado o envio da empresa é recusado antes da Meta, com ou sem token gravado.

## Ponte provisória WPPConnect (#2071)

Alternativa por WhatsApp Web enquanto a habilitação da Meta está pendente. Não é uma conexão
oficial e pode sofrer desconexão ou bloqueio. O aceite de envio, recebimento, mídia e reconexão
exige teste com um número real; testes HTTP com respostas controladas não substituem esse aceite.

O console oferece **Configurações › Canais › Conexão provisória por QR**. A sessão tem nome
`ez-{EmpresaId:N}`; o token é cifrado no cofre existente, provider `wppconnect`. Nenhum token
ou segredo do servidor é devolvido ao navegador. A conexão Meta e suas credenciais permanecem
preservadas. Com o piloto habilitado, empresa sem sessão nem Meta real recebe erro ao enviar,
em vez de sucesso do stub.

### Configuração

Use `docker-compose.wppconnect.yml` como sobreposição de `docker-compose.local.yml`, com
`WPP_SECRET_KEY` e `WPP_WEBHOOK_TOKEN` aleatórios e `WPP_PUBLIC_FILES_URL` com a URL HTTP(S)
dos arquivos (incluindo `/files`) no arquivo de ambiente fora do Git. O token
do webhook deve ter pelo menos 16 caracteres. O cofre também exige a KEK já documentada acima.

```powershell
docker compose --env-file .env.local -f docker-compose.local.yml -f docker-compose.wppconnect.yml up -d --build
```

O servidor está fixado em `2.10.68`, com digest no compose. A porta 21465 não é publicada:
somente a API acessa o gateway na rede Docker. Volumes de `tokens` e `userDataDir` preservam
o vínculo no reinício. O pré-carregador `scripts/wppconnect/configurar.cjs` desliga a mídia
inline, recupera mensagens não lidas na partida e mantém a sessão de QR disponível para leitura.
O compose substitui o `entrypoint` da imagem: mudar apenas `command` não carrega essa configuração.
Um `flock` exclusivo protege o volume; antes de iniciar o Chromium, são removidos somente os
três links `Singleton*` dos perfis `ez-*` deixados pelo contêiner anterior. O perfil e as chaves
não são apagados. Não compartilhar esse volume com processos fora desse entrypoint.
Mensagens anteriores ao início da captura são ignoradas para não responder ao histórico da loja.

Em outro ambiente, configure na API:

- `Atendimento__WppConnect__Habilitado=true`;
- `Atendimento__WppConnect__BaseUrl=http://wppconnect:21465/`;
- `Atendimento__WppConnect__SecretKey` e `Atendimento__WppConnect__WebhookToken`;
- no gateway, `WEBHOOK_URL=http://api:8080/api/webhooks/wppconnect?token=<mesmo-token>`;
- `FileStorage__PublicBaseUrl` com URL HTTP(S) dos arquivos que o gateway consiga acessar,
  incluindo `/files` para o storage local. Não usar somente a raiz do host.

Não publicar o segredo no terminal, no log nem em URL acessível ao usuário. O cliente HTTP
desliga seus loggers porque a geração de token do WPPConnect inclui o segredo no caminho.

### Contratos e validação

- `GET/POST/DELETE /api/integracoes/whatsapp/wppconnect`: estado, iniciar e desvincular;
- `POST /api/integracoes/whatsapp/wppconnect/reconectar`: fecha sem logout e reinicia;
  durante a geração/leitura do QR, apenas atualiza o estado para não abandonar o navegador
  ainda em inicialização;
- `POST /api/webhooks/wppconnect?token=...`: callback autenticado do gateway. A sessão é
  conferida com a credencial cifrada antes de processar conteúdo.

O preload preserva os argumentos seguros da biblioteca para cache, origem e TLS. Callbacks
HTTP 429/5xx são repetidos até cinco tentativas com espera exponencial limitada a 60 segundos;
erros permanentes e endpoints de envio não recebem esse retry.

O estado conectado exige confirmação do cliente WhatsApp; o QR residual do gateway é retirado
da resposta e da tela após essa confirmação.

Envio retorna identificador real; resposta sem id ou falha do servidor não é sucesso. Não há
retry automático de envio. Recebimento reutiliza idempotência, histórico, eventos do console
e a fila de mídia do atendimento. A mídia é baixada pela sessão autenticada, com limite de
resposta de 30 MB no transporte e os limites existentes do armazenamento. LID é resolvido
pelo gateway antes de tratar o contato como telefone. Grupos e broadcasts são ignorados.

Para liberar: parear pelo menu **Aparelhos conectados** do WhatsApp Business, conferir o
número conectado, receber e responder texto, imagem e áudio pelo console, verificar o conteúdo
no aparelho destinatário e repetir após reiniciar o gateway. Conferir que mensagens já gravadas
não duplicam e que uma segunda empresa não acessa a sessão nem os anexos da primeira.

### VPS

O compose local não é o compose de produção. A preparação específica fica em
`scripts/deploy/wppconnect/compose.yaml`, para sobrepor os arquivos existentes em
`/opt/stacks/easystok`. API e Worker entram também na rede `whatsapp`; o gateway fica apenas
nessa rede, sem porta pública. Os volumes nomeados conservam perfil e tokens, e o Docker
reinicia o processo. Isso não recupera um vínculo revogado pelo WhatsApp.

Após homologar, copiar o fragmento para `compose.wppconnect.yaml` na stack e o preload para
`wppconnect/configurar.cjs`. Definir `WPP_SECRET_KEY` e `WPP_WEBHOOK_TOKEN` no `.env` protegido
da VPS. Preservar a URL pública de arquivos que API e Worker já usam. Configurar
`COMPOSE_FILE=compose.yaml:compose.override.yaml:compose.wppconnect.yaml` nesse `.env` para
o deploy canônico continuar carregando a integração. Conferir o compose mesclado antes de
subir o serviço. A ativação depende de texto, imagem, áudio e reconexão reais aprovados;
`/healthz` do gateway confirma o processo HTTP, não a sessão WhatsApp.

### Limites do piloto

- A interface e o domínio conservam a janela de atendimento existente. Modelos da Meta não
  são enviados pelo WPPConnect; botões saem como opções em texto.
- Respostas feitas diretamente no celular não são sincronizadas como ecos neste piloto:
  `onSelfMessage=false` evita que a própria resposta do agente assuma a conversa como humana.
- Não há importação do histórico anterior, grupos, campanhas ou garantia contra bloqueio.
- Remover o vínculo pela tela faz logout e desativa somente a credencial WPPConnect da empresa;
  o próximo envio usa Meta quando ela estiver configurada. Sem Meta real, o envio é recusado.
