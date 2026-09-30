# Onda 8 — Multicanal e sistema completo do protótipo (S34–S47)

Decisão: ADR-0051, que amplia o ADR-0050. Issue: #1060.
Origem: protótipo `michel-az-de/casa-da-baba-atendimento`, em `prototipo-omni/src`, rodada 10 de 26/09.

O protótipo não tem backend. A `api-falsa` (`servidor/agente.mjs`) só serve o agente de IA. O
contrato sai das ações do reducer (`aplicacao/acoes.js`, `aplicacao/casos/*.js`), e cada spec
abaixo cita as ações que cobre.

Os endpoints ficam em `api/atendimento/...` e respeitam a flag `modulo.atendimento` (S01).

> **Estas specs são mais curtas que as das ondas 1 a 6.** Trazem Problema, Abordagem, Escopo,
> Aceite, Testes e Leitura mínima, e cada sessão executora mede os caminhos antes do Red. Se algum
> caminho citado não existir, **PARE e reporte** (R10). Não é para inventar o arquivo.

## Ordem

```
S03 ─► S34 (porta de canal + identidade) ─► S05 ─► S35 (Instagram/Messenger) ─► S36 (chat do site)
                                   │
                                   ├─► S37 (e-mail/SMS saída) ─► S38 (consentimento) ─► S39 (programada)
                                   │
S08 ─► S40 (expediente) ─► S42 (respostas + automações)      S07 ─► S41 (atendentes) ─► S43 (lembretes)
S12 ─► S44 (entregadores/viagens) ─► S46 (lote de papel)      S06 ─► S47 (assistente)   S45 (cadastros) livre
```

**Caminho crítico do go-live:** S34 precisa entrar antes do S05, porque muda a chave da identidade
do cliente. As demais fatias correm em paralelo às ondas 2 e 3 conforme as dependências acima.

---

### S34 · Canal como porta e identidade do cliente por canal

**Problema.** `Conversa.ContatoWaId` (`Conversa.cs:35`) e `CanalConversa { WhatsApp = 1 }` amarram a
conversa ao WhatsApp. No go-live entram Instagram, Messenger, chat do site, e-mail e SMS (ADR-0051,
itens 1 a 4).
> **Entregue na #1065, com abordagem mais simples que a primeira versão desta spec.** A entidade
> `IdentidadeContato` ficou para a S05. Ligar várias identidades a um mesmo cliente é o problema
> dela, e na S34 a tabela seria só migration sem consumidor. O texto abaixo descreve o que foi feito.

**Abordagem.**
- `CapacidadesCanal` (Domain, `ValueObjects/`) diz, por canal, se há janela e de quantas horas, se
  aceita modelo, quais tags valem fora da janela e por quantos dias, que mídia aceita e se aceita
  botões. Canal não declarado lança exceção.
- A `Conversa` troca `ContatoWaId` por `ContatoIdExterno`, normalizado por canal: dígitos E.164 no
  WhatsApp e no SMS, minúsculas no e-mail, id opaco nos demais. O índice aberto passa a ser
  `(EmpresaId, Canal, ContatoIdExterno)`.
- `DentroDaJanela24h` vira `DentroDaJanela(agora)`, que lê as capacidades do próprio canal.
  `GarantirPodeEnviarTextoLivre(agora, tag?)` recusa no domínio.
- Porta `ICanalMensageria` (`App/Ports/Output/Atendimento/`), `ResolvedorCanal` (lança
  `CanalNaoSuportadoException`, que é o `CANAL_DESCONHECIDO` do protótipo) e adaptador `CanalWhatsApp`
  sobre o `IWhatsAppCloudClient`.
