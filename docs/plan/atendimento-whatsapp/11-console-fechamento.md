# Console do operador: correções para fechar (F06 a F13)

Revisão de 2026-09-30 sobre o master `530dc462` · Issue: #1232 · Base: [10-console.md](10-console.md) e ADR-0054 ·
Protótipo de referência: `casa-da-baba-atendimento/prototipo-omni` @ `a9831ab` (igual ao `main`).

## Veredito da revisão

| Frente | Resultado | Como foi medido |
|---|---|---|
| Modo massa × protótipo | **IGUAL.** 15 de 16 telas idênticas pixel a pixel; a 16ª difere em 0,02 % (rótulos do passo a passo do pedido) | Playwright, 1440×900, 390×844 e 1024×768, relógio congelado, `pixelmatch` |
| Modo API × protótipo | **DIVERGE.** Ações que só mudam o navegador sem avisar, contadores da massa aparecendo, Cozinha e Entregas bem mais pobres que o protótipo | API local (`docker-compose.local.yml`) + 3 conversas reais pelo chat do site + captura lado a lado |
| Código do console (F01–F05) | 3 altos, 6 médios, 4 baixos. Fronteira de camadas limpa, 401 correto, sem XSS | Leitura + `npm run qualidade` (exit 0, 33 warnings) + provas F03/F04/F05 (exit 0) |
| Backend que o console usa | Isolamento de tenant correto. 2 altos (concorrência e autorização), 1 tabela sem RLS | Leitura de controllers, use cases e migrations |

Legenda da coluna **Prova** nas tabelas: **vivo** = reproduzido na API local; **visual** = visto na
captura; **código** = lido na linha citada, não reproduzido.

## Ordem

```
F06 honestidade do modo API ──► F07 sincronização ──► F09 ficha ─► F10 respostas/lembretes ─► F11 lote/cardápio
F08 backend (paralelo, tier ALTO) ─┘                                                           │
F12 paridade visual Cozinha/Entregas + defeitos visuais (após F06) ─────────────────────────────┤
F16 integrações (chaves + teste) ─► F17 Lalamove ────────────────────────────────────────────────┤
F14 caixa ─► F15 fidelidade (desconto entra no pedido) ─────────────────────────────────────────┤
F13 deploy e validações do Felipe (último) ◄────────────────────────────────────────────────────┘
```

**Go-live mínimo = F06 + F07 + F08 + F16 + F18 + F13.** A F16 entra no mínimo porque sem ela uma chave
vencida do Mercado Pago ou da Meta para a esteira sem ninguém ver. A F18 entra porque, sem ela, nenhuma
conversa fora do WhatsApp vira pedido (decisão do Felipe, 2026-10-01). F09 a F12, F14, F15 e F17 fecham a
paridade com o protótipo.

