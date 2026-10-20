# Changelog

Todas as mudancas relevantes deste projeto sao documentadas aqui.
Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/).

## [Unreleased]

## [1.11.0] - 2026-10-20

Primeira versão operável da Casa da Baba (#1889): a jornada "pedido a dinheiro" de ponta a ponta no
console, com o que está fora dela congelado (some da tela, o código fica em `dominio/congelados.js`).
Jornada: abrir loja e caixa, atender pelo WhatsApp e pelo chat do site, montar o pedido, cobrar por link
do Mercado Pago ou na entrega, Cozinha, entrega própria e fechamento do caixa. Congelados: Campanhas e
fidelidade, financeiro gerencial (fica o Caixa do dia), canal de e-mail e cotação de transportadora.
O agente automático nasce desligado. Testes de integração da API voltaram ao CI.

### Removed
- `AdminStorefrontController` (`api/admin/storefronts`) e `AdminNotificacoesController`
  (`api/admin/notificacoes`), com `ListarStorefrontsAdminUseCase` e `EnviarNotificacaoManualUseCase`.
  Broadcast de plataforma e consulta de consentimentos por usuário saem sem substituto. O
  `AdminTenantsController` fica: é o único caminho de criar empresa. (#1173)
- **Tabelas sem consumidor** (P06): a migration `RemoverTabelasSemConsumidor` derruba 19 tabelas que
  as P01, P03, P04 e P05 deixaram sem código: helpdesk (`admin_tickets`, `admin_ticket_mensagens`,
  `ticket_anexos`, `ticket_historico`, `admin_ticket_tecnico_meta`, `sla_configuracao`), FAQ
  (`faq_categorias`, `faq_itens`, `faq_visualizacoes`, `faq_feedbacks`), `banners`,
  `banner_confirmacoes`, `leads_publicos`, `anuncios_ia`, `admin_impersonation_logs`,
  `admin_acessos_pii_logs`, `admin_notas_tenant`, `apk_releases` e `empresa_configuracao_fiscal`, com
  entidades, configs EF e DbSets. `faturas.TicketRelacionadoId` perde a FK e fica como coluna solta.
  O `Down` recria as tabelas vazias. Billing, `Nfe*`, `admin_audit_logs` e os campos fiscais do
  storefront ficam: ainda têm consumidor. (#1207)
- O remetente padrão `noreply@easystock.com`, que é de um domínio fora do produto, e o `smtp.gmail.com` do `appsettings.json`
  da API. Nenhum remetente nem servidor é mais inventado em código. (#1351)
- O POST morto em `/api/ci/tickets` (e o `Ci:AutoTicketKey`) do `EndpointHealthMonitorService`, e o cooldown de 24 h.
- Ramo cron do avaliador de notificações (só logava "matched"), `RotinaScheduler` e o pacote `Cronos`.
- **Job legado do link de avaliação +24 h** (#1519): nunca enviou (a varredura esbarrava na RLS) e, consertado, mandaria um segundo pedido de avaliação com link para uma página que o site não tem. O pedido de avaliação continua saindo pelo outbox, com botões, 30 min após a entrega (S26).
- **`obterItemCardapio`** (#1563): ação sem chamador que devolvia `null` no erro; a ficha do prato vem de `lerFichaDoPrato`.
- **99 simulada do modo demonstração** (#1691): saiu de `provedoresDeEntrega.js` e do cadastro de provedores; nenhuma tela mostra mais cotação ou corrida inventada da 99.
- **Deploys mortos** (#1890): workflows e arquivos de Render, Fly.io, Azure (compose, pipeline,
  script da VM, `deploy-verify`) e k8s saem do repositório, junto com o job que retagueava a imagem
  do Admin. A VPS é publicada só por `scripts/deploy/vps-deploy.sh` e `console-deploy.sh`.
- **Código morto do console** (#1891): `acoesMidia.js`, `useComposer.js`, `SecaoEmail.jsx` com o CSS e `useCaixaEmailApi.js`, os SVGs de `assets/icones-marca-v4`, `react.svg`, `vite.svg` e `hero.png` (a prova 1841 passa a usar `casa-da-baba.png`).
- **Lixo versionado sai do repositório** (#1892, refs #1889): respostas de API e health na raiz, protótipos v6, `.build/apiobj/`, 21 SQL avulsos de maio e `scripts/run-tests-wsl.sh`, que apontava para um worktree morto.
- **Painel `EasyStock.Admin` e controllers `Admin*`** (poda P01): saem o projeto Admin e seus testes,
  19 controllers `Api/Controllers/Admin*` e o `FaturasPdfController` (77 endpoints; 89 com os 12 que
  saem do `AdminTenantsController`), com os use cases, portas, repositórios e relatórios que só eles usavam (faturas do admin, MRR/inadimplência,
  dashboard, busca global, frota, status, configurações do sistema, cupons, planos, APK, seed de
  cenários, helpdesk do cliente). Na Web sai o `POST /auth/impersonate` (handoff que só o Admin
  chamava). Deploy: sem serviço `admin` no compose Azure, Caddy, Render, Fly e na matriz do
  `build-images.yml`; `vm-deploy.sh` e scripts de verificação sem o container `easystok-admin`.
  Ficam as rotas de plataforma sem substituto: `AdminTenantsController` enxuto (criar empresa,
  consultar, módulos, vínculo do número da Meta), `AdminStorefrontController` e
  `AdminNotificacoesController`. Tabelas ficam para a P06. (#1169)

### Fixed
- **Template global de notificação no escopo da empresa**: `TemplateNotificacaoRepository.GetAtivoAsync`
  com empresa nula lê o global com bypass de RLS; antes o filtro de tenant e a policy
  `tenant_isolation` o escondiam de quem rodava no escopo de uma empresa. (#1202)
- `vps-deploy.sh` grava o dump em `/home/felipe/backups` por padrão e cria a pasta: `/opt/backups` é do root e o usuário do deploy não escreve lá. (#1223)
- fix(infra): `PollingOutboxSignaler` aceita `TimeProvider`; teste do tick agora é determinístico com `FakeTimeProvider` e saiu do inventário de flaky (#1225).
- **Início previsto fora do Mercado Pago** (S21): pedido que entra na fila por pagamento na entrega, troca de
  status no console, criação no ERP ou na sync do mobile (com agendamento) e pagamento manual (Pix, dinheiro,
  cartão) passa a gravar o início previsto; o KDS deixa de usar a regra provisória para ele e o aviso de atraso
  sai. Um teste de arquitetura barra caminho novo que mude status de pedido sem gravar o início previsto. (#1230)
- **Pix do Mercado Pago expira junto com o link** (#1233): a preferência do Checkout Pro passa a enviar
  `date_of_expiration` além de `expiration_date_to`. Antes, um QR Pix gerado aos 29 minutos continuava
  pagável depois de o pedido ser cancelado por falta de pagamento.
- **Pagamento que chega depois do cancelamento é devolvido na hora** (#1233): o webhook estorna o valor pelo
  Mercado Pago (chave de idempotência `estorno-tardio-{pagamento}`, uma vez só mesmo com notificação repetida)
  e avisa o cliente na conversa. Antes, o motivo era gravado e ninguém era avisado.
- **Modo API do console honesto** (F06): Encerrar pelo modal fecha a conversa no EasyStok
  (`POST .../encerrar`) e manda a despedida pelo envio real. Foto no WhatsApp sai pelo S02
  (multipart). Áudio, arquivo, figurinha e peça da galeria avisam, assim como as 91 ações ainda sem
  endpoint, listadas em `aplicacao/api/naoLigadas.js`. Nenhuma delas mexe mais só na memória do
  navegador. O aviso fica na faixa até a dona fechar. No modo API não aparecem Simular, a contagem
  da massa em Automáticas nem o avatar fixo. O canal entre janelas fica desligado e
  `#/cardapio-link` pede login. Na Gestão, Produção, Caixa, Janelas, Fidelidade e Integrações
  abrem com a faixa "ainda não ligado" e os controles desabilitados. Prova:
  `ferramentas/prova-f06-honestidade.mjs`. (#1236)
- **Console no modo API sincroniza de verdade** (F07): o relógio relê a hora real a cada tique e
  ao voltar para a aba; a conversa aberta tem as mensagens relidas a cada ciclo (Falhou e Entregue
  aparecem); a pausa diz quem assumiu (você, outro atendente ou o automático, que vira "Passou para
  você" com `motivoEscalada` quando a API mandar); `Entregue` deixa de virar `lida`; cobrança online
  sem meio anterior é link, e a troca de forma que falha recarrega o pedido; aviso 10 min antes do
  JWT vencer e rascunhos por conversa guardados até o novo login; lista paginada e GET de pedido só
  para a conversa aberta, as que mudaram e, nas outras, a cada minuto; datas em
  `America/Sao_Paulo`; debounce do horário cancelado ao desmontar; canhoto pelo `chamarApi`, em
  janela sem `opener` por Blob URL. (#1237)
- **Console pronto para publicar em modo API** (F13): `Dockerfile.web` aceita `VITE_FONTE_DADOS`/`VITE_API_BASE`,
  o nginx tem o destino da API configurável (`API_UPSTREAM`) e repassa o SSE sem buffer; o operador comum
  não vê mais "sem permissão" ao abrir o console, e abrir ou fechar a loja diz que é do administrador e
  desfaz a troca na tela. Roteiro de go-live em `docs/plan/atendimento-whatsapp/12-go-live.md`. (#1243)
- **Cota do botão Testar por loja**: o limite de taxa rodava antes da autenticação e lojas atrás do
  mesmo IP dividiam a cota. (#1246)
- **Site sem tenant em requisição anônima**: as rotas `api/storefront/{slug}/...` passam a fixar o tenant
  pelo slug (`[TenantDoStorefront]`). Antes, o filtro global e a RLS zeravam a leitura e o login por OTP
  nunca achava o código, a sessão recém-criada não valia, "meus pedidos" dava 401, a segunda compra
  sem login do mesmo telefone dava 500 e a lista de avaliações vinha vazia. O job que expira sessões
  do site também volta a apagar. (#1259)
- **Cobrança no console sem pedido fingido** (modo API): gerar ou reenviar a cobrança antes do envio
  cria o pedido no EasyStok; marcar pago, comprovante, estorno, cancelar e esteira avisam até o
  pedido existir, em vez de mostrar "Pago" só no navegador. (#1271)
- **Backup externo na primeira execução real** (#1256): `instalar.sh` aceita o `rclone.conf` gerado no Windows (CRLF);
  `enviar.sh` não sai mais com exit 1 depois de enviar (trap com variável local); `testar-restauracao.sh` espera o
  postgres descartável por TCP, sem cair no reinício do init. Validado na VPS: 157 tabelas restauradas do Drive. (#1275)
- **Entrada dos webhooks da Meta** (#1285): o webhook de Instagram e Messenger usa o rate limit `webhook-meta`
  em vez do `public-post`; mensagem apagada ou sem suporte e `mid` acima de 128 caracteres não fazem mais a Meta
  reenviar para sempre (o `mid` longo vira `h:` + SHA-256); nota de voz, vídeo e documento recebidos são
  guardados no storage privado com allowlist própria do atendimento, sem afrouxar o upload público; legenda e
  localização chegam ao texto da mensagem; reação, mensagem sem suporte e evento de sistema não acionam o agente;
  conversa aberta por botão `acao:` não recebe saudação nem a automática de entrada.
- **Console no modo API: pedido, cobrança, envio e sincronização**: pedido criado sem link avisa e "Gerar cobrança" emite o link; duplo clique não cria dois pedidos; recarga que falha não diz "Pedido não criado"; balão não duplica nem some na corrida com a polling; modelo e automática não viram texto livre; primeiro sync sem som em massa; número do pedido igual ao do cliente; Enter vazio não envia; encerradas não carregam mensagens na entrada; foto, áudio e arquivo do cliente aparecem no balão (`GET api/atendimento/conversas/{id}/mensagens/{mensagemId}/midia`). (#1287)
- **Turno do agente de atendimento robusto** (#1288): timeout do LLM, de ferramenta ou do envio com o
  turno vivo escala ou registra a falha em vez de abortar em silêncio; mensagem do cliente que chega
  durante o turno (rajada) é respondida no turno seguinte, via `Mensagem.ProcessadaEm`; se a dona assume
  enquanto o LLM responde, o agente não envia nem escala; LLM desligado ou canal sem agente passa a
  conversa para a dona, e "liberar automático" é recusado em canal sem agente (409).
- **Recusa de pedido já pago devolve o dinheiro**: a recusa estorna na hora a cobrança paga no Mercado Pago
  (chave idempotente), libera a vaga, publica `pedido.mudou_status` e avisa o cliente na conversa; os três
  eventos sem handler deixaram de ser publicados. Estorno recusado responde 502 e o pedido continua aguardando. (#1289)
- **Segundo pagamento do mesmo pedido é estornado**: com o pedido já pago, o novo pagamento é devolvido
  (`estorno-duplicado-{pagamento}`), sem segundo registro nem canhoto; o webhook deixou de responder 500 em loop. (#1289)
- **Pix vencido não pede para tentar de novo no mesmo link**: `cancelled` de cobrança vencida ou com
  `status_detail = expired` não manda o link morto ao cliente. (#1289)
- **Telefone e conversa nos avisos e programadas do WhatsApp**: a conversa aberta passa a ser achada
  com e sem o nono dígito (o `wa_id` antigo da Meta não tem o 9; o cadastro tem), então o aviso dentro
  da janela sai como texto com botões e entra no histórico em vez de virar template pago. Falha ao gravar
  o histórico depois do envio não reenvia mais a mensagem. Programada presa a conversa encerrada usa a
  conversa aberta atual do contato. `5511997573992` (como o cadastro grava) vale como telefone, a busca por
  telefone olha também a lista de telefones do cliente, e o cliente do ERP achado pelo WhatsApp ganha o
  `TelefoneHash`, o que evita cadastro duplicado no login do site. (#1290)
- **Frete do checkout igual ao da cotação**: o checkout do site e o da conversa cobram o frete pelo mesmo
  cálculo do `POST /frete/calcular`, com raio quando a loja tem config e zona quando não tem. Loja só com raio
  deixa de recusar todo checkout com 422. (#1291)
- **Idempotência do checkout logado**: a resposta registrada passa por commit, e o retry com a mesma
  `X-Idempotency-Key` devolve o mesmo pedido. (#1291)
- **Link do Mercado Pago**: item de quantidade fracionária (1,5 kg) vai como 1 unidade com o subtotal. A
  cobrança pendente só é reaproveitada quando o valor ainda é o total do pedido; se o total mudou, ela é
  cancelada e sai outra. (#1291)
- **Pedido abandonado sem cobrança**: a varredura de 30 minutos agora alcança o guest do site e o pedido da
  conversa, com bypass de RLS na leitura e escrita no tenant do pedido, e a vaga deixa de ficar presa. (#1291)
- **Cliente bloqueado** não fecha pedido pelo site (guest e logado), como já acontecia na conversa. (#1291)
- **Troca para pagar na entrega** respeita `RequerAprovacao`: lead liberado fora de área vai para a aprovação
  da Babá, não direto para a cozinha. (#1291)
- **Envio ao cliente** (#1292): o `POST /messages` da Meta não é mais repetido pelo retry (mensagem
  duplicada após timeout); empresa sem número de WhatsApp vinculado não envia pelo número global; aviso de
  preparo e de saída respeita os avisos desligados pelo cliente; aviso de reembolso ganha telefone no payload
  e rotina com template no seed; encerrar conversa já encerrada não repete a automática; automática não sai
  para cliente bloqueado; logs de SMS e e-mail sem telefone ou endereço; nota registrada pelo agente sai
  identificada no resumo do dossiê; limite semanal de campanha conta quem está na fila de outra campanha.
- **Vaga presa quando o Mercado Pago falha** (#1301): se a primeira cobrança do pedido não sai (erro ou
  timeout de 5 s), o pedido é cancelado com motivo `mercado_pago_indisponivel` e a vaga da janela volta,
  nos cinco caminhos que criam pedido com cobrança (site logado, site sem login, `criar_pedido` da
  conversa, comanda do console e cardápio da conversa). O site segue respondendo 503; a comanda e o
  cardápio da conversa passam a responder 503 em vez de prometer um link que nunca chegava, e o cardápio
  devolve o link para o cliente reenviar.
- **Checkout sem login do site não quebra antes da mudança do site** (#1306): `janelaId` e `dataEntrega` voltam a
  ser opcionais em `POST api/storefront/{slug}/checkout/guest`. Sem os dois, o pedido segue o modo antigo
  (aprovação da dona, sem vaga e sem cobrança); com os dois, reserva a janela e cobra pelo Mercado Pago (#1254).
  Só um dos dois devolve 422. Ponte temporária, sai quando o site publicar o passo de janela.
- **Teste da avaliação 30 min** deixou de depender da data fixa 2026-10-01 13:00 UTC, que derrubou o build do master quando o relógio passou dela. (#1307)
- **Tag do cliente** (#1311): tag nova (ex.: restrição registrada pelo agente) era gravada como UPDATE de 0 linhas e
  derrubava o turno inteiro do agente com conflito de concorrência, deixando o cliente sem resposta. Chave da
  `ClienteTag` agora é `ValueGeneratedNever` (ADR-0028); migration sem mudança de schema.
- **Alergia no atendimento**: `consultar_cardapio` passa a levar alérgenos e ingredientes da ficha do item ao agente, e o prompt só responde alergia pelo que a ficha diz; sem o alérgeno na ficha, o agente nunca garante ausência e escala para a dona. (#1314)
- **Superadmin no console pelo Google** (#1326): com `Auth__Google__EmpresaPadrao` (CNPJ ou nome), o superadmin entra nessa empresa (nível SuperAdmin mantido); antes o console recusava token sem empresa.
- **Respostas da IA curtas e sem travessão** (#1330): o texto do agente sai para o WhatsApp sem travessão nem meia-risca (troca determinística por vírgula, marcador ou hífen), e o prompt limita a resposta a 3 frases curtas e proíbe o travessão.
- **Resposta no número certo** (#1332): conversa achada pela outra grafia do celular (com/sem o nono dígito) passa a responder no `wa_id` que escreveu; antes a Meta recusava com 131030.
- **Unidades do cardápio em minúsculo** (#1334): vitrine e agente mostram "500 g" e "250 ml", não "500 G" e "250 Ml".

- **Deploy recusa SHA antigo** (#1336): `scripts/deploy/vps-deploy.sh` faz `git fetch` e recusa (exit 7) commit que não seja o `origin/master` atual nem descendente dele; voltar a versão anterior só com `--rollback <sha> --motivo "..."` e confirmação (exit 8 sem isso). Cada deploy grava data, modo, SHA, resultado, quem e motivo em `/home/felipe/build/deploy-history.log`. Incidente em `docs/dev/incidentes/2026-10-01-deploy-sha-antigo.md`.
- **Motivo real da falha de envio** (#1339): o balão do console mostra o que o canal respondeu (ex.: Meta #131030) em vez de só "Falha ao enviar pelo canal".
- **Superadmin escolhe a empresa no login por senha** (#1342): com `empresaId`, o token leva essa empresa (nível SuperAdmin mantido), o que libera as rotas da loja e do cardápio.
- **Vitrine pública em produção** (#1345): a busca da loja pelo endereço liga o bypass de RLS, como o login; antes o cardápio público dava 404 para toda loja (o Postgres local é superusuário e escondia o problema).
- **Verdade do envio das notificações** (#1350, N2): stub e console de e-mail deixam de virar `Enviado` sem nada ter saído. O outbox fica `Simulado` (sem `EnviadoEm`, log com o provider real e `Sucesso = false`) e nenhum deles loga telefone, e-mail nem corpo, só o `OutboxId`. A falha é classificada por protocolo: HTTP 4xx (menos 408 e 429) e SMTP 5xx são permanentes e não reagendam; HTTP 5xx, timeout, 408, 429 e SMTP 4xx são transitórios; em WhatsApp e SMS, timeout, queda de conexão e 5xx viram `Indeterminado` (terminal, sem reenvio e sem fallback, para não duplicar). Só o outbox repete: saem o Polly do e-mail e do Twilio e o laço de 3 tentativas do `SmtpEmailService`, que tratava o 550 como transitório (até 36 tentativas SMTP por mensagem). A categoria `Seguranca` apaga corpo, assunto e metadados ao terminar e o payload do evento quando termina a última mensagem aberta dele. Sem migration.
- **"Enviar cardápio" do console manda o link da loja** (#1353): no modo API o console pedia ao cliente que abrisse uma tela do próprio console, que sempre dava erro. Agora busca em `POST /api/atendimento/conversas/{id}/link-cardapio` o mesmo link do cardápio que o atendimento automático manda (vale 24 h).
- **Login sem botão do Google duplicado** (#1354): o botão é desenhado uma vez só, mesmo quando a tela de login redesenha; o objeto do login Google ficou com identidade estável.
- **Motor de notificações destravado sob RLS** (N1, #1367): Dispatcher, Avaliador, Coletor e anonimizador liam zero linhas porque o papel de produção é NOBYPASSRLS e nenhum loop ligava o bypass nem fixava tenant. O claim do outbox (`FOR UPDATE SKIP LOCKED`, com lease) e a lista do Avaliador passam pela porta `IRowLevelSecurityBypass`; cada mensagem e cada evento roda em escopo próprio, com o tenant da empresa, e a venenosa termina `Falhado` sem parar o lote. O catálogo global fica legível ao tenant por policy de SELECT (migration aditiva) e o kill switch vale também no envio. Backlog velho vira `Expirado` por prazo do tipo, `EmEnvio` tem lease (WhatsApp e SMS viram `Indeterminado`), o id do provider vai para `ProviderMensagemId`, só o Worker roda os loops, `/health/notificacoes` mede o backlog e o Worker pinga o Healthchecks.io.
- **Motor de notificações por tipo e canal, rotina da empresa e pausa por empresa** (N5, #1371): o template agora é achado pelo código da rotina e, sem ele no canal, por tipo e canal (`ITemplateRepository.GetAtivoPorTipoAsync`, empresa antes do global, maior versão), o que destrava o fallback de canal e o primeiro canal permitido que não é o primeiro da lista. A rotina da empresa vale antes da global (`SeletorRotina`; a da empresa sem canais não esconde a global) e `ListarAtivasAsync` ordena. O fallback usa o contato e os metadados do novo canal e preserva número e booleano do payload (`ConstrutorMensagemOutbox` único para o avaliador e o dispatcher). A pausa da empresa, por canal ou geral, passa a valer no avaliador e no envio, sem nunca segurar `Seguranca`; a pausa global segue calando tudo. O InApp que `Operacional` acrescentava sozinho só entra com template do tipo.
- **Atenção ao publicar:** as pausas de empresa ativas deixam de ser decorativas e passam a calar as notificações daquela empresa (inclusive avisos ao cliente final).

- **Esqueci a senha pelo motor, com link seguro e código no WhatsApp** (N8, #1385, fecha #301): `POST api/auth/forgot-password` responde sempre 202 com o mesmo corpo e não faz rede; enfileira um evento `ResetSenha` (categoria Segurança) na mesma transação e o modo `todos` entrega o link por e-mail (`seguranca@`, 30 min) e o código de 6 dígitos pelo WhatsApp de plataforma (10 min, 5 tentativas), só para conta com telefone verificado e opt-in, nunca superadmin (que recebe só o link). O `baseUrl` do corpo é ignorado: a base vem de `Auth:LinkRedefinirSenha` (ou da primeira origem confiável).
- **Uso único de verdade**: `ResetTokenRepository.ConsumirAsync` é um `UPDATE ... WHERE Usado = false AND ExpiraEm > agora` e só 1 linha afetada troca a senha (2 POST simultâneos: 1 sucesso); o código gasta a tentativa num `UPDATE ... RETURNING` antes de comparar (10 chutes em paralelo contam 5) e a comparação é em tempo constante. Cada pedido invalida os segredos anteriores e um reset concluído invalida todos.
- Limites: 5 pedidos por IP em 15 min (429 com `Retry-After`, também em `reset-password` e `reset-password-code`) e, por conta, 3 por hora, 6 por dia e 60 s entre pedidos (202 sem envio, auditoria `forgot-password-limitado`). IP e agente vão para `reset_tokens` e para o `AuditLog`. Nenhum log do fluxo leva e-mail, telefone, token ou código (inclui os logs do login que citavam o e-mail).
- O reset revoga as sessões pelo `RevogadorSessoes` (N7), zera o contador de falhas e avisa os canais verificados (`SenhaAlterada`).

- **Migração do reenvio** (#1387): o arquivo volta a ter o nome igual ao ID gravado no banco (`20261002071519_AddReenvioMensagens`).
- fix(backend): correções do code review (#1394): perfil SuperAdmin não atribuível por Admin de tenant, pareamento mobile exige Gerente, webhook Pix não engole falha, estorno de caixa e recebimento de fornecedor corrigidos.
- fix(atendimento): reenvio do console e agendamento após falha da Meta (#1396): falha no envio pelo console grava a mensagem como falhou e devolve o id real (o Reenviar não dá mais 404); callback `failed` da Meta com código temporário agenda o reenvio automático.
- **Anexo do WhatsApp com erro visível, nova tentativa e fila durável**: falha ao baixar a mídia fica gravada na mensagem e é tentada de novo até 3 vezes (1 min, 5 min); a pendência fica no banco e a varredura da partida retoma o que a fila em memória perdeu; a lista de tipos aceitos cobre os que a Meta envia (planilha, apresentação, zip); o console mostra "não foi possível baixar o anexo". (#1397)
- fix(estoque): estorno de saida desfaz descoberto e caixa ignora estornos; pedido baixa lotes FEFO elegiveis (#1399).
- **Cobrança pelo Mercado Pago voltava 400** (#1400): o `+` do fuso das datas da preferência ia escapado (`\u002B`) e o Mercado Pago respondia `error_parsing_date`; agora vai literal. Recusa do Mercado Pago passa a aparecer no log com o motivo.
- **Achados da revisão do canal WhatsApp** (#1411): timeout do HttpClient no envio pelo console grava a mensagem como falha; código desconhecido no callback `failed` da Meta não reenvia sozinho; só texto agenda reenvio automático; falha ou cancelamento da transcrição de áudio não marca erro de mídia num anexo já salvo.
- **Transcrição de áudio** (#1422): a Fireworks desativou o áudio (401); a transcrição passa ao faster-whisper próprio na VPS (`ez-whisper`, `scripts/deploy/whisper-deploy.sh`). `Transcricao:ApiKey` vira opcional (ADR-0058, emenda).
- Console e avaliador usam o expediente da empresa; tentativas pendentes/falhas e notas internas não encerram a espera. O sino no modo API usa também os automáticos persistidos, sem a segunda contagem local de 10 minutos.
- O Atendimento consulta o expediente e o SLA da inbox; edição de horários/mensagens/prazo continua da dona. Abertura/fechamento manual mantém a regra de gerente; capacidades ausentes não liberam controles.
- Gravação conserva o rascunho após falha ou consulta atrasada, impede envio concorrente e evita repetir o PUT do modelo de retomada quando ele não mudou.
- Serializa o recebimento por contato e relê a conversa sob trava para preservar mensagens não lidas em processos concorrentes; regressão com PostgreSQL real.
- **SuperAdmin sem empresaId na query** (#1434): a API usa a empresa do token quando o console não manda `?empresaId=`; Entregas, Cozinha e notas do cliente deixam de responder 400. Entregas não fica mais em "Carregando" quando a carga falha.
- **Nota interna e Histórico do cliente** (#1436): no console em modo API a nota interna grava em `POST /api/clientes/{id}/notas` e a Ficha relê o dossiê; Histórico mostra total de pedidos, últimos pedidos e notas reais em vez de zero.
- **Foto da galeria do cardápio pelo console** (#1437): o "Enviar" de "Fotos e peças" ficava "Não enviada: Failed to fetch" porque o navegador baixava a URL da foto (cross-origin, host antigo gravado). A peça agora vai por `POST api/atendimento/conversas/{id}/mensagens/imagem-cardapio` com `{ cardapioItemId, indice, legenda }`: o EasyStok lê a foto do storage pela chave, converte para JPEG e envia como a rota `/imagem`. Foto do computador segue em multipart. A miniatura da galeria deixa de usar `loading="lazy"`.
- **Faixa da sessão sem "· null"** (#1447): com a empresa sem nome, a faixa mostra só o usuário.
- **Lote do PWA não entra mais em dobro no estoque** (#1458): ao sincronizar um lote, o sync somava a quantidade no item de estoque já existente do produto e o vínculo do lote criava outro item com a mesma quantidade. Do 2º lote em diante, a produção contava duas vezes. Agora a entrada no ERP vem só do vínculo do lote (um item por lote, com validade). O saldo do aparelho continua somando localmente.
- **Descarte e exclusão de lote do PWA chegam ao ERP** (#1464): o servidor ignorava o lote reenviado e respondia "aceito". O descarte nunca virava perda, a exclusão nunca estornava a entrada, e o pull devolvia o lote sem as marcas, que "voltava" no aparelho. Agora `mobile_batches` guarda exclusão e descarte (com motivo), e o pull devolve essas marcas. No ERP, descartar baixa o saldo restante do lote como Perda e excluir baixa como Ajuste; desfazer ou restaurar estorna a baixa. Reenviar o mesmo estado não baixa de novo.
- **Descartar lote no PWA baixa o saldo do aparelho** (#1466): o descarte só marcava o lote, e o saldo local ficava maior que o do ERP, que já baixa desde a #1464. Agora há uma regra só: o lote conta no saldo enquanto não está excluído nem descartado. Descartar, desfazer, excluir e restaurar aplicam a diferença, sem contar duas vezes (excluir um lote descartado não baixa de novo) e sem saldo negativo. Descartes feitos antes desta versão continuam contando, porque nunca baixaram.
- **Custo e estoque mínimo do produto do PWA não somem mais** (#1467): o PWA mandava `cost` e `minStock`, o servidor descartava, e o pull apagava os dois também no aparelho. Agora `mobile_products` guarda os dois e o pull os devolve. O produto criado no ERP a partir do PWA já nasce com custo e mínimo, e a edição no PWA de um produto vinculado atualiza `CustoReferencia` e `QuantidadeMinima` no ERP, com auditoria. Payload sem os campos não apaga o que existe, e um PWA sem custo não apaga o custo do ERP.
- **Produto criado no ERP não some mais do grid de produção do PWA** (#1469): o pull reverso mandava categoria "Geral", e o grid só desenha Massas, Molhos e Extras. Agora a categoria do ERP é traduzida pelo nome (massa, molho) e o resto vai para Extras. O grid do PWA também passa a mostrar em Extras qualquer produto com categoria desconhecida.
- **Etiqueta do PWA com o código de lote do ERP e sem colisão** (#1471): a etiqueta imprimia só o lote do dia (`LOT-YYMMDD`). No ERP o lote é `LOT-YYMMDD-<sufixo>`, e dois lotes do mesmo produto no mesmo dia geravam códigos de barras idênticos, com a conferência marcando a unidade errada. Agora o PWA calcula o mesmo código do ERP, offline, e o código de barras fica `SKU-LOT-YYMMDD-<sufixo>-001` (até 30 caracteres). Etiquetas já impressas no formato antigo continuam sendo encontradas.
- **Revisão visual do console e do PWA, fluxo inteiro** (#1474): percorremos clicando, num ambiente local com WhatsApp e Mercado Pago falsos, o caminho do cliente que chega no WhatsApp até o caixa do dia, e corrigimos o que travava ou enganava a operação.
  - **Pedido de ponta a ponta no console:** a esteira da Ficha (Iniciar preparo, Embalado, Despachar, Entregue) agora avança o pedido no EasyStok; "Marcar pago" com link do Mercado Pago pendente cancela o link e registra o recebido, e barra antes quando o pedido ainda precisa de aprovação; um único botão "Gerar cobrança e enviar", com o que falta (comanda, cadastro, endereço, janela) escrito acima dele; botões que ainda não funcionam por esta tela deixaram de aparecer.
  - **O cliente não recebe mais texto errado:** resposta pronta com `{pedido}` sem pedido não sai; legenda da foto sem peso repetido nem "Foto 1"; na primeira mensagem fora do horário ou com a loja fechada sai a mensagem de Horários e mensagens; resumo do pedido com o horário da janela e os nomes como no cardápio.
  - **Ficha e notas:** a Ficha relê telefone, tags e notas ao recarregar; notas internas mostram o nome de quem escreveu ou agiu, não o id; nota do sistema ("escalado para a dona") aparece como evento, não como mensagem "enviando".
  - **Caixa do dia:** "Na gaveta (dinheiro)" separado de Pix e cartão, conferência começando em branco ("Faltam/Sobram R$ X"), suprimento como entrada, lançamentos em ordem de hora; o mesmo vale ao fechar o caixa esquecido de ontem.
  - **Cozinha e entregas:** fila de Hoje e de Amanhã, nome do cliente nos cartões, número do pedido igual em todas as telas, confirmação antes de recusar e devolver o pagamento, barra lateral com volta para Módulos e Balcão.
  - **Erros legíveis:** os avisos mostram o motivo que a API devolve, sem ids, e somem quando a ação seguinte dá certo.
  - **PWA:** aparelho recém-pareado recebe o catálogo do ERP (não cria produto duplicado), sem pull completo repetido nos aparelhos já pareados; calculadora funciona com o aparelho pareado; código do lote igual no modal, na etiqueta e no ERP; peso pré-preenchido; nome da loja depois de parear; textos acentuados.
  - **WhatsApp:** mídia com endereço relativo falha na hora com motivo claro (antes passava sem erro); Configurações › Canais mostra a última mensagem recebida.
Revisão da onda 0: preserva itens avulsos na cozinha/PWA, recusa pareamento cruzado entre empresas, identifica cliente pela conversa no bloqueio e só confirma o cursor do PWA após persistir os dados. Timeouts não descartam o histórico ainda não recebido. Concilia o cardápio arquivado, a validação de itens e o controle do atendimento automático com master.
- **Interruptor "Atendimento automático" do console passa a valer** (#1475): desligado, o agente não responde mais o cliente; a conversa vai direto para a dona, com nota interna do motivo. A saudação de primeiro contato e as automações de entrada continuam saindo. A tela agora explica o efeito de cada posição. Em produção os dois tenants estavam com o automático ligado, então nada muda até alguém desligar.
- **Teste de estorno de caixa não falha mais entre 00h e 03h UTC** (#1478): o teste montava a data do fechamento em UTC, e o use case usa a data operacional do Brasil. Às 21h de Brasília os dias divergiam e o teste falhava sempre.
- **Subir e descer prato na gestão do cardápio funcionam com itens em ordem 0** (#1486): o console calculava a ordem nova pelo meio dos vizinhos. Com itens nascidos em 0, subir o primeiro mandava ordem negativa, que o EasyStok recusa, e itens empatados não saíam do lugar. Agora `POST api/atendimento/comanda/cardapio/{item}/mover` recebe só a direção, troca o item de lugar e renumera de 1 a n, com o mesmo desempate da lista.
- **Pagamento e lançamento de caixa do PWA gravam a forma real, não mais sempre dinheiro** (#1493): o PWA pergunta a forma (Pix, dinheiro, crédito, débito, transferência ou outro) ao marcar o pedido como entregue, no pedido retroativo e no lançamento de caixa. O servidor guarda o campo em `mobile_orders.metodo` e `mobile_cash_entries.metodo` (migração `AddMetodoPagamentoMobile`) e o pagamento automático do pedido e o movimento de caixa usam essa forma. Aparelho que ainda não manda a forma continua caindo em dinheiro. Com isso, "Na gaveta (dinheiro)" no Caixa do dia do console deixa de somar Pix e cartão recebidos pelo PWA.
- **Custo da produção com unidade convertida** (#1498). Quando a unidade da receita era diferente da unidade base do insumo (1,2 kg de um molho com custo por grama), o custo saía 1.000 vezes menor. Agora `CalculoProducaoCore.CustoNaUnidadeDoInsumo` converte a unidade antes de multiplicar.
- **Custo estimado da cesta de produção** (#1502). O custo consolidado multiplicava o custo por unidade-base do insumo pela quantidade na unidade da receita, e com 1,2 kg de um insumo cotado por grama o custo saía 1.000 vezes menor. O erro aparecia na calculadora do PWA e agora também no planejamento.
- **Rollback da migration `RemoverPermissoesLegadas`** (#1504). O `Down` lançava exceção sempre, e isso deixava vermelhos o master e todo PR com os 6 testes que descem migrations. Agora ele só recusa no banco quando há perfil com permissões explícitas ou arquivo de permissões legadas, e a migration continua aplicada nesse caso. Sem esses dados, o `Down` remove as duas colunas.
- **Regras de pedido, caixa, estoque e datas** (#1506): pagamento manual recusado em pedido cancelado; todo cancelamento cancela a conta a receber e libera a vaga da janela; caixa não abre por cima de caixa de dia anterior esquecido; cancelamento devolve as saídas de estoque ainda não estornadas; subtotal do item em centavos; validade e código do lote pelo dia de Brasília; "Vencida" pelo dia de Brasília; o site não aceita mais janela de entrega já encerrada.
- **Jobs, notificações e relatórios** (#1507): relatório não roda mais em duplicata nem apaga o arquivo pronto, e o heartbeat sobrevive a falha transitória; incidente de endpoint é publicado no tenant (antes a RLS recusava); queda de DNS/conexão antes da Meta vira nova tentativa em vez de mensagem perdida; imagem com legenda longa não é reenviada; WebPush usa HttpClient da fábrica; Livro de Saídas sem N+1; lembrete de pedido agendado dispara só o mais próximo e nada para entrega passada; Zenvia sem retry que duplicava SMS; handler que falha no outbox não envenena o resto do lote.
- **Avaliação pós-entrega**: cookie `__Host-` com `Path=/`; antes o navegador o descartava e todo envio de avaliação dava 401.
- **Sync do PWA do caixa** (#1509). Texto maior que a coluna não derruba mais o lote inteiro do sync; recusa de validação vai para a dead-letter em vez de ser reenviada para sempre; conflito fica guardado na fila de conflitos; o boot une a fila do IndexedDB com a do localStorage e não perde venda; o service worker abre a fila sem rede; o módulo de etiquetas carrega em `/pwa/`. O filtro de empresa no pull vai pela PR de segurança (#1508).
- **Console de atendimento** (#1510): link do cardápio na bolha só abre http(s) da própria origem (fecha XSS por mensagem do cliente); anexo, áudio e "Recebi" não passam mais de uma conversa para outra; microfone liberado ao sair; "R$ 1.500" colado vira mil e quinhentos; trava de envio por ação na comanda; cadastro de janelas sem duplicar no toque duplo; saldo do cardápio serializado por item; relógio e dia do roteiro acompanham o dia real; planejamento recalcula ao mudar porções.
- **Estoque do PWA contado duas vezes no servidor** (#1520, ADR-0060). Ao marcar o pedido "pronto" o aparelho ficava com 9 e o servidor com 8; lote novo virava 20 em vez de 15. Agora cada mudança de saldo sobe como um movimento (`stock.delta`) contado uma vez só, em qualquer ordem de chegada e com mais de um aparelho.
  - Aparelho com a versão antiga do PWA e o botão "Sincronizar tudo" continuam mandando o saldo absoluto, agora sem a segunda contagem.
- **Editar ou excluir lançamento de caixa no PWA não chegava ao servidor** (#1520). A edição atualiza o lançamento e o movimento no Caixa do ERP. A exclusão sobe depois do "Desfazer" e vira estorno do movimento; a linha não é apagada.
  - Dia com caixa fechado: o servidor recusa e o PWA avisa.
- **Edição feita offline com hora antiga não chegava aos outros aparelhos** (#1520). O pull e a detecção de conflito passam a usar o carimbo do servidor em vez do relógio do aparelho. Descarte de lote e edição de lançamento também passam a chegar.

- **Teto da sangria** (#1521): saída em dinheiro é limitada pelo dinheiro da gaveta (mesma conta do console), não pelo saldo esperado que soma Pix e cartão. Venda ou pagamento sem método conta como possível dinheiro, para não travar sangria legítima. Saída por Pix ou cartão segue limitada pelo saldo do dia.
- **Adicionar porção a um prato já gravado dava erro 500** (#1529). A chave da porção é gerada no app, e o EF mandava UPDATE numa linha que não existia. O mapeamento agora usa `ValueGeneratedNever`, como no #512.
- **Editar uma porção apagava o vínculo com o estoque** (#1529). A reconciliação das opções chamava `Atualizar` sem o vínculo, e o valor padrão `null` o zerava.
- **Pedido do site e da comanda não baixava estoque** (#1534). Esses pedidos nascem sem loja, e a baixa (e a devolução no cancelamento) saía sem fazer nada quando o pedido não tinha loja. Agora o pedido sem loja baixa do estoque da empresa, em FEFO, e devolve ao lote que baixou. O pedido com loja segue baixando só da loja dele.
- **Vaga da janela de entrega** (#1540, #1302): a varredura de pedido abandonado cancelava o pedido e deixava a vaga ocupada (a liberação vinha depois do único `SaveChanges`); a janela seguia esgotada no site. Medido em produção em 10/10: 2 vagas presas, de 06/10.
- **Remover pagamento do pedido** (#1540): a rota antiga apagava recebimento do Mercado Pago (cobrança paga sem pagamento, sem estorno possível) e deixava o atendimento zerar o pagamento de pedido em preparo para cancelar como "sem pagamento". Recebimento com cobrança online só sai por estorno e, com o preparo iniciado ou o pedido entregue, remover exige gestão (na Web, o atendimento deixa de remover pagamento de venda de balcão já entregue). O autor do pagamento vem da sessão, não do corpo.
- **Link para pedido já pago** (#1540): gerar cobrança só barrava cobrança paga; com os pagamentos cobrindo o total (estorno total ou maquininha) o link saía e o webhook devolvia o novo pagamento como duplicado.
- **Caixa e pagamento repetidos por queda de rede** (#1540): sangria, suprimento e pagamento parcial iam sem `Idempotency-Key`. `/api/caixa/movimentos` entra na lista do middleware e o console guarda a chave do mesmo corpo por 2 minutos ou até a API confirmar. "Desfazer o último pagamento" manda o id do pagamento mostrado.
- **Perda de prato com porções** (#1540): depois da M1.4c a perda respondia "Só há 0 em estoque" com saldo na prateleira. A perda recebe a porção e o FEFO fica nos lotes dela; o console mostra a porção no formulário.

- **Console** (#1540): preço de porção "34.90" virava R$ 3.490,00; receita recusava "1,2"; peso "350g" passava e o lote nascia sem peso; Entregas limpava o formulário quando a API recusava; o bloco de devoluções não recarregava no primeiro pagamento; o refresh do token recomeçava o laço da inbox.
- **Sync do PWA** (#1540): excluir um lançamento de caixa criado no mesmo envio não excluía nada.
- **Baixa do pedido do PWA no estoque** (#1543): sai do lote que vence primeiro e atravessa os lotes, em vez de um lote qualquer. A falta de saldo vira descoberto no lote em vez de sumir, e o pedido que já chega "pronto" também baixa. A loja é a do pedido, e dois produtos do aparelho ligados ao mesmo produto baixam a soma.
- **Cancelamento do pedido do PWA** (#1543): devolve só o que aquele pedido baixou, aos lotes de onde saiu. Antes somava a quantidade do pedido mesmo sem ter saído nada.
- **Pedido do console ou do site avançado pelo PWA** (#1543): a troca de status passa pela esteira do ERP (máquina de estados, estoque pelos itens do pedido, evento). Antes era gravada direto na tabela e o estoque baixava por fora. Quando o aparelho manda só o último status, a esteira percorre o caminho passo a passo, sem pular pagamento nem aprovação; o aparelho também consegue cancelar a entrega que ele mesmo registrou. Recebimento e Venda só nascem se o ERP aceitou a entrega.
- **Lote do PWA excluído** (#1543): devolve o insumo e a embalagem que a produção dele baixou; restaurar o lote refaz a baixa, inclusive no lote que já chegou excluído. Descarte segue sem devolver.
- **Pedido entregue no mesmo envio do lote** (#1543): o lote é vinculado antes do pedido, que passa a achar o estoque que acabou de entrar.
- **Falha no meio do vínculo do sync** (#1543): o que o passo que falhou deixou pela metade não é mais gravado pelo passo seguinte.
- **Fechamento de caixa do PWA** (#1543, #954): não sobrescreve mais o fechamento da empresa nem o de outra loja; casa pelo escopo exato.
- **Estorno online reservado e nunca enviado** (#1547): a solicitação pendente só saía sendo enviada ao Mercado Pago. Com a devolução feita por fora (Pix direto), o único desbloqueio devolvia em dobro. Nova rota `POST /api/pedidos/{id}/estornos-online/{operacaoId}/descartar` (gestão) encerra a solicitação sem enviar e libera o saldo; depois do envio responde 409 `estorno_ja_enviado`. Fica na trilha do pedido como `devolucao_online_descartada`.
- **Remover e desfazer pagamento de dia fechado** (#1547): o estorno manual recusava com o Caixa fechado, mas remover ou desfazer o mesmo recebimento passava e o fechamento do dia ficava com um total que o Caixa não soma mais. As três operações usam a mesma guarda; a remoção olha o dia do recebimento e orienta a registrar uma devolução.
- **Sangria simultânea** (#1547): duas saídas ao mesmo tempo liam o mesmo saldo e as duas passavam do dinheiro na gaveta (medido contra Postgres: 8 de 8 sangrias de R$ 80 aceitas numa gaveta de R$ 100). A conferência e a gravação da saída rodam na mesma transação, atrás de uma trava do caixa da loja.
- **Autor do pagamento na venda de balcão** (#1547): `POST /api/pedidos/balcao` levava `criadoPorNome` do corpo para o autor do pagamento. O nome vem da sessão; o do corpo só vale quando a sessão não traz nome.

- **Pedido que voltava atrás no console** (#1548): a leitura da inbox que saiu antes de um pagamento ou troca de forma chegava depois e desfazia o pedido na tela por até 5 s. Cada leitura do pedido leva a ordem em que saiu e a atrasada é descartada.
- **Produção do dia presa depois de queda de rede** (#1548): corrigir a linha depois de uma resposta perdida reenviava a mesma `Idempotency-Key` com outro corpo e a API recusava (422) até recarregar a página. A chave passa a valer para o mesmo corpo, como no caixa: corpo corrigido ganha chave nova, o mesmo corpo repetido em até 2 minutos devolve o lote já gravado.
- **Reabrir conversa** (#1550): reabrir uma conversa encerrada com o cliente falando por último disparava, em até 1 minuto, o aviso de cliente sem resposta e o prazo estourado aos gestores, contados da mensagem antiga. A reabertura passa a valer como a última palavra; a mensagem que o cliente mandar depois é cobrada normalmente.
- **Lembrete de início do preparo** (#1550): saía para pedido já em preparo, pronto ou a caminho, e para todo pedido antigo com início no passado quando o avaliador ligava. Vale só antes do preparo e por 24 h depois do início previsto; fora disso o aviso aberto fecha sozinho.
- **Lembrete de véspera** (#1550): continuava aberto dizendo "entrega amanhã" no dia da entrega e depois. Fecha sozinho quando a véspera passa.
- **Capa do prato** (#1550): trocar a capa de um prato com a capa gravada fora da galeria apagava a primeira foto da galeria. A foto fica; com a galeria cheia (5), a troca é recusada antes do upload.
- **Envio de modelo pelo Balcão** (#1550): empresa sem número do WhatsApp vinculado recebia "confira a conversa antes de iniciar outro envio", como se algo pudesse ter saído. Recusa antes da rede é falha definitiva; resposta da Meta sem id de mensagem continua incerta.
- **Aviso da ação na Ficha do tablet** (#1551): entre 760 e 1179 px a Ficha abre em janela e o aviso da API ("Escolha a janela de entrega…", "Pedido não criado: …", "Pedido criado, mas a cobrança não saiu…") ficava só na faixa do rodapé, atrás dela; o botão parecia não fazer nada. O aviso aparece dentro da Ficha.
- **Dia da entrega entre conversas** (#1551): o "Dia da entrega" escolhido na comanda de um cliente continuava valendo ao abrir a conversa seguinte, e a janela dela caía nesse dia. O seletor recomeça a cada conversa.
- **"Modelos aprovados" na demonstração** (#1551): o botão aparecia em toda conversa de WhatsApp e só devolvia erro, porque modelo aprovado exige a API. Ele e a dica do campo que o citava só aparecem no modo API.
- **CI do master** (#1560): os testes de integração que criam pedido registram o cliente de CEP e o de geocoding de teste. Desde o endereço de entrega no pedido (`67127e43`), criar pedido depende deles, e o provedor desses testes não os tinha.
- **Caixa fechado sem loja protege o pedido da loja** (#1562): o Console e a Web fecham o Caixa da empresa, sem loja, e esse fechamento soma os recebimentos de todas as lojas. Devolver, remover ou desfazer um recebimento de pedido que tem loja passava mesmo com o dia fechado, porque a conferência só olhava o fechamento daquela loja. Agora vale o fechamento da loja ou o da empresa. Pedido sem loja segue livre quando só uma loja fechou.
- **"Aguardando resposta" em conversa reaberta** (#1562): reabrir uma conversa encerrada com o cliente falando por último voltava a marcá-la como aguardando resposta, pela mensagem antiga. A inbox passou a seguir a regra do lembrete: a reabertura conta como última palavra, e só a mensagem que chegar depois dela volta a esperar resposta.
- **Modelo aprovado preso em "enviando"** (#1562): se a API caísse entre gravar o envio e chamar o WhatsApp, a mensagem ficava pendente para sempre. Depois de 10 minutos sem desfecho ela aparece como envio incerto, com o aviso "Confira a conversa no WhatsApp antes de iniciar outro envio". O modelo não é reenviado sozinho.
- **Webhook de pagamento que falha não grava resto** (#1562): quando o processamento falhava e voltava atrás, marcar a falha gravava junto o que tinha ficado pela metade. A marca de falha agora descarta esse resto antes de gravar.
- **Descartar estorno online que nunca foi enviado** (#1563): a solicitação pendente só oferecia "Conferir estorno pendente", que envia o pedido ao Mercado Pago. Quem desistiu agora tem "Descartar solicitação não enviada", com confirmação, só para dona ou gerente e só enquanto nada foi enviado; se a solicitação já saiu, a tela mostra o motivo da recusa.
- **Hora da mensagem programada e do lembrete** (#1563): a hora digitada era lida no fuso do aparelho e a lista mostrava a da loja; num aparelho fora do fuso de Brasília o envio saía deslocado. A hora digitada é a da loja em qualquer aparelho, e o sininho mostra o prazo na mesma hora.
- **Apagar o mínimo do insumo** (#1563): apagar o valor e salvar não mudava nada. O console manda `limparMinimo: true` (campo novo e opcional do `PUT /api/atendimento/producao/insumos/{id}`), o mínimo fica vazio e o insumo volta a herdar o da categoria e o da loja. Quem não manda o campo continua como antes.

- **Leitor de código do PWA** (#1565): depois de ler um código que não bate, a leitura volta sozinha (a câmera ficava ligada sem ler até fechar e abrir) e roda a 10 leituras por segundo, não a cada quadro.
- **Limpeza de tokens que nunca rodava** (#1566): `refresh_tokens` e `email_confirmation_tokens` expirados há mais de 24 h passam a ser apagados em lote na rotina de 6 h do Worker, junto com os `reset_tokens`.
- **Pedido do PWA avançado também no console** (#1571): o estoque não baixa mais duas vezes. O aparelho e a esteira do ERP baixam a mesma venda com documentos diferentes; agora cada lado baixa só o que o outro ainda não baixou daquele produto. Item acrescentado pelo console a um pedido que o aparelho já baixou sai sozinho, sem rebaixar o pedido inteiro.
- **Cancelamento de pedido do PWA** (#1571): cancelar pelo console devolve o que o aparelho baixou, e cancelar pelo aparelho devolve o que o console baixou, uma vez só. O cancelamento pelo aparelho devolve também o item que tinha saído do pedido depois da baixa, e o do console devolve mesmo quando um aparelho atrasado levou o status de volta.
- **Pedido do PWA cancelado no console e reenviado pelo aparelho** (#1571): o pedido volta a valer e a baixa que o cancelamento devolveu é refeita. Estorno feito à mão no ERP segue sem voltar.
- **Pedido do PWA com mais de um produto** (#1571): cada item do pedido no ERP fica com o produto certo. Antes o par podia sair trocado e o console baixava o produto errado.
- **Jobs que rodavam ao mesmo tempo na API e no Worker** (#1575): a retenção de auditoria, o alerta de aparelho sem sinal e o cancelamento de pedido abandonado sobem nos dois processos; cada rodada agora pega uma trava no banco e quem não pega pula, sem erro. O mesmo pedido abandonado não é mais cancelado pelos dois ao mesmo tempo.
- **Mensagens que sumiam por um ciclo** (#1576): leitura que falha sem cache não troca mais as mensagens da tela por lista vazia.
- **SSE meio-aberto** (#1577): conexão que cala por mais de 62 s (sem o heartbeat de 25 s da API) é dada como caída e reconecta; antes a tela ficava "Ao vivo" sem atualizar.
- **Relógio do Pix** (#1577): para de contar com cobrança paga, vencida ou cancelada. O PDF aberto em aba é liberado da memória em 60 s.
- **Chaves de idempotência expiradas** (#1578): a requisição idempotente apaga as chaves vencidas da própria empresa, no máximo uma vez por hora. A tabela `idempotency_keys` não tinha limpeza e só crescia.
- **Volta ao hall no tablet** (#1582): no Balcão entre 1024 e 1179 px (iPad em paisagem) a marca do topo leva aos módulos; antes não havia caminho visível.
- **Título da aba por tela** (#1582): cada tela escreve o próprio nome ("Fila de preparo · Casa da Baba") e o Balcão mantém a contagem na frente; o título não fica mais congelado com a contagem ao sair do Balcão.
- **Ajuda e Sair no celular** (#1582): os dois botões ganharam colunas próprias no hall e na barra do módulo; a 390 px o Sair cobria 20 px do Ajuda e o toque encerrava a sessão.
- **Abrir e fechar a loja em Horários e mensagens** (#1582): o gesto abre na própria tela; antes o botão não fazia nada e o modal aparecia sozinho ao voltar para o Balcão.
- **Sessão vencida** (#1582): o login diz que a sessão venceu, e o aviso prévio não manda mais sair (Sair apaga o rascunho).
- **Tablet da cozinha** (#1582): falha ao reler a matriz de acesso depois de renovar a sessão não derruba mais a fila; vale a matriz já carregada.
- **Telas sem saída** (#1582): Cozinha › Produção e cardápio do dia aponta Produção do dia e Estoque do dia; o link "Balcão" de Gestão › Canais fecha a janela; Entregas em outra janela abre com o menu marcado e cai para a mesma aba se o popup for bloqueado.
- **Hoje e Amanhã na virada do dia** (#1584): com a Cozinha aberta em "Amanhã", depois da meia-noite o tablet passava a mostrar depois de amanhã e escondia os pedidos do dia; agora a fila volta sozinha para Hoje. Em Entregas, o chip "Hoje" abria ontem na tela que virou o dia aberta; hoje e amanhã acompanham o relógio.
- **Categoria do lançamento do caixa** (#1584): escolher "Sangria" e trocar para Entrada gravava uma entrada rotulada Sangria. Trocar Entrada/Saída tira a categoria sugerida do outro tipo; a categoria digitada fica.
- **Resultado de "Alterar agendamento"** (#1584): quando o pedido mudava de janela, o modal fechava antes de mostrar se o aviso ao cliente entrou na fila. O resultado fica na tela até a pessoa fechar.
- **Erro dentro da comanda da cozinha** (#1584): o erro de "Marcar pronto" aparecia atrás do modal da comanda; agora aparece dentro dele, e "Reimprimir na fila" confirma quando o canhoto volta para a fila.
- **Confirmação antes de baixar e excluir** (#1584): "Registrar perda do lote", "Desfazer" perda e "Remover" bloqueio executavam com um toque; agora pedem confirmação em linha. "Desfazer" só aparece para quem pode estornar (Gerente na produção).
- **Produção do dia não perde o que foi digitado** (#1585): trocar de tela apagava as linhas. O rascunho fica guardado até o lote gravar ou a página recarregar, e as porções vindas do Planejamento entram junto dele.
- **Etiquetas de cada lote** (#1585): "Imprimir etiquetas" sumia ao lançar outro lote, fechar a confirmação ou sair da tela. Os lotes lançados na visita ficam na tela até dispensar, cada um imprime as próprias etiquetas e o erro de impressão aparece no lote.
- **Erro junto do formulário** (#1585): em Insumos o aviso do cadastro e o do ajuste saem no formulário que os causou; em Produção do dia o erro diz o prato ("Prato 02 (Lasanha): Quantas porções saíram?").
- **Links nas telas de Produção** (#1585): os textos de Receitas, Planejamento e Estoque do dia que citam Produção do dia e Perdas levam à tela.
- **Lista de compras do Planejamento** (#1585): a lista gerada aparece na tela e o botão só volta quando as porções ou a data mudam; antes cada clique criava outra lista igual.
- **Encerramento, ficha e pagamento com o retorno no lugar da ação** (#1586): achados da banca de navegação e UX de 11/10 no Balcão. No modo API a resposta ia para a faixa do rodapé, atrás do modal.
  - **Encerrar:** o botão trava no primeiro toque e mostra "Encerrando…" (dois toques mandavam a despedida duas vezes ao cliente); se o EasyStok recusa, o erro aparece dentro do modal e a nova tentativa não repete a despedida que já saiu.
  - **Só aparece o que grava:** avaliação, anotação e "Guardar também nas notas do cliente" saíram do encerramento no modo API, e a aba "Atendimentos" do Histórico também. A faixa não acende mais depois de encerrar que deu certo, nem diz que o pedido "segue aberto" quando o modal acabou de cancelá-lo.
  - **Ficha:** a edição em linha mostra "Salvando…" e só depois "Salvo"; na recusa o erro fica no campo, com o texto digitado. "Salvar cadastro" e "Confirmar e cadastrar" esperam a resposta e travam o segundo toque.
  - **Dinheiro:** "Marcar pago", "Recebi" e "Desfazer pagamento" só fecham no sucesso; na recusa o valor, o método e o motivo ficam na tela, com o erro ao lado.
  - **Bloquear cliente** fica desabilitado, com o motivo escrito, para quem ainda não tem cadastro.
  - **Cobrança vencida:** no lugar do botão "Reenviar cobrança" da barra, que só avisava, a Ficha diz o que o EasyStok faz sozinho: manda um link novo e, se esse também vencer, cancela o pedido e avisa o cliente.
- **Comanda de pedido já criado** (#1587): com o pedido no EasyStok, o console trava somar, tirar e anotar item na comanda e no cardápio e escreve o motivo no lugar. Antes os botões seguiam ativos e só avisavam no rodapé.
- **Cardápio diz de quem é a comanda** (#1587): o painel mostra o primeiro nome do cliente no cabeçalho e no rodapé. Trocar de conversa com ele aberto não joga mais o prato no pedido de outro cliente sem aviso.
- **Anexo e foto na conversa** (#1587): no modo API o arquivo que o EasyStok não envia (PDF, ou foto fora do WhatsApp) é recusado ao escolher, com a frase no lugar, e o botão não promete mais PDF. Foto que falha na galeria ou no cardápio fica escrita ao lado do botão.
- **Fio da conversa** (#1587): só rola sozinho ao trocar de conversa, quando a mensagem é enviada daqui ou quando a leitura já estava no fim. Mensagem do cliente não tira mais do lugar quem lê mais acima.
- **Resposta pronta pelo botão** (#1587): depois de escolher a resposta o foco volta ao campo de texto, e o Enter envia.
- **Campo de moeda multiplicava o valor por mil** (#1595): nos campos de dinheiro do console (caixa, pagamento, preço, estorno) o valor fica alinhado à direita e o clique caía antes do texto; digitar 5000 virava R$ 50.000,00. A tecla agora conta sempre como último dígito e o cursor volta para o fim.
- **Caixa da empresa fechado vale para sangria, suprimento e estorno de loja** (#1598): lançar ou estornar um movimento de caixa que tem loja passava mesmo com o dia já fechado pelo Caixa da empresa (sem loja), que soma todas as lojas. Agora vale o fechamento da loja ou o da empresa. Movimento sem loja segue livre quando só uma loja fechou.
- **Estorno de pagamento de conta não mexe em dia fechado pela empresa** (#1598): estornar o pagamento de uma parcela (a pagar ou a receber) marcava como estornado o movimento de caixa de uma loja, mesmo com o dia fechado pelo Caixa da empresa. A reversão continua acontecendo na conta; o movimento do dia fechado fica como estava.
- **Pagamento de pedido depois do fechamento não reabre o Caixa** (#1598): com o Caixa da empresa já fechado, o pagamento de um pedido de loja abria sozinho um Caixa novo para a loja naquele dia. O pagamento continua sendo gravado, sem a abertura automática.
- **Webhook Pix com vários recebimentos não grava resto do que falhou** (#1598): quando um recebimento do lote falhava no meio da baixa, o que tinha ficado pela metade era gravado junto com o recebimento seguinte. Agora o resto é descartado antes de seguir para o próximo.
- **Modelo aprovado confirmado depois do prazo não fica com erro** (#1599): se o WhatsApp confirmasse o envio de um modelo depois de a mensagem já ter sido marcada como envio incerto, ela aparecia como enviada e com o aviso de erro junto. A confirmação do WhatsApp agora vence e limpa o aviso. A busca por modelo sem desfecho, que roda a cada 30 segundos, ganhou um índice próprio e deixou de percorrer todas as mensagens.
- **Apagar o custo do insumo** (#1600): apagar o valor e salvar não mudava nada. O console manda `limparCusto: true` (campo novo e opcional do `PUT /api/atendimento/producao/insumos/{id}`) e o insumo volta a "sem custo informado"; a ficha deixa de fechar o custo da receita que o usa. Limpar e informar custo no mesmo pedido responde 400. Quem não manda o campo continua como antes.
- **Janela de entrega, prazo da rota e próxima abertura na hora da loja** (#1600): a hora da janela ("11h30 às 12h30"), o fim do prazo da entrega e o "abre amanhã às 8h" eram montados no fuso do aparelho. Num aparelho fora do fuso de Brasília, ou depois das 21 h com o aparelho em UTC, o corte da janela, o aviso de atraso e o texto ao cliente saíam deslocados. Agora valem o dia e a hora da loja em qualquer aparelho.
- **"Hoje", "ontem" e o dia do período no dia da loja** (#1600): o separador de dia da conversa, o "chegou há N dias" do lead, o alcance por bairro e o dia do caixa da demonstração contavam o dia pelo relógio do aparelho.
- **Conversa reaberta sem "aguardando resposta" antigo** (#1600): com as mensagens carregadas, o console voltava a marcar como sem resposta a mensagem que o cliente tinha mandado antes do encerramento, e a lista (que segue a API) não. A nota de reabertura passa a ser a última palavra também no console; mensagem que chega depois da reabertura continua contando.
- **Urgência que não subia no Balcão** (#1606): pedido de devolução, restrição alimentar, cliente insistindo sobre o pedido e "paguei" com cobrança ainda aberta ficavam em "Automático" quando a mensagem não terminava em "?". Agora sobem para "Precisa de você" na hora, com o motivo no cartão ("Problema no pedido", "Restrição alimentar", "Cobrando o pedido", "Diz que pagou"). "Sem querer ser chato" deixou de ser lido como restrição alimentar.
- **Painel "Agora" dizia "em dia" sem ter a lista** (#1611): no modo API, enquanto as conversas carregavam ou quando a carga falhava, a fila Responder mostrava "0 · Ninguém esperando você". Agora diz "Carregando as conversas…" ou "Não consegui carregar as conversas", sem número. A fila também passou a seguir a ordem de urgência do Balcão: quem espera há mais tempo aparece primeiro.
- **Cliente difícil não recebe mais texto de venda** (#1613): reembolso ("dinheiro de volta"), cancelamento, troca de endereço, reclamação sem a palavra da lista ("chegou gelado", "faltou", "veio muito cozida", atraso), ameaça (Procon, advogado), pedido de gerente, desconto, ligação, retirada e pressa passam a subir para "Precisa de você" na hora, com o motivo escrito no cartão. O rascunho dessas falas acolhe e não promete nada, em vez de "Fechado! Confirma o endereço". Restrição alimentar dita pelo verbo ("não pode comer ovo") ou pela condição ("diabética", "grávida") também vai para a dona. Acrescentar item em pedido já em andamento sobe; sem pedido, segue como conversa normal.
- **Resposta que ajuda quem escreve pouco** (#1613): "como eu pago?" e "pode mandar o pix" dizem o caminho (link por Pix ou cartão); "quanto custa a lasanha clássica?" responde o valor do cardápio; "quero um jantar" sem item vai para o cardápio em vez de "Fechado"; "paguei" promete conferir, nunca confirma. A massa de 30 conversas foi de 22 para 25 ações certas em 27 avaliadas.
- **Pagamento do link antigo não fica mais retido** (#1615): quando o pedido mudava de valor, o link do Mercado Pago era trocado, mas o antigo continuava pagável. Se o cliente pagava o antigo, o dinheiro ficava parado como "valor menor", sem registro no pedido, e o pedido era cancelado quando o link novo vencia. Agora o valor entra no pedido, o pedido segue para a cozinha e a diferença fica como saldo a receber na entrega.
- **Abertura de caixa é sempre de agora** (#1615): a abertura aceitava uma data vinda da requisição. Com uma data no futuro ela passava a ser o último evento do caixa e nenhum dia fechava mais. A data enviada é ignorada.
- **Lançamento de caixa não aceita data no futuro** (#1615): sangria, suprimento ou despesa com data adiante sumia do caixa de hoje. Lançamento retroativo continua valendo.
- **Desvincular e promover de novo um lançamento do aparelho não duplica o caixa** (#1615): a promoção manual criava um segundo movimento para o mesmo lançamento e o valor entrava duas vezes. Agora religa o movimento que já existe e recusa lançamento excluído no aparelho.
- **Item sem preço no cardápio não vira pedido** (#1615): pelo site e pelo atendimento, um item a R$ 0 gerava pedido de total zero que seguia até a entrega. O pedido agora é recusado antes de ser criado, com o nome do item.
- **Mensagem longa do cliente no WhatsApp não some** (#1615): texto acima de 4096 caracteres era descartado sem aviso. Agora é gravado cortado no limite. Resposta de lista ou de formulário, que também era descartada, passa a ser registrada.
- **Mensagens programadas não ficam presas quando o envio é interrompido** (#1615): uma publicação ou erro no meio do lote deixava travadas até as mensagens que ainda nem tinham sido tentadas, sem como cancelar. Agora só a que estava saindo naquele instante pode ficar para revisão.
- **Janela avulsa abria um segundo Balcão** (#1616): na Cozinha e nas Entregas abertas pelo Balcão em outra janela, a barra do módulo troca "Módulos" e "Balcão" por "Voltar ao Balcão", que traz a janela do Balcão já aberta (e fecha a avulsa quando o navegador não troca de janela, como em aba de tablet). Aberta direto, a tela segue com os dois links.
- **Anel do Pix vencido** (#1616): no modo API o anel não mostra mais "Reenviar cobrança", que só avisava no rodapé; no lugar, diz que o EasyStok manda o segundo link sozinho e cancela o pedido se ele também vencer.
- **Financeiro vazio em dia sem abertura** (#1624): o Caixa do dia escondia tudo enquanto ninguém abria o caixa, mesmo com Pix de pedido, venda ou lançamento já no dia. Agora o movimento de hoje aparece junto do abrir o caixa.
- **Resumo do caixa sem a linha de vendas** (#1624): com venda no dia, as linhas não somavam o total. A linha "Vendas" entra quando há venda.

- **Lote em dobro ao trocar de tela durante o envio** (#1627): a Produção do dia que voltava no meio de um envio nascia liberada; mudar uma porção e confirmar gravava outro lote. Agora o envio em voo trava a tela que volta, e um segundo envio é recusado antes de sair.
- **Etiquetas cortadas na impressão** (#1627): lote com mais etiquetas do que cabem numa página imprimia só a primeira página (40 etiquetas saíam em 1 página; agora saem em 5).
- **Venda sem forma de pagamento contada como "Pix e cartão"** (#1627): o que chega sem forma (venda do pedido entregue pelo PWA, #1630) aparece em "Sem forma de pagamento", no resumo e no fechamento.
- **Caixa sem abertura afirmava gaveta e saldo inicial** (#1627): sem abrir o caixa a tela mostra só o que entrou e saiu, por dinheiro, Pix e cartão.
- **Fluxo dos 7 dias contava suprimento e sangria** (#1627): reforço de troco e retirada para o cofre não entram mais em entrou e saiu. Dia fechado que recebeu dinheiro depois diz quanto mudou.
- **Limpar apagava o lote num toque** (#1627): agora pergunta antes.
- **Produção do dia na moldura com menu lateral** (#1627): o lote volta a ficar ao lado da grade em tablet deitado; em tela estreita a barra leva até o lote e o Confirmar fica no fim dele.

- **Startup revertia migrações diante de uma migração de carimbo antigo** (#1639): a API aplicava cada pendente como alvo; uma pendente anterior à última aplicada fazia o EF rodar o `Down` de todas as posteriores (medido em Postgres real). Agora, havendo pendente fora de ordem, as pendentes são aplicadas sem alvo, como no `--migrate-only`: nada é revertido.
- **Webhook do Mercado Pago** (#1667): a rota existe com ou sem token (sem segredo responde 401, antes 404). Reconsulta que volta 404 com cobrança online pendente responde 500, para o Mercado Pago reenviar; antes respondia 200 e o pedido pago podia ficar sem baixa.
- **Estorno** (#1667): a chave de idempotência passa a ser obrigatória e de quem chama, então dois estornos parciais de mesmo valor são dois reembolsos. Timeout, 5xx e estorno em análise viram `estorno_indeterminado`, não `estorno_recusado`.
- **Sem credencial** (#1667): sem `MercadoPago:AccessToken` o checkout diz que o pagamento online não está configurado, em vez de ir ao provedor com token vazio.
- **UX do pedido**: recolher observações, destacar quantidade e total, priorizar o pedido confirmado e separar o histórico do link. Remover a rolagem duplicada da coluna e preservar a edição ao mudar de tela. (#1670)
- **Copy de frete**: orientar combinação de entrega e frete quando não há cotação automática, sem recusar o cliente na resposta pública HTTP 422. (#1670)
- **Atendimento e comanda**: preservar endereço do cadastro, coletar e salvar os dados do lead pelo link, receber CEP fora da cobertura na fila humana e combinar frete com confirmação do cliente. O assistente interno passa a oferecer retomada efetiva do automático; o envio do link respeita a conversa em andamento. Cadastro público não identifica cliente anterior apenas pelo telefone digitado. (#1670)
- **Teste intermitente da telemetria de geocodificação** (#1680): `TelemetriaGeocodingTests` dependia do timeout de 1 s do cliente de CEP e reprovou o CI do master. O prazo passa a ser largo só dentro do teste. Sem mudança de código de produção.
- **Console no computador: comanda, pedido e pagamento** (design sprint): a Ficha deixa de mostrar duas comandas com o mesmo nome, "Manter minha edição" não apaga mais o endereço e a janela que o cliente trocou, o item do pedido não some quando o prato sai do cardápio, o erro de "Gerar cobrança e enviar" aparece ao lado do botão, link vencido mantém "Marcar pago", a tela não diz "Enviada ao cliente" quando a mensagem não saiu, encerrar atendimento só cancela o pedido com escolha explícita, a Cozinha diz "Na fila" em vez de "Aguardando" e as confirmações aceitam Enter e Esc. (#1683)
- Encomenda pode aguardar sinal externo sem Mercado Pago configurado. Baixa manual por Pix, dinheiro ou maquininha acumula a entrada de 50%, confirma a reserva e preserva o saldo para a entrega.
- A reserva conserva a regra de sinal no dia da entrega. Link online pendente e falha transitória de provedor continuam protegidos.
- Atendimento distingue registrar sinal de quitar o pedido, sugere somente o valor restante e mostra saldo e próxima etapa coerentes.
- **Cobrança sabe de qual conta é** (#1688): a cobrança online guarda a conta do Mercado Pago que vai receber (`collector_id`) e a origem da credencial. Pagamento de outra conta com `external_reference` de um pedido desta loja não confirma nem é estornado; fica registrado como `conta_divergente`.
- **Credencial trocada não cancela pedido pago** (#1688): com a credencial do Mercado Pago trocada, ausente ou recusada (401, 403), o job não expira a cobrança nem cancela o pedido. A cobrança fica pendente, aguardando credencial, fora da varredura, e volta sozinha quando a credencial volta.
- **Fila de cobranças por loja** (#1688): três falhas de uma loja deixam o resto dela para a próxima rodada, sem interromper as outras. Antes, três falhas quaisquer paravam a rodada inteira.

- Corrige o seed em empresa nova: a vitrine inativa usa slug de 37 caracteres, preservando a UUID inteira. O teste agora parte sem loja nem vitrine e verifica dez pedidos e repetição sem duplicação. (#1693)
- **Atendimento automático:** o agente do WhatsApp volta a conversar com o cliente. O texto só é barrado, e a conversa vai para a equipe, quando afirma pedido, pagamento, valor ou link que nenhuma ferramenta confirmou, ou promete frete grátis ou desconto. (#1704)
- **Pedido pelo site:** a rua e o complemento digitados no site passam a compor o endereço de entrega do pedido e o cadastro do visitante, em vez de ficar só na observação. (#1704)
- **Atendimento no desktop:** compacte observações vazias, elimine a ação duplicada do link, delimite o histórico e mantenha a fila de impressão legível em colunas estreitas. Rodada complementar com banca, preservando o layout mobile e a PR #1702. (#1705)
- **Ficha do pedido sem seção repetida** (console): blocos irmãos da Ficha usavam a mesma chave do React, o que avisava a cada render e mostrava "Ocorrências" duas vezes com o pedido criado. (#1709)
- **Pedido recusado por área de entrega orienta a equipe** (console): em vez da frase escrita para o cliente, a linha diz que o endereço está fora da área e aponta o Link de compra e o cadastro de frete. (#1709)
- **Cozinha avisa quando a fila chega ao teto** (console): com 200 pedidos na fila, os mais novos ficam fora da tela; agora há um aviso. (#1709)
- **Atendimento antes da confirmação:** ajuda registrada pela comanda, entrega fora da cobertura sem exigir janela e confirmação assistida da mesma seleção pelo atendente. Lalamove acessível para consulta externa, com distinção do laboratório de transportes e orientação para registrar a corrida. (#1712)
- Sem pagamento online configurado, confirmar a comanda devolvia "Erro interno do servidor" (500) ao cliente; agora responde 503 com uma frase que diz o que fazer, e a seleção continua salva. (#1716)
- A Ficha do atendimento deixava "Link do cliente" e "Ocorrências" da conversa anterior na conversa aberta: blocos irmãos dividiam a mesma chave. Cada bloco tem a sua. (#1716)
- A nota interna do frete combinado mostrava o identificador do usuário em vez do nome da atendente. (#1716)
- **Back end da comanda e do checkout, depois da banca de servidor:** quantidade por item limitada a 99 no site e no console (um visitante criava pedido com 2 bilhões de unidades e ocupava a vaga); nome acima de 150 caracteres recusado em vez de erro interno; a chave de idempotência do checkout volta a valer quando a tentativa falha sem criar pedido; o link de pagamento reemitido da comanda cobra o saldo, sem devolver ao cliente o que o cupom já abateu; a consulta de janelas olha no máximo 50 pratos, sem repetição; e a comanda aberta do cliente só relê seu retrato quando o evento é da própria conversa ou do próprio pedido (10 mensagens em outras conversas custavam 578 consultas por comanda aberta, agora nenhuma). (#1719)
- **Primeira onda da banca de homologação:** pedido cancelado recebe aviso e deixa de gerar confirmação de pagamento, inclusive quando ela já está na fila. Frete combinado permanece ao trocar somente o horário do mesmo dia; alterações de seleção e destino continuam exigindo novo acordo. Módulos conservam a identidade da sessão com uma única saída e avisos úteis. Financeiro corrige o singular de contas. O roteiro visual registra cada uma das 30 telas com índice de capturas em computador e celular. (#1720)
- Telas de Produção e cardápio, Fidelidade e Integrações em preparação deixam de exibir formulários e dados demonstrativos no modo API. A produção disponível recebe um atalho conforme o acesso do usuário.
- Canais diferencia o WhatsApp já ligado do cadastro de outro número pela Meta ainda indisponível, sem oferecer uma ação desabilitada.
- **Título dos avisos in-app**: preserve o assunto real da mensagem, inclusive avisos de caixa, mantendo o título anterior como fallback quando o assunto estiver vazio. (#1723)
- **Dinheiro retido e cobrança em dobro** (banca de back end): o estorno automático que o Mercado Pago recusa passa a deixar o motivo na cobrança e um evento no pedido, em vez de sumir com a transação; o aviso de cobrança que o canal não entrega fica na conversa como mensagem com falha; e gerar cobrança pelo console trava o pedido, de modo que quatro cliques simultâneos criam um link, não quatro. (#1724)
- **Chat do site e pedidos do cliente** (banca de back end): o stream do chat aceita no máximo 3 conexões abertas por sessão (40 conexões com o mesmo token eram todas aceitas e custavam 1.168 consultas em 5 s; agora 3 passam e as demais recebem 429); e a lista de pedidos do cliente logado consulta avaliações e janelas uma depois da outra, porque as duas dividem a mesma conexão. (#1725)
- **Pedido confirmado pela atendente e liberado sem pagamento** (decisão do Felipe, opção A): o pedido confirmado pelo atendimento sempre nasce com cobrança (link, ou a receber na entrega quando o provedor está fora), em vez de ficar sem cobrança e ser cancelado sozinho 30 minutos depois; mudar para "aguardando" um pedido com link pendente cancela o link, conserva a dívida na entrega, registra quem liberou e respeita a aprovação da dona; e o cancelamento automático de pedido abandonado passa a deixar evento no pedido. (#1726)
- Atendimento permite conferir pagamento online antes de mudar a forma de recebimento. Respostas perdidas preservam o recibo da baixa com a mesma chave, evitam duplicação e mostram quando é necessário conferir o pedido.
- **Comanda com valor pago a maior**: o cliente recebe a seleção enviada para conferência, sem erro 403 após salvar. Acertos financeiros continuam restritos à gerência; avisos distinguem crédito de devolução pendente e preservam o código válido do cupom. (#1735)
- A montagem do atendimento mostra o total antes dos itens, recupera rascunho e conversa após recarga na mesma aba e identifica o cardápio de pedido criado como consulta. A memória fica separada por empresa e usuário e é limpa ao sair.
- **Pedido cancelado na Ficha**: link vencido informa o cancelamento, e recebimento parcial preservado não aparece como item novo ou diferença a cobrar. Pedidos ativos mantêm suas orientações e ações. (#1739)
- O chat do site confirma o recebimento uma vez por conversa e identifica a entrada direta na fila da equipe, sem atribuir falha ao atendimento automático.
- A confirmação preserva dúvidas sem resposta, contador e prévia do Console; entrada e confirmação são gravadas juntas, inclusive com envios simultâneos.
- O cardápio do atendimento reúne tamanhos de receitas conhecidas no mesmo cartão; cada escolha conserva o item, preço e disponibilidade originais, sem mudar estoque ou dados de cadastro.
- Pedido cancelado com recebimento registrado mostra "Conferir acerto" na seção existente de devoluções. A abertura em um clique preserva permissões, histórico, confirmações pendentes e o resumo de devoluções já registradas.
- O chat do site recebe confirmação do pedido, link de pagamento disponível e valor do frete combinado, uma vez por etapa. Notas internas e espera por resposta da equipe permanecem preservadas. (#1748)
- O atendimento explica o sinal de 50% da encomenda antes de gerar a cobrança na maquininha, junto da escolha do meio, sem alterar a regra de pagamento.
- Histórico de acesso filtra pela empresa de origem do evento, com coluna nullable e índice próprios. Eventos legados sem origem ficam fora da visão da loja para impedir vazamento entre empresas de um usuário compartilhado.
- A ativação de avisos no aparelho permite tentar novamente quando o navegador não responde à permissão em 30 segundos. A desativação mostra o estado correto e mantém o botão bloqueado enquanto termina. (#1755)
- **Impressão na impressora certa** (#1756): cada aparelho guarda, por documento, a impressora (HP A4, HP A5, térmica 80 ou 58 mm, etiqueta 10×15 ou o diálogo do sistema) e o papel dela; o "Imprimir" já abre com essa escolha e diz como configurar o diálogo. A bobina deixa de sair em folha Carta: o comprimento é medido antes de imprimir, e conteúdo mais largo que o papel ou etiqueta que não cabe avisam em vez de sair cortados.
- O Banco de imagens mostra as capas reais do cardápio, com busca por produto e escolha de tamanho, antes dos originais e da produção assistida. Os arquivos e vínculos são preservados, sem importar fotos nem atribuir aprovação ao acervo.
- Produção do dia: confira porções, validade e peso em uma revisão única no computador e no celular, volte aos pratos sem perder o lote e receba foco no campo inválido antes de registrar a produção.
- No atendimento, o resumo ao encerrar mostra o total com frete, o que já foi pago e por qual meio; a mensagem marca quando foi lida; erros de envio e avisos do automático aparecem em português simples; o Balcão ficou com menos botões e a conversa ganhou espaço em telas de notebook.
- Registrar pagamento em conta a pagar e a receber voltou a funcionar: antes respondia erro interno e nenhum pagamento era gravado. Adicionar parcela a uma conta em rascunho também deixou de acusar conflito falso.
- Atendimento: avaliações recebidas por botão aparecem no histórico, conteúdos sem texto têm identificação e notas de reabertura mostram o horário da loja.
- Produção do dia: nomes e tamanhos ficam separados nos cartões, sem dividir o número da unidade e mantendo cada porção distinta.
- Avisos antigos recuperam o título original do envio quando disponível, preservando os registros e o isolamento entre empresas.
- Atualizados os pacotes de criptografia XML e PKCS já utilizados para 10.0.13, corrigindo os alertas de segurança detectados pelo CI.
- Pedido de compra: gerar pedidos da lista duas vezes com a mesma chave não cria mais pedido repetido nem avisa o fornecedor de novo; marcar como recebido um pedido com itens agora dá entrada no estoque; receber acima do pedido é recusado; pedido com recebimento parcial volta à lista de abertos; valor estimado sai em centavos; item com quantidade negativa é recusado. Banco de imagens: a miniatura deixa de ser baixada e redimensionada a cada exibição, e arquivo de tipo não aceito é recusado antes de ser decodificado, com a mensagem certa.
- Site: prato com porções passa a mostrar o preço que o pagamento cobra (antes mostrava o preço do item e cobrava a porção padrão), e prato com todas as porções esgotadas aparece indisponível; pedido sem janela pelo site respeita a loja fechada na mão; a consulta de frete pública ganhou limite por IP. Pedido de compra: fechar o recebimento com estoque também gera a conta a pagar automática.
- **Atendimento e comanda**: encerrar a conversa limpa o contexto do pedido anterior; a ficha mostra um resumo com próxima ação e abre pedido, pagamento, cliente e pós-venda em modais. A seleção compartilhada preserva edições e apresenta conflitos e falhas no próprio editor. (#1785)
- **Seleção e confirmação**: o resumo distingue seleção não confirmada, pedido criado e alteração ainda não aplicada. Salvar atualiza o link; criar pedido exige revisão explícita. A aprovação compara o pedido atual com a proposta e a recusa mantém o original. (#1785)
- Atendimento: repetir o envio de texto, foto ou áudio depois de uma resposta perdida não manda a mensagem duas vezes ao cliente; reenviar e encerrar ao mesmo tempo não duplicam a mensagem nem a automática de encerramento; o console para de consultar a inbox com a aba oculta e não relê a conversa aberta quando nada mudou. (#1786)
- Atendimento: mensagem enviada pelo console fica registrada mesmo se a aba for fechada durante o envio, e a conversa passa a ser da atendente; aviso de falha repetido pela Meta não gasta outra tentativa de reenvio; falha em botão ou em "SAIR" não contamina a próxima mensagem; mensagem programada e automática que estouram o tempo do canal viram falha em vez de ficar presas ou repetir; automática sem conversa informada faz a conversa subir na caixa de entrada; a lista de conversas não repete nem pula itens entre páginas. Console: envio para cadastro bloqueado avisa em vez de sair calado, a foto enviada não pisca e a varredura grande não tira a conversa aberta da tela.
- Atendimento automático: a comanda e os botões de janela que o agente envia no meio da conversa ficam gravados na hora. O aviso de entregue ou de falha da Meta deixa de se perder (o reenvio automático e o SMS voltam a valer para essas mensagens), a cópia que a Meta devolve não passa mais a conversa para a dona, e uma falha ao gravar o fim do turno não faz o agente responder de novo ao cliente.
- **Corridas do atendimento** (#1792): duas mensagens do cliente chegando juntas contam as duas como não lidas, e a mensagem que chega logo depois de marcar como lida não traz de volta as já lidas; responder pelo console enquanto outra aba encerra a conversa grava a mensagem sem reabrir a conversa; reenviar com clique duplo, ou no mesmo instante do reenvio automático, manda o texto uma vez só.
- **Corridas no chat do site e no pedido pela página** (#1797): a mensagem do visitante, ou o pedido, que chega logo depois de a equipe marcar a conversa como lida não traz de volta as mensagens já lidas; o pedido feito com a conversa encerrada no meio fica registrado nela sem reabri-la.
- Atendimento: permite à gerente registrar uma ocorrência em Pós-venda, preserva o relato após falha e mantém a resolução no histórico do pedido.
- Testes: distingue indisponibilidade temporária do pagamento online da encomenda com cobrança manual sem provedor configurado.
- Balcão: o canhoto deixa de oferecer "Imprimir PDF" quando o pedido está no EasyStok, para a impressão seguir o perfil do documento; o PDF não troca sinal desconhecido por "?", não repete a porção que o nome já diz e não imprime "Janela não escolhida".
- Cardápio: a tela de Categorias, sem categoria nova, mostra as categorias antigas que os pratos já usam e explica o que a conversão faz antes de converter.
- Cardápio: no Banco de imagens, a imagem aprovada pode ser usada em qualquer prato ("Usar em um prato") e o clique na foto do prato abre a escolha da imagem do acervo.
- Balcão: conversa reaberta de pedido entregue ou cancelado sai de "Precisa de você" até o cliente escrever, e a barra explica que só falta encerrar.
- Balcão: com a janela de 24 h fechada, "Gerar cobrança e enviar" fica travado com o motivo, a saída (modelo aprovado) e onde fica a comanda anotada.
- Login: quem já escolheu a conta Google neste navegador entra direto no console, sem passar pela tela de login; depois de "Sair" a tela espera a pessoa escolher de novo.
- Tela inicial (hall) enxuta: quatro números clicáveis e as áreas só com ícone e nome, tudo numa tela em 1280x720 (eram 54 controles e 2 telas de rolagem, agora 14). Favoritar, organizar e o modelo do painel ficam em "Ajustar tela inicial".
- Gaveta de Entregas: no computador o trilho fica fora da cortina; um clique em Módulos volta ao hall (antes o primeiro clique só fechava a gaveta e parecia travado).
- **Teste da comanda sem pagamento online** (#1811): passa a cobrir o pedido do dia, o caso que responde "indisponível"; entrega em outro dia é encomenda e aguarda o sinal externo. Só teste, sem mudança no produto.
- Login da loja: repetir em paralelo o código certo do OTP responde "código inválido", não "tentativas excedidas". Testes: a trava de outro host é solta explicitamente, sem depender do fim da conexão.
- **Cardápio de hoje à direita não cobre mais a conversa** (#1828): do lado do Resumo o painel alarga
  a coluna do Resumo, como do lado do Balcão já alargava o Balcão. Medido no laboratório, o amplo
  cobria 160 px da conversa em 1280, 156 em 1440 e 56 em 1920; agora 0 nas três, com a conversa
  em 320 px ou mais. Com a conversa estreita, o botão Assistente do cabeçalho fica só com o ícone
  em vez de cair numa segunda linha.

- **Cobrança na entrega** (#1776, item 19): o pedido só aparece "Pago" com o saldo zerado no servidor; com sinal pago, Entregas, a viagem e o roteiro impresso dizem "Receber R$ X na entrega". Marcar entregue com saldo a receber pela Cozinha, pelo Balcão ou pela viagem pede confirmação, e a API recusa sem ela (409 `SALDO_A_RECEBER`). (#1839)
- **Janelas, bloqueios e zonas de frete** (#1776, itens 20 a 22): a API recusa janela sobreposta no mesmo dia e mais de 500 vagas; bloqueio no passado, além de um ano, duplicado ou com janela de outro dia da semana. A lista de bloqueios mostra o ano inteiro, a janela editada acompanha o horário novo e a zona de frete ganha editar, excluir e aviso de bairro ou faixa de CEP repetidos. (#1839)
- **Link de compra sem endereço** (#1839): a cliente finaliza a seleção sem endereço ou com ele incompleto pelo caminho da entrega a combinar,
  sem pedido nem cobrança; a conversa recebe "Endereço pendente" e o endereço registrado na Ficha passa a aparecer na comanda.
- **Cardápio:** duplo clique na linha abre a edição; a ficha permite escolher uma foto aprovada do banco de imagens e salvá-la como capa ou na galeria, preservando o upload do computador. (#1841)
- **Reimpressão do histórico** (#1845): imprimir originais não avança a fila do pedido atual; cada nova via ganha uma tentativa própria vinculada ao original, com retentativa idempotente quando a gravação falha.

- O Banco de imagens permite buscar e paginar as fotos aprovadas na escolha para um prato, com recuperação de falhas e preservação dos filtros da tela ao fechar ou publicar.

- Balcão: o rascunho da nota permanece vinculado ao cliente original ao navegar e durante a gravação.
- Com janela de mensagens fechada, a cobrança pode ser criada para copiar o link, sem envio nem falsa falha no histórico.
- O resumo IA considera novas notas mesmo quando o dossiê já atingiu o limite de 20 notas.
- Extrato de pedido cancelado não apresenta dívida a receber; movimentos agregados são descritos como ajuste de saldo, sem presumir devolução.
- Cobranças canceladas mostram o registro original, sem instruções de sinal ou recebimento na entrega e sem oferecer nova baixa.
- Corrige o resumo dos cartões de Atendimento, Cozinha e Entregas para não afirmar fila vazia quando a leitura é parcial.
- Mantém nomes e setas dos módulos dentro dos cartões em larguras intermediárias, preservando o desenho aprovado nas telas maiores.
- **Fixtures de integração** (#1855): isola o PostgreSQL do agendamento, semeia empresa e pedido válidos, mantém a data de entrega futura, distingue o aviso de pagamento ao cliente das notificações internas e usa telefone próprio no cadastro do lead.

- **Saldo dos pedidos na cozinha**: calcula o valor a receber em lote, mantendo a mesma quantidade de consultas para 1 ou 200 pedidos e preservando estornos, créditos e isolamento por empresa. (#1859)
- **Edição de janelas legadas** (#1860): permite ajustar nome e vagas sem mudar dia ou horário, seguindo a API; novas janelas e alterações de período continuam recusando sobreposição.
- **Saldo a receber no lote do KDS** (#1863): `POST /api/kds/pedidos/status-lote` confere o saldo como o PATCH; o item "entregue" com valor a receber volta recusado com `codigo: SALDO_A_RECEBER` e `valor`, os demais seguem, e passa com `confirmarSaldoAReceber` no item. O console ganha `mudarStatusKdsEmLote` e `itensDoLoteComSaldoAReceber`.
- **Pente fino da jornada de atendimento** (#1865): o painel "Link do cliente" do Balcão não cai mais em 500 quando a conversa do visitante tem pedido do site (o link não expõe o pedido de outro cadastro); reemitir a cobrança de encomenda com sinal pago gera o link do restante ("Restante do pedido") em vez de "Pedido já pago"; o acompanhamento público do pedido traz `pagoCentavos` e o `initPointUrl` da cobrança pendente para o site mostrar o saldo e o botão de pagar; a Ficha diz "Sinal pago" quando só o sinal entrou; a nota do pedido do site no chat sai em formato brasileiro; a dica da janela aponta para "Seleção do atendimento".
- **Conta no menu do tablet** (#1872): a camada do avatar abria 213 px à direita e 100 px acima dele dentro do menu de 1024 px, porque o `<dialog>` do Modal guardava um `transform` e virava o bloco do `position: fixed`. Agora a camada é popover (camada de topo do navegador) e o Esc fecha só ela.
- **Janela em curso não é mais oferecida no site** (#1874): sem prazo mínimo informado a listagem de janelas só tirava a janela já encerrada, e uma janela das 10h às 12h seguia aparecendo para hoje às 11h30. Agora o prazo nulo vale 0 e só fica a janela que ainda não começou, no fuso da loja; o agente e o Balcão, que informam o prazo do pedido, seguem como antes. O checkout do site revalida com a mesma regra e recusa (`janela_abaixo_do_prazo`) a janela que já começou, mesmo vinda de uma página antiga. Contrato HTTP igual.
- **Requisição abortada pelo cliente deixa de sair como erro 500 no log** (#1874): o cancelamento por `RequestAborted` vira 499 sem exceção e um log de Debug, em vez de `Error` "responded 500" que escondia a falha de verdade. Vale também para o cancelamento embrulhado (driver do banco, `AggregateException`). Cancelamento com a requisição viva (timeout interno) segue pelo tratamento de sempre.
- **`GET /api/auth/google/config` responde 200 com o Google desligado** (#1874): `{ habilitado: false, clientId: null }` no lugar do 404 que o console registrava a cada carga. Com o Google configurado vem `habilitado: true` e o mesmo `clientId`.
- **Testes de integração da API de volta ao CI** (#1890): 32 testes de `Api.IntegrationTests` caíam
  no coverage do master e ninguém via. Oito classes passavam conexão e JWT só por
  `ConfigureAppConfiguration`, que chega tarde para o `Program.cs`; elas só conectavam pegando
  carona na variável de ambiente que outra classe deixava no processo, e seis ainda usavam a senha
  `postgres` que o `StartupHardening` recusa. Agora toda classe usa `UseSetting`, senha descartável,
  devolve as variáveis de processo ao terminar, e o projeto tem job próprio no `ci.yml`. Cinco
  testes desatualizados (202 do esqueci-senha, título com entidade HTML, 16 chaves do menu, usuários
  do seed demo) foram alinhados ao produto. Fecha #824, #1026, #784 e #912.

- Mercado Pago com credencial real exige `NotificationUrl` e `BackUrlSuccess` na subida; sem elas o link nunca baixava sozinho.
- Cliente que escreve depois do encerramento com pedido em andamento volta à conversa anterior em vez de abrir outra sem o pedido.
- Histórico de fechamentos informa o dinheiro que ficou na gaveta; a sugestão de abertura deixa de somar Pix e cartão.
- Timeout do envio ao WhatsApp vira falha incerta: sem reenvio automático nem reserva por SMS; o reenvio manual continua.
- Venda do pedido entregue pelo PWA nasce com a forma de pagamento do pedido.
- Remover item de pedido com valor recebido responde 409; a comanda confere a devolução.
- Trocar a forma do restante com sinal pago não responde "Pedido já pago" e cobra só o restante na entrega.
- Trocar só a data ou a janela da comanda preserva o frete combinado.
- `/health/notificacoes` ignora canal desligado de propósito por `Notifications:<Canal>:Desligado=true`.
- Link vencido pela segunda vez não cancela o pedido: a conversa recebe o aviso e a atendente reemite ou cancela.
- Agente automático de IA nasce desligado; o checkbox em Atendimento > Horários continua ligando.
- Criar usuário em cliente pelo SuperAdmin (`POST api/admin/usuarios-tenant` e "Novo usuário" do
  Admin) respondia 500: o RLS recusava o `Perfil` da empresa alvo porque o token do SuperAdmin não
  tem empresa. O caso de uso agora define o tenant da empresa alvo antes de gravar. (#1159)

### Added
- **Console do operador no repositório** (ADR-0054): o protótipo `prototipo-omni` da Casa da Baba
  (React 19 + Vite, commit de origem a9831ab) passa a viver em `EasyStock.Console/`, ainda com dados
  simulados. CI própria (`console.yml`: lint, fronteira de camadas e build) só quando a pasta muda.
  Matriz de paridade e ordem de ligação à API em `docs/plan/atendimento-whatsapp/10-console.md`. (#1120)
- **Console: caixa de entrada na API real** (F01): com `VITE_FONTE_DADOS=api`, login em dois passos
  (superadmin recusado), polling de 5 s da inbox, enviar, assumir, devolver ao automático, encerrar e
  marcar como lida pelos endpoints do S07; recusa da API (janela de 24 h, canal) aparece no balão.
  Simulações de cliente e digitação automática desligadas no modo real. Validado com WhatsApp real. (#1132)
- **Campanhas: cadastro e agendamento** (S28): entidades `Campanha` (rascunho, agendada, enviando,
  enviada, encerrada, cancelada; mensagem Scriban, arte, template de marketing, filtro de público,
  tags de restrição, ondas) e `CampanhaDestinatario` (único por campanha e cliente, motivo de
  exclusão). `GET|POST api/campanhas`, `GET|PUT api/campanhas/{id}`, `POST api/campanhas/{id}/cancelar`
  (policy `Gerente`; cancelar exclui só os pendentes) e `POST api/uploads/campanha/arte`. Migration
  `AddCampanhas` com RLS e índices `(EmpresaId, Status)` e `(ClienteId, EnviadoEm)`. Público (S29) e
  disparo (S30) ficam fora. (#1160)
- **Cardápio público mostra o saldo produzido** (#1171, desdobramento da S23): `estoqueAtual` do item
  vinculado deixa de ser 0 fixo e passa a trazer o saldo do `ItemEstoque` do produto (soma dos lotes
  com saldo e não vencidos, arredondada para baixo), numa única consulta por cardápio. `disponivel`
  segue a flag manual: saldo 0 não esgota o item, porque o checkout recusa item indisponível (#1158)
  e falta de estoque avisa, não trava o pedido (S17). Schema `menu-publico.v2` só teve a descrição
  do campo atualizada; a forma do contrato não mudou.
- **Configuração da vitrine pela loja** (P01-B): `GET|PUT api/minha-vitrine/configuracao` e
  `POST .../ativar|desativar` (policy `Admin`), com a vitrine resolvida pela empresa do token.
  Loja padrão de outra empresa responde 400 e domínio próprio já usado por outra vitrine, 409. (#1173, #1176)
- **Notificações no contexto da empresa** (P01-B): templates, rotinas, canais, kill switch e envios
  em `api/notificacoes/*` (policy `Admin`). Recurso de outra empresa ou global responde 404. (#1173, #1176)

- **Respostas prontas e mensagens automáticas** (S42): `RespostaPronta` (atalho único por empresa, 409
  no duplicado) e `RegraAutomatica` (uma por gatilho: primeiro contato, fora do horário, loja fechada,
  pagamento confirmado, pós-entrega e encerramento). CRUD em `api/atendimento/respostas-prontas` (com
  `GET {id}/render?conversaId=` que recusa variável sem valor) e `api/atendimento/automacoes`. O
  disparo roda nos handlers do outbox (`conversa.aberta`, `conversa.encerrada`, `pedido.pago`,
  `pedido.mudou_status` entregue), só dentro da janela do canal e com consentimento transacional; na
  entrada sai uma só (loja fechada > fora do horário > primeiro contato). Migration
  `AddRespostasProntasEAutomacoes` com RLS. (#1182)
- **Entregadores, viagens e chamado** (S44): `Entregador`, `Viagem` com paradas ordenadas e
  `ChamadoEntregador`, migration `AddEntregadoresViagens` com RLS. `api/atendimento/entregadores`
  (CRUD) e `api/atendimento/viagens` (criar, entregador, incluir, retirar e reordenar parada, sair,
  marcar parada entregue, desfazer), com link de rota no Google Maps sem chave. "Sair" leva todos os
  pedidos para `saiu_para_entrega` num commit só e publica `pedido.mudou_status` de cada um; sem
  entregador é recusado (RN-32) e cliente bloqueado não entra (RN-14). Cada parada guarda o retrato
  do entregador na saída. `GET api/atendimento/relatorios/entregas-por-bairro` (policy `Admin`). (#1183)
- **Avaliação em dois botões** (S26): `PedidoAvaliacao` ganha `Resultado` (positiva/negativa) e
  `Estrelas` passa a nullable (migration `AddResultadoPedidoAvaliacao`). O botão
  `acao:avaliacao:<resultado>:<pedidoId>` grava a avaliação sem LLM e responde ao cliente; a ferramenta
  `registrar_avaliacao` cobre o texto livre. `Entregue` agenda o pedido de avaliação no outbox para
  +30 min (`enviarApos` no payload vira `ProximaTentativaEm`), respeitando `AvisosStatusAtivos`; o
  provider da Meta manda botões dentro da janela e o template `avaliacao` com quick replies fora. Negativa
  passa a conversa para a dona até a S27. (#1185)
- **Ocorrência e reembolso** (S27): tabela `ocorrencias` (RLS) e `api/ocorrencias` (listar, obter, abrir,
  resolver). Abrir com conversa escala para a dona e publica `ocorrencia.aberta`. Resolver com reembolso
  estorna a cobrança paga (idempotência pelo id da ocorrência), grava `ClienteNota` e enfileira
  `ReembolsoEfetuado`; valor acima do pago dá 400; pago fora do gateway responde
  `reembolso_manual_necessario`; estorno recusado pelo Mercado Pago dá 502 e a ocorrência segue aberta. (#1186)
- **Pedido fora de área vai para a dona** (S14): quando a dona liberou o lead fora de área, o pedido
  criado pelo atendimento nasce com `RequerAprovacao` e, pago, espera em `AguardandoAprovacaoBaba` em vez
  de ir para a cozinha. (#1195)
- **Campanhas: público, exclusões e limite semanal** (S29): `POST api/campanhas/{id}/publico` calcula
  o público pelo filtro (todos, tags incluídas e excluídas, comprou o item em pedido entregue nos
  últimos N dias) e materializa os destinatários como pendentes ou excluídos pelo primeiro motivo da
  ordem bloqueado, sem consentimento (WhatsApp/marketing do S38, com o opt-in do cadastro quando não
  há linha), restrição, limite de uma campanha a cada 7 dias e sem telefone. Recalcular refaz
  pendentes e excluídos e preserva quem já saiu. Devolve total, pendentes, excluídos por motivo e
  amostra de 10 nomes. `GET api/campanhas/{id}/destinatarios?status=` lista com o nome. Sem
  migration. (#1197)
- **Console: gestão, assistente e consentimento na API real** (F02): com `VITE_FONTE_DADOS=api`, o
  controle Aberta/Fechada e o horário por dia gravam no expediente (S40); a Gestão ganha a aba
  Atendimento com "voltar a seguir o horário", mensagens de loja fechada e a configuração do atendimento
  (S08); o balão do assistente pergunta à API (S47); os avisos por E-mail e SMS da Ficha gravam o
  consentimento transacional do cliente (S38). Modo demonstração sem mudança. (#1201)
- **Campanhas: disparo, ondas, encerramento e lembrete** (S30): o `CampanhaJob` (a cada 60 s, advisory
  lock, flag `BackgroundJobs:EnableCampanhaJob`) dispara a primeira onda no `DisparoEm`, enfileirando
  no outbox de notificações uma mensagem por cliente (WhatsApp, categoria Marketing,
  `ProximaTentativaEm = DisparoEm`, template da Meta `campanha_generica` ou o da campanha nos
  metadados) até `TamanhoOnda`, quem já comprou o item primeiro. Bloqueio, consentimento, limite
  semanal e telefone são reconferidos no disparo. O job concilia o outbox (`Enviado`/`Falhou`),
  conclui a onda e, no `EncerramentoEm`, encerra e lembra quem recebeu e não pediu. Onda seguinte só
  por `POST api/campanhas/{id}/ondas`. Pedido da conversa de quem recebeu campanha nos últimos 7 dias
  marca o destinatário como `Pediu`. Cancelar tira da fila as mensagens ainda pendentes. Templates
  `campanha_marketing_whatsapp_v1` e `campanha_lembrete_whatsapp_v1` no seed. Sem migration. (#1202)

- feat(console): no modo API a comanda da conversa vira pedido real (S10) com cobrança do Mercado Pago (S11) enviada ao cliente pela conversa; a ficha acompanha pendente, pago e expirado pela polling. Endpoints novos em `api/atendimento` (cardápio, janelas e pedido da conversa) (#1210).
- **Cozinha do console na API real** (F05): com `VITE_FONTE_DADOS=api`, a tela `#/cozinha` lê a fila do
  KDS sobre Pedido (`api/kds/pedidos`), atualiza pelo SSE de operação com JWT, mostra início previsto e
  atraso, muda o passo pela API e abre ou reenfileira o canhoto. Sem a variável, nada muda. (#1218)
- **Entregas do console na API real** (F04): com `VITE_FONTE_DADOS=api`, a gaveta e a janela de Entregas
  leem pedidos prontos e em rota, viagens, entregadores e chamados (S44), aprovam a exceção fora de área
  (S12/S14) e cadastram janelas, zonas de frete e bloqueios (S45). O KDS (`api/kds/pedidos`) passa a
  devolver `endereco`, `requerAprovacao` e `motivoRequerAprovacao`. Sem a variável, nada muda. (#1221)
- **Deploy da VPS versionado** (`scripts/deploy/vps-deploy.sh`): envia o `git archive` do SHA, builda
  api, worker e web com `GIT_SHA`, faz `pg_dump` antes de subir (a migration roda no startup da API),
  guarda a imagem anterior como `vps-prev-<ts>` e volta para ela se o deploy não ficar healthy. Trava
  (`flock`) recusa dois deploys ao mesmo tempo; `--dry-run` mostra o plano. Testes do script remoto em
  `scripts/deploy/vps-deploy.test.sh`, com `docker` falso num container. (#1223)

- **Arte da campanha na mensagem**: com `ImagemUrl`, a onda leva a imagem no cabeçalho do template da
  Meta (`campanha_generica` ou o da campanha, parâmetro `header` do tipo `image`) e, dentro da janela
  de 24 h, sai como mensagem `image` com o texto na legenda (acima de 1024 caracteres, imagem e depois
  o texto). O lembrete segue sem arte. Sem migration. (#1226)
- **Pedido do site atribuído à campanha**: o checkout do storefront (logado e guest) aplica a mesma
  regra do pedido da conversa (`AtribuicaoPedidoCampanha`): quem recebeu campanha nos últimos 7 dias
  vira `Pediu`. (#1226)
- **Interesse em item: fechar e listar** (S31): `POST api/clientes/{id}/interesses/{interesseId}/atendido`
  para a dona fechar o interesse (idempotente, 404 para outra empresa ou outro cliente) e
  `GET api/clientes/{id}/interesses` para o dossiê. O pagamento confirmado do pedido (`pedido.pago`, pela
  conversa ou pelo site) fecha sozinho os interesses abertos do cliente nos itens comprados. (#1228)
- **Console avisado quando o pedido chega pela página do cardápio** (S48): depois do commit,
  `CriarPedidoPeloCardapioConversaUseCase` publica no SSE de operação (S18)
  `conversa.pedido_pela_pagina` `{conversaId, pedidoId, numero, total}` e `conversa.mensagem_recebida`
  `{conversaId, mensagemId}` da mensagem de resumo. Falha do aviso só gera log; o pedido segue. (#1229)
- **Spec de fechamento do console do operador** (F06 a F17): correções do modo API, backend, paridade visual com o protótipo, caixa, fidelidade, integrações com teste de conexão e Lalamove. (#1232)
- **Lote de papel no EasyStok** (#1241, F11, S46): no modo API, o que foi marcado no papel durante a queda de conexão vai para `POST api/atendimento/esteira/lote`, com uma linha por etapa (o pedido não pula de pago para entregue). Nenhuma mensagem falsa entra na conversa, porque o EasyStok não avisa o cliente do que já passou. Pedido recusado aparece com o motivo. A falta de rede vista pela sincronização liga a faixa do papel.
- **Cardápio do dia pelo console** (#1241, S45/S17): o operador liga e desliga o item hoje e ajusta o saldo contado em `api/atendimento/comanda/cardapio/{item}/disponivel` e `/saldo`. O produto do saldo é resolvido no servidor, e o ajuste reusa o S22. O cardápio é relido na hora e para de voltar ao estado anterior depois de 60 s.
- **Item do cardápio pelo console, para Gerente** (#1241, S45): incluir, editar, tirar e repor em `api/atendimento/comanda/cardapio` (`POST`, `PUT`, `{item}/visivel`, `fora`). Tirar esconde o item, nunca apaga. Os alertas de produto vendido sem saldo vêm de `api/estoque/desacertos`.

- **Integrações por loja com teste e vigia** (F16, go-live): chaves de Mercado Pago, Google Maps e
  Lalamove cifradas por loja (AES-256-GCM, chave mestra obrigatória em produção, RLS), API
  `api/integracoes` (listar, salvar, testar, desativar; o segredo nunca volta), vigia a cada 15 min com
  lembrete, aba Integrações do console no modo API e faixa "Integração parada" com atalho. (#1246)

- **Backup externo cifrado no Google Drive** (`scripts/infra/backup-externo/`): cron do usuário às
  03:45 envia o dump mais recente e o tar.gz do volume de uploads para `gdrive-crypt:easystok/diario/`
  (rclone `crypt` sobre `drive`, cifrado na VPS), confere com `rclone cryptcheck` e mantém 7 diários e
  4 semanais. `testar-restauracao.sh` restaura num postgres descartável. `.ps1` locais mandam cada
  script por ssh em base64. Passo a passo em `docs/dev/backup-externo.md`. (#1253)
- **Plano dos impressos da Casa da Baba** (onda 9, S49 a S51): motor de modelos por papel (etiqueta
  térmica 10x15, cupom 58 mm, A4 jato de tinta econômico), layout aprovado do Pedido versionado em
  `docs/plan/atendimento-whatsapp/impressos/` e backlog dos demais documentos. (#1267)
- **Impresso do pedido da Casa da Baba** (S49): `GET api/pedidos/{id}/impresso?modelo=etiqueta-10x15|cupom-58|a4`
  (policy `ImpressaoFila`, `?nota=` opcional) devolve o pedido em HTML com `@page` do tamanho do papel: térmicos só em
  preto, A4 colorido sem fundos cheios, fontes Lora e Nunito Sans (OFL) servidas em `/impressao/fontes/`, CODE128
  do número em SVG. Agendado mostra janela, "pronto até" (30 min antes) e solicitado; imediato mostra a previsão.
  O total diz se cobra na entrega ou se já foi pago; telefone do cliente completo. (#1269)
- **Rota real no frete por raio**: com `GeocodingProvider=google` e chave, a distância de carro medida
  pela Google Routes API decide a faixa de frete no lugar de `haversine × FatorRota`, e a duração da
  rota vira o ETA (20 min de preparo + deslocamento). Sem chave ou com falha, o cálculo anterior segue
  intacto. Chave só no header `X-Goog-Api-Key`. (#1274)
- **Cadastro do cliente pela conversa** (F18, go-live): `POST api/atendimento/conversas/{id}/cliente` acha
  ou cria o cliente pelo telefone, vincula à conversa e grava o endereço de entrega padrão; o console liga
  "Salvar cadastro", "Cadastrar endereço" e a edição do cliente, e a Ficha relê o dossiê. Conversa fora do
  WhatsApp passa a gerar pedido. (#1276)
- **Plano da comanda de cozinha** (S52 e S53): layout aprovado em
  `docs/plan/atendimento-whatsapp/impressos/comanda-aprovada.html`, com alergia vinda das tags `alergia_*` do
  cadastro, número do dia e separação de congelado. (#1279)
- **Comanda de cozinha** (S52): `GET api/pedidos/{id}/comanda?modelo=etiqueta-10x15|cupom-58` (policy `ImpressaoFila`)
  imprime o papel de produção sem preço nem endereço: pronto até, itens agrupados por linha com porção, molho e
  observação, caixa por item, "feito por" e "embalado por", e faixa de alergia vinda das tags `alergia_*` do cadastro
  do cliente. Número do dia e separação de congelado chegam na S53. (#1281)
- **Número do dia e conservação do item** (S53): o pedido ganha `NumeroDoDia` ("042") por empresa e dia de produção,
  tirado de um contador atômico (`sequencias_pedido_dia`, com RLS) quando entra na fila, é pago ou é reagendado para
  outro dia (pelo agendamento ou pela troca de janela do console). O item do cardápio ganha `Conservacao` (ambiente,
  refrigerado, congelado; campo `conservacao` em `api/minha-vitrine/cardapio`), copiada para o pedido no checkout. O
  canhoto de cozinha mostra o número grande sob o rótulo "Pedido" ("Comanda nº" é o número da conversa) e divide
  "Preparar em casa" em congelado e refrigerado. Migration `AddNumeroDoDiaEConservacao`. (#1283)
- **Provedor do agente de atendimento** (#1305): `Anthropic:ApiKeyAgente` dá ao agente uma chave própria e
  `Anthropic:AutenticacaoBearer` envia `Authorization: Bearer`, para apontar `BaseUrl`/`ModeloAgente` a um provedor
  no formato Anthropic Messages (ex.: DeepSeek V4.1 Flash na Fireworks) sem mudar o gerador de descrições.
- **Plano do ERP da Casa da Baba** (#1316): ADR-0056 (tenant único, front único no `EasyStock.Console`
  com login, sala de módulos e módulo; permissão por perfil × módulo; roupa da marca; `EasyStock.Web`
  fica para o bastidor) e `docs/plan/erp-casa-da-baba/` com o estudo, o mapa M0 a M8, site e impressos,
  78 fatias `Mx.y`/`SI.n` e as decisões pendentes consolidadas. ADR-0048 (FMA como 2º tenant) passa a
  Superseded. Só documentação.
- **Homologação orgânica do atendimento** (#1318): medida a aderência do console e do agente com 46 conversas
  simuladas (61 %), 14 achados Altos e spec de correção H00 a H16 revisada por banca de 5 lentes, em
  `docs/plan/atendimento-whatsapp/13-homologacao-organica.md`. Scripts da bateria em `scripts/homologacao/`.
- **Caderno da loja para o agente** (#1319, S54): trechos de conhecimento por empresa (`api/atendimento/caderno`).
  Os do núcleo entram sempre no prompt do agente; os demais entram como índice e o texto vem pela ferramenta
  `consultar_caderno`. Tabela `caderno_trechos` com RLS.
- **TomTom como provedor de mapas do frete**: com `Storefront:Frete:GeocodingProvider=tomtom` e
  `TOMTOM_API_KEY`, a geocodificação e a rota real de carro do frete por raio passam pela TomTom
  (plano gratuito, sem cartão), alternativa ao Google enquanto o faturamento dele não sai. Sem chave,
  NoOp e o cálculo anterior segue intacto. Google e Nominatim continuam selecionáveis. (#1322)
- **Login com Google** (#1324): `POST api/auth/google/login` valida o ID token do Google e entra no usuário que já existe
  (alias do Gmail aceito com uma conta só), na empresa dele; botão "Entrar com Google" no console. Liga com `Auth__Google__ClientId`.
- **Carga do cardápio real** (#1334): `scripts/cardapio/` com os 43 itens da Casa da Baba (um por sabor e tamanho) e script idempotente que carrega pela API `api/minha-vitrine/cardapio`.
- **Plano das notificações de plataforma** (S0, #1344): plano SDD em `docs/plan/notificacoes-sistema/`
  com diagnóstico medido e specs N0 a N13, revisado por banca adversarial; ADR-0057 (motor interno,
  remetente próprio fora do atendimento, entrega honesta) e `scripts/diagnostico/notificacoes-s0.sql`,
  somente leitura, para medir o backlog e o papel do banco em produção.
- **E-mail real pelo motor, sobre MailKit** (ADR-0057, N3): o `SmtpEmailService` troca o `System.Net.Mail.SmtpClient` pelo
  MailKit. Uma conexão por envio, teto de `Smtp:TimeoutSegundos` (padrão 20) e cancelamento pelo token do chamador; porta 465
  com TLS implícito e qualquer outra com STARTTLS obrigatório, nunca "se o servidor oferecer". `IEmailService.EnviarAsync`
  devolve o desfecho (`Enviado`, `Simulado`, falha transitória ou permanente): 5xx do servidor é permanente; 4xx, rede e teto
  estourado são transitórios; uma tentativa por chamada, porque a retentativa é só do outbox. Autenticação recusada vira erro
  de configuração no log, nomeando `Smtp:*` e nunca a senha. Log sem endereço de e-mail. (#1351)
- **Remetente por categoria**: reset de senha e conta criada pelo admin saem de `seguranca@`, e também toda mensagem do outbox
  de categoria `Seguranca` (os produtores dessa categoria chegam com a N8 e a N13); o resto sai de `avisos@`. A confirmação de
  cadastro saiu junto com a rota `/api/auth/register` (N0). Chaves `Smtp:Seguranca:Username`, `Password`, `FromEmail` e
  `FromName`; sem elas a segurança cai no remetente de avisos, com aviso na subida. Toda mensagem leva
  `Auto-Submitted: auto-generated` e `Message-ID` no domínio do remetente. (#1351)
- **Mailpit no `docker-compose.local.yml`** (http://localhost:8025) e testes de integração com Testcontainers contra um
  Mailpit de verdade (HTML íntegro, remetente por categoria, anexo byte a byte, STARTTLS e TLS implícito com certificado
  próprio), com canário anti-skip que fica vermelho na CI se o container não subir. (#1351)

- **Reenvio de mensagens que falharam** (#1355): falha temporária do WhatsApp (Meta fora, rede) é reenviada sozinha em 1, 5, 15 e 60 min, até 6 h; falha permanente e timeout ("envio incerto") não insistem. O console ganhou o botão "Reenviar" no balão que falhou.
- **Catálogo de notificações de plataforma versionado** (N13, #1369): quatro tipos novos (`ConviteAcesso`, `IncidenteSistema`, `PrazoEstourado`, `ResumoDiario`), rotina global e template de e-mail (HTML embutido) e de WhatsApp (metadados do modelo da Meta) para cada um e para `ResetSenha`, que passa a categoria `Seguranca` com e-mail e WhatsApp. O seed global agora é versionado: template do sistema com versão maior no catálogo ganha linha nova ativa e a anterior fica inativa; o que foi editado por pessoa não é sobrescrito e loga aviso, e a rotina do sistema é atualizada no lugar. Os quatro canais sem template saem das rotinas. Endpoint de superadmin `POST api/admin/notificacoes/disparo-teste` (motivo auditado, 10 por hora, destinatário sempre o e-mail do próprio superadmin, assunto `[TESTE] ...`) e `GET api/admin/notificacoes/disparo-teste/{eventoId}` com canal, status, provider e erro curto. `IEmpresaPadraoResolver` extraído do `AuthController`.
- Modo `todos` da rotina (`ParametrosJson.modoCanais`, uma mensagem por canal permitido) e chave `canais` no payload do evento para restringir os canais da rotina. Janela de horário da rotina (`JanelaInicio` e `JanelaFim`, fuso de Brasília): fora dela o outbox nasce com `ProximaTentativaEm` na abertura, sem suprimir; `Seguranca` ignora a janela e o `enviarApos` do payload vale se for mais tarde. `INotificadorService.EnfileirarEventoAsync` aceita `correlationId` (depois do `ct`). `POST api/notificacoes/rotinas` aceita `canais` em ordem de preferência.
- **Contato verificado e audiência de usuários internos** (N4, #1373): `Usuario` ganha `Telefone` (E.164 BR, VO `TelefoneE164`, que agora é a única regra e o `NormalizadorTelefone` só delega), `TelefoneVerificadoEm` e `EmailPendente` (migração aditiva `AddContatoUsuario`, três colunas nulas em `usuarios`). `IResolvedorAudiencia` resolve `ParametrosJson.audiencia` (`usuario`, `admins`, `gestores`, `superadmins`): e-mail para usuário ativo (em audiência coletiva, só com e-mail confirmado), WhatsApp só com telefone verificado, opt-in explícito na categoria da rotina e `Notifications:WhatsApp:Plataforma:PhoneNumberId` configurado; preferência desligada pula a pessoa, menos em `Seguranca`. Uma mensagem por pessoa e canal: a chave de idempotência de negócio inclui o destinatário só na audiência (a do cliente final não muda). Os superadmins vêm de consulta própria pela porta de bypass, em escopo de DI próprio, e a audiência `superadmins` só vale em rotina global (`POST` e `PUT` de rotina da empresa respondem 400). Interruptor `Notifications:Audiencia:Habilitada` (`false` volta ao destinatário só pelo payload).
- `PUT api/auth/me/telefone` (senha atual; zera a verificação, avisa o e-mail atual, derruba as sessões) e `POST api/admin/usuarios/{id}/telefone/verificar` (SuperAdmin, motivo de 10 caracteres, auditoria e consentimentos de WhatsApp `Seguranca` e `Operacional` em nome do superadmin). Tipo `ContatoAlterado` (80, categoria `Seguranca`, só e-mail), rotina `contato_alterado_global` e template `contato_alterado_email_v1`; `confirmacao_email_global` passa a `Seguranca`. Export de dados pessoais traz telefone, verificação, consentimentos e preferências; a anonimização revoga os opt-ins, zera o IP de origem e apaga as preferências.

- **Problemas no sistema avisam os superadmins** (N10): API que para de responder, banco crítico, falha de Redis e pico
  de erros 5xx publicam o evento `IncidenteSistema` por e-mail (WhatsApp quando a N6 existir), com aviso de "normalizado"
  e a duração. Dedupe de 15 min pela chave de idempotência do outbox, sem tabela nova; o aviso nunca carrega mensagem de
  exceção, query string nem dado de cliente. Interruptor `Notifications:Incidentes:Habilitado`; o Worker precisa de
  `Auth__Google__EmpresaPadrao` no `.env`. (#1375)

- **Prazo estourado chega à dona e à equipe** (N11, #1376): os quatro prazos vivos (cliente sem resposta, pedido atrasado, impressão travada e caixa esquecido) passam a enfileirar o evento `PrazoEstourado` no motor, que entrega por e-mail e WhatsApp à audiência `gestores` (Admin e Gerente), dentro da janela das 07:00 às 22:00 de Brasília. Payload sem dado de cliente (`tipo_legivel`, `referencia`, `prazo_texto`, `atraso_texto`), chave de negócio `prazo:{tipo}:{id}` e `CorrelationId` determinístico `prazo:{tipo}:{id:N}` (59 caracteres no máximo). O cliente sem resposta vai ao atendente que assumiu a conversa; sem atendente, aos gestores. Pagamento sem baixa e lembrete manual seguem só no Web Push.
- Pedido atrasado enfileira antes do `CommitAsync()` da marca `AtrasoNotificadoEm` (evento e marca saem juntos). Impressão travada ganha `NotificarImpressaoTravadaUseCase`: um escopo por impressão, com o tenant ligado (RLS) e pré-checagem do `CorrelationId` em `IEventoNotificacaoRepository.ExisteCorrelacaoAsync`, então o SSE repete a cada rodada e o e-mail sai uma vez. O miolo do `CaixaEsquecidoJob` foi extraído para `AvisarCaixasEsquecidasUseCase` (mesmo comportamento do sino) e acrescenta o `PrazoEstourado`; a marca `NotificadoEsquecidoEm` segue única e a chave determinística evita aviso externo duplo se o processo cair entre publicar e carimbar.
- Configuração `Notifications:Prazos`: `Habilitado` (padrão `true`; `false` pula o enfileiramento nos quatro adaptadores, sem deploy), `ClienteSemRespostaMin` (10) e `ImpressaoPendenteMin` (3).

- **Rotinas agendadas por horário diário local e resumo diário** (N12, #1377): a rotina da empresa ganha `ParametrosJson.agenda.horario` (`HH:mm`, hora de Brasília). O novo `ColetorRotinasAgendadas` roda a cada 5 min no loop do motor, gera um evento por rotina devida por dia (`CorrelationId` `agenda:{rotinaId}:{yyyyMMdd}`) e usa o próprio evento como última execução persistida, com índice único `(EmpresaId, CorrelationId)` em `notif_eventos` (migração aditiva `AddIndiceUnicoCorrelacaoEvento`, o `Down` só remove o índice). Catch-up só dentro do mesmo dia local: Worker parado depois da meia-noite pula o dia. O primeiro tipo agendado é o `ResumoDiario` (entregues, faturamento, ticket médio, pendentes, caixa e Pix do dia da empresa, em pt-BR); cada tipo novo só acrescenta um `IConstrutorPayloadAgendado`. A rotina `resumo_diario_global` segue como molde inativo (com `agenda.horario` `20:00`) e o resumo só sai para a empresa que criar a própria rotina ativa.

- **Janelas de entrega por script** (#1381): `scripts/entrega/configurar_janelas.py` cadastra terça a sábado, manhã e tarde, 3 vagas cada, pela API do console; idempotente e simula por padrão.
- **WhatsApp de plataforma pelo 2º número da WABA** (N6, #1383): `OrigemRemetente` (Loja ou Plataforma) é propriedade do tipo do evento (`RemetentePorTipoEvento`, sem valor padrão: tipo novo sem classificação quebra o teste), gravada em `notif_outbox_mensagens.Remetente` (padrão `Loja`) e entregue ao canal como `ProviderOverride`. O `WhatsAppCanal` escolhe o provider pela chave (`whatsapp:plataforma`); override sem provider é falha permanente `provider_nao_configurado`, sem cair no número da loja. O atendimento em produção não muda: sem override o provider da loja segue como sempre.
- `MetaCloudWhatsAppPlataformaProvider` (só template, sem `Conversa`, sem tenant) sobre `IClienteWhatsAppPlataforma`, implementado pelo `WhatsAppCloudClient` com `phone_number_id` explícito (`Notifications:WhatsApp:Plataforma:PhoneNumberId`). Botão `url` (`botaoUrl0` e `botaoUrl1`) e `biz_opaque_callback_data` (`{EmpresaId:N}.{OutboxId:N}`) no JSON do template, que sai de um ponto só (`MetaTemplatePayload`). Copy code: o mesmo código no corpo e no botão, até 15 caracteres (falha antes da rede). Marketing e template recategorizado para marketing nunca saem pela plataforma. No máximo uma vez: timeout, queda depois do envio e 5xx são `Indeterminado`; só falha de resolução de nome, conexão, TLS e disjuntor aberto são transitórias.
- Webhook `GET` e `POST api/webhooks/whatsapp-plataforma` (anônimo, política `webhook-meta`, HMAC do app e verify token próprio `Notifications:WhatsApp:Plataforma:VerifyToken`): `sent`, `delivered` e `read` fecham `EmEnvio` ou `Indeterminado` como `Enviado` (grava o `ProviderMensagemId`), `failed` fecha `Enviado` ou `Indeterminado` como `Falhado`, tudo monotônico e sob RLS (o opaco fixa o tenant). Contadores `notifications.whatsapp.plataforma.alerta{codigo}`, `status_sem_opaco` e `cobrado_como_marketing`. Resposta automática "não monitorado" uma vez a cada 24 h por remetente, sem `Conversa`.
- `template_category_update` no callback do app (`CamposWebhookMeta` roteia por `field`): categoria marketing, no aviso ou consumada, grava `notif_templates_meta_estado` (tabela global) e o envio seguinte falha permanente `template_recategorizado_marketing`. Field desconhecido devolve 200 sem `empresa_desconhecida`.
- Migração `AddWhatsAppPlataformaN6` (aditiva): coluna `Remetente` e tabela `notif_templates_meta_estado`.
- Validação de startup na API e no Worker: com `Plataforma:Provider=meta`, `PhoneNumberId` (só dígitos, até 32) e `VerifyToken` são obrigatórios, e token e `AppSecret` da Meta também.

- `POST api/auth/reset-password-code { email, codigo, novaSenha }` e a tela `/auth/redefinir-senha-codigo` do Web. As páginas de redefinição respondem `Referrer-Policy: no-referrer`.
- Tipo `SenhaAlterada` (valor 81, remetente Plataforma, quarentena de 30 min), rotina global `senha_alterada_global` (modo `todos`, audiência `usuario`), templates `senha_alterada_email_v1` e `senha_alterada_whatsapp_v1` (modelo da Meta `senha_alterada`, utilidade, parâmetro `data`). O template WhatsApp do reset sobe para a versão 2 e usa `codigo_expira_em_minutos` (10); o evento `ResetSenha` ganha essa chave.
- `LimpezaTokensAcessoService` (Worker, a cada 6 h; `Auth:LimpezaTokens:Habilitada` e `IntervaloHoras`) apaga em lote os `reset_tokens` expirados há mais de 24 h. Sai o `DeleteExpiredAsync` sem chamador.
- Migração `AddSegredosDeAcessoN8` (aditiva): `reset_tokens.Finalidade varchar(20) NOT NULL DEFAULT 'Reset'`, `Tentativas int NOT NULL DEFAULT 0`, `Canal varchar(20) NULL` e índice parcial `(UsuarioId, Finalidade) WHERE Usado = false`. As linhas existentes viram `Reset` e continuam valendo.
- Config `Auth:LinkRedefinirSenha` (modelo com `{0}`; vazio usa a origem confiável mais `/auth/redefinir-senha?token={0}`). Sem base de link não há envio e o log nomeia a chave.

- **Primeiro acesso por convite com link** (N9, #1389): `POST api/usuarios` aceita `Senha` opcional. Sem senha nasce o convidado (`Usuario.CriarConvidado`, ativo, hash inutilizável com o marcador `$2a$10$CONVIDADO_`, o login por senha falha e o hasher devolve `false` sem lançar) e sai UM evento `ConviteAcesso` com um token por canal (32 bytes em base64url, 72 h, só o hash no banco, finalidade `Convite` em `reset_tokens`): link por e-mail e, com telefone e o atestado da dona (`AtestaOptInWhatsApp`, gravado como `ConsentimentoNotificacao` com ela como autora), token no botão URL do modelo `convite_acesso_link` (`botaoUrl0`, template WhatsApp em v2). Sem atestado o payload restringe a `Email`. Superadmin nunca nasce por convite. Com `Senha` (legado) nada muda; sai na M7.2.
- **Aceite**: `POST api/auth/aceitar-convite { token, novaSenha }` (anônimo, limite por IP da N8, 429 com `Retry-After`). Abrir o link nunca consome (a tela `/auth/convite` do Web só mostra o formulário e lê o token da query ou do fragmento). O POST consome por `UPDATE` condicional (2 POST simultâneos: 1 sucesso), define a senha pela política existente, verifica o canal do token (e-mail marca `EmailConfirmado`; WhatsApp marca o telefone verificado e grava os opt-ins de Segurança e Operacional com data e IP), grava `ConviteAceitoEm` e `ConviteAceitoVia` já mascarada (`+55•••1234`, `e-mail` ou `Google`) e revoga os outros convites. Revogado, usado, vencido, de superadmin ou de conta inativa: sempre "Convite inválido ou expirado." Não emite sessão: a pessoa volta ao login.
- `POST api/usuarios/{id}/convite` (Admin) reenvia: revoga os abertos e emite novos, 3 por hora por usuário contados nas linhas (429).
- Audiência `convidado` no `IResolvedorAudiencia`: o e-mail do convidado sempre, e o WhatsApp só com o atestado e sem exigir telefone verificado, só enquanto o convite está pendente e só na rotina `convite_acesso_global`. As demais audiências seguem exigindo telefone verificado.
- `GET api/usuarios` devolve `convite { estado: pendente|aceito|nenhum, aceitoEm, via }`; o formulário "Convidar" do Web deixa de pedir senha (telefone e atestado opcionais) e a lista mostra "Convite pendente" com "Reenviar convite" ou "Convite aceito via ...".
- Configuração `Auth:LinkConvite` (modelo com `{0}`; vazio usa a primeira origem confiável mais `/auth/convite?token={0}`). Sem base de link a criação do convidado recusa antes de gravar.
- Migração aditiva `AddConviteAcessoN9`: `usuarios.ConviteAceitoEm timestamptz NULL` e `usuarios.ConviteAceitoVia varchar(40) NULL`.

- **Retomada fora da janela de 24 h** (#1391): com um modelo aprovado configurado na Gestão › Atendimento, o reenvio manda o modelo e a mensagem que falhou sai sozinha quando o cliente responde.
- **Não entregues** (#1391): faixa no Balcão com as mensagens que não chegaram ao cliente, com Reenviar e Abrir conversa.
- **Reserva por SMS** (#1391): pronta e desligada; liga só com `Atendimento:ReenvioMensagens:SmsReserva` e um provedor de SMS real.
- **Transcrição de áudio do WhatsApp** (#1398): depois que o áudio recebido é armazenado, o Whisper da Fireworks
  (`whisper-v3-turbo`, `pt`) transcreve e grava o texto em `Mensagem.Transcricao` (migração `AddTranscricaoMensagem`).
  O agente passa a ler `[áudio] <texto>` e o console mostra a transcrição abaixo do player. Seção `Transcricao`;
  sem `Transcricao:ApiKey`, reaproveita `Anthropic:ApiKeyAgente` quando o agente já usa a Fireworks. Sem chave ou
  `Transcricao:Enabled=false`, nada muda.
- **WhatsApp da loja por coexistência** (Embedded Signup v4): botão "Conectar WhatsApp Business" no console
  (Gestão › Entregas e integrações) e `POST api/integracoes/whatsapp/coexistencia`, que troca o code pelo
  business token, inscreve o app na WABA, confere o número, vincula à empresa, grava o token cifrado e pede a
  sincronização dos contatos (o histórico anterior não é importado). Envio e mídia da empresa usam o token dela (sem ele, o global). O que a loja responde pelo app
  do celular chega por `smb_message_echoes` como mensagem de saída da dona e assume a conversa, então o agente
  não responde por cima; `history` e `smb_app_state_sync` são aceitos e registrados. Config nova
  `Notifications:WhatsApp:Meta:AppId` e `EmbeddedSignupConfigId`; setup em `docs/dev/whatsapp-coexistencia.md`. (#1417)
- **Sugestão do agente no console em modo API**: o "Sugerir" do painel do agente (Ficha › Agente da casa) chama
  `POST api/atendimento/conversas/{id}/sugestao`, que monta o mesmo contexto do agente do automático (prompt,
  caderno, dossiê, histórico com as transcrições de áudio) e chama o mesmo LLM, só com as ferramentas de consulta.
  O texto volta para a dona usar no rascunho; nada é enviado ao cliente, nada é gravado e a conversa não muda de
  situação. Agente desligado (`Anthropic:Enabled=false`) ou sem chave devolve 503 com a mensagem no painel. A
  montagem do contexto e o laço do LLM saíram do `AgenteAtendimentoService` para `GeracaoAgenteAtendimento`,
  reaproveitados pelos dois. (#1420)
- **Mensagem programada pelo console** (#1424): botão "Programar" no compositor (modo API) agenda texto ou modelo aprovado da Meta na API de mensagens programadas (S39), lista as da conversa com a situação e cancela as agendadas. O erro da API (janela de 24 h, consentimento, sem telefone) fica na modal; conversa sem cliente cadastrado mostra o motivo. A listagem de mensagens da conversa passa a expor `programada`, e o balão mostra o selo.
- **Lembretes do Console no modo API** (#1426): criação com retentativa idempotente, conclusão e vistos persistidos. Falhas preservam o rascunho e a lista; a API valida empresa e destinatário. Os avisos InApp permanecem no sino transversal, que também permite ativar/desativar WebPush neste aparelho. A inscrição pertence à conta atual, é invalidada no navegador ao sair e só recebe mensagens da mesma empresa.
- **SLA de primeira resposta por loja**: um prazo só para a loja (padrão 5 min, de 1 a 240), editável na aba
  Atendimento da Gestão e gravado em `ConfiguracaoAtendimento.SlaRespostaMinutos` (migration
  `AddSlaRespostaConfiguracaoAtendimento`). A inbox da API passa a mandar `ultimaMensagemEntradaEm`,
  `aguardaResposta` e o SLA da loja em cada conversa. No console, o cartão pisca quando o cliente espera além do
  prazo (com movimento reduzido fica só o destaque de cor), os estourados sobem para o topo de "Precisa de você" e
  a contagem pausa fora do horário da loja. O lembrete `ClienteSemResposta` e o `PrazoEstourado` usam o SLA da
  loja; sem configuração gravada, segue valendo `Notifications:Prazos`. (#1427)

- **Lead do chat do site com nome e contato** (#1430): `POST /api/public/chat/{slug}/identificacao` (X-Chat-Token, 5/min por sessão) grava nome, telefone E.164 BR, e-mail opcional e o aceite da política de privacidade. A conversa nasce com o nome do visitante e o contato marcado como informado (migration `AddContatoInformadoChatSite`, colunas nulas em `sessoes_chat_site` e `atendimento_conversas`); nada liga a cliente pelo telefone. O dossiê de lead traz `contatoInformado`, e a Ficha do console confirma o cadastro com um clique. Telefone digitado não é telefone verificado: só o cadastro que nasce na confirmação recebe o `email` (endereço puro, sem nome de exibição, comentário ou lista) e o aceite como consentimento transacional do chat. Se o telefone informado já é de um cadastro, `POST /api/atendimento/conversas/{id}/cliente` responde 409 `TELEFONE_JA_CADASTRADO` com o nome e não liga nada; a Ficha pergunta e, com `confirmarVinculoExistente=true`, a conversa é ligada a esse cadastro sem gravar nele e-mail nem consentimento.
- **Atendimento por e-mail pela caixa de suporte da loja**: a caixa (endereço, IMAP, SMTP, usuário e senha cifrada em `credencial_integracao`) é configurada e testada em Integrações > E-mail; a cada minuto os e-mails não lidos viram mensagens da conversa do canal E-mail, com assunto, texto sem o histórico citado e anexos no storage privado, sem duplicar pelo Message-ID; a resposta do console sai pelo SMTP da própria caixa, em nome dela, com "Re: assunto" e In-Reply-To. Console ganha o chip E-mail e o assunto no cartão e no fio. (#1432)

- **Janelas de entrega, entregas do dia por janela e roteiro impresso no console** (#1440): Gestão › Janelas de entrega, no modo API, passa a ser o cadastro da loja (o mesmo de Entregas › Janelas e frete), com janela criada para vários dias de uma vez, edição, pausa, reativação e exclusão (`DELETE api/minha-vitrine/entrega/janelas/{id}`, recusado quando a janela já teve pedido ou bloqueio). "Entregas de hoje" escolhe o dia (hoje, amanhã ou outro) e mostra uma seção por janela com vagas, bloqueio, cada pedido com situação, pagamento, endereço, quem leva e o que falta para sair. O roteiro do dia sai em PDF A4 ou na bobina de 80 mm, só em preto. Corrige "Pôr na viagem" em viagem já gravada, que respondia "Conflito de concorrência" (a parada nova ia como UPDATE).
- **Abrir a loja abre o caixa, fechar no horário pede justificativa** (#1443): no console (modo API), "Abrir loja" mostra o último fechamento (saldo, quem fechou, observações), avisa o caixa de dia anterior esquecido aberto (e fecha conferindo a gaveta), pede a conferência do saldo inicial e o motivo se ele for retificado, abre o caixa e só então a loja. Fechar a loja dentro do horário de funcionamento exige gerente ou dona e justificativa (mínimo 10 letras): a API grava em `AuditLog` (`loja.fechada_no_horario`, quem, quando, porquê) e enfileira o aviso `LojaFechadaNoHorario` para os donos (Push e In-App, audiência `admins`). Fora do horário, fechar segue direto. A tela Financeiro › Caixa do dia agora fala com `api/caixa` (abrir, lançar, estornar, fechar e últimos fechamentos).

- **Áudio gravado no console sai pelo WhatsApp** (#1444): nova rota `POST api/atendimento/conversas/{id}/mensagens/audio` (multipart, até 16 MB). O WebM/Opus do Chrome é reempacotado em Ogg/Opus sem recodificar (a Meta não aceita WebM) e sai como nota de voz (`voice: true`) quando mono; Ogg/Opus, MP4, MP3, AAC e AMR passam como vieram, e o formato vem dos bytes. A mensagem da dona fica na conversa com o áudio tocável. No console, o gravador mostra "Enviando áudio…" até o EasyStok responder, o balão nasce "enviando" e a falha aparece no balão; o gravador prefere Ogg/Opus quando o navegador grava (Firefox) e pede mono.
- **Assistente executa ações com confirmação** (homologação 07/10): o "Pergunte ao assistente" propõe mandar o cardápio, anotar nota interna, preencher o rascunho e abrir cardápio ou comanda por tool use; cada ação vira um botão e só o clique da atendente executa. Nada sai ao cliente sem o "Enviar ao cliente". (#1445)

- **Cozinha viva no modo API** (#1446): o KDS do console imprime o mesmo canhoto de 80 mm em PDF do protótipo (sem preço, "não é cupom fiscal", com a observação do pedido), consome a fila de impressão da API numa gaveta "N canhotos na fila · Imprimir" que confirma `impressa`, abre a comanda do cliente ("Ver comanda": itens por linha, observações, trilha de etapas, avançar, imprimir e reimprimir), aceita arrastar para a coluna do próximo passo, filtra por linha e anima entrada, troca de coluna, saída e atraso (barra de tempo até o início), parando tudo com `prefers-reduced-motion`.
- **Hall de módulos no console** (#1447): depois do login o console abre num hall com os oito módulos do ERP da Casa da Baba (M1–M8, ADR-0056), na cor e na fonte da marca. Atendimento abre o Balcão, Cozinha a fila e Entregas o painel; Cardápio e Produção aparecem "Em breve". Cada módulo tem menu próprio derivado da rota (`#/m/<modulo>/<tela>`, ADR-0046 D1); `#/cozinha`, `#/entregas` e `#/cardapio-link/<id>` continuam valendo.
- **Configurações › Canais** (#1447): tela própria do WhatsApp Business, com o estado real do canal (`GET api/integracoes/whatsapp/status`) e o botão de conectar por coexistência.
- **Registro da homologação de 07/10** (#1447): `docs/plan/atendimento-whatsapp/homologacao-2026-10-07.md`, com os 70 itens, o destino de cada um e os checklists que dependem do Felipe.

- **Foto do cardápio pela API** (#1448): `GET api/public/cardapio/fotos/{caminho}` lê do storage só sob `cardapios/`. O console troca `.../files/cardapios/...` de qualquer host gravado (ex.: `ez-api.92.113.33.60.sslip.io`) por essa rota, que passa pelo `/api` da mesma origem. Nenhum dado é reescrito.
- **Chat do site pronto para foto da loja no backend** (#1448): `GET api/public/chat/{slug}/mensagens/{id}/midia` com `X-Chat-Token`, mensagem com `tipo` e `temMidia`, e o canal aceita imagem. A capacidade continua desligada até o widget do site (repo casa-da-baba) desenhar imagem; o aviso do console segue honesto.
- **ADR-0059: o Web sai, a PWA fica como backup e conflito vai para merge** (#1459): o `EasyStock.Web` deixa de ser usado e todas as telas vão para o console; a PWA de produção e cadastro continua junto ao console como backup offline, com sync bidirecional como contrato; conflito entre console e PWA offline entra numa fila e a dona decide qual versão fica. Emenda o ADR-0056 e o plano `docs/plan/erp-casa-da-baba/`.
- **M1 › Itens do cardápio no console** (#1481, M1.1): no modo API, o módulo Cardápio do hall abre a gestão do cardápio. A lista traz todos os pratos da vitrine, inclusive os ocultos do site e os desligados de hoje, com foto, linha, porção, preço e categoria. Os interruptores "Hoje" e "No site" gravam no EasyStok, e "subir/descer" grava só a ordem do item que andou. Novas rotas do Gerente: `GET api/atendimento/comanda/cardapio/gestao` e `POST .../{item}/ordem`.

- **Item do cardápio: tirar arquiva, item novo em validação, novidade com prazo** (#1482, M1.2). Decisões do Felipe de 08/10 (D-M1-07 e D-M1-08):
  - **Tirar:** "Tirar do cardápio" arquiva (`cardapio_item.ArquivadoEm`) e é diferente de "No site". O item arquivado sai do site, da comanda, do agente e do checkout, mesmo por link antigo, mas o pedido antigo continua achando o item, e "Repor" volta.
  - **Validação:** o item incluído pelo console nasce em validação (`EmValidacao`). O agente (`consultar_cardapio`) só passa a oferecer quando ela clica em **Validar**.
  - **Novidade:** `NovidadeAte` (data) some sozinha depois do prazo.
  - **Rotas novas do Gerente:** `{item}/arquivar`, `{item}/validar` e `GET {item}`, este com a ficha para editar.
  - **Formulário do item:** ganha descrição, ingredientes, alérgenos, preparo em minutos e instrução de finalização. No modo API, a ficha vem do EasyStok antes de editar, e só o que mudou é gravado.
  - **Gestão:** mostra "Em validação", "Novidade até" e "Fora do cardápio", com os botões Validar e Tirar/Repor.
- **Categorias do cardápio (seções) no console** (#1483, M1.3). D-M1-01, decisão do Felipe de 08/10: a categoria que o cliente vê é a seção do cardápio.
  - **Tela M1 › Categorias:** criar com nome único na vitrine, renomear, esconder (tira a categoria inteira do site e da comanda sem apagar os pratos), subir e descer (renumeração no servidor) e excluir só a vazia.
  - **Converter as categorias antigas:** cria uma categoria por texto livre que os pratos já tinham e liga cada prato a ela. É idempotente e não apaga o texto antigo.
  - **Formulário do item:** ganha o seletor de categoria.
  - **Menu público e comanda:** passam a seguir a ordem das categorias definida pela dona, não a ordem alfabética.
  - **Rotas do Gerente** em `api/atendimento/comanda/cardapio/secoes`, mais o repositório `ICardapioSecaoRepository`.
- **M2 › Estoque do dia no console** (#1490, M2.1). No modo API, o módulo Produção do hall abre o estoque em porções, lido do EasyStok, em três partes:
  - **Venda sem saldo:** os alertas em texto (avisa, não trava). "Ajustar" grava a contagem com o motivo e fecha o alerta.
  - **Saldo por prato:** só conta lote não vencido, como o cardápio.
  - **Lotes ativos:** "Vence hoje" e "Vence amanhã" destacados.

  Nova rota do Operador com a permissão de estoque: `GET api/atendimento/producao/estoque-do-dia`.

- **M2 › Produção do dia no console** (#1491, M2.2). Ela escolhe o prato do cardápio e anota porções, peso de cada porção, peso real e validade. Confirmar cria o lote no EasyStok, com etiquetas e entrada em porções, e "Imprimir etiquetas" imprime uma por porção.
  - **Prato avulso:** ganha um produto de estoque na primeira produção, na categoria de estoque "Cardápio" (D-M1-01).
  - **Peso real:** novo `lote_itens.PesoRealG`, com a sobra calculada (US-061: 1.144 g em 2 porções de 500 g sobram 144 g). Peso real menor que as porções é recusado.
  - **Rota nova do Operador:** `POST api/atendimento/producao`, com `Idempotency-Key`. Repetir com a mesma chave não cria lote novo, e o console mantém a chave até gravar.
  - **Baixa de insumo pela receita** (D-M2-01): entra com a M2.4.
- **M2 › Insumos no console** (#1496, M2.3). Cobre o intermediário (molho, recheio, massa laminada) e a embalagem; farinha e ovo ficam fora (US-067).
  - **Lista:** saldo na unidade base (só de lote não vencido), mínimo, custo e em quantas receitas o insumo entra.
  - **Comprar:** seção com os insumos abaixo do mínimo, que alimenta a M2.5 e a US-066.
  - **Cadastro rápido:** nome, unidade, mínimo e custo, na categoria de estoque "Insumos", com ajuste de mínimo e custo.
  - **Rotas:** `GET api/atendimento/producao/insumos` (Operador com a permissão de estoque); `POST` e `PUT .../insumos` (Gerente).
  - **Repositório:** novos `IProdutoRepository.GetInsumosAsync` e `IProdutoComposicaoRepository.ContarReceitasPorInsumoAsync`.
- **M2 › Receitas no console** (#1498, M2.4a). Para cada prato ligado ao estoque, a tela mostra quanto ele rende e quais insumos leva, com quantidade e unidade.
  - **Custo por porção:** o EasyStok soma o custo das linhas e divide pelo rendimento. Se algum insumo estiver sem custo, o total não é calculado.
  - **Rotas:** `GET api/atendimento/producao/receitas` e `.../receitas/{produtoId}`, com a permissão de estoque. Para gravar, a tela usa o `PUT api/produtos/{id}/composicao`, que já existia (Gerente).

- **Baixa de insumo pela receita na produção** (#1499, M2.4b, D-M2-01). Ao produzir um prato marcado "baixar insumos ao produzir" (S23 e console), os insumos da receita saem do estoque na mesma transação, como `UsoInterno`, citando o código do lote.
  - **Consumo:** quantidade da linha × produzido ÷ rendimento. A unidade é convertida para a do insumo. Se a receita rende em kg, entra o peso produzido.
  - **Falta avisa e não trava:** com algum saldo, o que faltou vira descoberto no lote (#540). Sem saldo nenhum, a produção só avisa. Os avisos aparecem na tela de produção do dia.
  - **Marca:** `Produto.BaixaInsumoAutomatica`, uma migration aditiva com default `false`. Liga e desliga em M2 › Receitas (`PUT api/atendimento/producao/receitas/{id}/baixa-automatica`, Gerente). Só liga se o prato tiver receita.
  - **Fora:** o lote que chega pelo sync do PWA (`BatchLinker`) ainda não baixa insumo.
- **M2 › Planejamento no console** (#1502, M2.5, D-M2-05).
  - **Sugestão:** para cada prato, o EasyStok sugere quanto produzir até a data, com a conta `max(mínimo + agendados + descoberto − saldo, 0)`. Agendados são os pedidos aguardando, aprovados ou em preparo, que ainda não baixaram estoque. Ela pode mudar as porções.
  - **Insumos e faltas:** usam a mesma cesta da calculadora do PWA, e o cálculo não mexe no estoque.
  - **Lista de compras:** é gravada no console pela API de listas (`POST api/listas-compras/gerar`), com as faltas e o que repõe o mínimo.
  - **Lançar como produção:** leva as porções para a Produção do dia.
  - **Rotas:** `GET api/atendimento/producao/sugestao?ate=` e `POST .../planejamento`, as duas com a permissão de estoque.

- **M2 › Perdas no console** (#1511, M2.6, D-M2-02 e D-M2-06).
  - **Motivo da lista:** vencido, perda no preparo, doação, degustação ou outro, este com texto. O motivo vira a natureza da saída, sem migration.
  - **Saída:** sai por FEFO com o custo de cada lote e não cria descoberto. Acima de R$ 50 só o Gerente lança.
  - **Resumo:** mostra o período por motivo e por prato, em quantidade e R$, e não conta ajuste de contagem nem a baixa de insumo da produção.
  - **Vencidos:** lote vencido com saldo vira sugestão e só sai quando ela confirma.
  - **Desfazer:** usa o estorno de saída, que é do Gerente.
  - **Rotas:** `POST`/`GET api/atendimento/producao/perdas` e `GET .../perdas/vencidos`.
- **Embalagens na produção** (#1523, M2.7, D-M2-03).
  - **A marca:** bandeja, selo e pote são insumos marcados como embalagem (`Produto.EhEmbalagem`, migration aditiva), na categoria de estoque "Embalagens" e contados por unidade.
  - **Baixa:** a embalagem entra na receita do prato e desce em toda produção, mesmo em prato sem a marca de baixa automática. Os outros insumos continuam descendo só no prato marcado (D-M2-01).
  - **Console:** M2 › Insumos marca a embalagem no cadastro ou na lista.
  - **Comprar:** a bandeja abaixo do mínimo aparece em "Comprar".
- **Baixa de insumo e embalagem no lote do PWA** (#1526). O lote que chega pelo sync (`BatchLinker`) agora baixa insumo e embalagem com as mesmas regras da produção do console:
  - prato marcado: baixa a receita toda (D-M2-01);
  - embalagem: baixa sempre (D-M2-03);
  - falta de insumo: avisa, não trava o sync, e o aviso vai para o log e para as observações do lote.

  No PWA, o `Qty` conta pacotes: porções = `Qty` e peso produzido = `Qty` × `WeightG`. A baixa roda uma vez só por lote.
- **Porções do prato no console** (#1529, M1.4a, D-M1-03). O formulário do item em M1 ganha o editor de porções: nome, peso, preço, padrão e "tem". O preço de cada porção é absoluto (ADR-0035).
  - **Saldo próprio:** cada porção de prato ligado ao estoque ganha a sua variação de estoque (`ProdutoVariacao`). O prato avulso recebe o vínculo na primeira produção.
  - **API:** `porcoes` entra no corpo e no detalhe de `api/atendimento/cardapio/itens`. A lista de gestão mostra o preço "a partir de" e quantas porções o prato tem.
  - **Fora desta entrega:** o menu público não muda. As porções chegam ao site depois da confirmação do schema v2 (ADR-0035, Fase 2).

- **Vender por porção** (#1531, M1.4b). O pedido passa a cobrar o preço da porção escolhida e a gravar na linha o rótulo, o SKU e a variação de estoque dela.
  - **Comanda do console:** o prato com porções mostra um botão por porção, com o preço de cada uma. A porção esgotada não entra, e a outra porção do mesmo prato continua vendendo. O toque no prato soma a porção padrão.
  - **Sem porção informada:** o pedido de um prato com porções usa a porção padrão. É o caso do site e do agente, que ainda não escolhem porção.
  - **API:** `variacaoId` na linha do pedido da conversa (`POST api/atendimento/conversas/{id}/pedido`) e na resposta. O cardápio da comanda (`GET api/atendimento/comanda/cardapio`) traz `porcoes` por item.
  - **Fora desta entrega:** o menu público e o ETag do site não mudam (ADR-0035, Fase 2).
- **Saldo por porção** (#1537, M1.4c, D-M1-03).
  - **Produção:** a linha informa a porção, e a entrada vai para o saldo dela. Prato com porções exige a escolha, e o mesmo prato pode sair em duas linhas, uma por porção. O prato avulso ganha o vínculo das porções com o estoque na primeira produção.
  - **Baixa do pedido:** a linha de porção baixa do saldo da porção vendida, e a movimentação grava a variação. Sem nenhum lote daquela porção (estoque antigo ou lote do PWA), a baixa cai nos lotes sem porção do prato, nunca em outra porção.
  - **Estoque do dia:** mostra o saldo de cada porção e o que ficou sem porção.
  - **Console:** seletor de porção em M2 › Produção do dia.
  - **Fora:** a receita é do prato, não da porção. A baixa de insumo conta porções produzidas, ou o peso quando a receita rende em kg.
- **Contagem por porção** (#1542): em prato com porções, "Ajustar" no Estoque do dia pede o que foi contado (uma porção com lote, o que ficou sem porção, ou o prato inteiro) e a contagem da porção só mexe nos lotes dela. O formulário abre na porção que tem venda sem saldo, e o alerta do prato fecha quando nenhuma porção tem. A rota `comanda/cardapio/{id}/saldo` aceita `variacaoId` ou `semPorcao`; sem eles segue contando o prato inteiro.
- **Estoque do dia** (#1542): cada porção traz a venda sem saldo, o que está guardado (vencido incluído) e se tem lote.

- **Embalagem por porção** (#1552, M1.4e): cada porção do cardápio escolhe a sua embalagem (bandeja de 300 g, bandeja de 800 g) no editor do prato. A produção baixa uma por porção produzida, em qualquer prato, e deixa de usar a embalagem da receita para aquela porção. Porção sem escolha segue com a embalagem da receita.
- **Receita que rende em peso, pelo console** (#1552): o editor da receita ganhou "Rende em" (porções, kg, g). Em peso, a baixa de insumo acompanha o peso produzido, e por isso o tamanho da porção. O editor orienta quando o prato tem mais de um tamanho.
- **Peso da porção na produção** (#1552): escolher a porção já traz o peso dela ("300 g" vira 300), sem apagar o que foi digitado.

- **Painel "Agora" na entrada do console** (#1608): antes da lista de módulos, três filas na ordem do trabalho. **Responder** mostra quem precisa da dona, com o motivo escrito, e um toque abre o Balcão já na conversa. **Cozinhar** mostra pago e em preparo, atrasado primeiro. **Despachar** mostra embalado e a caminho. Fila vazia diz que está em dia. No modo API só Responder aparece por enquanto: a lista de conversas ainda chega sem o pedido, e fila marcando zero com pedido no fogo mentiria.
- **Fluxo dos últimos 7 dias no Financeiro** (#1624): por dia, o que entrou, o que saiu, o saldo e a situação do caixa (fechado, aberto, sem abertura, sem movimento), com o total do período.
- **Proposta de impressos no layout da comanda do cliente** (#1628): pedido, entrega, agendamento e canhoto de cozinha em A4, A5, etiqueta 10x15, bobina 80 mm e 58 mm, todos com QR, em `docs/plan/atendimento-whatsapp/impressos/impressos-v4-proposta.html`. Inclui o parecer da banca revisora e o que cada peça exige do sistema. Nenhum modelo de produção muda.
- **Console imprime pela API** (#1632): ação Imprimir no pedido e na cozinha, com documento e papel, e a aba Impressão nas configurações (logo da loja, papel padrão por documento, logo na térmica). O PDF antigo continua como alternativa.
- **Assinatura do fecho configurável** (#1632): `Impressao:Assinatura` define quem assina o bilhete; sem ela, assina a casa.
- **Kit de teste para adapters de terceiros**: `HttpFalso` (handler HTTP roteável que grava as chamadas e simula
  erro, timeout e sequência de respostas) e fábricas de assinatura de webhook do Mercado Pago e da Meta em
  `EasyStock.TestHelpers`, conferidas contra os validadores reais; três suítes migradas e roteiro em
  `docs/dev/integracoes-novo-provedor.md`. (#1634)
- **Plano de integrações plug and play** (#1635): `docs/plan/integracoes/01-plug-and-play.md` com as decisões de 12/10 (Pix todo pelo Mercado Pago, conexão por OAuth, 99 por registro manual, Lalamove no sandbox), o desenho de conexão por loja, 23 invariantes com etapa dona e uma spec por fatia de PR0 a E11. O `README.md` da pasta vira índice e marca o que foi superado. Só documentação; nenhum código muda.
- **Frete por raio configurável pela loja** (#1648): `PUT api/minha-vitrine/entrega/raio` grava cozinha (CEP e número, geocodificados no servidor), raio máximo, distância grátis, fator de rota e faixas; só grava com localização confiável e recusa faixas repetidas, fora de ordem ou que não alcançam o raio. `POST api/minha-vitrine/entrega/raio/testar` cota um endereço com a mesma conta do checkout. Sem geocodificador ligado, os dois respondem 422.
- **Frete por distância no console** (#1651): em Entregas > Janelas, a aba Frete ganhou "Cozinha, raio e faixas" e o "Testar um endereço", que mostra distância e frete pela conta do checkout. GET api/minha-vitrine/entrega/raio devolve o que está gravado; no PUT, CEP e número em branco mantêm a cozinha atual.
- **Contrato do frete do site** (#1651): FreteCalculadoDto passa a enviar geocodeConfiavel, distanciaMetros, aixaId, gratis e oraDeCobertura, sem remover campo. Fora de cobertura segue respondendo 422.
- Testes do `ViaCepLookupClient` (achou, não achou, erro HTTP, timeout, falha de rede) (#1661).
- **Cobertura e produção assistida do acervo**: lacunas por produto e canal, filtros no acervo completo, classificação de imagens e peças, adequação explícita com resolução e medidas de impressão, briefings imutáveis versionados e reaproveitamento de solicitações sem duplicidade. Originais preservados e novas imagens dependem de aprovação antes de publicar. (#1663)
- **ADR-0061, conexões de integração** (#1664): regra de moradia de adapter, modo e saúde como eixos da conexão, webhook por provedor, Pix do pedido pelo Mercado Pago (revoga a D3 de 30/09), `CorridaExterna` como lugar único do custo de corrida e `IPagamentoGateway` fechado para adapter novo. Poda da cadeia sem chamador na #1665.
- **Corrida externa com custo** (#1669): a corrida pedida pelo app da 99, da Lalamove ou da Uber pode ser registrada na viagem com custo e motorista (`POST api/atendimento/viagens/{id}/corridas`) e corrigida depois, guardando quem corrigiu. Custo negativo, acima de R$ 5.000,00, de entregador próprio ou em viagem desfeita é recusado.
- **Relatório de custo de entrega** (#1669): `GET api/atendimento/relatorios/custo-de-entrega` compara, por viagem, o custo das corridas com o frete cobrado nos pedidos.
- `EmpresaEntregador` ganhou `Uber`.

- **Chave-mestra nos dois hosts** (#1674): API e Worker leem a KEK do cofre de `EZ_CRYPTO_KEK_ID` e `EZ_CRYPTO_KEK` (e das `_ANTERIOR` na rotação). Sem a chave, ou com chave que não é Base64 de 32 bytes, os dois sobem e registram um aviso; o valor nunca vai para o log.

- **Registrar corrida na viagem** (#1691): em Entregas, cada viagem ganhou "Registrar corrida" (empresa, custo e motorista) para a 99, a Lalamove ou a Uber pedida pelo app. A corrida pode ser corrigida e o custo aparece na viagem.

- **Entregas em empresa de demonstração isolada**: cotação, agrupamento por janela, transporte manual ou em mãos, acompanhamento, retorno e pagamento registrado no Caixa, com dez pedidos sintéticos e escolha automática configurável. Uber Direct e Lalamove usam contratos HTTP simulados; 99 permanece manual. (#1693)
- Rastreio demonstrativo com link assinado, consulta de entrega pelo assistente e proteção contra contratação/pagamento duplicados e alteração de viagem contratada. (#1693)

- **Laboratório da jornada do pedido sem Docker**: `scripts/homologacao/laboratorio.ps1` sobe PostgreSQL, API, Worker e o Mercado Pago falso e prepara a loja de demonstração. O link de pagamento simulado abre uma página em que se escolhe Pix aprovado, cartão aprovado ou cartão recusado, e o webhook assinado volta para a API. (#1716)
- O Mercado Pago falso passa a imitar o contrato de estorno (lista, `payment_id`, parcial), link vencido, mudança de estado do pagamento e conta recebedora, com teste próprio (`teste_fake_mp.py`). (#1716)

- **Teste de e-mail próprio no Console**: SuperAdmin pode solicitar o evento ContatoAlterado para o e-mail confirmado da própria conta e consultar o processamento, com estados de falha e simulação explícitos. (#1728)
- Configurações ganha Usuários e Histórico de acesso no Console: a dona convida por e-mail, troca o perfil, desativa e reativa, e lê quem fez o quê. Quem é convidado cria a própria senha pelo link, com a marca da casa. A dona não consegue mais se trancar para fora, e trocar o perfil de alguém troca de verdade.

- Atendimento sugere uma continuação curta com IA até o fim da frase iniciada, na mensagem ao cliente e na observação geral do pedido compartilhado. Tab ou “Usar sugestão” aceita; Escape dispensa. A sugestão preserva o texto digitado e nunca envia mensagens ou executa ações automaticamente. Respostas atrasadas são descartadas ao mudar texto, conversa ou sessão; falhas da IA mantêm a escrita disponível.
- Avisos: contato novo, passou para você, mensagem nova, pagamento confirmado e pedido que mudou de etapa aparecem na tela do console e no navegador com o console minimizado, além do som; Avisos e Lembretes ganham ícone e rótulo próprios no trilho.
- **Avisos com o console fechado**: contato novo, passou para você, pagamento confirmado e etapa do pedido chegam como Web Push em todos os aparelhos com os avisos ativados, só com o primeiro nome e o evento e sem repetir o aviso do console. A Ajuda do trilho traz o guia de cada módulo (o que faz, primeiros passos e telas) e mantém o diagnóstico. (#1807)
- Cardápio: dica por prato nos Itens (falta foto, categoria ou descrição) com "Dica da IA" para quem edita e explicação curta em cada controle; no Banco de imagens, "Sugerir imagens para os pratos" propõe pares imagem aprovada x prato pela IA (ou pelo nome, sem IA) e só publica depois da conferência. (#1808)
- **Frete pela cotação da Lalamove**: com a chave de produção da loja em Integrações, o frete do site, da comanda e do "testar um endereço" sai da cotação de moto da Lalamove, arredondada para cima no real. Raio e faixa grátis continuam valendo; sem chave ou com qualquer falha, vale a tabela da loja. (#1882)
- **"Quanto fica?" antes de gerar a cobrança** (#1884): `POST /api/atendimento/conversas/{id}/pedido/simulacao` calcula, só lendo, o frete, o total com frete, o sinal da encomenda (50% do total com o frete) e o restante, com as mesmas validações e a mesma conta do pedido (zona do CEP, porção, janela, prazo); CEP fora da área devolve `freteACombinar` sem inventar total nem sinal. O console pede a simulação quando a comanda tem itens e janela e mostra, na comanda e no resumo da barra, frete, total com frete e sinal em reais no lugar de "o frete entra ao gerar a cobrança"; sem resposta a tela segue com o texto de antes.

- **Comanda N na API do KDS e da agenda de entregas** (#1883, parte da API): `numeroComanda` em `GET /api/kds/pedidos` e `GET /api/atendimento/agenda`, o número da conversa dona do pedido, o mesmo da caixa de entrada; nulo quando o pedido não tem conversa. `numeroCurto` continua como referência. (#1902)
- **Alerta de desacerto e ajuste rápido de saldo** (S22): `GET api/estoque/desacertos` lista os
  produtos com `QuantidadeDescoberta > 0` com texto legível citando os pedidos (sem tabela nova).
  `POST api/estoque/desacertos/{produtoId}/ajustar` recebe a contagem e o motivo, reusa
  `ItemEstoque.AplicarAjusteContagem` numa contagem de um produto já aplicada, grava `AjusteInventario`
  e movimentação de ajuste por lote, zera o descoberto e publica `estoque.desacerto_resolvido`.
  Exige `GerenciarEstoque`. (#1181)
- **Avisos de status do pedido ao cliente pelo WhatsApp** (S13, #1142): pagamento confirmado,
  `preparando` (com a previsão da janela), `saiu_para_entrega` e `entregue` (agradecimento com o
  Instagram da loja) viram mensagem no outbox de notificações, canal WhatsApp, categoria transacional.
  Saem com a conversa assumida; só a revogação do transacional no WhatsApp bloqueia. Idempotente por
  pedido + status. O outbox passa a persistir os metadados do envio (template da Meta e parâmetros),
  fechando a pendência da S09; `Storefront.InstagramUrl` novo. Pedido pago com `RequerAprovacao` (S12)
  vai para `AguardandoAprovacaoBaba`. Migration `AddAvisosStatusPedido` (3 colunas nulas).
- **Webhook do Mercado Pago de ponta a ponta** (S32): `POST /api/webhooks/mercadopago` ganha o
  `MercadoPagoWebhookProcessor` (antes respondia 500). Só o tópico `payment`; o `data.id` do corpo
  dispara `GET v1/payments/{id}` e só o que a fonte devolve vale. `approved` confirma o pedido pela S11
  (`AguardandoPagamento → Aguardando`, `PedidoPagamento`, `pedido.pago`); valor menor e recusa só gravam
  o motivo (recusa avisa a conversa uma vez); `refunded`/`charged_back` marcam a cobrança `Estornada`.
  Idempotência pelo id da notificação (numérico) no `WebhookRecebido`. Client: preferência em
  `POST checkout/preferences`, consulta, `payments/search` por `external_reference` (o job de cobrança
  confirma webhook perdido antes de expirar), estorno com `X-Idempotency-Key` e `PUT checkout/preferences/{id}`
  para expirar o link antigo na troca de forma. Corrige o DI que entregava a porta com `HttpClient` sem
  `BaseAddress`. Sem credencial ainda (onda 0.9). (#1136)
- **Canhoto e fila de impressão** (S20): o pedido pago entra em `impressoes_pendentes` na mesma
  transação do `ConfirmarPagamentoPedidoUseCase` e sai `impressao.pendente` no SSE depois do commit.
  `GET api/pedidos/{id}/canhoto?formato=html|texto` (html de 80 mm com o CSS do recibo; texto de 42
  colunas sem acentos para ESC/POS), agrupado por linha com porção, molho e observação por item.
  Consumidor agnóstico por polling: `GET api/impressao/pendentes`, `POST api/impressao/{id}/impressa`
  (idempotente) e `POST .../falhou`, com JWT de operador ou header `X-Impressao-Api-Key`
  (`Impressao:ApiKey` + `Impressao:EmpresaId`). `POST api/pedidos/{id}/reimprimir` só para operador.
  `ImpressaoPendenteAlertaJob` publica `impressao.atrasada` para pendente há mais de 3 min
  (`BackgroundJobs:EnableImpressaoPendenteAlerta`). Migration `AddImpressaoPendente` com RLS. (#1156)
- **Início previsto e atraso** (S21): `Pedido.InicioPrevistoEm` = início da janela da vaga ativa (hora
  de Brasília) ou `AgendadoParaEm`, menos o prazo mínimo dos itens do cardápio (S15); gravado em
  `ConfirmarPagamentoPedidoUseCase` e recalculado em `AlterarAgendamentoPedidoUseCase`, que também zera
  `AtrasoNotificadoEm`. O KDS devolve `inicioPrevistoEm` e deriva `atrasado` (aguardando e início vencido;
  sem início previsto, vale a regra da S19). `PedidoAtrasoJob` (60 s, `BackgroundJobs:EnablePedidoAtraso`)
  publica `pedido.atrasado {pedidoId, numero, cliente, inicioPrevistoEm}` uma vez por pedido. Migration
  `AddInicioPrevistoPedido`. (#1165)
- **Dossiê do cliente ao lado da conversa** (S25): `GET api/clientes/{id}/dossie` e `GET
  api/atendimento/conversas/{id}/dossie` devolvem a mesma projeção (cadastro, endereços, tags, notas
  internas, 10 últimos pedidos com itens, item favorito, última compra, bloqueio, preferências,
  conversas recentes e pedido em andamento). Conversa de lead devolve o dossiê mínimo (nome do perfil e
  telefone). Sinal de mesmo domicílio por `cep+numero+complemento` normalizado, só com id e nome do
  outro cadastro (D10). O agente recebe um resumo com as notas marcadas `[interno]`. (#1161)
- **CRM leve do cliente** (S24): tags normalizadas e únicas por cliente (`GET|POST|DELETE
  api/clientes/{id}/tags`, repetida → 409, com sugeridas), notas internas datadas (`GET|POST
  api/clientes/{id}/notas`, pedido de outro cliente → 400), bloqueio em todos os canais (`POST
  bloquear|desbloquear`: sem saudação nem agente, conversa vai para a dona, `criar_pedido` recusa com
  `cliente_bloqueado`) e preferências (`PUT preferencias`: `AvisosStatusAtivos`, `ConsentiuMarketing`).
  Ferramentas do agente `registrar_restricao` e `registrar_nota`. Migration
  `AddClienteTagNotaBloqueioPreferencias` com RLS. O filtro de avisos de status por
  `AvisosStatusAtivos` fica para depois da S13. (#1148)
- **SSE de operação do console** (S18): `GET api/operacao/eventos` com JWT no header `Authorization`
  (sem token, 401; token sem empresa, 403), eventos nomeados só da empresa da claim e heartbeat a cada
  25 s. `ConfirmarPagamentoPedidoUseCase` publica `pedido.pago {pedidoId, numero, cliente, total, janela}`
  e `AtualizarStatusPedidoUseCase` publica `pedido.mudou_status`, sempre depois do commit. O broker
  in-memory do mobile virou `Api/Services/Operacao/OperacaoEventBroker` e atende os dois canais; o SSE
  mobile não muda. `IOperacaoEventPublisher` deixa de ser no-op na Api (Worker segue no-op). (#1146)
- **Instagram Direct e Messenger** (S35, ADR-0051): webhook `api/webhooks/meta/mensageria` (objetos
  `instagram` e `page`, mesmo HMAC do App Secret), roteado por `Empresa.InstagramAccountId` e
  `Empresa.FacebookPageId`, idempotente pelo `mid`. Adaptadores `CanalInstagram` e `CanalMessenger` na
  porta de canal (Send API v25.0, page token em `Atendimento:MetaMensageria`, stub sem provider `meta`).
  Fora da janela o console responde com `HUMAN_AGENT` até 7 dias; depois, 409. A conversa entra na
  fila humana e o agente não responde fora do WhatsApp. Flags `atendimento.canal.instagram` e
  `atendimento.canal.messenger`. (#1103)
- **Cadastro de entrega pela loja** (S45, parte 1): `api/minha-vitrine/entrega` com janelas (listar,
  criar, editar, ativar e desativar), zonas de frete por faixa de CEP ou por bairros (inclusive trocar
  a cobertura) e bloqueios de dia ou de janela (listar por período, criar, remover). A loja é sempre a
  da empresa do token e id de outra loja devolve 404. Canais por empresa ficam para a parte 2. (#1095)
- **Chat do site** (S36, ADR-0051): canal `ChatSite` público por loja em `api/public/chat/{slug}`.
  `POST sessoes` devolve um token (só o hash fica no banco, vale 24 h renovadas no uso), `POST|GET
  mensagens` com header `X-Chat-Token` e `GET stream` (SSE que lê do banco a cada 1 s, funciona
  com várias instâncias). A conversa entra na fila humana sem responsável; o agente ainda não responde
  no site. Flag `atendimento.canal.chatsite`, rate limit por IP e por sessão, tabela
  `sessoes_chat_site` com RLS e limpeza das vencidas de hora em hora. (#1097)

### Security
- **Cadastro anônimo `POST /api/auth/register` removido** (#1349): a rota deixa de existir (404). Ela criava usuário sem empresa a qualquer pedido da internet, enumerava conta (200 para e-mail novo, 409 para repetido) e, com o `BaseUrl` do corpo na allowlist, mandava e-mail a qualquer endereço. Saem junto o use case, o comando e o validator, que só ela usava. `confirmar-email` fica, sem emissor até a N9. Teste novo trava a lista de rotas anônimas do `AuthController`.
- **Senha errada do passo 1 do login passa a contar** (#1352): `lista-empresas` conferia a senha e não registrava a falha, então só o login completo bloqueava (5 falhas, 15 min) e o console chama o passo 1 primeiro. Os dois passos somam no mesmo contador e a conta bloqueada recusa os dois.
- **Sessão revogável na hora** (#1352): reset de senha, troca de senha (própria e por admin), desativação e troca de perfil derrubam as sessões do usuário. O JWT emitido antes da revogação recebe 401 em qualquer rota autenticada (`Usuario.SessoesValidasDesde`, migração aditiva, conferido a cada requisição com cache de 60 s), o refresh anterior também falha e o SSE de operação fecha no `exp` do token e na revogação. O login nunca revoga. `Auth:SessoesRevogaveis=false` desliga a checagem sem migração. `Jwt:ExpirationMinutes` continua 480 até o console renovar o token.
- **Só gerente altera a receita do produto** (#1462): o `PUT api/produtos/{id}/composicao` exigia apenas login, e até Visualizador reescrevia a receita. Agora exige a policy Gerente. Consultar a receita continua igual.
- **Autorização e superfícies anônimas** (#1508): preview/data de relatório e analytics financeiro/inteligência exigem `VisualizarRelatorios`; auditoria de entidade exige Admin; upload de foto de produto exige Gerente + Cardápio; segredo do unsubscribe sem literal fixo e sem opt-out de Segurança/Transacional; checkout guest e unsubscribe com rate limit; Web Push só aceita endpoints dos serviços de push (fecha SSRF) e cada usuário só desinscreve a própria assinatura; webhook do Mercado Pago sem assinatura recusado em produção; pull do sync mobile filtra pela empresa do aparelho e recusa aparelho não pareado.

- **Diagnóstico do SuperAdmin** (#1541, #955): o `X-Correlation-Id` do cliente ia cru para o log e voltava sem escape na tela de diagnóstico; requisição anônima que gerasse erro executava script na sessão do SuperAdmin. O header só passa quando é identificador (o resto vira id gerado), os campos de log são escapados e o filtro lê `data-cid` em vez de montar JavaScript. Nome da empresa, consulta lenta e trecho de log da investigação também passam a ser escapados.
- **Login de usuário desativado** (#1541): desativar zera o vínculo, não o usuário; sem empresa pedida o login seguia e emitia token sem empresa. Agora recusa quem não tem vínculo ativo.
- **Painel mobile entre empresas** (#1541): `api/mobile/operation/dashboard` e `devices-health` aceitavam a empresa da query quando o token não tinha empresa (as tabelas `mobile_*` ficam fora do filtro global e do RLS). Token sem empresa agora recebe 403.
- **Backup de aparelho** (#1541): listar e baixar o snapshot (clientes, pedidos e caixa do aparelho) exigem Gerente.
- **Foto da avaliação** (#1547): a rota pública de avaliação gravava qualquer texto em `fotoUrl`. Só entra URL http(s) absoluta de até 500 caracteres; vazio continua valendo como sem foto.
- **Endereço e CEP fora do span e do log** (#1661): nas chamadas ao ViaCEP, Nominatim, Google e TomTom, `url.full` do span fica só com esquema, host e porta, e o log por requisição do `IHttpClientFactory` é desligado nesses clientes. Antes, CEP e endereço do cliente iam na URL para o trace e para o log.

- **Mercado Pago falha fechado** (#1667): em Production a API não sobe com `MercadoPago:UseStub=true`, com token `TEST-` ou com `MercadoPago:BaseUrl` diferente de `https://api.mercadopago.com`. O simulador só é construído fora de Production e devolve estorno `simulado`, nunca `approved`.

- **Segredo fora de todos os logs** (#1698): console e OTLP da API e do Worker passam a redigir segredo, como o arquivo já fazia. Cobre `access_token` em JSON e `key=` em URL. O erro do Mercado Pago é redigido antes de ir para o log e para `WebhookRecebido.Erro`, corpo de endpoint de autenticação não é registrado, e webhook aparece no log pelo molde da rota.
- **Sem chave-mestra valida, nao sobe** (#1708): em Production, API e Worker recusam subir sem a chave-mestra corrente ou se alguma credencial ativa nao decifra com as chaves configuradas. Antes a API ficava verde e o envio quebrava. Fora de Production continua so o aviso.
- Comandas de visitantes com telefone existente passam a exigir código de verificação antes de reutilizar o cadastro. O login OTP aceita o token opcional da comanda e só vincula após validar posse, empresa, telefone, conversa e validade do link, preservando a seleção.
- **Login sem enumeração e sem corrida nas tentativas**: o e-mail passa a ser gravado e buscado em minúsculas e sem espaços (`ADMIN@...` entra; migração normaliza as contas existentes que não colidem), o login roda o BCrypt também para conta inexistente, inativa ou bloqueada e responde a mesma mensagem da senha errada, e as tentativas de senha e do OTP da loja são reservadas num UPDATE atômico antes da conferência, então rajada paralela não passa do limite. (#1781)
- **OTP da loja de uso único de verdade**: dois envios do código certo ao mesmo tempo abriam duas sessões (ou davam erro 500 com telefone novo); agora só um leva o código e o outro responde código inválido. (#1781)
- **OTP preservado em falha de sessão**: consumo e criação da sessão são confirmados juntos; se a gravação falhar, o cliente pode repetir o código, sem devolver a tentativa já contada. (#1823)
- **Atendimento sem atalho para quem não atende**: perfil sem permissão de atender conversas (ex.: Cozinha) não agenda mais mensagem ao cliente nem cria, edita ou arquiva respostas prontas; continua vendo as listas. (#1781)
- **Sair derruba o JWT já emitido** (#1879): o logout passa a gravar o corte de sessão do usuário (#1352), então o access token de antes do "Sair", que valia até 8 h, recebe 401 no pedido seguinte e no SSE. O logout com refresh inválido, revogado ou vencido continua não fazendo nada. Efeito: sair encerra todas as sessões da pessoa, não só a do aparelho. `Auth:SessoesRevogaveis=false` desliga a checagem.
- **Log do cancelamento sem quebra de linha vinda do cliente** (#1886): o `RequisicaoAbortadaMiddleware` tira CR e LF do método e do caminho antes de logar (alerta `cs/log-forging` do CodeQL), como já faz o middleware de CSRF.
- CSRF do storefront: POST/PUT/PATCH/DELETE com cookie `__Host-cdb_*` só da mesma origem
  (`Sec-Fetch-Site` `same-origin`/`none`, ou `Origin` igual ao `Host`); outro site recebe 403.
  O front é same-origin e não muda; chamadas bearer do Web e do Admin seguem livres (ADR-0053). (#1088)
- CodeQL: `cs/web/missing-token-validation` filtrada na Api bearer (CSRF não se aplica a JWT) e
  mantida nos 4 controllers do storefront que usam cookie; `CodeQlCsrfFilterTests` amarra a lista
  do `codeql.yml` ao código. Web e Admin seguem com a regra inteira (ADR-0052). (#1089)

### Added
- **Núcleo do checkout compartilhado** (S10): `CheckoutCoreService` concentra as fases 1 e 2 do
  checkout (validação de CEP, janela, bloqueio e cardápio, frete por zona, pedido em `Rascunho`,
  vaga ocupada, `AguardandoPagamento`, rollback para `Cancelado` com janela lotada).
  `IniciarCheckoutUseCase` e `IniciarCheckoutGuestUseCase` passam a delegar; o site não muda de
  comportamento. Novo `CriarPedidoAtendimentoUseCase` cria o pedido da conversa pelo mesmo caminho
  (`Origem = "whatsapp"`, observação por item, `Conversa.PedidoEmAndamentoId`); a cobrança é da S11. (#1101)
- **Cobrança do pedido pelo Mercado Pago** (S11): `CobrancaPedido` (migration `AddCobrancaPedido`,
  RLS) guarda a preferência com `external_reference = PedidoId`, link e expiração de 30 min; site e
  conversa passam pelo mesmo `GerarCobrancaPedidoUseCase`. `ConfirmarPagamentoPedidoUseCase` (ponto de
  entrada do webhook da S32) leva o pedido de `AguardandoPagamento` a `Aguardando`, registra o
  pagamento e publica `pedido.pago`; repetido é no-op e valor menor não confirma. `CobrancaPedidoJob`
  (60 s, `BackgroundJobs:EnableCobrancaPedido`) reemite uma vez para pedido da conversa e cancela o
  resto liberando a vaga. Operadora: `POST api/pedidos/{id}/cobranca`, `.../cobranca/forma`
  (`online` ou `na_entrega`) e `.../pagamento-manual/desfazer`. A ferramenta `criar_pedido` do agente
  fecha o pedido e devolve resumo, total e link. (#1115)
- **Mensagem programada ao cliente em todos os canais** (S39, ADR-0051): `MensagemProgramada`
  (texto ou modelo aprovado, agendada, enviando, enviada, cancelada ou falhou). Ao agendar e de
  novo no disparo: horário no passado é recusado; fora da janela no horário do envio, o WhatsApp
  exige modelo e Instagram e Messenger recusam; marketing sem consentimento no canal é recusado
  (S38). O destino vem da conversa aberta ou do cadastro (telefone para WhatsApp e SMS, e-mail
  para e-mail). Disparador no processo da API (`MensagensProgramadasBackgroundService`, desliga
  com `BackgroundJobs:EnableMensagensProgramadas=false`), com reserva `FOR UPDATE SKIP LOCKED` que
  não duplica entre processos. A saída entra no histórico com `Mensagem.Programada`.
  `POST|GET|DELETE api/atendimento/mensagens-programadas` (Operador). (#1082)
- **WhatsApp por empresa** (#1102): a resposta sai pelo `phone_number_id` da empresa do tenant
  (`IRemetenteWhatsApp`), com fallback para `Notifications:WhatsApp:Meta:PhoneNumberId` e erro
  permanente, sem chamar a Meta, quando não há nenhum. Chave nova `Atendimento__WhatsApp__Cliente=meta`
  liga o cliente real da Cloud API sem trocar o provider de notificações (que segue `stub`);
  `Provider=meta` continua ligando o cliente real. O `StartupHardening` exige AccessToken, AppSecret
  e VerifyToken com qualquer uma das duas em `meta`. `PUT api/admin/tenants/{id}/whatsapp`
  (`{ phoneNumberId }`, `null` desvincula, 409 se outra empresa já usa) e card "WhatsApp" na aba
  Features do detalhe do tenant no Admin.
- **Consentimento do cliente final por canal e finalidade** (S38, ADR-0051): `ConsentimentoContato`
  (transacional ou marketing, concedido ou revogado, com origem), `PoliticaConsentimento` (marketing
  só com opt-in no canal; transacional passa salvo revogação) e `PoliticaEnvioCliente` para a
  mensagem programada e as campanhas. "SAIR", "PARAR" ou "STOP" sozinhos no WhatsApp revogam o
  marketing daquele canal, confirmam ao cliente e não acionam o agente.
  `GET|PUT api/atendimento/clientes/{id}/consentimentos` (Admin). Tabela `consentimentos_contato`
  com RLS e backfill de `ConsentiuMarketing=true` para WhatsApp e e-mail. (#1078)
- **E-mail e SMS na porta de canal do atendimento** (S37, ADR-0051): `CanalSms` (texto pelo
  `IProvedorSms` ativo, Twilio em produção, com `+` no número) e `CanalEmail` (texto, ou imagem por
  link em HTML escapado, pelo `IEmailService`). O `ResolvedorCanal` passa a achar `Sms` e `Email`.
  Falha do provedor vira `EnvioCanalFalhouException`; operação que o canal não suporta lança
  `NotSupportedException`. (#1080)
- **Atendentes e atribuição de conversa** (S41, ADR-0051): permissão `AtenderConversas` (Operador,
  Gerente e Admin pelo nível; perfil com permissões explícitas só se a tiver). Assumir, responder,
  transferir, liberar e encerrar devolvem 403 sem ela. `POST api/atendimento/conversas/{id}/transferir`
  (422 se o destino não atende na empresa), `GET api/atendimento/atendentes` e filtro
  `?responsavel=eu|ninguem|{id}` na inbox. Mensagem do console grava `EnviadaPorUsuarioId`. A regra
  de permissão efetiva foi para o domínio (`PoliticaPermissao`). (#1085)
- **Expediente da loja** (S40, ADR-0051): `ExpedienteLoja` por empresa com horário por dia (virada
  da meia-noite), controle manual que vence o relógio e não volta sozinho, e mensagens de "fora do
  horário" (`{abre}` vira "amanhã às 08:00") e "loja fechada". `GET|PUT api/atendimento/expediente`
  e `POST api/atendimento/expediente/controle` (Admin), evento `expediente.alterado`. O checkout do
  site devolve 409 só com a loja fechada na mão (o pedido é agendado; o horário governa o
  atendimento). Tabela `expedientes_loja` com RLS. (#1074)
- Provider da Meta no outbox de notificações e regra da janela de 24 h (S09): `MensagemPronta.Metadados`
  (`template`, `idioma`, `param1..N`); `MetaCloudWhatsAppProvider` envia pela porta de canal (S34) texto
  dentro da janela e template fora dela ou sem conversa aberta; fora da janela sem template (ou 131047
  da Meta) vira `ResultadoEnvio.FalhaPermanente` e o outbox não reagenda; com conversa aberta a saída
  entra no histórico como `Mensagem(Saida, Sistema)` com o `wamid`. O provider continua `stub` por
  padrão: liga com `Notifications__WhatsApp__Provider=meta` junto das credenciais da Meta.
- Handoff pelo console do atendimento (S07, ADR-0050): `api/atendimento/conversas` (policy
  `Operador`) com inbox (última mensagem, não lidas, busca por nome ou contato), histórico
  paginado por `antesDe`, envio de texto e imagem pela porta do canal (S34) como
  `Mensagem(Saida, Dona)` que deixa a conversa `Assumida` e cala o agente, `assumir`,
  `liberar-automatico` (nota interna com o usuário), `encerrar` e `marcar-lida`. Fora da janela de
  24 h (domínio ou erro 131047 da Meta): 409 `{ erro: "fora_da_janela_24h", sugestao: "template" }`
  e nada gravado. `EscalarConversaUseCase` completa a porta `IEscaladorConversa` da S06: evento
  `ConversaEscalada = 46` no outbox, Web Push para todas as subscriptions da empresa (destinatário
  `empresa:{id}`) e SSE `conversa.escalada` (no-op até S18). (#1068)
- Identidade da conversa **por canal** e porta de envio (S34, ADR-0051): `CanalConversa` ganha
  Instagram, Messenger, ChatSite, Email e Sms; `CapacidadesCanal` diz o que cada um aceita (janela,
  modelo, tag fora da janela, mídia, botões). `Conversa.ContatoWaId` vira `ContatoIdExterno`
  normalizado por canal, com índice aberto `(EmpresaId, Canal, ContatoIdExterno)`; a migration é
  rename (não perde dados) e o Down recusa com conversa de outro canal. `GarantirPodeEnviarTextoLivre`
  recusa texto livre fora da janela sem tag válida. `ICanalMensageria` + `ResolvedorCanal` +
  adaptador `CanalWhatsApp`. (#1065)
- Agente de atendimento por WhatsApp com LLM e ferramentas (S06, onda 1, ADR-0050):
  `AgenteAtendimentoService` responde fora da requisição (fila `TurnoAgente`, consumida na Api)
  com a API Messages da Anthropic (`AnthropicMessagesClient`, modelo em `Anthropic:ModeloAgente`,
  padrão `claude-sonnet-5`), até 6 iterações de ferramenta; ao estourar, envia a frase de espera e
  escala. Ferramentas desta fatia: `consultar_cardapio`, `enviar_cardapio_imagem`,
  `consultar_pedido`, `escalar_para_dona` (mínimo; aviso por push/SSE na S07) e
  `encerrar_conversa`; pedido, endereço, janelas e CRM entram com a onda 2. Prompt com RN-01 a
  RN-08 e D3 (snapshot), notas internas marcadas `[interno]`, consumo em `UsoIa`.
  `RoteadorAcoesBotao` resolve `acao:<nome>:<payload>` sem LLM. Desligado com
  `Anthropic:Enabled=false`, `Anthropic:AgenteAtendimentoEnabled=false` ou sem chave. (#1064)
- Identificação do cliente ou lead pelo telefone no atendimento por WhatsApp (S05, onda 1,
  ADR-0050): na primeira mensagem de uma conversa nova, `IdentificarClientePorTelefoneUseCase`
  procura o `Cliente` pelo `TelefoneHash` do OTP e pelo `Telefone` do cadastro (E.164 e dígitos
  nacionais, com a variante do nono dígito que a Meta omite em celulares antigos) ou cria o lead
  com o telefone marcado como WhatsApp; a conversa é vinculada ao cadastro. A saudação
  (`SaudacaoAtendimento`: primeiro contato ou retorno com `{nome}`, link do cardápio e frase de
  espera) sai antes do agente e fica gravada como `Mensagem(Saida, Sistema)`.
  `NormalizadorTelefone` passa a ser a normalização E.164 única do OTP e do atendimento. (#1062)
- Webhook da Meta Cloud API (S03, onda 1, ADR-0050): `GET|POST api/webhooks/whatsapp` — GET
  responde a verificação, POST valida `X-Hub-Signature-256` (HMAC-SHA256, tempo constante) e
  devolve 200 quando a assinatura é válida, ou 503 quando uma mensagem falhou por motivo que um
  reenvio da Meta resolve (corrida na 1ª mensagem do contato, banco); regra de domínio não pede reenvio. `ProcessarEventoWhatsAppUseCase` resolve o
  tenant por `phone_number_id`, idempotência por `wamid` (reaproveitando `WebhookRecebido`), abre
  `Conversa`/grava `Mensagem`, marca como lida (best-effort), enfileira mídia e turno do agente
  (stub — S06 ainda não existe). `ITenantContextAccessor` liga o filtro de tenant e a RLS para o
  webhook, que não tem claim JWT — mesmo mecanismo do módulo Mobile. Fila de mídia com consumidor
  real (`AtendimentoFilaMidiaBackgroundService`, no processo da API porque a fila é em memória, com o
  tenant definido no job); fila do agente ainda sem consumidor. A janela de 24 h conta do instante
  da mensagem na Meta, não do processamento; wamid já gravado não é reinserido.
  (#1052)
- Plano do atendimento ampliado para **multicanal e sistema completo do protótipo** (ADR-0051,
  que amplia o ADR-0050): onda 8 com S34–S47 em `docs/plan/atendimento-whatsapp/09-sistema-completo.md`
  (canal como porta, Instagram, Messenger, chat do site, e-mail e SMS, consentimento por canal,
  mensagem programada, expediente da loja, atendentes, entregadores e viagens) e Onda 0 com os itens
  0.10 a 0.12. (#1060)
- Configuração do atendimento por WhatsApp por empresa (S08, onda 1, ADR-0050):
  `ConfiguracaoAtendimento` (tom, nível de sugestão, saudações, frase de espera, mensagem fora de
  área, respiro e tempo de preparo padrão), `GET|PUT api/atendimento/configuracao` (Admin — GET
  nunca 404, devolve o padrão) e `Storefront.CardapioImagemUrl`. `PromptAtendimento` compõe o
  cabeçalho do system prompt do agente a partir de tom e nível de sugestão (S06 estende). (#1050)
- Cliente da Cloud API da Meta para o atendimento WhatsApp (S02, onda 1, ADR-0050):
  `IWhatsAppCloudClient` (texto, imagem, botões interativos, template, marcar como lida, baixar
  mídia) com categoria de resiliência Polly própria (`whatsapp`) — erro de aplicação da Meta
  (ex.: `131047`, fora da janela de 24h) nunca é reenviado automaticamente. `MetaCloudWhatsAppProvider`
  passa a delegar a esse cliente em vez de falar HTTP direto. `ArmazenadorMidiaWhatsApp` guarda a
  mídia recebida no storage privado. (#1048)
- Fundacao da integracao com a **Meta Cloud API** (S01, onda 1 do atendimento WhatsApp,
  ADR-0050): `MetaCloudWhatsAppOptions` ganha `AppSecret`, `VerifyToken`, `ApiVersion` e
  `WabaId`, com o startup falhando cedo (nomeando a chave) quando o provider `meta` esta ligado
  sem credencial. `Empresa.WhatsAppPhoneNumberId` (indice unico parcial) roteia o webhook (S03)
  ate o tenant. Flag `modulo.atendimento` em `FeatureCatalogo` (ADR-0048) e
  `GET api/integracoes/whatsapp/status` (Admin) expondo o status por tenant, 404 sem a flag.
  (#1046)
- Agregado **Conversa/Mensagem** do atendimento por WhatsApp (S04, ADR-0050): entidades com
  situacao (automatica, assumida, encerrada), janela de 24 h contada da ultima mensagem do cliente,
  contexto JSON e status de entrega monotono; tabelas `atendimento_conversas` e
  `atendimento_mensagens` com indice parcial "uma conversa aberta por contato", `wamid` unico por
  empresa, filtro global de tenant e policy de RLS; `IConversaRepository` + repositorio Postgres.
  Nada consome ainda: e a fundacao de S03/S05/S06. (#1044)
- Plano do atendimento revisado para **Meta Cloud API direto** (ADR-0050 supersede ADR-0049):
  pasta renomeada para `docs/plan/atendimento-whatsapp/`, onda 1 reescrita (credenciais da Meta,
  cliente Graph com botoes e midia, webhook com `hub.challenge` e HMAC, conversa por `wa_id`,
  handoff pelo console, provider com regra da janela de 24 h) e ajustes nas ondas 2, 5, 6 e 7.
  Somente documentacao. (#1042)
- Plano executavel do backend para o atendimento da Casa da Baba via **Huggy** (ADR-0049,
  `docs/plan/atendimento-huggy/`): 31 specs em 6 ondas (fundacao Huggy, pedido e cobranca Pix,
  esteira e cozinha, estoque minimo, CRM e pos-venda, campanhas) + 6 specs de poda, cada uma com
  Problema/Abordagem/Escopo/Aceite/Rollback, testes Red e leitura minima, escritas para execucao
  por um modelo mais barato, uma spec por sessao. Somente documentacao. (#1040)
- Cadastro de cliente **pessoa juridica** (ADR-0048, item 1 do epico #1013): `TipoPessoa`,
  `NomeFantasia` e `InscricaoEstadual` no `Cliente`, com o formulario do Web alternando entre
  PF e PJ (rotulo, placeholder e campos condicionais). CNPJ ja era aceito antes — o que
  faltava era o discriminador e os dois campos. Nao ha campo `RazaoSocial`: seguindo o
  vocabulario da entidade `Empresa`, o **`Nome` E a razao social** quando o cliente e PJ.
  A busca passa a encontrar PJ tambem pelo nome fantasia. Migration aditiva com default
  `"fisica"` no banco — todo cadastro existente segue pessoa fisica sem UPDATE. (#1018)
- `TenantFeatureFlag` passa a ser lido e escrito em runtime (ADR-0048): repository com filtro
  explicito de `EmpresaId`, endpoint `GET /api/feature-flags` para o tenant logado, e servico
  no BFF Web com cache de 5min por empresa. O portal e o menu lateral escondem modulo que o
  tenant nao tem, com decisao **fail-closed** (Api fora do ar esconde, nao mostra). Item sem
  feature exigida nunca e afetado. Pre-requisito de todo o epico B2B (#1013). (#1016)
- Back-office ganha os endpoints `GET`/`PATCH api/admin/tenants/{id}/features`, que **acendem
  a aba "Features" ja existente** no Admin — ela chamava rotas inexistentes e o `catch`
  engolia a falha, exibindo "Nenhuma feature especial habilitada" como se fosse estado
  normal. O GET devolve o catalogo inteiro com o estado de cada feature, senao um tenant sem
  linha gravada nao teria como ligar o primeiro modulo. (#1016)
- ADR-0048: decisao de **plataforma unica** para o ERP B2B da FMA Informatica — ela entra como
  segundo tenant (`Empresa`) do EasyStok, com modulos novos gated por `TenantFeatureFlag`, em
  vez de fork, greenfield ou segundo backend. Epico com as speks em #1013. (#1014)
- Shell modular no Web: portal de modulos em `/launcher` como home autenticada (tela cheia,
  com pulso do dia, missoes e "Meu dia"), e menu lateral filtrado pelo modulo em que o
  usuario esta. O modulo e DERIVADO DA ROTA — sem querystring, cookie ou sessao —, entao o
  filtro sobrevive a redirect, formulario e paginacao. Nenhuma rota interna muda; o Dashboard
  continua como ancora, visivel dentro de qualquer modulo. Ver ADR-0046. (#1007)
- Missoes do dia no portal: pendencias computadas de dados que o sistema ja tem (pedidos em
  aberto, lotes vencidos, estoque critico, caixa do dia, parcelas vencidas). Sem tabela nova
  e sem migracao; missao sem fonte de dado nao e exibida em vez de aparecer como concluida. (#1007)
- Login em duas etapas com selecao de empresa: usuario com 2+ empresas ativas agora escolhe
  com qual entrar. Ver ADR-0047. (#1007)
- Telemetria OpenTelemetry (traces + metricas + logs) nos 4 hosts (Api/Web/Admin/Worker),
  exportada via OTLP pro otel-collector da VM consolidada -> OpenObserve. Opt-in por
  `OpenTelemetry:OtlpEndpoint` (vazio = desligado; dev/CI intactos). Api ganha Npgsql
  tracing, sink Serilog OTLP e sampler ParentBased (preserva trace distribuido). (#1002)

### Fixed
- `SeedSchemaBootstrap` rodava `ALTER TABLE "Empresas"` (a tabela e `empresas`) e falhava com 42P01
  a cada startup, quebrando todo seed pelo painel admin no passo 1. O `ALTER` sai: `IsSeedData` foi
  dropada pela migration `20260507011959` e e `[NotMapped]` no dominio. (#1092)
- `ToString` de `Quantidade`, `Dinheiro` e `Dimensoes` dependia da cultura do host (`1,5` no
  Windows pt-BR, `1.5` no CI Linux) e passa a ser invariante, como a Api ja produzia no container.
  O teste de wiring de metricas coleta so o proprio reader InMemory: o `ForceFlush` do provider
  esperava ~4 s pelo OTLP sem coletor e, sob carga, pulava o InMemory. (#1075)
- `POST /api/empresas/registrar` respondia 500 sob role sem `BYPASSRLS`: registrar empresa e
  cross-tenant por definicao (CRIA o tenant), entao a requisicao anonima nao tem `app.empresa_id`
  e a policy `tenant_isolation` recusava os INSERTs com `42501` em `assinaturas_empresa`. O
  `RegistrarEmpresaUseCase` passa a rodar sob `IRowLevelSecurityBypass.Begin()`, escopado por
  `using`. O escopo cobre o use case inteiro e nao so os INSERTs: `perfis` tambem tem RLS, e sem
  bypass `GetPadroesAsync` voltava vazio — o registro criava um perfil Admin por empresa em vez de
  reusar o padrao, **sem erro nenhum**. Coberto por teste de integracao com role
  `NOSUPERUSER NOBYPASSRLS` mais um controle negativo que prova que a RLS esta viva no ambiente
  (sem ele, um verde poderia significar apenas "a policy nao estava valendo"). (#1024)
- Usuario com 2+ empresas ativas nao conseguia entrar no Web: a Api emite token sem o claim
  `empresaId` nesse caso e o Web tratava como erro terminal ("entre em contato com o suporte").
  O caminho ja existia na Api (`auth/lista-empresas` + `login` aceitando `empresaId`) e nunca
  fora consumido. (#1007)
- `GET /` derrubava a landing publica com `AmbiguousMatchException`: o portal reivindicava o
  mesmo template de rota que o `SiteController`. (#1007)
- Card Financeiro do portal exibia texto de negocio fabricado ("2 contas a vencer hoje",
  variando por tenant) sem consultar nada; passa a usar as parcelas vencidas hoje do
  `financeiro/dashboard`, ou nenhum numero quando a fonte nao responde. (#1007)
- Cards do portal geravam URL malformada (`/estoque?status=vencido?modulo=producao`), que
  quebrava ao mesmo tempo o filtro de vencidos e o modulo. (#1007)

### Changed
- **Instruções dos agentes enxutas**: `CLAUDE.md` cai de 16,5 KB para 3,7 KB e importa `@AGENTS.md`; `AGENTS.md` corrigido para .NET 10, PR-first (ADR-0043) e produção na VPS; hooks `graphify hook-guard` removidos do `.claude/settings.json` por não existir `graphify-out/`. Tier alto passa a valer pela natureza da mudança, sem corte por tamanho; fluxo completo até o deploy fica pré-autorizado; o build sai do início de sessão. (#1034)
- **Processo de merge** (ADR-0055): spec de plano aprovado entra no verde, sem esperar `aprovado`
  (migração, RLS, autenticação e policy continuam esperando). CHANGELOG passa a ser por fragmento em
  `changelog.d/<issue>.md`, juntado no release por `scripts/changelog/juntar.ps1`. (#1175)
- **Registro por convenção no atendimento**: use case, ferramenta do agente e tratador de botão entram
  sozinhos; spec nova não edita mais `ServiceCollectionExtensions.Atendimento.cs`, que era ponto de
  conflito entre PRs paralelas. Testes garantem os 33 use cases, 9 ferramentas e 2 tratadores. (#1178)
- **Cardápio pelo link no modo API** (#1241, S48): `#/cardapio-link` explica que o cardápio do cliente é a página do site. O pedido feito lá já aparece na conversa pela sincronização.
- **Permissões (decisão do Felipe, 08/10/2026):** disponibilidade e saldo ficam com o Operador; incluir, editar e tirar item, com o Gerente.
- **Checkout sem login do site cobra pelo Mercado Pago** (#1254): `POST api/storefront/{slug}/checkout/guest`
  passa pelo mesmo núcleo do checkout logado e da conversa. Reserva a vaga da janela, cria o pedido em
  `aguardando_pagamento` e devolve `linkPagamento` e `expiresIn` junto do token de acompanhamento.
  **Quebra de contrato:** o corpo passa a exigir `janelaId` e `dataEntrega`. CEP fora da área de entrega
  passa a ser recusado (422) como no checkout logado. Janela esgotada e loja fechada devolvem 409, Mercado
  Pago indisponível devolve 503. Sai junto com a mudança do site `casa-da-baba`.
- **Plano do ERP da Casa da Baba: decisões da fundação** (#1328): três perfis (Dona, Atendimento, Cozinha),
  nome "Casa da Baba · EasyStok", modo escuro em tudo com a logo em placa clara, dados de outras empresas
  apagados na M0.5, sessão longa só na Cozinha e entregador por link de viagem sem login. Só documentação.
- **Uma só fábrica de e-mail para a API e o Worker** (`AddEasyStockEmail`): com a mesma configuração os dois hosts resolvem o
  mesmo provider e as mesmas opções. Antes a API nunca caía no console (o `appsettings.json` trazia `smtp.gmail.com`) e o
  Worker caía, então com o mesmo `.env` um mandava e-mail e o outro fingia. Sem `Smtp:Host`, `Smtp:Port` e um remetente
  (`Smtp:FromEmail`, ou `Smtp:Username` com arroba) o provider cai no console, com aviso na subida nomeando a chave que falta.
  Porta, modo, timeout ou remetente inválidos, e `Smtp:Modo=Nenhum` em Production, recusam subir nomeando a chave. Os nomes do
  `.env` (`Smtp__Host`, `Smtp__Port`, `Smtp__Username`, `Smtp__Password`, `Smtp__FromEmail`) continuam valendo. (#1351)
- **Com `Email:Provider=sendgrid`, `SendGrid:FromEmail` passa a ser obrigatório**: sem ele a API e o Worker recusam subir
  nomeando a chave, em vez de usar um remetente inventado. O SendGrid usa um remetente só e ignora a categoria. (#1351)
- **O console diz que nada saiu**: `ConsoleEmailService.EnviarAsync` devolve `Simulado` com provider `console`, e o outbox
  grava `Simulado` (N2), nunca `Enviado`. O canal de e-mail só repassa o desfecho do serviço, então a marcadora
  `IEmailServiceSimulado` da N2 saiu. Na conta criada pelo admin, `EmailEnviado` só fica verdadeiro quando o e-mail saiu de
  verdade. (#1351)

- **Publicação do console versionada** (#1358): `scripts/deploy/console-deploy.sh` monta o `origin/master` em modo API, carimba o SHA na página, guarda backup e confere o SHA servido; avisa quando a API está noutro SHA.
- **Plano do Mercado Pago com o estado de 02/10** (#1361): código de cobrança completo; produção sem zonas, janelas, coordenada da cozinha e credencial do Mercado Pago.
- **Troca de e-mail em duas etapas** (N4): `PATCH api/auth/me` exige `senhaAtual` (ausente ou errada responde 403, e a errada conta como falha de login) e só grava o endereço como pendente: a confirmação sai para o endereço novo e o aviso, com o novo mascarado, para o antigo; o `Email` troca no clique do link (`confirmar-email`), que também derruba as sessões. Um novo pedido apaga os tokens anteriores. O endpoint ganha o limite `auth`. O Admin (`PUT api/usuarios/{id}`) só troca o e-mail de usuário exclusivo da empresa e nunca de quem tem perfil SuperAdmin (403, "peça ao próprio usuário ou ao suporte"), pelo mesmo fluxo e com auditoria; alterar só o nome segue permitido.
- **Atenção ao publicar:** o front precisa enviar `senhaAtual` no `PATCH api/auth/me` ao trocar o e-mail, e ainda não há tela para o link `/auth/confirmar-email?token=` (o `POST api/auth/confirmar-email?token=` já existe). Rollback: o `Down` remove só as três colunas; `git revert` devolve a troca de e-mail antiga, com a brecha.
- `AlertarImpressoesAtrasadasUseCase.ExecuteAsync` devolve a lista de atrasadas (antes, a contagem) e lê o limite da configuração. `AvaliarLembretesUseCase`, `NotificarAtrasoPedidoUseCase` e `AlertarImpressoesAtrasadasUseCase` recebem `IOptions<PrazosOptions>`; `ICaixaRepository` e `IRotinaRepository` ganham as consultas que o job usava direto no `DbContext`. As constantes públicas continuam como padrão. SSE e Web Push não mudam.
- O selo "Precisa de você" do console (5 min) não muda: é sinal visual, diferente do alerta externo, que usa o limite do backend (opção A, padrão da spec; a decisão final é do Felipe). Rollback: `Notifications:Prazos:Habilitado=false` desliga sem deploy; `git revert` do squash devolve os quatro fluxos como estavam. Sem migração.
- `POST/PATCH api/notificacoes/rotinas` recusa `TriggerTipo = Cron`/`CronExpression` e `agenda.horario` fora do formato com 400 (antes aceitava cron que nunca disparava; erros de validação de caso de uso nessas rotas agora são 400, não 409).
- `ColetorProdutosVencendo` só é registrado com `Notifications:Coletores:ProdutosVencendo:Habilitado=true` (padrão `false`): tinha data UTC, `CorrelationId` de 65 caracteres numa coluna de 64 e payload que não casa com o template.
- `ContaFinanceiraVencimentoJob` só publica os avisos de vencimento e carimba o dedup com `BackgroundJobs:EnableContaFinanceiraNotificacoes=true` (padrão `false`); desligado, continua marcando a parcela vencida.

- **Opt-in do WhatsApp de plataforma**: o canal só sai para usuário identificado com `ConsentimentoNotificacao(WhatsApp, categoria, OptIn = true)`, inclusive em `Seguranca`; telefone solto no payload ou sem registro pula o canal (o e-mail segue). Loja igual a hoje. `ResolverCanaisPermitidos` ganha `remetente` e `usuarioDestinoId` opcionais.
- **Uma versão da Graph**: `Notifications:WhatsApp:Meta:ApiVersion`, padrão `v26.0` (plano B `v25.0`), `BaseUrl` derivada (override só na homologação). A `v19.0` saiu do código e do compose de homologação.
- **Tabela única de códigos de erro da Meta** (`CodigosErroMeta`), também para o provider da loja: `132001`, `131050`, `132000` e outros viram falha permanente; `131056` e `130429`, transitória.
- O Admin não vincula mais o `phone_number_id` da plataforma a uma empresa (409 `NumeroReservadoDaPlataforma`).
- Arch-test `PlataformaNaoDependeDoAtendimentoTests` (com canário) e `fake_meta.py` com número de plataforma, validação do copy code, telefone final `7777` (aceita e demora) e `template_category_update`.

Rollback: `Notifications__WhatsApp__Plataforma__Provider=stub` desliga o envio de plataforma sem deploy (as mensagens passam a `Simulado`); esvaziar o override do número na Meta devolve o webhook ao app; a versão da Graph volta por `Notifications__WhatsApp__Meta__ApiVersion`. Antes de reverter o squash: `UPDATE notif_outbox_mensagens SET "Status" = 'Suprimido' WHERE "Remetente" = 'Plataforma' AND "Status" IN ('Pendente', 'EmEnvio')`.
- `EsqueciSenhaCommand` e `ResetarSenhaCommand` ganham `Ip` e `UserAgent` (montados pelo controller, nunca do corpo) e perdem o `BaseUrl`; `forgot-password` passa de 200 para 202. O Web manda só o e-mail.

Rollback: reverter o squash; a migração é aditiva e o código antigo ignora as colunas novas. Para parar só os envios do reset sem deploy, desligar a rotina `reset_senha_global` ou `senha_alterada_global` no catálogo.
- **Troca de e-mail de convidado pendente é imediata** (`PUT api/usuarios/{id}`): dispensa o `EmailPendente` da N4 (o convidado nunca teve acesso), revoga os convites abertos e emite um novo para o contato novo. Para quem já aceitou vale a regra da N4 e o convite não entra.
- **Login Google de convidado consome o convite**: identidade com e-mail verificado confirma o e-mail, grava a via `Google` e revoga os convites; a senha segue inutilizável até a pessoa definir uma por "esqueci a senha".
- **"Esqueci a senha" de quem ainda não aceitou** reemite o convite (mesma resposta 202, 3 por hora) e nunca gera token de reset: o reset só vale para quem já aceitou.
- Modelo WhatsApp `convite_acesso_whatsapp_v1` sobe para a versão 2 (botão URL com o token) e a rotina `convite_acesso_global` ganha `audiencia: convidado`; o seed troca o v1 sem tocar em template editado por pessoa.
- **Agente entende áudio transcrito**: o prompt explica que `[áudio] <texto>` é a transcrição automática
  (tratar como fala do cliente, pode ter erros) e que `[áudio recebido]` é áudio sem transcrição. Com transcritor
  disponível, o turno do agente para áudio sai do job de mídia depois da transcrição (ou na primeira falha do
  download), e não mais do webhook, que fazia o agente responder antes do texto chegar. (#1406)
- **Respostas rápidas no compositor do console**: "/" no campo ou o botão Respostas abrem uma lista compacta de respostas prontas, com busca, setas e Enter que insere no campo; o modal de cartões saiu. Criar, editar e arquivar respostas e ligar, desligar e escrever as mensagens automáticas mudaram para a aba "Respostas e automáticas" da Gestão, ligadas à API (S42), com aviso de variável que o EasyStok não preenche. Tags do cliente gravam no cadastro pela API (S24). Notas internas aparecem como post-it no fio da conversa e na Ficha. O que o automático mandou fica marcado na conversa e listado na aba "Automáticas" do Histórico. (#1441)
- **Balcão, banner de pausa e Ficha mais simples** (homologação de 07/10): o Balcão abre em "Todas", "Precisa de você" e os canais viram chips numa linha, a linha da conversa mostra estado, prévia e contador de mensagens sem resposta; o banner de pausa cabe em uma linha com as duas ações; a Ficha ganha seções recolhíveis (Cliente, Contato e endereço, Tags, Histórico, Comanda) lembradas no navegador. (#1442)
- **Quem abre e fecha a loja** (#1443): `POST api/atendimento/expediente/controle` passou de Admin para Gerente+; voltar ao horário (`Automatico`) continua só da dona. Horário e mensagens seguem só de Admin.
- **Respostas mais objetivas**: assistente em até 2 frases, sugestão e agente em 1 a 3 frases de WhatsApp sem floreio; `avaliar-agente.mjs` conta respostas prolixas. (#1445)
- **Gestão desmontada** (#1447): o modal "Gestão" saiu; cada aba abre como tela do módulo dono (Horários e mensagens no Atendimento, Produção e cardápio do dia na Cozinha, Caixa do dia no Financeiro, Fidelidade e cupons em Campanhas, Integrações de entrega em Configurações, Janelas de entrega em Entregas). O botão do Balcão virou "Módulos".

- **Painel "Cardápio de hoje" com foto e categoria** (#1448): cada item mostra a foto da vitrine (a carta desenhada só quando não há foto ou ela não carrega), os itens se agrupam pela categoria, há busca por nome, categoria ou porção, e um botão envia a foto do item ao cliente pela rota de #1439. "Anotar na comanda" e "Gerir o dia" seguem iguais.

- **Decisões do cardápio registradas no plano** (D-M1-01, 05, 07 e 08, Felipe 08/10).
- **Decisões da produção registradas no plano** (D-M2-01, 04 e 06, Felipe 09/10).
- **Migrações** `AddExclusaoLancamentoCaixaMobile` e `AddCarimboServidorMobile` (#1520): colunas `deleted_at` e `deleted_by` em `mobile_cash_entries`, e `server_updated_at` com índice nas cinco tabelas do pull. Aditivas, com `Down`.
- **O que a contagem cobre** (#1542): o formulário de ajuste começa pelo que está guardado, vencido incluído, e diz quantas são vencidas. Antes começava pelo saldo vendável e a API repartia a contagem também entre os lotes vencidos.
- **Produção** (#1542): ao produzir uma porção de prato com mais de um tamanho, avisa quando a receita não acompanha o tamanho (rende em porções, ou rende em peso e tem embalagem). A conta da baixa não muda.
- **Planejamento com receita em peso** (#1552): o prato deixava de entrar na conta dos insumos (porções contra kg). Agora o planejamento usa o peso da porção padrão do cardápio; sem peso escrito na porção, a tela diz qual prato ficou de fora e o que fazer.
- **Aba Insumos** (#1552): a embalagem escolhida numa porção aparece como "embalagem de N porções", em vez de "não entra em receita".
- **Aviso de tamanho da porção na produção** (#1542, #1552): some quando a receita rende em peso e a porção tem embalagem própria. Onde ainda não está montado, diz o que fazer.
- **Migration** `AddEmbalagemDaPorcao`: coluna opcional `EmbalagemProdutoId` em `cardapio_item_variacao`, com `ON DELETE SET NULL`. Sem mudança em dado existente.
- **Menos idas ao banco, mesmo resultado** (#1564): a lista de viagens carrega os pedidos e os clientes de todas as paradas em duas consultas, em vez de duas por parada; o avaliador de lembretes pergunta uma vez por tipo quais avisos já existem, em vez de uma vez por pedido ou conversa; o resumo de perdas deixa a baixa de insumo da produção no banco; e o cancelamento de pedido lê só as saídas daquele item, não o histórico inteiro do produto. Nada muda na tela.
- **Console parado não refaz trabalho** (#1565): a sincronização de 5 s com a mesma leitura mantém o estado (a tela não re-renderiza inteira), a recarga do relógio de lembretes e de notificações não pisca "carregando", a agenda de encomendas deixa o polling de 15 s para quando o SSE cai, e toda leitura tem prazo de 20 s.

- **Menos consultas no polling** (#1566): o aparelho pareado grava `last_seen_at` no máximo uma vez por minuto, ou quando o IP muda; a inbox do atendimento guarda o SLA em cache por 60 s e confere a conversa por leitura leve antes de listar as mensagens; a sugestão de produção busca os produtos dos pratos numa consulta só.
- **Motor de notificações ocioso** (#1566): sem mensagem aberta no outbox, o dispatcher termina o wakeup com uma consulta e não abre transação. No health, a contagem de eventos processados sem outbox olha só os eventos ocorridos nas últimas 48 h.

- **Inbox do atendimento mais leve** (#1575): o resumo das conversas (consultado a cada 5 s) não lê mais o contexto da conversa no banco e passa a trazer `pedidoStatus`, o status do pedido em andamento, igual ao da rota do pedido. Os demais campos seguem iguais.
- **Cardápio da comanda lido uma vez** (#1575): o menu e as porções saem da mesma leitura do cardápio, em vez de duas por requisição; a lista de itens tirados do cardápio filtra os arquivados no banco. O cardápio do site não muda.
- **Sugestão de produção sem lote zerado** (#1575): a sugestão busca só os lotes com saldo ou descoberto; o resultado é o mesmo.
- **Outbox de integração com uma consulta por rodada** (#1575): o Worker buscava os pendentes em quatro consultas (uma por shard) a cada 30 s; agora é uma, na mesma ordem e com o mesmo limite.
- **Relatórios simultâneos cabem no pool** (#1575): o padrão de relatórios em paralelo no Worker cai de 20 para 2, porque cada um usa até 2 conexões e o pool padrão tem 10. `Reporting:Runner:MaxConcurrentRunsPerInstance` segue valendo para quem quiser mais.

- **Laço da inbox do console** (#1576): as voltas de 5 s leem só a página 1 (a inbox inteira vem ao entrar e a cada minuto), a carga abre no máximo 4 conversas por vez, mensagens e pedidos já lidos sobrevivem a ir ao Hall, à Cozinha ou às Entregas, e o pedido passa a ser relido pela mudança de status do resumo quando a API o envia.

- **Entregas e Cozinha** (#1577): a ação que já sai no SSE ("Sair", "Entregue", avançar no KDS) não relê a tela duas vezes, e o cadastro de entregadores, janelas e bloqueios deixa de ser relido a cada evento de pedido (um toque em "Entregue" caiu de 14 leituras para 4).
- **Mídia da conversa** (#1577): foto, áudio e arquivo só são baixados quando o balão chega perto da tela, e os últimos 30 ficam guardados para a volta à conversa.
- **Digitação no Balcão** (#1577): a tecla re-renderiza só o campo de escrever; o rascunho vai ao estado e ao navegador na pausa (400 ms) e na hora em blur, envio, troca de conversa e saída da tela. Cartões da lista e balões em `memo`: mudar uma conversa re-renderiza um cartão, e mensagem nova não re-renderiza os balões antigos.
- **Animações do console** (#1577): o pisca do SLA estourado e o ponto "ao vivo" do KDS animam só `opacity` e `transform`; topo e rodapé sem `backdrop-filter`.

- **Jobs de estoque sem tenant desligados em produção** (#1588): o analisador de estoque (60 min) e o recálculo de velocidades (6 h) rodavam sem tenant e não liam nada. Ficam desligados até ganharem tenant; um guard de arquitetura trava as duas flags.
- **Produção do dia por toque, como no PWA** (#1624): a tela deixa de ser um formulário com lista de pratos. Cada prato (e cada porção) é um cartão com o saldo; tocar soma uma porção, "−" tira, e o lote do dia fica ao lado com validade em um toque (3, 5, 7, 10, 15 dias) e o peso da porção já preenchido. "Falta produzir" traz a sugestão do planejamento e um toque põe a quantidade no lote. O envio, as etiquetas e o rascunho são os mesmos.

- **Lote compacto** (#1627): cada prato do lote é uma linha com porções, validade e peso; os campos abrem em "Ajustar". O lote segue a ordem do cardápio.
- **Cartão do prato não muda de tamanho ao entrar no lote** (#1627): o "−" e a contagem têm lugar reservado; o toque seguinte não cai em outro prato.
- **Linha vinda do Planejamento** (#1627): já traz o peso do prato e, em prato com porções, vira a porção do primeiro toque.
- **Tabela dos 7 dias em tela estreita** (#1627): cada dia vira um cartão, sem rolagem lateral.
- **Impressos no layout "bilhete da casa"** (#1632): pedido, agendamento, etiqueta de entrega e canhoto de cozinha saem da API em A4, A5, etiqueta 10x15, bobina 80 mm e 58 mm, com QR. O QR da cliente abre o WhatsApp da casa já com o número do pedido; o do canhoto traz o código do pedido. A etiqueta de entrega na térmica sai em duas vias (entregador e pacote), uma página cada, para a guilhotina cortar. O código de barras Code128 saiu: nenhum fluxo lia.
- **Logo dos impressos vem da loja** (#1632): o impresso usa o logo cadastrado na loja e, na falta dele, o da vitrine. No jato de tinta sai sempre; na térmica só quando o aparelho pede, senão sai o nome da casa em texto.

- **Geocodificadores em cadeia no frete por raio** (#1658): o provedor com chave (`tomtom` ou `google` em `Storefront:Frete:GeocodingProvider`) vem primeiro e o Nominatim, se ligado por `ENABLE_NOMINATIM_GEOCODING`, depois. Provedor fora do ar, sem resposta ou com resultado impreciso cai no seguinte, sem erro para o checkout. Sem chave e sem Nominatim, segue NoOp. Mudança de comportamento: provedor configurado sem chave, com o Nominatim ligado, agora usa o Nominatim em vez de NoOp.
- `AddCorridasExternas` (#1669): tabela `corridas_externas` com RLS por loja. Aditiva; sem ela a funcionalidade fica inerte.
- **Deploy da VPS** (#1674): `scripts/deploy/vps-deploy.sh` reprova e reverte quando o `ez-worker` reinicia depois de recriado. Antes, um Worker em laço de restart passava como verde por estar `Running` entre uma queda e outra.
- `AddCobrancaContaRecebedora` (#1688): três colunas anuláveis em `cobrancas_pedido` e `OrigemCredencial = 'global'` nas cobranças online existentes. Aditiva.
- **Módulos do Console:** aplica o acabamento Contorno escolhido, com cartões de papel, ícones V4 de 32 px no hall e contorno de seleção nos menus; preserva favoritos, organização e exibição compacta. (#1692)
- **CI do Console**: as tres provas da design sprint (#1683) passam a rodar no job de qualidade. (#1695)
- **Atendimento no computador: o pedido primeiro** (crítica de UX na tela real): com pedido em andamento, cadastro, tags e histórico nascem recolhidos e a comanda sobe para a primeira tela da Ficha; o cardápio abre do lado do Balcão e deixa a conversa inteira à vista; as larguras padrão acompanham a tela (conversa de 380 para 480 px em notebook, Ficha de 420 para 520 px em tela larga). Quem já ajustou seções ou larguras continua com a escolha. (#1697)
- CI: as imagens do Postgres vêm de espelhos com fallback (mirror.gcr.io, ECR pública, Docker Hub) e novas tentativas, para o limite de uma fonte não reprovar o build.
- **Console mais direto**: explica modelos aprovados, destaca filtros e compacta ações do cardápio, foca a busca ao adicionar itens, reduz o agendamento, esclarece a confirmação da produção e evita o destaque duplicado na página inicial. (#1801)
- Balcão: o Cardápio de hoje troca de lado (Balcão ou Resumo), muda de largura pela alça (mouse ou setas) e amplia ou reduz; o Assistente encaixa à esquerda, à direita ou fica solto e arrastável pela barra de título, com largura ajustável. A escolha fica lembrada no aparelho e a conversa sempre mantém 320 px à vista. (#1806)
- Balcão: lead sem cadastro vê "Cadastrar cliente" como primeiro passo, com o que a conversa trouxe já preenchido; o pedido só nasce para cliente cadastrado.
- Produção: a Produção do dia mostra a foto do prato em cada cartão, os três passos (tocar, conferir, registrar) no topo, tooltip no "−" e no contador e um estado vazio que leva aos Itens do cardápio. (#1809)
- Cardápio: os Itens do cardápio ganham filtro por categoria com contagem, combinado com a disponibilidade e a busca; reordenar pede Todos e Todas as categorias. (#1808)
- **CI do console**: as provas de #1803 a #1810 e da R11 de impressão passam a rodar no `console.yml`. (#1826)
- **Spec da homologação gravada de 15/10** (#1839): seis pacotes sem migração (impressão única,
  Balcão, link de compra, tela inicial, cardápio e entregas) com as decisões do Felipe.
- **Atendimento encerrado vira história** (#1839): reabrir a conversa solta o pedido já entregue ou
  cancelado; o novo atendimento começa sem pedido e o antigo segue em Cliente e histórico.
- **Cliente sempre à vista no Resumo** (#1839): cartão com o resumo da IA, o total de pedidos e de
  compras, o que costuma pedir, as tags e a última nota interna como post-it. O título diz "Comanda N".
- **Nota interna em post-it** (#1839): abre como caixinha acima do composer e fica fixa no Resumo.
- **Modelos aprovados dentro de Respostas** com a janela fechada, como no protótipo (#1839).
- **Pagamento** (#1839): o selo de pago só anima quando o pagamento chega; extrato do pedido (itens,
  frete, pagamentos e saldo) no topo da modal.

- A tela inicial do console deixa arrastar os quatro números e os oito cartões de módulo para qualquer ordem, com mouse, toque ou teclado, sem entrar em modo de organização. A ordem fica salva por pessoa.
- Cada cartão de módulo ganhou uma ilustração da Casa da Baba e, quando há dado, uma linha viva com o que pede atenção (conversas sem resposta, pedidos no preparo, prontos para sair, situação do caixa e pratos com pendência).
- **Imprimir em um clique, com o papel na tela** (#1839): o modal de impressão abre já com a prévia do
  layout aprovado e um botão "Imprimir", que abre o diálogo na hora e grava a tentativa no histórico; a
  conferência "saiu certo?" fica no rodapé sem travar o Fechar. "Ver canhoto" no Balcão abre direto o
  canhoto de cozinha da API; Cozinha e fila de canhotos deixam o PDF próprio e usam o mesmo caminho.
- **Resumo do atendimento no layout dos impressos** (#1839): o "Exportar PDF" do Encerrar vira
  "Imprimir resumo", desenhado pela API no estilo bilhete em qualquer papel.
- **Lalamove fora do pedido do cliente** (#1839): contratar transporte fica em Entregas.
- **Polimento visual do console no desktop** (#1868): sem moldura no foco programático do início da tela; janelas da Seleção do atendimento em grade (Manhã e Tarde lado a lado); títulos do Hall alinhados; "Organizar transportes" alinhado à esquerda; topo da conversa em uma linha na coluna larga; blocos da modal na mesma régua da folha; Balcão 420 e Resumo 480 em 1920; ativo do trilho e do submenu sem anel que parecia foco; botões da mesma linha com a mesma altura e raio; desabilitado sem opacidade; tamanhos soltos levados aos tokens.
- **Balcão e Resumo sem repetição** (#1868): o cartão do visitante mostra "Comanda N" no lugar do nome genérico e perde a etiqueta "Aguardando equipe" e a origem "Chat do site" repetidas (a atenção vira trilho lateral, o estado segue para o leitor de tela); Balcão e Resumo sem cabeçalho no desktop de três colunas e Responsável na linha dos chips quando cabe; o saldo ganha destaque no Resumo e deixa de se repetir no acesso Pagamento, no "Já coberto" (quando o botão do complemento já diz) e no bloco Cobrança abaixo do Extrato; a janela não se repete no papel da comanda; o composer do chat do site some com Anexar, Fotos e microfone que o canal não aceita (o motivo segue escrito); o pedido do site no fio se lê à esquerda, em corpo de apoio.
- **Navegação do console no desktop** (#1870): o rodapé "Conversas ao vivo" só aparece com aviso, falha, integração parada ou sessão vencendo; o Sair (e quem está logado) mora no menu do avatar, no pé do trilho do Balcão e dos módulos e no canto do hall, com o tema junto no Balcão; Balcão e módulos usam o mesmo trilho (100/84 px, `--largura-trilho`) e o mesmo nome do módulo ("Atendimento"); módulo de trabalho (Entregas, Cozinha, Atendimento) mostra as telas na barra no lugar do menu lateral de 216 px, sem o título "Entregas" repetido; a densidade compacta mora em `tokens.css` e vale inteira no Balcão, na moldura dos módulos e nas janelas de Entregas e Gestão (título de área com 20 px em todos).
- **Ficha, conversa e composer mais enxutos** (#1870): a ficha com pedido cabe em 1280x720 sem rolar e com Pós-venda visível; a faixa de quem responde fica em uma linha e não repete o estado; o chat do site mostra "Só texto neste canal", com o motivo inteiro no title e no leitor de tela.
- **Trilho do console em um CSS só** (#1872): o Balcão e os módulos compõem item, ativo, início, espaço e avatar de `componentes/Trilho.module.css`; a largura do trilho vem só de `TRILHO` (`dominio/paineis.js`), que gera `--largura-trilho` na partida, e o item deriva dela (100 e 84 px). Nos módulos o item deixa de passar de 54 px por padding e espaçamento próprios (58,8 e 66,8 px antes).

- **Revisão antes de cobrar na barra do pedido** (#1875): tocar em Pix, Cartão por link ou Maquininha na pergunta "Como o cliente vai pagar?" só marca o meio; aparece o resumo (total dos itens, meio, frete ao gerar e, na encomenda, o sinal de 50% em reais) e o pedido só é criado, e a cobrança enviada, no botão "Confirmar e enviar". Antes um toque no chip já fazia o `POST` do pedido.
- **"Marcar pago" deixa de ser o botão mais forte com link do Mercado Pago aberto** (#1875): vira secundário, com a nota de que o provedor confirma sozinho; sem link (demonstração, maquininha, sinal manual) segue primário.
- **"Quanto fica?" antes de gerar** (#1875): a comanda de uma encomenda de outro dia mostra o sinal e o restante em reais ao lado da nota do frete (o frete em número continua do servidor).
- **Janela de entrega antes da comanda na Seleção do atendimento** (#1875): em 1366x768 a lista de 10 janelas começava a 541 px de um conteúdo de 1026 e nenhuma cabia sem rolar; agora começa a 164 px e as 10 cabem (também em 1280x720).
- **Receita das provas de navegador com fixtures do servidor** (#1876): o README do console passa a dizer como gerar as fixtures dos testes .NET (`EVIDENCIAS_CHAT_RECEPCAO`, `EVIDENCIAS_CHAT_COMANDA`) e rodar `prova-1742-chat-recepcao` e `prova-1748-chat-comanda`.
- `CheckoutCoreService` ganha `SimularAsync` e extrai `ValidarJanelaAsync`, `ValidarEncerramentoEPrazo` e `CotarFreteAsync`, usados também pela criação do pedido, para a simulação e o pedido recusarem igual.
- **Console congela o que está fora da jornada "pedido a dinheiro"** (#1891, refs #1889): Campanhas, o financeiro gerencial (fica só o Caixa do dia), o chip do canal E-mail e os botões de cotação e consulta da Lalamove somem da interface nos dois modos. A lista única fica em `dominio/congelados.js`; descongelar é tirar o item da lista. O cartão Lalamove em Integrações continua (a chave segue cotando o frete do pedido). Prova nova `prova-congelados.mjs` no CI.

- **Fragmentos do changelog na forma da ADR-0055** (#1892): os 37 fragmentos sem seção ou com seção em português ganham a seção Keep a Changelog que o `juntar.ps1` espera.
- **`.env.example` completo** (#1892): lista, sem valores, todas as variáveis que o código e o deploy leem (SMTP, Meta, Mercado Pago, seed, IA, Web Push, healthchecks, migrations).
- **Prova do congelamento no CI do console** (#1897, refs #1889): o `console.yml` roda `ferramentas/prova-congelados.mjs` logo após a prova do hall, garantindo que cada item de `dominio/congelados.js` existe no catálogo e está fora da tela.
- **Balcão sem o ruído "Automático"/"Com você" nos cartões** (#1899, refs #1889): a etiqueta do estado some quando é só quem conduz (o texto segue no leitor de tela e a pílula da BarraModo diz quem está com a conversa); Bloqueado, Encerrada, Outro atendente e os motivos continuam escritos. Medido no laboratório: de 8 cartões com etiqueta para 1 em 50.
- **Um nome só para o pedido no console** (#1883, parte do console): Entregas e Cozinha mostram "Comanda N" quando a API entregar `numeroComanda`, com o código curto como referência pequena; sem o campo, segue o código curto. Textos de recusa e feedback do KDS usam a mesma frase.
- **Acessibilidade na jornada**: avisos de situação como `<output>` no lugar de `role="status"`, seções nomeadas no lugar de `role="group"`; o `Vazio` aceita a tag do bloco.
- Plano do atendimento: a spec S11 passa a cobrir a troca da forma de pagamento com a cobrança já
  enviada e o desfazer de pagamento registrado à mão, defeito achado pela operadora no protótipo. A S44
  ganha veículo, placa e empresa do entregador, retrato do entregador por parada e relatório de
  entregas por bairro; a S48 nova liga o cardápio que o cliente marca à conversa. (#1072)
- Home autenticada passa a ser `/launcher` em todos os pontos (landing logada, onboarding,
  cardapio, lojas, 404 e primeiro slot do bottom nav mobile). Deep link continua vencendo:
  `returnUrl` valido tem precedencia sobre o portal. (#1007)
- Homolog consolidada na VM unica `hiram-demo-vm` (eastus2, `*.20.98.234.200.sslip.io`), host
  compartilhado com jornada + levante atras de um Caddy central; stack sobe sem caddy proprio
  (`docker-compose.azure.noedge.yml` + `EASYSTOK_COMPOSE_EXTRA` no vm-deploy.sh). VM antiga
  (westus2) descomissionada; observabilidade (Dozzle/Uptime Kuma/OpenObserve) no ar. (#997)

### Security
- `RlsBypassAllowlistTests`: allowlist de quem pode referenciar `IRowLevelSecurityBypass` em
  codigo. O port ja existia e ja tinha consumidores; o que faltava era o portao — injetar uma
  interface e barato demais para uma decisao que desliga a segunda camada de defesa do ADR-0010.
  Agora consumidor novo exige editar a lista, e isso aparece no diff da PR. Mencao em `<see cref>`
  nao conta: falso positivo em guarda de seguranca treina quem mantem o codigo a engordar a
  allowlist ate calar o teste. (#1024)
- Bump `System.Security.Cryptography.Xml` 10.0.7 -> 10.0.10 (5 advisories high / NU1903 que
  quebravam todo PR no CI com `-warnaserror`). (#999)
- Adota o Protocolo Operacional v4.0 (PR-first, issue-driven, auto-merge por tier). Supersede a v3.1
  (master-first). Ver ADR-0043 e CLAUDE.md. (#881)
