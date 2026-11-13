# Ponte provisória de WhatsApp enquanto a Meta não aprova o Tech Provider

- Data: 2026-10-26. Fontes no fim.
- Decisão do Felipe (26/10): WAHA com motor GOWS no NÚMERO PRINCIPAL da loja. ADR-0062, issue #2072, guia `docs/dev/whatsapp-ponte-waha.md`.
- Pergunta: como operar a Casa da Baba pelo WhatsApp da loja AGORA, pelo app/WhatsApp Web e não pela Cloud API, com o menor risco de banimento, até o App Review e o Tech Provider saírem.
- Volume esperado: dezenas de conversas por dia, uma loja, um número.

## 1. O que trava hoje na Meta (medido em 12/10, memória da sessão)

| Item | Estado |
|---|---|
| Portfólio FMA Sistemas | verificado |
| App EasyStok, webhook, coexistência v4 no código | no ar (#1417) |
| Tech Provider | etapa 1 de 2; App Review em rascunho, NÃO enviado; faltam 2 vídeos e ícone 1024x1024 |
| Número da loja | continua no app WhatsApp Business do celular, portfólio da Tatiana |

Inferência: o caminho oficial não está parado na Meta, está parado no envio do App Review. Enviado hoje, o prazo típico é de dias, não meses. A ponte abaixo cobre esse intervalo e só isso.

## 2. Risco real: perder o número da loja

Fatos observados na comunidade (out/2025 a mai/2026):

- Baileys issue #1869: bans em série, inclusive bots com 3 anos de uso; sem resposta técnica dos mantenedores; o que reduziu bans foi volume baixo, delays e nada de grupos ou status. Fonte: https://github.com/WhiskeySockets/Baileys/issues/1869
- Todas as pontes não oficiais violam os Termos do WhatsApp. Ban por cliente não oficial costuma ser permanente e sem recurso.
- Blogs de fornecedores falam em 2 a 8 semanas até o ban e em 40 a 60 por cento de contas banidas no 1º trimestre de 2026. São números sem fonte verificável; trate como alerta, não como medida.
- Relato recorrente: detecção por padrão de uso (ritmo, grupos, broadcast, número novo, IP de servidor), não só por volume.

Conclusão calculada: com volume pequeno, só respondendo a quem escreveu, sem grupos e sem disparo, o risco cai bastante, mas não zera. O dano, se acontecer, é o número da loja, para sempre.

Decisão que só o Felipe toma: qual número entra na ponte.

| Opção | Probabilidade de ban | Dano se banir |
|---|---|---|
| A. Número principal da loja | menor (número antigo, reputação) | máximo: perde o número e os contatos |
| B. Chip secundário, aquecido 2 a 3 semanas no app do celular, só para avisos automáticos (status, cobrança) | maior (número novo) | pequeno: troca o chip; o principal segue humano no celular |

## 3. Candidatos medidos (GitHub em 26/10/2026)

| Projeto | Motor | Stars | Última release | Último push | Licença | Observação |
|---|---|---|---|---|---|---|
| devlikeapro/waha | WEBJS (Chromium), GOWS (whatsmeow), NOWEB (Baileys), WPP | 7.692 | 2026.9.2 em 02/10/2026 | 22/10/2026 | Apache-2.0 | REST + webhook + Swagger, Docker; motor trocável sem mudar o cliente. Core grátis: 1 sessão. Blogs dizem que mídia é só no Plus, mas no código do motor GOWS (`session.gows.core.ts`) o Core envia imagem, arquivo e voz |
| asternic/wuzapi | whatsmeow (Go) | 1.045 | v1.1.0 em 26/10/2026 | 26/10/2026 | MIT | binário único, SQLite ou Postgres, webhook, multi-sessão; grátis; API mais crua |
| evolution-foundation/evolution-api | Baileys, ou Cloud API oficial | 9.962 | 2.3.7 em 05/12/2025 | 14/07/2026 | Apache com cláusulas extras (GitHub marca NOASSERTION) | maior comunidade PT-BR; 2.4 exige ativação de licença; 250 issues abertas; lite arquivado em 05/2026; motor Baileys é o mais citado nos bans |
| wppconnect-team/wppconnect-server | Puppeteer + Chromium | 1.061 | v2.10.68 em 25/10/2026 | 26/10/2026 | Apache-2.0 | comunidade BR ativa; pesado (um Chromium por sessão) |
| wwebjs/whatsapp-web.js | Chromium | 22.802 | v1.34.7 em 24/04/2026 | 25/10/2026 | Apache-2.0 | biblioteca Node, exigiria microserviço próprio |
| WhiskeySockets/Baileys | protocolo reverso | 11.457 | v7.0.0-rc14 em 29/07/2026 | 26/10/2026 | MIT | biblioteca; ainda em RC; mais relatos de ban em 2025 e 2026 |
| Z-API, Uazapi, W-API | QR, pagos | n/a | n/a | n/a | SaaS | mesmo risco das anteriores, sem self-host; só compra suporte |
| Chatwoot | inbox | 38.361 | ativo | 26/10/2026 | custom | descartado na ADR-0050: a inbox é o console |

Leitura: Evolution tem a maior comunidade no Brasil, mas em 2026 mostra os piores sinais de manutenção e o motor com mais bans. WAHA e wuzapi estão com release neste mês e permitem o motor whatsmeow, que sustenta pontes de longa data (mautrix-whatsapp) com poucos relatos de ban comparado ao Baileys.

## 4. Recomendação

1. WAHA como sidecar no compose da VPS, motor GOWS (whatsmeow), Core gratuito (mídia conferida no código). Se o motor começar a derrubar sessão, troca para WEBJS sem tocar no EasyStok.
2. Sem custo e mais enxuto: wuzapi. Mesmo motor, menos abstração, mais código no adaptador.
3. Evolution só se a comunidade PT-BR pesar mais que a manutenção; fixar na 2.3.7 e aceitar o motor Baileys.

Em qualquer caso, a ponte é um adaptador atrás da porta que já existe. Nada do domínio muda.

## 5. Encaixe no EasyStok

Porta: `EasyStock.Application/Ports/Output/Atendimento/IWhatsAppCloudClient.cs`. Entrada: `ProcessarEventoWhatsAppUseCase` via `/api/webhooks/whatsapp`.

| Método da porta | Na ponte não oficial |
|---|---|
| EnviarTextoAsync, com resposta a wamid | existe |
| EnviarImagemAsync, EnviarAudioAsync | existe (WAHA só no Plus) |
| EnviarBotoesAsync | NÃO existe de forma confiável; cair para texto numerado ("1 Sim, 2 Não") e interpretar a resposta |
| EnviarTemplateAsync | não existe e não precisa: não há janela de 24 h fora da API oficial; enviar como texto |
| MarcarComoLidaAsync | existe |
| BaixarMidiaAsync | existe (URL ou base64 do sidecar) |

Trabalho estimado: 1 adaptador `WahaWhatsAppClient`, 1 endpoint de webhook que traduz o evento do sidecar para o mesmo caminho do use case, 1 serviço no compose, 1 flag por empresa para escolher o canal. O agente precisa aceitar resposta numerada no lugar do clique de botão.

## 6. Regras de operação anti-ban (consenso dos relatos, sem garantia)

1. Só responder a quem escreveu primeiro. Nenhuma lista, nenhum grupo, nenhum status, nenhum disparo.
2. Delay humano de 2 a 8 s com "digitando" antes de cada envio; nunca mais de 1 mensagem em sequência sem resposta.
3. Teto diário baixo (dezenas de contatos), sem rajada no horário de pico.
4. Número já ativo há meses, com foto, nome e catálogo preenchidos; se for chip novo, 2 a 3 semanas de uso humano antes.
5. QR, painel e API do sidecar só na rede interna do compose; nada exposto na internet.
6. O celular continua ligado e com o app aberto de vez em quando; a sessão é um dispositivo vinculado.
7. Não atualizar a lib do motor sem necessidade; fixar a versão da imagem.
8. Logar cada envio para provar o volume se precisar reclamar.

## 7. Hipótese barata a testar antes de montar a ponte (15 minutos, risco zero)

Com Standard Access, o app EasyStok já pode operar ativos da própria empresa dona do app. Testar o fluxo de coexistência que já existe no console (Gestão, Entregas e integrações, WhatsApp), logado como Felipe, mirando o portfólio FMA e escaneando o QR no celular da loja. Se a Meta aceitar sem o Tech Provider, a ponte não oficial é desnecessária. Se recusar, o erro diz qual permissão falta e o custo foi nulo.

## Fontes

- https://github.com/WhiskeySockets/Baileys/issues/1869
- https://github.com/kobie3717/baileys-antiban
- https://github.com/devlikeapro/waha
- https://github.com/asternic/wuzapi
- https://github.com/evolution-foundation/evolution-api
- https://docs.evolutionfoundation.com.br/en/licensing
- https://github.com/wppconnect-team/wppconnect-server
- https://github.com/wwebjs/whatsapp-web.js
- https://www.libhunt.com/compare-waha-vs-whatsmeow
- https://wasphere.com/blog/open-source-whatsapp-api-landscape-2026/
- https://dev.to/abhishek_rajpurohit/evolution-api-vs-openwa-vs-meta-cloud-api-which-whatsapp-api-should-you-use-4bcn
- https://blog.cubosuite.com.br/meta-banindo-whatsapp-nao-oficial-em-2026-o-que-mudou-e-o-que-fazer/
- https://www.pipes.bot/pt-br/blog/baileys-vs-whatsapp-cloud-api
- https://automacaohoje.com/guias/evolution-api-banimento-como-evitar/
- https://wasenderapi.com/blog/how-to-use-evolution-api-without-getting-banned-on-whatsapp-2026-guide
- https://developers.facebook.com/documentation/business-messaging/whatsapp/embedded-signup/overview/
- https://whautomate.com/whatsapp-coexistence
- https://www.zoko.io/post/use-whatsapp-api-without-business-verification
- StackOverflow bloqueia o buscador usado nesta sessão; não entrou na amostra.