**Escopo.** Enum, VO, `Conversa`, configuração EF e migration. **A migration é escrita à mão como
`RenameColumn`**, porque o EF gera drop mais add e apagaria o contato. Também repositório, webhook
(R8), porta, resolvedor, adaptador e DI.
**Fora.** Adaptadores dos outros canais (S35–S37). Gravar `StatusMensagem.Falhou` quando o canal
não tem adaptador fica com o use case de envio do console (S07), que ainda não existe.
**Aceite.**
- [x] Conversas existentes migram sem perda (`MigrationS34_PreservaContatoDasConversasExistentes`).
- [x] Mensagem do WhatsApp continua entrando pelo webhook (testes do use case do webhook).
- [x] Envio num canal sem adaptador lança `CanalNaoSuportadoException`.
- [x] Texto livre com a janela vencida e sem tag válida é recusado no domínio.
**Testes (Red).** `ConversaTests.DentroDaJanelaUsaCapacidadeDoCanal`, `...Abrir_NormalizaContatoConformeOCanal`,
`...TextoLivreForaDaJanela_SemTag_Recusa`, `...InstagramComHumanAgent_PermiteAteSeteDias`,
`ResolvedorCanalTests.CanalSemAdaptadorLanca`, `CanalWhatsAppTests`,
`ConversaRepositoryIntegrationTests.MesmoIdEmCanaisDiferentesSaoContatosDiferentes` e `...MigrationS34_PreservaContatoDasConversasExistentes`.
**Rollback.** Migration `Down`, que volta o nome e o índice e recusa rodar se houver conversa de
outro canal, em vez de apagar dados.
**Depende de.** S03 (PR #1053).
**Leitura mínima.** `Domain/Entities/Atendimento/Conversa.cs`, `Domain/Enums/Atendimento/CanalConversa.cs`,
`Postgre/Data/Configurations/Atendimento/ConversaConfiguration.cs`, `App/Ports/Output/Atendimento/IWhatsAppCloudClient.cs`,
e o parser e o use case do webhook da PR #1053.
**Tamanho.** G, fatiado em 3 commits: domínio, migration e porta. **Tier.** alto.

---

### S35 · Instagram Direct e Messenger

**Problema.** O protótipo atende o Instagram (`catalogo.js:15-61`: janela de 24 h, sem modelo, foto e
áudio). O Felipe incluiu Facebook, ou seja, Messenger (ADR-0051, item 2).
**Abordagem.** O webhook da Meta já assina com o `AppSecret`. O parser passa a aceitar
`object: "instagram"` e `object: "page"` (`entry[].messaging[]`: `sender.id`, `recipient.id`,
`message.mid`, `message.text`, `message.attachments[]`, `postback.payload`). O roteamento do tenant
usa `recipient.id`, que é o ID da página ou o IG user id, gravado em `Empresa`, do mesmo jeito que o
`WhatsAppPhoneNumberId` (S01).
- Os adaptadores `CanalMessenger` e `CanalInstagram` enviam por `POST /{page-id}/messages`, com
  `recipient.id` e `messaging_type`.
- Fora da janela, os dois enviam só com a tag `HUMAN_AGENT` (até 7 dias) e só quando quem envia é
  humano. Pedir tag quando quem envia é o agente é recusado.
- **Conferir na documentação vigente da Meta, dentro da sessão, as tags ainda válidas e o prazo
  delas antes do Red.**
**Escopo.** `Empresa.FacebookPageId` e `Empresa.InstagramAccountId`, com índice único parcial, e
`MetaMessagingOptions` (o page access token vem de configuração, nunca de código). Parser, adaptadores
e registro no `ResolvedorCanal`. Mídia recebida passa pelo `ArmazenadorMidiaWhatsApp`, que será
renomeado para `ArmazenadorMidiaAtendimento` (R8).
**Fora.** Comentários de post e menções em story.
**Aceite.**
- [ ] DM de Instagram gera `Conversa(Canal=Instagram)` com a identidade IGSID. O mesmo vale para
  Messenger com PSID.
- [ ] Resposta pelo console chega ao cliente, verificado com payload gravado e stub HTTP.
- [ ] Com 25 h desde a última entrada: envio da dona sai com `HUMAN_AGENT` e envio do agente é
  recusado. Com 8 dias, ambos são recusados.
- [ ] Assinatura inválida devolve 401. Página desconhecida devolve 200 e loga, sem criar nada.
**Testes (Red).** `WebhookMetaParserTests.InstagramDm`, `...MessengerPostback`, `CanalMessengerTests.ForaDaJanelaUsaHumanAgent`,
`...AgenteNaoUsaTag`, `...OitoDiasRecusa`.
**Rollback.** Desligar a flag por canal (`atendimento.canal.instagram` e `atendimento.canal.messenger`)
e remover os adaptadores.
**Depende de.** S34 e a Onda 0.10.
**Tamanho.** G. **Tier.** alto.

---

### S36 · Chat do site

> **Entregue na #1097.** O stream lê do banco (o broker SSE que existe é em memória e só serve uma
> instância). O token vai no header, não na URL, então o widget usa `fetch` e não `EventSource`. A
> conversa do site vai para a fila humana: o agente envia direto pelo WhatsApp e só responde no site
> quando passar a enviar pela porta de canal. Vincular o cliente logado ficou para depois.

