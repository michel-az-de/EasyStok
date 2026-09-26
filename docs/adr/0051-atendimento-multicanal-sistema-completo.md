# ADR-0051 — Atendimento multicanal e o sistema completo do protótipo no go-live

- Status: Aceito
- Data: 2026-09-26
- Amplia: ADR-0050. Supersede os itens 5 (Instagram e widget fora de escopo) e 6 (sem Twilio). O restante do ADR-0050 continua valendo: Cloud API direto para o WhatsApp, inbox no console novo, "assumir" como estado do EasyStok, sem BSP no WhatsApp.
- Relacionados: ADR-0048, ADR-0030, ADR-0010; issue #1060; protótipo `michel-az-de/casa-da-baba-atendimento` (`prototipo-omni/`)

## Contexto

O plano da issue #1042 cobriu o que a entrevista pediu como núcleo, que é o WhatsApp mais pedido,
cobrança, esteira, CRM e campanhas. Depois disso, o protótipo do console (`prototipo-omni`, rodada 10,
26/09) passou a ter coisas que o plano não cobre:

| Feature do protótipo | Onde está no protótipo | No plano antes deste ADR |
|---|---|---|
| Três canais (WhatsApp, Instagram e chat do site), cada um com janela e mídia próprias | `infra/catalogo.js:15-61`, `dominio/canal.js` | Só WhatsApp |
| Abrir e fechar a loja: horário por dia e controle manual que vale por cima do relógio | `dominio/funcionamento.js`, `app/Moldura.jsx:287` | Ausente |
| Atribuição de conversa a uma atendente | `aplicacao/reducer.js:121-128` | Só o `usuarioId` opcional em `Conversa.Assumir` |
| Entregadores, viagens com paradas e chamado de entregador | `dominio/viagem.js`, `dominio/entrega.js` | Ausente |
| Respostas prontas com variáveis e mensagens automáticas por gatilho | `dominio/respostas.js`, `dominio/automacao.js` | Em parte no S06 e no S13 |
| Lembretes da dona (pagamento sem baixa, cliente sem resposta) | `dominio/lembrete.js` | Ausente |
| Lote de papel, para quando a conexão cai | `dominio/loteDePapel.js` | Ausente |
| Assistente da dona | `dominio/assistente.js` | Ausente |

Em 26/09/2026 o Felipe decidiu três coisas:

1. O sistema como um todo entra no plano.
2. O go-live sai com todos os canais.
3. A mensagem programada ao cliente fica e vale para todos os canais: e-mail, WhatsApp,
   Facebook, Instagram e SMS.

## Decisão

1. **O canal vira uma porta.** O WhatsApp passa a ser um dos adaptadores de `ICanalMensageria`.
   Cada canal declara o que suporta: se tem janela e de quantas horas, se aceita modelo aprovado,
   que mídias aceita e se aceita botões. Isso espelha a tabela de canais do protótipo.
   `CanalConversa` ganha `Instagram`, `Messenger`, `ChatSite`, `Email` e `Sms`. Canal que não está
   declarado recusa o envio.
2. **Instagram Direct e Messenger entram pela mesma app da Meta.** O webhook é o mesmo, assinado
   com o mesmo `AppSecret` (objetos `instagram` e `page`), e o envio usa a Send API da Graph. As
   duas redes têm janela de 24 h. Fora dela, só tag de mensagem permitida pela Meta, por exemplo
   `HUMAN_AGENT` por até 7 dias. **A regra vale no domínio:** nada fora da janela sai sem a tag
   válida.
3. **O chat do site é um canal próprio do EasyStok.** O widget em casadababa.com usa uma API pública
   com token de sessão anônima e recebe a resposta por SSE. Esse canal não tem janela.
4. **E-mail e SMS são canais de saída**, usados por aviso, mensagem programada e campanha. O e-mail
   sai pelo `SmtpEmailCanal` já existente. O SMS sai pelo `TwilioSmsProvider` já existente, o que
   confirma a decisão de 08/08 (WhatsApp e SMS). O Zenvia e o `TwilioWhatsAppProvider` continuam
   saindo na poda. A entrada de SMS e de e-mail fica fora do go-live (ver Consequências).
5. **Mensagem programada é entidade do atendimento.** `MensagemProgramada` guarda cliente, canal,
   conteúdo ou template e o instante. O Worker faz o disparo. Ao criar, o domínio valida a regra do
   canal: fora da janela o WhatsApp exige template, e Messenger e Instagram exigem tag. E-mail e SMS
   exigem consentimento do cliente final naquele canal.
6. **O consentimento do cliente final passa a ser por canal e por finalidade** (transacional ou
   marketing), com opt-out pela palavra "SAIR". Esse modelo substitui o booleano
   `Cliente.ConsentiuMarketing`. As campanhas (S28–S31) passam a filtrar por ele.
7. **As features operacionais do protótipo viram fatias do backend.** São elas: expediente da loja,
   atendentes e atribuição, entregadores e viagens, respostas prontas e automações, lembretes, lote
   de papel, assistente da dona e os cadastros que no protótipo são só semente (canais, janelas e
   zonas). As fatias estão em `docs/plan/atendimento-whatsapp/09-sistema-completo.md` (S34–S47).

## Alternativas consideradas

- **Manter só WhatsApp no go-live** (ADR-0050 item 5). O Felipe descartou em 26/09.
- **Voltar para um BSP multicanal** (Huggy, Zenvia, Twilio Conversations). Traria de volta os
  quatro problemas que o ADR-0050 apontou, mais a segunda tela. Instagram e Messenger saem da mesma
  app da Meta que já será verificada para o WhatsApp.
- **Chatwoot como inbox multicanal.** É mais um sistema para operar. A inbox continua no console.

## Consequências

- **O go-live passa a depender de mais itens da Onda 0.** São eles: Página do Facebook e conta
  profissional do Instagram vinculadas ao mesmo Business, permissões `pages_messaging`,
  `instagram_manage_messages` e `pages_manage_metadata` na app (é provável que exijam acesso
  avançado por App Review, a confirmar na S35), conta Twilio com número remetente para SMS, e
  domínio de e-mail com SPF, DKIM e DMARC.
- **O S04 e o S05 mudam de chave.** A identidade do cliente deixa de ser só o `wa_id` e passa a ser
  `(Canal, IdExterno)`. Um cliente pode ter várias identidades (telefone, PSID do Messenger, IGSID
  do Instagram, sessão do site, e-mail). O S34 faz a migração antes do S05.
- **O prazo do go-live cresce.** As 14 fatias novas somam cerca de 3 ondas. A tabela de ordem
  está no README do plano.
- **Resposta do cliente por SMS e por e-mail** não abre conversa no go-live. Se houver demanda,
  isso vira fatia nova: webhook de entrada da Twilio e caixa de entrada por IMAP ou webhook do
  provedor de e-mail.
- **Se um canal mudar**, só troca o adaptador de `ICanalMensageria` e o parser do webhook dele.
  Conversa, agente, pedido e avisos continuam iguais.
