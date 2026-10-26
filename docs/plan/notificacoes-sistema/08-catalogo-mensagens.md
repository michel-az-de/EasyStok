# 08 · Catálogo de mensagens: inventário e modelos Meta

Revisão de 26/10/2026 (issue #2074), medida no master `039daa6a2` e em produção. Complementa o plano M0 a M5, que
está todo mergeado (N0 a N13, #1357 a #1390).

## 1. Estado em produção (medido em 26/10)

| Canal | Provedor no ar | Sai de verdade? | Destrava com |
|---|---|---|---|
| E-mail (plataforma) | `console` | **Não**, vira `Simulado` | `Smtp__*` na API e no Worker (seção 4) |
| WhatsApp da loja | stub ou Meta sem número | **Não** | ponte WAHA (#2072) ou número Meta aprovado |
| WhatsApp de plataforma (2º número) | stub | **Não** | chip, `phone_number_id` e 6 modelos aprovados |
| Push (PWA) | Web Push | sim, com VAPID no `.env` | |
| Sino (InApp) | banco | sim | |
| SMS | stub | não | fora do plano (ADR-0057, item 10) |

`GET https://api.easystok.online/health/notificacoes` responde `Degraded`: "E-mail: provider console em Production".

O serviço isolado de envio já existe: é o **Worker** (`ez-worker`). Ele roda dispatcher, avaliador, coletores e
outbox, e envia todos os canais. A API não despacha (`Notifications:Hosting:Mode` forçado para `Disabled`).

## 2. Matriz tipo x canal (depois da #2074)

Só os tipos que têm produtor em código. Os 25 tipos de SaaS (assinatura, fatura, helpdesk, SLA, CSAT, bug-fix,
broadcast) têm rotina e template, mas nada os publica: ficam dormentes até existir o produtor.

| Tipo | Quem recebe | Sino | Push | E-mail | WhatsApp |
|---|---|---|---|---|---|
| Esqueci senha, senha alterada, convite | a própria pessoa | | | sim | 2º número |
| Contato alterado, confirmação de e-mail | a própria pessoa | | | sim | |
| Incidente do sistema | superadmins | | | sim | 2º número |
| Prazo estourado | gestores | **sim (#2074)** | | sim | 2º número |
| Resumo diário (liga por empresa) | admins | **sim (#2074)** | | sim | 2º número |
| Contas a pagar e a receber, vencendo e vencidas | admins | **sim (#2074)** | | **sim (#2074)** | |
| Produto vencendo (coletor desligado) | gestores (#2074) | sim (#2074) | | sim | |
| Relatório pronto ou falhou | quem pediu | sim | | reserva | |
| Caixa esquecido, agendados, cobrança na equipe, mediação, pagamento sem aviso | equipe | sim | | | |
| Conversa escalada, lembrete, loja fechada, avisos do balcão | equipe ou donos | sim | sim | | |
| Pedido pago, em preparo, saiu, entregue, avaliação, reembolso | cliente | | | | loja |
| Pedido atualizado (cancelado, pago na entrega, cupom), reagendado | cliente | | | | loja (**modelo novo, #2074**) |
| Campanha e lembrete de campanha | cliente | | | | loja |

## 3. Modelos do WhatsApp para submeter na Meta

Idioma `pt_BR`, parâmetros posicionais. A Meta recusa parâmetro no começo ou no fim do corpo e dois parâmetros
colados; os corpos abaixo já respeitam isso. Na ponte WAHA (#2072) os modelos não são submetidos: fora da janela
de 24 h ela manda o `Corpo` do catálogo no lugar do modelo, e só para tipo que tem modelo no seed.

### Número da loja

| Nome | Categoria | Corpo | Botões |
|---|---|---|---|
| `pedido_pago` | Utilidade | Oi, {{1}}! Recebemos seu pagamento. O pedido nº {{2}} está confirmado e a previsão de entrega é {{3}}. Obrigada pela preferência! | |
| `pedido_em_preparo` | Utilidade | Olá, {{1}}! Seu pedido nº {{2}} já está em preparo na nossa cozinha. Previsão de entrega: {{3}}. Qualquer dúvida, é só responder. | |
| `pedido_saiu` | Utilidade | Olá, {{1}}! Seu pedido nº {{2}} saiu para entrega e logo chega até você. | |
| `pedido_entregue` | Utilidade | Olá, {{1}}! Seu pedido foi entregue. Obrigada pela preferência, esperamos você de novo! | |
| `avaliacao` | Utilidade | Olá, {{1}}! O que achou do seu último pedido? É só tocar num botão. | resposta rápida: `Gostei`, `Não gostei` |
| `reembolso_efetuado` | Utilidade | Olá, {{1}}! Devolvemos {{2}} referente ao pedido nº {{3}}. O prazo para aparecer no extrato depende do seu banco. Desculpe o transtorno! | |
| `pedido_atualizado` (novo) | Utilidade | Olá, {{1}}! Atualização do seu pedido nº {{2}}: {{3}} Qualquer dúvida, é só responder esta mensagem. | |
| `pedido_reagendado` (novo) | Utilidade | Olá, {{1}}! Seu pedido nº {{2}} foi reagendado. A nova previsão de entrega é {{3}}. Qualquer dúvida, é só responder. | |
| `retomar_conversa` | Utilidade | Oi {{1}}, aqui é a Casa da Baba. Ficou uma resposta nossa pendente para você. Responda esta mensagem para continuar. | |
| `campanha_generica` | Marketing | Olá, {{1}}! {{2}} Para não receber mais, responda SAIR. | cabeçalho de imagem opcional |
| `campanha_lembrete` | Marketing | Olá, {{1}}! A campanha {{2}} está terminando. Ainda dá tempo de pedir! Para não receber mais, responda SAIR. | |

`retomar_conversa` não está no seed: o nome é gravado na Gestão › Atendimento (`ModeloRetomadaNome`).

### 2º número (plataforma)

| Nome | Categoria | Corpo | Botões |
|---|---|---|---|
| `codigo_redefinir_senha` | Autenticação | modelo de autenticação da Meta: "{{1}} é o seu código de verificação." | copiar código |
| `senha_alterada` | Utilidade | EasyStok informa: a senha da sua conta foi alterada em {{1}}. Se não foi você, fale agora com a responsável da sua empresa. | |
| `convite_acesso_link` | Utilidade | Olá, {{1}}! Você recebeu um convite para o sistema da {{2}}. Toque no botão para entrar com Google ou criar sua senha. O convite vale por 3 dias. | URL dinâmica: base do `Auth:LinkConvite`, sufixo = token |
| `incidente_sistema` | Utilidade | EasyStok informa: o componente {{1}} está {{2}} desde {{3}}. Veja o resumo no e-mail enviado agora. | |
| `prazo_estourado` | Utilidade | Atenção na loja: {{1}} há {{2}} (referência {{3}}). Abra o EasyStok para resolver. | |
| `resumo_diario` | Utilidade | Resumo de {{1}} no EasyStok: {{2}} pedidos entregues, faturamento de {{3}} e caixa {{4}}. O resumo completo está no seu e-mail. | |

## 4. O que depende do Felipe

| Passo | Destrava |
|---|---|
| Escolher o transporte do e-mail: caixa Hostinger de `easystok.online` (MX, SPF, DKIM, DMARC) ou uma caixa já contratada | todo e-mail |
| Gravar `Smtp__Host`, `Smtp__Port`, `Smtp__Username`, `Smtp__Password`, `Smtp__FromEmail` (e `Smtp__Seguranca__*`) no `.env` e mapear na API **e** no Worker do compose da VPS | todo e-mail |
| `Auth__LinkConvite=https://app.easystok.online/#/convite?t={0}` no `.env` (sem ela o link cai na 1ª origem do CORS, um host antigo) | link do convite |
| Submeter os modelos da seção 3 no WhatsApp Manager | WhatsApp fora da janela, pela Meta |
| Chip e `phone_number_id` do 2º número | WhatsApp de plataforma |

Prova depois de configurar: `/health/notificacoes` em `Healthy`, e `POST /api/admin/notificacoes/disparo-teste`
(ou o teste de e-mail do Console) com `dkim=pass` no cabeçalho recebido.

## 5. Fora da #2074

- **Aviso de cobrança fora da janela.** `AvisoCobrancaConversa` recusa fora das 24 h antes de chegar ao provider.
  Para sair por modelo, precisa ir pelo outbox; issue própria.
- **Stub do atendimento grava `Enviado`.** `StubWhatsAppCloudClient` devolve `wamid-stub` e a mensagem fica como
  enviada. Registrado pela sessão da ponte (#2072) como follow-up.
- **Boas-vindas com senha temporária** em `CriarTenantPorAdminUseCase` (HTML montado em string, senha no corpo). O
  Admin está congelado; vale trocar por convite quando voltar.
- **Loader morto** `EasyStock.Application/Resources/EmailTemplateLoader` e `cadastro_usuario_confirmacao.html`:
  remover pede mexer no `.csproj` (tier alto).
- **Coletor de produto vencendo** continua desligado (data em UTC e `CorrelationId` de 65 caracteres). O template já
  casa com o payload.
- **E-mail ao cliente final** (confirmação de pedido, recibo) não existe: o cliente da Casa da Baba é atendido pelo
  WhatsApp. Só faz sentido como plano B se o WhatsApp cair; decisão do Felipe.