**Problema.** O protótipo tem o canal "Chat do site", sem janela e só com texto. O casadababa.com
hoje manda o cliente para o WhatsApp.
**Abordagem.** Canal próprio do EasyStok.
- `POST api/public/chat/sessoes`: é anônimo e devolve um token de sessão curto, preso à origem do
  storefront.
- `POST api/public/chat/mensagens` e `GET api/public/chat/stream` (SSE).
- O adaptador `CanalChatSite` publica no stream da sessão.
- Cliente logado no storefront vincula a identidade `ChatSite` ao `ClienteId`.
- Rate limit por IP e por sessão (existe middleware? medir). CORS só para as origens do storefront.
**Escopo.** Controller público, adaptador, sessão com TTL e limpeza pelo Worker. O widget no
casadababa.com fica em outro repositório, e aqui entra só o contrato documentado no OpenAPI.
**Aceite.**
- [ ] Visitante abre sessão, envia mensagem e a conversa aparece no console com `Canal=ChatSite`.
- [ ] A resposta do console chega pelo SSE em menos de 2 s, medido em teste de integração.
- [ ] Token de outra sessão devolve 403. Acima do limite devolve 429.
**Testes (Red).** `ChatSiteControllerTests.SessaoAnonima`, `...TokenAlheio403`, `...RateLimit429`,
`CanalChatSiteTests.PublicaNoStream`.
**Rollback.** Flag `atendimento.canal.chatsite` desligada.
**Depende de.** S34. **Tamanho.** M. **Tier.** alto (endpoint público).

---

### S37 · E-mail e SMS como canais de saída

> **Entregue na #1080, com escopo reduzido.** Os adaptadores ficam em `Infra.Notifications/Atendimento/`,
> ao lado dos provedores. O remetente por empresa e a validação no startup ficaram fora: os provedores
> já leem `Notifications:Sms:*` e `Email:*`, e com uma empresa só o remetente global basta.

**Problema.** A mensagem programada e as campanhas precisam de e-mail e SMS (ADR-0051, item 4). Hoje
`SmtpEmailCanal` e `TwilioSmsProvider` existem, mas estão inertes: o Stub é o único usado em
produção (#783).
**Abordagem.** Os adaptadores `CanalEmail` e `CanalSms` implementam `ICanalMensageria` e delegam
aos providers existentes. As capacidades são: sem janela, sem botão, e o e-mail aceita imagem por
link. O remetente fica em configuração por empresa.
- #783 é resolvida pela opção A: Twilio SMS, SMTP e Meta ficam, e Zenvia e `TwilioWhatsAppProvider`
  saem na poda (P0x).
**Escopo.** Adaptadores, `ConfiguracaoAtendimento.RemetenteEmail` e `.RemetenteSms`, e validação no
startup com a chave nomeada, igual ao S01.
**Fora.** Entrada de SMS e de e-mail (ADR-0051, Consequências).
**Aceite.**
- [ ] Mensagem com `Canal=Sms` sai pelo Twilio e `Canal=Email` sai pelo SMTP, verificado com stubs HTTP e SMTP.
- [ ] Provider ligado sem credencial derruba o startup nomeando a chave.
- [ ] Comentário de fechamento na #783.
**Testes (Red).** `CanalSmsTests.DelegaAoTwilio`, `CanalEmailTests.DelegaAoSmtp`, `...StartupSemCredencialFalha`.
**Rollback.** Flag por canal. **Depende de.** S34 e a Onda 0.11/0.12. **Tamanho.** P. **Tier.** baixo.

---

### S38 · Consentimento do cliente final por canal

> **Entregue na #1078.** O `ConsentiuMarketing` não recebeu `[Obsolete]` (o build trata aviso como
> erro). Ele fica documentado como legado até a poda. Opt-out por palavra: SAIR, PARAR ou STOP,
> sozinhas na mensagem. "quero sair do grupo" vai para o atendimento normal.