Toda fatia segue as convenções do [README](README.md#convenções-do-executor-vinculantes-resumo-do-claudemd-v40):
issue, branch, worktree, `npm run qualidade`, prova em `EasyStock.Console/ferramentas/prova-*.mjs` que
falha antes (Red), e "sem `VITE_FONTE_DADOS`, o console abre como hoje" como aceite fixo.

---

## F06 · Honestidade do modo API (P0) · #1236

**Problema.** No modo API, 26 ações vão para a API e todas as outras mudam só o navegador, sem aviso
(`aplicacao/acoesApi.js:19-24`). A sincronização de 5 s desfaz a maior parte delas. A dona acha que
fez e não fez. O caso mais grave: **Encerrar não encerra.**

**Achados.**

| # | Achado | Prova | Onde |
|---|---|---|---|
| 1 | Encerrar pelo modal (botão real) não chama a API. O banco segue `Assumida`, sem `EncerradaEm`. `encerrarAtendimento` da API existe e nenhuma tela chama | **vivo** | `ModalEncerrar.jsx:84`, `casos/encerramento.js:124` |
| 2 | Foto, áudio, figurinha e peça da galeria aparecem como enviadas ("lida") e nunca saem. O S02 (`.../mensagens/imagem`) já existe | código | `AtendimentoProvider.jsx:225`, `reducer.js:611-617`, `PainelGaleria.jsx:132-144` |
| 3 | Reabrir, recebido na entrega, refazer cobrança, bloquear, cupom/recompensa/endereço na comanda: só memória | código | `PainelAtendimento.jsx:123`, `BlocoCobranca.jsx:348,365`, `comandaApi.js:139-146` |
| 4 | "Simular" continua visível: cria conversas `sim-*` que somem em 5 s e desloca o relógio | código | `Moldura.jsx:346` |
| 5 | Gestão mostra Produção, Caixa, Janelas, Fidelidade e Integrações só em memória. Integrações guarda credencial no navegador. Janelas duplica "Janelas e frete" da gaveta Entregas, que é a ligada | **visual** + código | `ModalGestao.jsx:20-26` |
| 6 | Trilho e topo mostram dados da massa: "Automáticas 6", avatar "T" da Thati no lugar do usuário logado | **visual** | trilho do `App.jsx` |
| 7 | O aviso de ação ("Pedido não criado…") some no ciclo seguinte de 5 s | código | `reducer.js:238` |
| 8 | No modo API o Balcão publica o estado inteiro no BroadcastChannel e aplica `acao` recebida sem passar por `comApi`. `#/cardapio-link` abre sem login | código | `AtendimentoProvider.jsx:116-122`, `canalEntreJanelas.js:31,38`, `useEspelhoCardapioLink.js:35` |

**Abordagem.**
- Uma lista única em `aplicacao/api/` das ações **não ligadas**; `comApi` troca cada uma por
  `AVISO_API` ("ainda não ligado nesta versão"), como `api/comanda.js` já faz. Teste de prova:
  percorre todas as chaves de `acoes` e falha se alguma não estiver em "ligada" nem em "avisa".
- Encerrar: `encerrarComResumo` chama `POST conversas/{id}/encerrar`; resumo, avaliação e anotação
  locais ficam fora (avisam) até existir endpoint.
- Mídia: foto pela API do S02 no WhatsApp; áudio, figurinha e canais sem mídia avisam.
- Esconder no modo API: Simular. Contadores e avatar vêm da sessão e da API; sem dado, sem badge.
- Abas da Gestão sem backend **não somem** (decisão do Felipe, 30/09): até a fatia de cada uma
  entrar, a aba abre com a faixa "ainda não ligado" e os controles desabilitados, nada é gravado no
  navegador (Integrações nunca guarda chave no `localStorage`). Janelas reusa o componente "Janelas e
  frete" já ligado (S45), sem segunda fonte. Produção liga na F11, Caixa na F14, Fidelidade na F15,
  Integrações na F16.
- `aviso` sobrevive à sincronização até a dona fechar.
- Modo API não conecta o `canalEntreJanelas`; `#/cardapio-link` exige sessão.

**Aceite.**
- [ ] Encerrar pelo modal deixa a conversa `Encerrada` no banco e ela sai de "Precisa de você" após recarregar.
- [ ] Prova `prova-f06-honestidade.mjs`: nenhuma ação do provider fica fora de "ligada" ou "avisa".
- [ ] Foto enviada pelo console chega ao WhatsApp (validação do Felipe) ou o botão avisa.
- [ ] Simular não aparece com `VITE_FONTE_DADOS=api`; as abas sem backend mostram "ainda não ligado" e não gravam nada.
- [ ] Nenhum número da massa aparece no modo API (captura de `01-principal` sem "6" em Automáticas).

**Fora.** Ligar as ações que hoje avisam (F09 a F11).

---

## F07 · Sincronização correta · #1237

| # | Achado | Prova | Onde | Correção |
|---|---|---|---|---|
| 1 | Relógio soma 30 s por tick e nunca relê o real. Aba em segundo plano ou notebook suspenso atrasa a janela de 24 h, loja aberta, atraso do KDS | código | `hooks/useRelogio.js:9` | No modo API, `setAgora(Date.now())` a cada tick e no `visibilitychange` |
| 2 | Mensagens só são relidas quando `ultimaMensagemEm` muda; mudança de status (Falhou, Entregue) não muda esse campo. A dona não vê que a mensagem falhou | código | `useSincronizacaoApi.js:30`, `Conversa.cs:157-204` | Reler a conversa selecionada a cada ciclo, ou carimbo de alteração vindo da API |
| 3 | Toda conversa `Assumida` vira "Pausado porque você assumiu", mesmo quando o agente escalou (sem `AssumidaPorUsuarioId`) ou outro atendente assumiu. O motivo da escalada não chega ao console | **vivo** | `reducer.js:235-237`; `ConversaResumoResult` sem motivo | API: expor `motivoEscalada` no resumo. Console: três textos (você assumiu / outro atendente / o automático passou para você: motivo), e o selo "Precisa de você" com o motivo como no protótipo (`passagem`) |
| 4 | `Entregue` traduzido como `lida` | código | `traducaoConversas.js:14` | `Entregue → entregue` (✓✓ cinza) |
| 5 | Cobrança online sem `meioAnterior` aparece como "Pix"; troca de forma que falha deixa o meio local errado | código | `comandaApi.js:72`, `api/comanda.js:48,51` | Cair em `cartao-link`; no `catch`, `recarregar(id)` |
| 6 | JWT de 8 h sem refresh nem aviso: no fim do turno o 401 desmonta tudo e perde rascunho e comanda | código | `sessao.js`, `cliente.js:52-54` | Aviso 10 min antes e rascunhos guardados por conversa |
| 7 | GET de pedido por conversa a cada 5 s; lista para em 50 conversas | código | `useSincronizacaoApi.js:40-52`, `conversasApi.js:7` | Status do pedido no resumo da conversa; paginação |
| 8 | Datas pelo fuso da máquina; "hoje" dos bloqueios em UTC (após 21 h vira amanhã) | código | `useCadastroEntregaApi.js:7`, `dominio/formato.js:6`, `dominio/funcionamento.js:50` | `America/Sao_Paulo` explícito |
| 9 | Debounce de 800 ms do horário não é limpo ao sair: pode gravar o horário da empresa A com o token da B | código | `aplicacao/api/expediente.js:29,56` | Cancelar no unmount |
| 10 | Canhoto usa `fetch` direto (401 não desloga) e `document.write` com `opener` | código | `kdsApi.js:17-27`, `useCozinhaApi.js:81-86` | `chamarApi` + Blob URL `noopener` |

**Aceite.** Uma prova Red por item 1, 2, 3 e 5 antes da correção; `npm run qualidade` verde; o item 3
validado na API local com conversa escalada pelo agente (sem chave da Anthropic, o agente escala).

---

## F08 · Backend (tier ALTO, paralelo à F06) · #1238

| # | Sev. | Achado | Prova | Onde | Correção |
|---|---|---|---|---|---|
| 1 | Alta | Pedido duplicado: checagem de "sem pedido em andamento" antes do commit, `Conversa` sem token de concorrência. Duplo clique ou console + ferramenta do agente = 2 pedidos, 2 links do MP | código | `GerarPedidoConversaUseCase.cs:72`, `CriarPedidoAtendimentoUseCase.cs:83` | `xmin` em `Conversa` (o 409 já é mapeado) e teste com duas chamadas paralelas |
| 2 | Alta | Aprovar e recusar pedido só com `[Authorize]`: `Visualizador` recusa e dispara estorno | código | `AprovacaoPedidoController.cs:27` | `Policy = "Operador"` e teste do 403 |
| 3 | Média | Tabela `ocorrencias` sem RLS (banco local: `relrowsecurity = f`) | **vivo** | `20260930204100_AddOcorrencia.cs` | Migration com `tenant_isolation` |
| 4 | Média | Mesmo pedido em duas viagens (índice só `(ViagemId, PedidoId)`) | código | `ViagensUseCases.cs:121`, `EntregasConfiguration.cs:60` | Índice único parcial por `PedidoId` em viagem ativa |
| 5 | Média | Pedido fora de área para daqui a 3 dias não aparece para aprovar: o KDS corta por dia de produção | código | `KdsPedidoQueries.cs:32-37` | Sem corte de data para `aguardando_aprovacao_baba` |
| 6 | Média | Aprovar e recusar devolvem `Ok(result)` sem `{data}` e erro como ProblemDetails: o console recebe `null` e mostra "Confira os campos." no 409 | código | `AprovacaoPedidoController` × `entregasApi.js` | Envelope padrão `DataOk` / `{error}` |
| 7 | Baixa | SSE só `[Authorize]`, não fecha no `exp`, sem limite por empresa | código | `OperacaoEventosController.cs:13` | Policy `Operador`, fechar no `exp` |
| 8 | Baixa | Aprovar e recusar não publicam `pedido.mudou_status` | código | use cases de aprovação | Publicar no `IOperacaoEventPublisher` |
| 9 | Baixa | `FOR UPDATE` sem `EmpresaId` | código | `PedidoStorefrontRepository.cs:42` | `AND "EmpresaId" = {1}` |
| 10 | Baixa | Reembolso com teto por ocorrência, não cumulativo por pedido | código | `ReembolsarPedidoUseCase.cs:63` | Subtrair o já reembolsado |
| 11 | Baixa | Mensagem programada e respostas prontas sem `AtenderConversas`; automações (valem para a empresa) com `Operador` | código | controllers S39/S42 | Confirmar na spec; provável `Admin` para automações |
| 12 | Baixa | N+1 na lista de viagens, inclui concluídas | código | `ViagensUseCases.cs:39-45` | Projeção única, filtro por situação |
| 13 | Nova | `ConversaResumoResult` sem motivo da escalada (pré-requisito da F07 item 3) | código | `ConversaAtendimentoResults.cs:7` | Campo aditivo `motivoEscalada` |

**Aceite.** Um teste de integração por item 1 a 6 que falha antes; gate verde; PR tier ALTO com label `aprovado`.

---

## F09 · Ficha do cliente e ocorrência na API · #1239

Endpoints já no master (a matriz do 10-console diz "Não" e **está desatualizada**):

| Spec | Rotas |
|---|---|
| S24 | `GET/POST api/clientes/{id}/tags`, `DELETE .../tags/{tag}`, `POST .../bloquear` e `/desbloquear` (policy Gerente), `PUT .../preferencias` |
| S25 | `GET/POST api/clientes/{id}/notas`, `GET api/clientes/{id}/dossie`, `GET api/atendimento/conversas/{id}/dossie` |
| S27 | `GET/POST api/ocorrencias`, `GET {id}`, `POST {id}/resolver` |
| S41 | `GET api/atendimento/atendentes`, `POST conversas/{id}/transferir` |

**Problema.** Tag, nota, bloqueio e cadastro somem em até 5 s (`cliente` volta vazio da tradução); o
Histórico mostra 0 atendimentos; a ocorrência é apurada só no navegador.

**Aceite.** Tag, nota e bloqueio persistem após recarregar; Histórico mostra pedidos e atendimentos do
dossiê; ocorrência aberta pelo console aparece em `api/ocorrencias`. Transferir para atendente (S41) entra aqui.

**Pendência de backend.** S26 (avaliação) não tem leitura autenticada para o console: abrir spec antes de ligar.

---

## F10 · Respostas prontas, automações e lembretes · #1240

| Spec | Rotas |
|---|---|
| S42 | `GET/POST api/atendimento/respostas-prontas`, `PUT {id}`, `POST {id}/arquivar`, `GET {id}/render?conversaId=`; `GET api/atendimento/automacoes`, `PUT {gatilho}` |
| S43 | `GET/POST api/atendimento/lembretes`, `POST {id}/concluir`, `POST vistos` |

**Problema.** A biblioteca não persiste e as regras não rodam no modo API (`App.jsx:70`); lembretes
se perdem ao recarregar. O contador "Automáticas 6" vem da massa.

**Aceite.** Resposta criada no console aparece em outra aba após recarregar; ligar e desligar gatilho
reflete no `GET automacoes`; lembrete concluído some nas duas abas. Contador vem da API.

---

## F11 · Lote de papel e cardápio · #1241

| Spec | Rotas |
|---|---|
| S46 | `POST api/atendimento/esteira/lote` |
| S45/S48 | `api/minha-vitrine/cardapio`, `api/minha-vitrine/configuracao`, `api/storefront/{slug}/menu` |
| S17/S22 | saldo e alerta de desacerto (rotas na spec) |

**Problema.** O lote cria mensagens "lidas" falsas; editar item, saldo e disponibilidade reverte em 60 s;
o pedido pelo link (`#/cardapio-link`) espelha só o estado local.

**Aceite.** Lote lançado vira pedidos no EasyStok; ajuste de saldo persiste; pedido pelo link aparece na conversa.

**Decisão do Felipe.** Mensagem programada (S39): o protótipo só tem o selo "programada" e a API existe
sem tela. Criar a tela nesta fatia ou deixar fora do go-live.

---

## F12 · Paridade visual e defeitos de tela · #1242

Capturas lado a lado em 1440×900 (evidência fora do repositório, pasta da sessão de revisão).

**Defeitos visuais do modo API (valem para qualquer tela).**

| # | Defeito | Prova |
|---|---|---|
| 1 | A faixa inferior "Conversas ao vivo do EasyStok" é sobreposta ao layout: corta a dica do compositor ("Chat do site não aceita foto…") e o avatar do trilho. No celular (390 px) quebra em 3 linhas e o botão flutuante encosta | **visual** |
| 2 | A barra de abas da Gestão não cabe com a aba Atendimento: "Entregas e integ…" cortada a 1440 px, sem rolagem | **visual** |
| 3 | "Mensagens automáticas" diz **Aberta agora** com os 7 dias "Fechado o dia todo" (empresa sem expediente cadastrado). Uma das duas está errada | **visual** |
| 4 | Três conversas do chat do site aparecem todas como "Visitante do site", indistinguíveis. O protótipo sempre mostra nome | **visual** |
| 5 | Ficha de lead: o placeholder "(11) 98765-4321" (`BlocoCliente.jsx:243`, igual ao protótipo) tem contraste de dado preenchido. Baixa prioridade | **visual** |

**Cozinha (`TelaCozinhaApi`) × protótipo.**

| Protótipo | Console API | Fazer |
|---|---|---|
| 5 colunas: Pago, Em preparo, Embalado, Em entrega, Entregue (recolhível) | 4: Aguardando, Em preparo, Pronto, Saiu para entrega | Mapear os status do KDS para os rótulos e a ordem do protótipo; Entregue recolhível |
| Filtro "Todas as linhas / Para servir / Preparar em casa" | Não tem | Filtro por `linha` (já vem no KDS) |
| Cartão com itens agrupados por linha, porção (800 g), observação em itálico, faixa lateral colorida por estado, ícone por passo | Cartão simples | Mesmo componente de cartão do protótipo |
| Despacho: entregador, veículo, placa no cartão Em entrega | Não tem | Ler da viagem (S44) |
| Arrastar entre colunas com recusa e motivo | Não tem | **Decisão:** manter só o toque (plano da F05) ou arrastar |

**Entregas (`TelaEntregasApi`) × protótipo.**

| Protótipo | Console API | Fazer |
|---|---|---|
| Chips de estado com contagem (A preparar, Prontas, Com entregador, Entregues, Canceladas) | Abas Entregas de hoje / Entregadores / Janelas e frete | Chips no topo da aba "Entregas de hoje" |
| Ordenar (Mais próxima, Mais longe, Por janela, Por bairro) e filtro por bairro | Não tem | Ordenação e filtro no cliente sobre o que já vem |
| Resumo do dia (entregues, total, copiar) e próxima conferência | Não tem | Somar dos pedidos entregues do dia |
| Cartão com anel de minutos, pedido, comanda, itens, bairro, janela, "Conferi" | Linha simples | Mesmo cartão do protótipo |
| Gerar rota com desfazer | Subir/Descer e link Maps | Ordem da rota pelo Maps (F16/F17) |
| Cotação e chamada Lalamove/99 | Chamado em texto livre | Lalamove por API na F17; 99 fora |
| Arrastar para a viagem | Botões "Pôr na viagem N" | Fora do go-live (decidido em 30/09) |

**Aceite.** Captura lado a lado com a mesma massa de pedidos (seed pela API local) sem diferença de
estrutura nas duas telas; defeitos 1 a 5 corrigidos e conferidos em 1440, 1024 e 390 px.

---

## F13 · Deploy e validações pendentes · #1243

| # | Item | Onde |
|---|---|---|
| 1 | `Dockerfile.web` só gera o modo demonstração (sem `ARG VITE_FONTE_DADOS`) | `EasyStock.Console/Dockerfile.web` |
| 2 | nginx manda `/api/` para `api-falsa` com `proxy_read_timeout 30s` e buffer ligado: o SSE cai ou chega em blocos | `EasyStock.Console/nginx.conf:8-13` |
| 3 | 10-console.md cita `VITE_API_URL`; o código usa `VITE_FONTE_DADOS` e `VITE_API_BASE` | `10-console.md:47` |
| 4 | Matriz do 10-console.md com S24, S25, S42, S43 e S46 como "Não" | `10-console.md` |
| 5 | Policy `Admin` em consentimentos, configuração, expediente e `minha-vitrine/*`: operador comum vê 403 no botão do topo e nos avisos da Ficha, sem texto próprio | controllers |

**Validações que só o Felipe fecha** (aceites abertos das fatias anteriores):
- [ ] F01: console publicado em `app.easystok.online` atrás do Caddy.
- [ ] F03: pedido pela conversa, link chega ao cliente, vira pago no sandbox do MP.
- [ ] F04: viagem com entregador sai e pedidos vão para "saiu para entrega".
- [ ] F05: pedido pago aparece na cozinha e muda de passo pela tela.

## F14 · Caixa na API real · #1244

**Fato medido.** O backend do caixa existe e a poda não o tocou (`07-poda.md:69`). O console não chama
`api/caixa` em lugar nenhum. A dona usa o caixa hoje e ele precisa continuar (US-034).

| Peça | Backend | Onde |
|---|---|---|
| Dia, abrir, movimentos, fechamentos | `GET api/caixa/dia`, `POST abrir`, `GET/POST movimentos`, `GET fechamentos` (Operador) | `CaixaController.cs:29,44,75,93,126` |
| Fechar, estornar movimento | `POST fechar`, `POST movimentos/{id}/estornar` (**Gerente**) | `CaixaController.cs:61,111` |
| Pagamento do MP soma no caixa | `PedidoPagamento` | `ConfirmarPagamentoPedidoUseCase.cs:207-219` |
| Recebido na entrega, desfazer | `POST api/pedidos/{id}/pagamentos`, desfazer na cobrança | `PedidosController.cs:178`, `PedidosCobrancaController.cs:50` |
| Venda de balcão | `FinalizarVendaBalcaoUseCase` | `PedidosController.cs:83` |
| Caixa esquecido aberto | job | `CaixaEsquecidoJob.cs` |

**Lacunas (backend).**

| # | Lacuna | Protótipo | Backend hoje | Correção |
|---|---|---|---|---|
| 1 | Fechamento sem valor contado e diferença | `caixa.js:294-295` | só `Observacoes` (`FecharCaixaUseCase.cs:5-11`) | `ValorContado` e `Diferenca` em `FechamentoCaixa` (migration aditiva) |
| 2 | Fórmula do saldo soma `Venda` do PDV | vendas não entram, só régua | `CaixaSaldoCalculator.cs:106` | Saldo do console = inicial + pagamentos de pedido − estornos + entradas − saídas; vendas do PDV à parte |
| 3 | Reembolso da ocorrência (S27) não desconta do caixa | `caixa.js:162-166`, RN-36 | `ReembolsarPedidoUseCase.cs:58,70` | Reembolso gera saída de caixa com `Origem` = ocorrência |
| 4 | Venda de balcão pula a esteira | entra na esteira e baixa estoque (RN-26) | vai direto a entregue (`FinalizarVendaBalcaoUseCase.cs:63`) | Balcão cria pedido pago que segue a esteira |
| 5 | Resumo por método | `caixa.js:221-265` | não vem em `CaixaDiaResult` | Campo aditivo `porMetodo` |

**Console.** `infra/api/caixaApi.js` e ações em `aplicacao/api/caixa.js`; `AbaCaixa.jsx` lê o dia da
API; "Pagamentos de pedidos hoje" vem do dia; o som de pagamento confirmado (RN-25) segue no SSE.

**Decisão já tomada nesta spec.** A dona entra como **Gerente** para fechar e estornar; operador comum
vê os dois botões desabilitados com o motivo.

**Aceite.**
- [ ] Abrir, lançar, estornar e fechar pelo console persistem e aparecem no `GET api/caixa/dia` após recarregar.
- [ ] Fechamento grava contado e diferença; teste de use case com diferença positiva, negativa e zero.
- [ ] Pedido pago pelo MP e "recebido na entrega" aparecem em "Pagamentos de pedidos hoje".
- [ ] Reembolso de ocorrência vira saída no caixa do dia (teste de integração).
- [ ] Venda de balcão aparece na cozinha (teste de integração do item 4).

**Fora.** Relatório de caixa por período; PWA de caixa (sai quando esta fatia estiver em paridade, P05).

---

## F15 · Fidelidade e cupons · #1245

**Fonte.** Pedido do Felipe em 2026-09-30, a partir de pedido do cliente: gerar cupons de desconto,
acúmulo de pontos com valor configurável e definir o que gera ponto. **As entrevistas com a Thati não
citam pontos nem cupom** (quando ela diz "cupom" é o canhoto, `01-TRANSCRICAO.md:98`); ela cita
promoção de preço em campanha. A regra padrão abaixo é configurável justamente por isso.

**Fato medido.** Não existe nada no backend: o `Cupom` atual é do SaaS, global, sem `EmpresaId`
(`Cupom.cs:3-14`); `Pedido` não tem desconto (`Pedido.cs:244-252`); checkout e cobrança usam itens +
frete (`CheckoutCoreService.cs:203-205`, `GerarCobrancaPedidoUseCase.cs:85`). O protótipo tem tudo
(`dominio/fidelidade.js`, `casos/fidelidade.js`, `AbaFidelidade.jsx`, `ModalFidelidade.jsx`), e o
`comandaApi.js` não envia cupom.

**Modelo (todas as tabelas com `EmpresaId` e bloco RLS).**

| Entidade | Campos | Regra do protótipo que vale |
|---|---|---|
| `CupomLoja` | código (único por empresa, sem diferenciar maiúscula), tipo (percentual ou valor), valor, validade, limite de usos, usos, pedido mínimo, ativo | desconto nunca passa do total; validação devolve o motivo (`fidelidade.js:34-94`) |
| `RegraFidelidade` (1 por empresa) | modo (`valor`: N pontos a cada R$ X; `pedidos`: N pontos a cada K pedidos), gatilho (pago ou entregue), ativa | padrão: 1 ponto a cada R$ 10, a partir de pago (`fidelidade.js:98-118`) |
| `LancamentoPontos` (livro) | cliente, pedido, pontos (+/−), origem (ganho, resgate, estorno, ajuste), em | livro, não cálculo: estorno do pedido lança o negativo |
| `Recompensa` | nome, custo em pontos, tipo (produto, frete grátis, número da sorte), ativa | `fidelidade.js:172-206` |

**Onde o desconto entra.** `Pedido.Desconto` (valor objeto, nunca negativo, nunca maior que itens +
frete) e `Pedido.CupomCodigo`; `CheckoutCoreService` aplica o cupom e reserva o uso na mesma transação;
`GerarCobrancaPedidoUseCase` cobra o total descontado e manda ao Mercado Pago um item de desconto
para os itens fecharem com o total; cupom só antes da cobrança existir (como no protótipo).

**Pontos.** Ganho lançado pelo `PedidoPagoEvent` (ou entregue, conforme a regra), idempotente por
pedido; a linha "você ganhou N pontos" vai na mensagem de pagamento confirmado, sem mensagem avulsa
(RN-40); resgate na Ficha debita o livro.

**API.** `api/fidelidade/cupons` (CRUD, Admin), `api/fidelidade/regra` (GET/PUT, Admin),
`api/fidelidade/recompensas` (CRUD, Admin), `api/clientes/{id}/pontos` (saldo e extrato, Operador),
`POST api/clientes/{id}/resgates` (Operador); `cupomCodigo` no `POST conversas/{id}/pedido` e no checkout do site.

**Aceite.**
- [ ] Teste de domínio: percentual, valor fixo, teto no total, pedido mínimo, validade, limite de usos, uso devolvido ao tirar o cupom.
- [ ] Teste de integração: pedido com cupom cobra o total descontado no MP e o caixa soma o valor pago.
- [ ] Pedido pago lança pontos uma vez só (evento repetido não duplica); estorno lança o negativo.
- [ ] Mudar a regra na Gestão muda o próximo ganho, não o passado.
- [ ] Isolamento: cupom de uma empresa não vale na outra.
- [ ] Console: aba Fidelidade e "Resgatar" na Ficha ligados; `comandaApi.js` envia o cupom.

**Fora.** Sorteio (número da sorte) além de registrar o número; cashback; campanha com preço promocional (S28–S30).

**Confirmar com a Thati antes do merge.** Valor da regra (quantos pontos por real), gatilho (pago ou
entregue) e as recompensas iniciais. O código não depende da resposta: é configuração.

---

## F16 · Integrações: chaves por loja e botão de teste (go-live) · #1246

**Problema.** A esteira e o atendimento não podem parar por chave vencida ou errada, e hoje ninguém vê
quando isso acontece. A aba do console só cobre logística, guarda a chave na memória e não testa nada.
Hoje as quatro integrações leem chave global de config.

**Fatos medidos.**

| Integração | Credencial hoje | Escopo certo | Teste barato (sem efeito colateral) |
|---|---|---|---|
| Mercado Pago | `MercadoPago:AccessToken` global (`MercadoPagoOptions.cs:5-12`) | **Loja**: access token (D2 do #1205: OAuth por loja). Webhook secret é da app, **global** | `GET /users/me` com o token (confirmar o host `api.mercadopago.com` na doc) |
| WhatsApp (Meta) | token, AppSecret e VerifyToken globais (`WhatsAppCloudOptions.cs:10-12`); número por empresa (`Empresa.cs:35`) | Token, AppSecret e VerifyToken **globais** (app FMA); **loja** só o número | `GET /{phone-number-id}?fields=display_phone_number,verified_name,quality_rating` |
| Google Maps | `GOOGLE_MAPS_API_KEY` global (`GeocodingServiceCollectionExtensions.cs:39-44`) | Global, com chave própria da loja opcional | Geocode do endereço da cozinha: `OK` passa; `REQUEST_DENIED` e `OVER_QUERY_LIMIT` já são distinguidos (`GoogleGeocodingClient.cs:63-65`) |
| Lalamove | não existe | **Loja**: key, secret, ambiente | `GET /v3/cities` com `Market: BR` |

**O núcleo já existe e está ocioso.** Tabela `credencial_integracao` (`AddIntegrationCore.cs:13-43`:
`empresa_id`, `categoria`, `provider_key`, `ambiente`, `payload_cifrado`, `kek_id`, `iv`, `tag`,
`valido_ate`, `ativo`, `ultimo_uso_em`) e `IntegrationCredentialResolver` (AES-256-GCM com KEK,
`SalvarAsync`, `RotacionarKekAsync`), com zero consumidores. Dois defeitos antes de usar:
- **Sem RLS** (medido no banco local em 2026-09-30: `relrowsecurity = f`; a migration de RLS só casa a coluna `EmpresaId`, e aqui é `empresa_id`).
- **Sem KEK configurada** (`Crypto:Keks` não existe em nenhum ambiente): `SalvarAsync` lança exceção. O cache de 5 min não é invalidado na rotação (`IntegrationCredentialResolver.cs:247-253`).

**Abordagem.**
- Migration: RLS em `credencial_integracao`; colunas `ultimo_teste_em`, `ultimo_teste_ok`, `ultimo_teste_mensagem`. `CategoriaIntegracao` ganha Mensageria e Mapas.
- KEK em `EZ_CRYPTO_*` no `.env` de cada ambiente; a API não sobe sem KEK (fail-fast); o cache é invalidado ao salvar e ao rotacionar.
- Leitura: credencial da loja primeiro; na falta, a global de config (transição sem quebrar o que está no ar).
- Segredo nunca volta da API: só `temCredencial`, máscara (últimos 4), ambiente, `validoAte`, último teste.
- API (policy Admin): `GET api/integracoes`, `PUT api/integracoes/{provider}` (salva cifrado), `POST api/integracoes/{provider}/testar` (timeout 5 s, 6 por minuto por loja e provider, grava o resultado), `POST api/integracoes/{provider}/desativar`.
- Vigia: job a cada 15 min testa as integrações ativas; falha ou `validoAte` a menos de 7 dias vira lembrete da dona (S43) e faixa vermelha "Integração parada: Mercado Pago" no topo do console, com atalho para a aba.
- Corrigir `GET api/integracoes/whatsapp/status`: `WebhookVerificadoEm` e `UltimaMensagemRecebidaEm` voltam sempre nulos (`ObterStatusIntegracaoWhatsAppUseCase.cs:8-35`).
- Console: aba "Integrações" com um cartão por provider (Mercado Pago, WhatsApp, Google Maps, Lalamove, Entregador próprio); campo de segredo só de escrita; botão **Testar** com resultado e hora; chip Ligado/Desligado; "Usar como padrão do despacho" (entregas).
- O que é global (token Meta, AppSecret, webhook secret do MP) aparece só como leitura ("gerido pela FMA"), para uma loja não derrubar o canal de todas.

**Aceite.**
- [ ] Salvar chave pelo console grava cifrado (`payload_cifrado` sem o texto) e o `GET` nunca devolve o segredo.
- [ ] Testar com chave boa dá ok; com chave errada dá o erro do provedor em português; nada é cobrado nem pedido.
- [ ] RLS: consulta de outra empresa em `credencial_integracao` volta vazia (teste de integração com dois tenants).
- [ ] Vigia: chave inválida gera a faixa vermelha e o lembrete em até 15 min (teste com relógio falso).
- [ ] Sem KEK a API não sobe e diz o motivo.

**Divergência a reconciliar no mesmo PR da fatia.** `docs/plan/integracoes/README.md` (#1205) diz
que a credencial por loja não existe e que o Mercado Pago não tem processor do webhook. As duas coisas
já existem (`credencial_integracao`; `MercadoPagoWebhookProcessor`, S32).

**Fora.** OAuth do Mercado Pago com `marketplace_fee` (F2 do #1205); Pix pela Efí (D3).

---

## F17 · Lalamove por API e rota da viagem pelo Maps · #1247

**Problema.** Chamar entregador hoje é texto livre. O protótipo já desenha cotação, chamada,
cancelamento e acompanhamento (`infra/provedoresDeEntrega.js:15-40`, `PainelViagem.jsx:77-133`), com
dados simulados; a cotação usa só a primeira parada (`PainelViagem.jsx:94-95`). Premissa refutada: a
viagem **não** usa API do Maps; `RotaMaps.Montar` (`RotaMaps.cs:11`) só monta o link.

**Ficha Lalamove v3** (https://developers.lalamove.com/):
- Autenticação: `Authorization: hmac {key}:{timestamp_ms}:{assinatura}`, com assinatura = HMAC-SHA256
  hexadecimal de `"{ts}\r\n{METODO}\r\n{PATH}\r\n\r\n{BODY}"`. Também `Market: BR` e `Request-ID`.
- Sandbox `rest.sandbox.lalamove.com/v3` (`pk_test`); produção `rest.lalamove.com/v3` (`pk_prod`, exige
  saldo na carteira; sem saldo, `402 ERR_INSUFFICIENT_CREDIT`).
- `POST /v3/quotations`: paradas com coordenadas, de 2 a 16, `isRouteOptimized`; **cotação vale 5 min**.
  `POST /v3/orders` com `quotationId`; `GET /v3/orders/{id}`; `DELETE` só em `ASSIGNING_DRIVER` ou até
  5 min de `ON_GOING`; `GET .../drivers/{driverId}`; `PATCH /v3/webhook`.
- Status: `ASSIGNING_DRIVER → ON_GOING → PICKED_UP → COMPLETED`, mais `REJECTED`, `EXPIRED` (2 h sem aceite), `CANCELED`.
- Limite por minuto: 30 cotações no sandbox e 100 em produção.
- **Assinatura do webhook não documentada na página** (PDF v1.3 não lido): nunca confiar no corpo.

**Abordagem.**
- Porta `ILogisticaGateway` (Cotar, Criar, Consultar, Cancelar), como no #1205 (F6), e `LalamoveGateway` (F7) com a credencial da F16.
- Viagem com N pedidos = **uma** cotação com N+1 paradas (coleta na cozinha, `storefront.CozinhaLat/Lng`) e `isRouteOptimized`. As coordenadas vêm do `IGeocodingClient`, gravadas no endereço de entrega do pedido na captura (S14), para não geocodificar a cada cotação.
- Viagem ganha: provedor, `cotacaoId`, preço, `pedidoExternoId`, status externo, motorista (nome, telefone, placa), link de acompanhamento.
- Chamar exige cotação válida; vencida recota e mostra o preço novo antes de confirmar.
- Webhook `POST api/webhooks/lalamove/{token}` (token secreto por loja na URL, rate limit dedicado como o da Meta): deduplica e **sempre reconsulta** `GET /v3/orders/{id}`; com viagem ativa, consulta a cada 60 s como reserva.

| Lalamove | EasyStok |
|---|---|
| `ASSIGNING_DRIVER` | chamado aberto |
| `ON_GOING` + `DRIVER_ASSIGNED` | entregador a caminho da cozinha (motorista na viagem) |
| `PICKED_UP` | pedidos da viagem em "saiu para entrega" (aviso S13 ao cliente) |
| `COMPLETED` | pedidos entregues |
| `REJECTED`, `EXPIRED`, `CANCELED` | viagem volta a "sem entregador" + lembrete da dona |

- Rota do entregador próprio: "Ordenar pela rota" na viagem usa a Routes API do Google
  (`computeRoutes` com `optimizeWaypointOrder`), com desfazer, como o "Gerar rota" do protótipo; o link
  do Maps continua para o entregador abrir no celular.
- Console: `DespachoPorProvedor` ligado (cotar com todas as paradas, chamar, cancelar, acompanhar); cartão Lalamove na F16.

**Aceite.**
- [ ] Teste unitário da assinatura HMAC contra o exemplo da documentação.
- [ ] No sandbox: viagem com 2 pedidos cotada com 3 paradas, chamada, e o webhook leva os pedidos a "saiu para entrega" e "entregue".
- [ ] Webhook com corpo falso não muda nada sem a reconsulta confirmar.
- [ ] Cancelar fora da janela mostra o motivo da Lalamove em português.
- [ ] Sem saldo (402) aparece a mensagem e a integração fica em alerta na F16.
- [ ] "Ordenar pela rota" reordena as paradas e o desfazer volta à ordem anterior.

**Depende do Felipe.** Conta Lalamove Business no Brasil; chaves de sandbox para o desenvolvimento;
saldo na carteira para produção; a assinatura do webhook (PDF v1.3) confirmada com o suporte; a
Routes API habilitada na chave do Google.

**Fora.** 99 (depende de contato comercial, F9 do #1205); taxa de prioridade; troca de motorista.

---

## F18 · Cadastro do cliente pela conversa (go-live) · #1276

Achado da verificação lado a lado de 2026-10-01 (API local do master `10b3528d`, banco conferido).

**Problema.** `GerarPedidoConversaUseCase` exige cliente vinculado e endereço padrão, mas só o webhook do
WhatsApp vincula cliente. Chat do site, Instagram, Messenger, e-mail e SMS recebiam 409 "Cadastre o
cliente desta conversa" sem caminho para cadastrar: "Salvar cadastro", "Cadastrar endereço" e a edição
do cliente avisavam "ainda não ligado", e a sincronização de 5 s reescrevia o cliente vazio.

**Abordagem.** Uma rota nova que compõe o que já existe:
`POST api/atendimento/conversas/{id}/cliente` (`CadastrarClienteDaConversaUseCase`) =
`IdentificarClientePorTelefoneUseCase` (acha ou cria pelo telefone, sem trocar o nome de quem já existe)
+ `Conversa.VincularCliente` + `ValidarEnderecoUseCase` + `ConfirmarEnderecoClienteUseCase` (endereço de
entrega padrão; fora da área grava e devolve `dentroDaArea=false`). O console liga as três ações, relê o
dossiê (`GET .../dossie`, S25) depois de salvar e ao abrir conversa com cliente, e mantém o cliente lido
da API na sincronização. Nome do lead sem cadastro é rascunho; no chat do site o cadastro exige o nome
escrito pela dona (o do contato é "Visitante do site").

**Pré-condições do pedido que continuam valendo:** CEP dentro de uma zona `cep_range` (bairro só conta
com ViaCEP ligado) e loja aberta. As duas aparecem como aviso honesto no console.

**Fora.** Avisos do cliente (segue "ainda não ligado"); busca de cliente por e-mail, IGSID ou PSID.

---

## Rollback

Cada fatia é um PR isolado: `git revert` do squash. F08 item 3 e 4 trazem migration aditiva (política
RLS e índice); o revert da migration derruba só a política e o índice.

## Decisões do Felipe (2026-09-30)

| # | Pergunta | Decisão |
|---|---|---|
| 1 | Abas da Gestão sem backend | **Têm backend.** Caixa (F14, reusa o caixa do EasyStok), Fidelidade (F15), Integrações (F16) |
| 2 | Arrastar na Cozinha e nas Entregas | Fora do go-live; toque basta |
| 3 | Provedores de entrega externos | **Lalamove por API** (F17); Google Maps é integração (F16). 99 fora |
| 4 | Tela de mensagem programada (S39) | Fora do go-live |