**Problema.** `Cliente.ConsentiuMarketing` (`Cliente.cs:87`) é um booleano único. A LGPD e as
políticas da Meta e da Twilio pedem opt-in por canal e por finalidade, além de opt-out simples.
**Abordagem.** Nova entidade `ConsentimentoContato (EmpresaId, ClienteId, Canal, Finalidade
{Transacional, Marketing}, Situacao {Concedido, Revogado}, Origem, Em)`.
- A mensagem de entrada "SAIR" (ou "PARAR", sem distinguir maiúsculas nem acentos) revoga o
  marketing daquele canal e confirma a revogação.
- Mensagem transacional dentro de um atendimento iniciado pelo cliente é permitida, conforme a
  regra de cada canal.
- Backfill: `ConsentiuMarketing=true` vira marketing concedido nos canais que o cliente já tem.
**Escopo.** Entidade, migration e backfill, `GET|PUT api/atendimento/clientes/{id}/consentimentos`,
e o serviço de domínio `PodeEnviar(cliente, canal, finalidade)` usado pelas S39 e S28–S31. O campo
antigo fica marcado `[Obsolete]` e só é removido na poda.
**Aceite.**
- [ ] "sair" no WhatsApp revoga o marketing do WhatsApp e mantém o do e-mail.
- [ ] `PodeEnviar` nega marketing sem consentimento e permite transacional.
- [ ] O backfill preserva quem tinha `ConsentiuMarketing=true`.
**Testes (Red).** `ConsentimentoContatoTests.SairRevogaSoOCanal`, `PoliticaEnvioTests.MarketingSemConsentimentoNega`, `...TransacionalPermite`.
**Rollback.** Migration `Down`. As campanhas voltam a ler o booleano.
**Depende de.** S34. **Tamanho.** M. **Tier.** alto.

---

### S39 · Mensagem programada ao cliente (multicanal)

> **Entregue na #1082, com duas decisões.**
> 1. A tag `HUMAN_AGENT` não vale para envio agendado: fora da janela, Instagram e Messenger recusam.
> 2. Uma mensagem presa em `Enviando` por queda do processo **não é reenviada sozinha**, para não
>    sair duas vezes. Ela fica para revisão, e a reentrega automática entra junto com os lembretes (S43).

**Problema.** Decisão 3 do ADR-0051. O protótipo teve "Programar envio" (`prototipo-casa-da-baba.html`,
build de 22/09) e ficaram restos no código atual: `PROGRAMAR_ENVIO`, `CANCELAR_PROGRAMADO` e
`DISPARAR_PROGRAMADO` em `aplicacao/acoesMidia.js:2-4`, e o selo "programada" em `Balao.jsx:61`.
**Abordagem.** Nova entidade `MensagemProgramada (EmpresaId, ClienteId, ConversaId?, Canal, Finalidade,
Conteudo | ModeloNome+Parametros, AgendadaPara, Situacao {Agendada, Enviada, Cancelada, Falhou},
Tentativas, CriadaPorUsuarioId)`.
- **A validação acontece ao agendar** e se repete no disparo, porque a janela pode ter vencido.
  Fora da janela, o WhatsApp exige modelo e Messenger e Instagram exigem tag válida (senão a
  mensagem é recusada). Marketing sem consentimento é recusado (S38). Instante no passado é recusado.
- O disparo roda num `BackgroundService` do Worker, com lock por linha (`FOR UPDATE SKIP LOCKED`) e
  idempotência pelo `Id`. O resultado vira uma `Mensagem(Saida)` na conversa com o selo `Programada`.
**Escopo.** Entidade, migration e `POST|GET|DELETE api/atendimento/mensagens-programadas` (o
`DELETE` cancela). O dispatcher e o campo `Mensagem.Programada` (bool) entram juntos.
**Aceite.**
- [ ] Mensagem programada para daqui a 1 min sai no canal certo e aparece na conversa com o selo.
- [ ] A validação na criação recusa: WhatsApp sem modelo com janela vencida, SMS de marketing sem
  consentimento e horário no passado.
- [ ] Mensagem cancelada não sai. Com dois Workers, a mensagem não duplica.
- [ ] A janela vence entre agendar e disparar: o disparo marca `Falhou` com o motivo e avisa a dona (S43).
**Testes (Red).** `MensagemProgramadaTests.RecusaWhatsSemModeloForaDaJanela`, `...RecusaPassado`,
`DisparadorMensagensProgramadasTests.NaoDuplicaComDoisWorkers`, `...JanelaVencidaNoDisparoFalha`.
**Rollback.** Desligar o dispatcher e remover a entidade.
**Depende de.** S34, S37, S38 e S09. **Tamanho.** M. **Tier.** alto.

---

### S40 · Expediente da loja (abrir e fechar)

> **Entregue na #1074, com um refinamento:** o checkout do site é sempre agendado (data e janela),
> então **só a pausa manual (`ForcarFechada`) devolve 409**. O horário não bloqueia o checkout. Ele
> governa o atendimento, com a mensagem de "fora do horário" do agente e das automações (S42). O
> expediente fica em `Domain/Entities/Storefront/ExpedienteLoja.cs`, com uma linha por empresa, no
> molde da S08.

**Problema.** O protótipo tem horário por dia, com virada da meia-noite e padrão de 08 a 22 h
(`dominio/funcionamento.js`), e um controle manual `lojaAberta` que pode ser `null`, `true` ou
`false`. O manual vence o relógio e não volta sozinho (`Moldura.jsx:287`, ações `ALTERNAR_LOJA` e
`EDITAR_FUNCIONAMENTO`). No EasyStok **não existe** nada disso: só `Storefront.Ativo`, `Loja.Ativa` e o
caixa.
**Abordagem.** Nova entidade `ExpedienteLoja (EmpresaId, Horarios[DiaSemana, Abre, Fecha], ControleManual
{Automatico, ForcarAberta, ForcarFechada}, AlteradoPor, AlteradoEm)`, em `Domain/Entities/Storefront/`.
O serviço `EstaAberta(agora, fuso America/Sao_Paulo)` é consultado:
- pelo agente (S06), que responde com a mensagem de "fora do horário" com `{abre}`;
- pelo checkout do site, que recusa o pedido com loja fechada, salvo agendamento para quando abrir;
- pelas automações (S42).
**Escopo.** Entidade e migration, `GET|PUT api/atendimento/expediente` e `POST api/atendimento/expediente/controle`,
evento `expediente.alterado` no SSE (S18) e a leitura de `EstaAberta` no `IniciarCheckoutUseCase`.
**Aceite.**
- [ ] Horário de 18 h às 02 h, consultado às 01 h do dia seguinte, está aberto (virada da meia-noite).
- [ ] `ForcarFechada` dentro do horário fecha e continua fechada no dia seguinte até voltar para `Automatico`.
- [ ] Checkout com a loja fechada devolve 409 com o próximo horário de abertura.
**Testes (Red).** `ExpedienteLojaTests.ViradaDaMeiaNoite`, `...ManualVenceRelogioENaoVolta`,
`IniciarCheckoutUseCaseTests.LojaFechadaRecusa`.
**Rollback.** Remover a entidade. O checkout ignora o expediente se a linha não existir.
**Depende de.** S08. **Tamanho.** M. **Tier.** alto (toca o checkout).

---

### S41 · Atendentes e atribuição de conversa

**Problema.** O protótipo fixa uma atendente (`reducer.js:48`) e muda o responsável ao escrever ou
assumir (RN-04). No EasyStok existe `Conversa.AssumidaPorUsuarioId` (`Conversa.cs:55`), mas falta
papel de atendente, transferência e o autor da mensagem.
**Abordagem.**
- A nova `Permissao.AtenderConversas` é concedida a um perfil "Atendente" semeado.
- `Mensagem.EnviadaPorUsuarioId` é preenchido em toda saída humana.
- `Conversa.Transferir(de, para)` exige que o destino tenha a permissão.
- A fila "Precisa de você" filtra por responsável ou sem responsável.
- `AutorMensagem.Dona` fica como está, porque o nome é de negócio (ADR-0011), e passa a significar
  "humano da empresa".
**Escopo.** Permissão, seed, `GET api/atendimento/atendentes`, `POST api/atendimento/conversas/{id}/transferir`
e o filtro `?responsavel=eu|ninguem|{id}` na listagem do S07.
**Aceite.**
- [ ] Usuário sem a permissão recebe 403 ao assumir.
- [ ] Transferir para quem não tem a permissão devolve 422.
- [ ] Mensagem enviada pelo console grava quem enviou.
**Testes (Red).** `ConversaTests.TransferirExigeDestinoAtendente`, `AssumirConversaUseCaseTests.SemPermissao403`.
**Rollback.** Remover o endpoint e a permissão. A coluna fica nula.
**Depende de.** S07. **Tamanho.** P. **Tier.** alto (auth).

---

### S42 · Respostas prontas e mensagens automáticas por gatilho

**Problema.** O protótipo tem a biblioteca de respostas com as variáveis `{nome}`, `{pedido}` e
`{faixa}` e com atalho (`dominio/respostas.js`, US-009). Tem também automáticas para primeiro
contato, fora do horário, loja fechada, pagamento confirmado, pós-entrega e encerramento
(`dominio/automacao.js:10-90`).
**Abordagem.**
- A nova entidade `RespostaPronta (EmpresaId, Titulo, Atalho único, Texto, Arquivada)` tem
  renderização de variáveis no servidor. Variável sem valor é recusada, para não sair `{nome}` literal.
- `RegraAutomatica (EmpresaId, Gatilho, Ligada, Texto)` usa um enum fechado de gatilhos. Cada gatilho
  é disparado pelo handler do evento que já existe, pelo outbox (ADR-0030), e respeita a janela e o
  consentimento do canal (S34 e S38).
**Escopo.** Duas entidades e uma migration, CRUD em `api/atendimento/respostas-prontas` e
`api/atendimento/automacoes`, e os handlers de `PrimeiroContato`, `ForaDoHorario`/`LojaFechada`
(lendo a S40), `PagamentoConfirmado`, `PosEntrega` e `Encerramento`. Não duplicar os avisos de status
do S13: o S13 continua dono dos avisos de esteira.
**Aceite.**
- [ ] Atalho duplicado devolve 409. Render com `{pedido}` numa conversa sem pedido devolve erro de validação.
- [ ] Primeiro contato fora do horário gera só uma automática (a de fora do horário, não as duas).
- [ ] Regra desligada não dispara.
**Testes (Red).** `RespostaProntaTests.RenderSemVariavelFalha`, `AutomacoesHandlerTests.ForaDoHorarioVencePrimeiroContato`, `...RegraDesligada`.
**Rollback.** Desligar as regras.
**Depende de.** S40, S09 e S13. **Tamanho.** M. **Tier.** alto.

---

### S43 · Lembretes da dona

**Problema.** O protótipo tem lembretes manuais e automáticos, quando uma passagem fica pendente,
quando um pagamento fica 15 min sem baixa e quando o cliente fica 10 min sem resposta
(`dominio/lembrete.js`, `Sininho.jsx`). São internos: nada sai para o cliente.
**Abordagem.** Nova entidade `Lembrete (EmpresaId, ConversaId?, PedidoId?, Tipo, Texto, VenceEm, Situacao,
ParaUsuarioId?)`. Um avaliador no Worker, a cada minuto, cria os automáticos a partir de consultas
e é idempotente por `(Tipo, Referencia)`. A notificação vai por Web Push e pelo SSE.
**Escopo.** Entidade e migration, avaliador, `GET|POST api/atendimento/lembretes`,
`POST .../{id}/concluir` e `POST .../vistos`.
**Aceite.**
- [ ] Pedido 15 min em `AguardandoPagamento` gera 1 lembrete, e só 1 mesmo com o avaliador rodando 3 vezes.
- [ ] Cliente sem resposta por 10 min numa conversa assumida gera lembrete, que some quando a dona responde.
**Testes (Red).** `AvaliadorLembretesTests.PagamentoSemBaixaIdempotente`, `...SemRespostaResolveAoResponder`.
**Rollback.** Desligar o avaliador.
**Depende de.** S07, S11 e S41. **Tamanho.** M. **Tier.** alto.

---

### S44 · Entregadores, viagens e chamado

**Problema.** O protótipo tem entregador (motoboy, plataforma ou próprio), viagem com paradas
ordenadas, rota no Maps, chamado de entregador e marcos de tempo (`dominio/viagem.js`,
`dominio/entrega.js`, ações `CRIAR_VIAGEM`, `POR_NA_VIAGEM`, `REORDENAR_PARADA`, `CHAMAR_ENTREGADOR`,
`SAIR_PARA_ENTREGA`, `MARCAR_PARADA_ENTREGUE` e `DESFAZER_VIAGEM`). RN-32: sem entregador resolvido,
nenhum aviso sai (US-040).
**Abordagem.** Três entidades novas:
- `Entregador (EmpresaId, Nome, Tipo, Empresa (propria | 99 | lalamove | ifood | outra), Telefone, Veiculo?, Placa?, Ativo)`;
- `Viagem (EmpresaId, EntregadorId?, Situacao, SaiuEm, Paradas[PedidoId, Ordem, EntregueEm])`;
- `ChamadoEntregador`, em texto livre e com situação.
"Sair para entrega" da viagem transita todos os pedidos (S12) e publica os avisos (S13) somente se
houver entregador.
Feedback da operadora (26/09/2026): o entregador de plataforma identifica o pedido pelo **número do
pedido**, não pelo nome do cliente nem pela comanda (interna), e com três "José" no mesmo dia é preciso
saber quem levou cada pedido. Por isso cada parada guarda um **retrato do entregador** no momento da
saída (`EntregadorNome`, `Veiculo`, `Placa`, `Empresa`), que não muda se o cadastro for editado depois.
**Escopo.** Entidades e migration, `api/atendimento/entregadores` (CRUD) e `api/atendimento/viagens`
(criar, incluir e retirar parada, reordenar, sair, marcar parada entregue, desfazer). O link de rota é
uma URL do Google Maps montada com os endereços, sem chave de API. O despacho devolve, por parada, o
número público do pedido, o entregador, o veículo e a placa. `GET api/atendimento/relatorios/entregas-por-bairro?de=&ate=`
(policy `Admin`): pedidos entregues e valor por bairro no período, ordenado do maior para o menor.
Integração com 99, Lalamove e iFood Entregas fica fora: o cadastro é manual e já tem os campos que a
integração vai preencher.
**Aceite.**
- [ ] Viagem com 3 paradas: "sair" leva os 3 pedidos para "saiu para entrega" e cada cliente recebe um aviso.
- [ ] Viagem sem entregador: "sair" é recusado com o motivo (RN-32).
- [ ] Cliente bloqueado impede o pedido de entrar na viagem (RN-14, igual ao protótipo `casos/entregas.js:192`).
- [ ] Editar a placa do entregador depois da saída não altera o retrato gravado nas paradas já despachadas.
- [ ] Relatório por bairro soma só pedidos entregues no período e respeita o tenant.
**Testes (Red).** `ViagemTests.SairSemEntregadorRecusa`, `...ClienteBloqueadoNaoEntra`, `...RetratoDoEntregadorNaSaida`, `SairParaEntregaUseCaseTests.AvisaCadaParada`, `EntregasPorBairroQueryTests.SomaSoEntreguesNoPeriodo`.
**Rollback.** Remover as entidades. A esteira volta a transitar pedido a pedido.
**Depende de.** S12, S13 e S24 (bloqueio). **Tamanho.** G. **Tier.** alto.

---

### S45 · Cadastros operacionais completos

> **Parte 1 entregue na #1095** (janelas, zonas e bloqueios; medido: não havia CRUD em controller
> nenhum). A parte 2, `CanalEmpresa`, espera S37–S39 entrarem, porque mexe no `ResolvedorCanal`.

**Problema.** No protótipo, canais, janelas com capacidade e zona por prefixo de CEP são só semente
(`catalogo.js:15,82,267`). No EasyStok, `JanelaEntrega`, `BloqueioEntrega` e `FreteZona` existem, mas
`AgendamentoController` e `FreteController` (Storefront) só têm `GET`. Não foi medido se há CRUD admin
em outro controller.
**Abordagem.** A sessão **mede primeiro** (`git grep` por `JanelaEntrega` e `FreteZona` em
`Api/Controllers`) e só completa o CRUD que faltar, reaproveitando o que houver no
`AdminStorefront*`. Os canais por empresa viram a configuração `CanalEmpresa (Canal, Ligado)`, lida
pelo `ResolvedorCanal` (S34) e pelas flags de canal.
**Aceite.** Criar, editar e desativar janela, zona e canal pela API, com `EmpresaId` no `WHERE` (ADR-0010).
**Testes (Red).** Um por endpoint que faltar.
**Depende de.** S34 (canais). **Tamanho.** P a M, conforme a medição. **Tier.** alto.

---

### S46 · Lote de papel (operação sem conexão)

**Problema.** Quando a conexão cai, a dona anota no papel e depois lança em lote (US-042,
`dominio/loteDePapel.js`, ações `LANCAR_LOTE_PAPEL` e `CONEXAO_CAIU/VOLTOU`).
**Abordagem.** `POST api/atendimento/esteira/lote` recebe `[{PedidoId, Passo, OcorreuEm}]` e aplica
na ordem de `OcorreuEm`, pela máquina de estados do pedido, gravando o horário real. Passo inválido é
reportado por linha, sem abortar o resto. Os avisos ao cliente **não** saem retroativamente, salvo o
último estado se ele ainda estiver dentro da janela.
**Aceite.**
- [ ] Lote com 5 passos, sendo 1 inválido: 4 são aplicados com o horário real e 1 é rejeitado com o motivo.
- [ ] Nenhum aviso retroativo é enviado.
**Testes (Red).** `LancarLotePapelUseCaseTests.AplicaEmOrdemEReportaInvalido`, `...SemAvisoRetroativo`.
**Depende de.** S12 e S44. **Tamanho.** P. **Tier.** alto.

---

### S47 · Assistente da dona

**Problema.** O protótipo tem dicas de CDC e pergunta livre (`dominio/assistente.js`,
`features/assistente/BalaoAssistente.jsx`), separadas do agente que fala com o cliente.
**Abordagem.** `POST api/atendimento/assistente` recebe a pergunta e o contexto opcional da conversa.
A chamada usa o cliente Claude existente (`GeradorAutoPreenchimentoClaude`), com system prompt próprio
e sem ferramentas de escrita. **Nada sai para o cliente.** Custo e latência são logados, como no
`agente.mjs` do protótipo.
**Aceite.**
- [ ] A resposta nunca vira `Mensagem` na conversa.
- [ ] Sem `Anthropic:ApiKey`, devolve 503 com a mensagem clara.
**Testes (Red).** `AssistenteDonaUseCaseTests.NaoGravaMensagem`, `...SemChave503`.
**Depende de.** S06. **Tamanho.** P. **Tier.** alto.

---

### S48 · Cardápio que o cliente marca, ligado à conversa

**Problema.** Feedback da operadora (26/09/2026): mandar só o link do cardápio obriga o cliente a
digitar o pedido no chat e a operadora a montá-lo. Ela quer uma página com fotos onde o cliente marca
itens e quantidades, como no iFood, e o pedido volta pronto para a conversa.
**Abordagem.** O site (casadababa.com) já tem cardápio, carrinho e checkout. Em vez de uma página nova,
o link enviado na conversa leva um token de curta duração (`?c=<token>`, 24 h, uso único após o envio)
que amarra o carrinho à `Conversa`. Ao enviar, o site chama a API, que cria o pedido pelo mesmo caminho
da conversa (S10), vincula a `Conversa.PedidoEmAndamentoId`, grava `Mensagem(Sistema)` com o resumo e
segue para a cobrança (S11) com a forma escolhida.
**Escopo.** `LinkCardapioConversaService` (gera e valida o token, sem PII na URL);
`POST api/storefront/cardapio-conversa/{token}/pedido` (anônimo, rate limit `public-post`); a ferramenta
`enviar_cardapio_imagem`/envio do cardápio (S06) passa a mandar o link com token; itens indisponíveis
chegam desabilitados (`ListarCardapioPublicoUseCase`).
**Aceite.**
- [ ] Pedido enviado pela página aparece na conversa certa com o resumo e o pedido vinculado.
- [ ] Token vencido ou já usado → 410 e a página orienta a pedir um link novo na conversa.
- [ ] A URL não carrega telefone, nome nem id interno legível.
**Testes (Red).** `LinkCardapioConversaServiceTests.TokenVencidoRecusa`, `...UsoUnico`, `CriarPedidoPeloCardapioConversaUseCaseTests.VinculaAConversa`.
**Depende de.** S06, S10 e S11. **Tamanho.** M. **Tier.** alto.
