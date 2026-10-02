# M3 Atendimento · balcão, pedidos, clientes, canais, status e histórico

Issue: #1316 · Decisão: [ADR-0056](../../adr/0056-erp-da-casa-da-baba-front-unico.md) · Data: 2026-10-01
Base medida: master `b713263a`. Plano irmão (dono de S01–S53 e F01–F18):
[10-console.md](../atendimento-whatsapp/10-console.md), [11-console-fechamento.md](../atendimento-whatsapp/11-console-fechamento.md).
Este documento **não reescreve** S nem F: mapeia cada sub-tela para a fatia que a cobre e especifica só o que falta.

Fundação usada aqui: [01-fundacao.md](01-fundacao.md) M0.1 tema, M0.2 shell (rotas `#/m/<modulo>/<tela>`,
porta de entrada por perfil), M0.3 perfis × módulos (matriz com porta "M3 balcão" para o perfil
Atendimento). O site como canal é a SI.6 de [10-site-e-impressos.md](10-site-e-impressos.md).

## 1. Veredito

**O M3 JÁ É O CONSOLE DE HOJE. FALTA: LIGAR O QUE AINDA AVISA, DAR CASA AOS PEDIDOS E CLIENTES FORA DA CONVERSA, E ENCAIXAR NO SHELL SEM PERDER A TELA ÚNICA.**

| Medida | Valor | Fonte |
|---|---|---|
| Fatias do plano irmão que tocam o M3 | 14 F (F01–F11, F13, F14, F18) | 11-console-fechamento.md |
| Mergeadas | F01, F02, F03, F06, F07, F08, F18 | `gh pr list` (PRs #1200, #1204, #1216, #1258, #1264, #1266, #1284) |
| PR aberta | F13 (#1310) | `gh pr list` |
| Commits em branch, sem PR | F09 (worktree `console-ficha-1239`, `2ce6d229`, 19 arquivos, +811) | worktree medido em 2026-10-01 |
| Sem PR | F10 (#1240), F11 (#1241) | `gh issue view` |
| Ações do console que ainda só avisam no modo API | 88 ações em `NAO_LIGADAS` (todos os módulos) + 9 da comanda com pedido criado | `EasyStock.Console/src/aplicacao/api/naoLigadas.js:27-68`, `aplicacao/api/comanda.js:23-33` |
| Fatias novas deste documento | **9** (M3.1 a M3.9) | §4 |

## 2. Como o M3 vive dentro do shell (D5, ADR-0056 item 4)

```
 login ──► perfil com M3 como porta? ──sim──► #/m/atendimento  (sem passar pela sala)
                   │não
                   ▼
            sala de módulos ──► card M3 ──► #/m/atendimento

 ┌──────────── #/m/atendimento (tela única, igual a hoje) ──────────────┐
 │ trilho │ lista de conversas │ conversa + compositor │ ficha + comanda │
 │ (menu  │                    │                       │                 │
 │  do M3)│                    │                       │                 │
 └────────┴────────────────────┴───────────────────────┴─────────────────┘
 trilho = Loja aberta · Balcão · Pedidos · Clientes · Entregas↗ · Cozinha↗ · Automáticas · Sala
```

| Regra | Por quê | Fato que apoia |
|---|---|---|
| O menu lateral do M3, "recolhido por padrão" na M0.2, **é o trilho que já existe**, não um menu novo | O trilho já é a navegação do console e não come largura das 3 colunas | `EasyStock.Console/src/app/Moldura.jsx:303-345` (Balcão, Entregas, Cozinha, Automáticas, Gestão); `01-fundacao.md` M0.2 |
| `#/m/atendimento` é a rota raiz do M3 e a porta do perfil Atendimento | D5: "uma tela só"; a sala é mapa de gestão, não passo do pedido | ADR-0056 item 4; `03-ANALISE…md` D5; matriz da M0.3 |
| Pedidos e Clientes entram como itens do trilho, não como abas da conversa | São consultas fora da conversa; dentro da conversa a ficha continua mandando | §3.3 e §3.4 (M3.2, M3.5) |
| Cozinha e Entregas continuam abrindo em janela própria (↗) | Tablet de parede e gaveta: telas de dispositivo do M4 e do M8 | `Moldura.jsx:209-218` (`abrirCozinha` com `window.open`) |
| "Gestão" sai do trilho: cada aba vai para o seu módulo | As abas são de outros módulos (Produção, Caixa, Janelas, Fidelidade, Integrações) | `features/gestao/ModalGestao.jsx:20-26` |
| "Sala" só aparece se o perfil enxerga mais de um módulo | Perfil só de atendimento não ganha clique a mais | ADR-0056 item 5 |
| Modo massa continua idêntico ao protótipo | Aceite fixo do plano irmão; 15 de 16 telas iguais pixel a pixel | `11-console-fechamento.md:10` |

Destino das abas da Gestão (proposta; ver D3-01):

| Aba hoje (`ModalGestao.jsx:20-31`) | Vai para | Fatia dona |
|---|---|---|
| Atendimento (expediente, configuração do agente) | botão "Loja aberta" fica no trilho do M3; configuração vai para M7 | F02 (ligada); mudança de lugar em M0.2 |
| Produção e cardápio | M1 / M2 | F11 |
| Caixa | M5 | F14 |
| Janelas de entrega | M8 (já duplica "Janelas e frete" da gaveta Entregas) | F04 / F06 |
| Fidelidade e cupons | M6 | F15 |
| Entregas e integrações | M7 (credenciais) e M8 (despacho) | F16 / F17 |

## 3. Mapa: sub-tela → fatia que cobre → estado

Legenda: ✅ mergeada · 🟡 PR aberta · 🔶 commits em branch sem PR · ⬜ sem PR · ❌ sem fatia (vira M3.x).

### 3.1 Balcão (conversas)

| Sub-tela / ação | Backend (S) | Console (F) | Estado | Lacuna |
|---|---|---|---|---|
| Lista, filtros (aba, canal, busca) | S04, S34 | F01 #1200 | ✅ | nenhuma |
| Thread, assumir, devolver ao automático, encerrar | S07, S09 | F01, F06 #1258 | ✅ | nenhuma |
| Sincronização, relógio, motivo da escalada | S07 | F07 #1264, F08 #1266 | ✅ (issue #1237 aberta: item 3 valida na F13) | nenhuma |
| Foto pelo WhatsApp | S02 | F06 | ✅ | áudio e figurinha avisam |
| Enviar **modelo aprovado** fora da janela de 24 h | porta existe (`ICanalMensageria.cs:22-33`), sem rota | avisa (`naoLigadas.js:75-78`) | ❌ | **M3.6** |
| Reabrir conversa encerrada | não existe (`Conversa.cs:217-222` só encerra) | avisa (`naoLigadas.js:29`) | ❌ | **M3.6** |
| Sugestão do agente | S06 | avisa (`naoLigadas.js:28`) | ⬜ | fora deste plano (agente) |
| Assistente da dona | S47 | F02 #1204 | ✅ | nenhuma |
| Respostas prontas, automações | S42 | F10 #1240 | ⬜ | nenhuma |
| Lembretes | S43 | F10 #1240 | ⬜ | nenhuma |
| Mensagem programada | S39 | fora do go-live (decisão 30/09, `11-console-fechamento.md:470`) | ⬜ | nenhuma |
| Atendentes, transferir | S41 | F09 #1239 | 🔶 | filtro "minhas" e nome: **M3.7** |

### 3.2 Canais

| Canal | Fatia | Estado | Observação |
|---|---|---|---|
| WhatsApp | S01–S03, S09 | ✅ | nenhuma |
| Instagram, Messenger | S35 (#1104) | ✅ | nenhuma |
| Chat do site (backend) | S36 (#1099) | ✅ | o site não consome; nome do lead: F12 defeito 4 |
| **Site casadababa.com como canal**: chat no site e pedido do checkout abrindo ou reaproveitando a conversa `ChatSite` do cliente, com o pedido na Ficha | **SI.6** ([10-site-e-impressos.md](10-site-e-impressos.md)) | ⬜ | **Decidido pelo Felipe em 01/10.** Não duplicar aqui; cardápio e compra do site seguem o fluxo atual, só a cara muda (SI.4) |
| E-mail, SMS (só saída) | S37 (#1081) | ✅ | nenhuma |
| **iFood** | nenhuma | ❌ | só `CategoriaIntegracao.Marketplace` (`EasyStock.Domain/Integration/CategoriaIntegracao.cs:17`) e `EmpresaEntregador.Ifood` (`Enums/Atendimento/EmpresaEntregador.cs:12`); `CanalConversa` vai de 1 a 6 sem iFood (`Enums/Atendimento/CanalConversa.cs:9-14`). **M3.9**, condicionada a D3-02 |

Fato da entrevista: a Thati fala do iFood no futuro ("quando você estiver operando… vai adicionar um
canal", `Downloads/01-TRANSCRICAO.md:153`). **Inferência:** a casa não opera iFood hoje.

### 3.3 Pedidos

| Sub-tela / ação | Backend | Console | Estado | Lacuna |
|---|---|---|---|---|
| Comanda e pedido da conversa, cobrança | S10, S11, S16 | F03 #1216 | ✅ | nenhuma |
| Cadastro do cliente pela conversa | F18 | F18 #1284 | ✅ | nenhuma |
| Avançar esteira, voltar etapa, pago à mão, comprovante, desfazer pagamento, estorno, cancelar | `PedidosController.cs:97,113,178,194`; `KdsController.cs:41` | avisam "Use o EasyStok" (`aplicacao/api/comanda.js:19-33`) | ❌ | **M3.3** |
| Recebido na entrega, refazer cobrança | `PedidosController.cs:178`; S11 | avisam (`naoLigadas.js:39-40`) | ❌ | **M3.3** (o lado caixa é da F14) |
| Aprovar ou recusar fora de área pela ficha | S12 (`api/storefront/pedidos/{id}/aprovar`) | ligado só na tela Entregas (F04); na ficha avisa (`naoLigadas.js:51`) | ❌ | **M3.3** |
| Trocar janela do pedido criado | `AlterarAgendamentoPedidoUseCase.cs:41-42` troca só `AgendadoParaEm`, **não move a `VagaOcupada`** | avisa (`naoLigadas.js:30,44`) | ❌ | **M3.4** |
| Endereço do pedido diferente do cadastro | `Pedido` não tem endereço próprio (KDS lê o do cliente, `KdsPedidoQueries.cs:58-67`) | avisa (`naoLigadas.js:37`) | ❌ | **M8.3** |
| Pedido pelo link do cardápio | S48, #1234 | F11 #1241 | ⬜ | nenhuma |
| Aviso de status ao cliente | S13 | automático | ✅ | nenhuma |
| **Lista de pedidos fora da conversa** (balcão, outros dias, todos) | `GET api/pedidos` com status, cliente, período e busca (`PedidosController.cs:36-55`) | não existe; o legado é `EasyStock.Web/Views/Pedidos/Index.cshtml` | ❌ | **M3.2** |

Fato: o checkout do site não cria nem vincula conversa (`CheckoutCoreService.cs` sem referência a
`Conversa`); hoje o pedido do site só chega ao balcão quando nasce do link da conversa (#1234). A decisão
de 01/10 resolve isso na SI.6 (pedido do checkout entra no balcão pela conversa `ChatSite`). A lista do
M3.2 continua necessária para o que não tem conversa: venda de balcão, pedido de outro dia, pedido antigo.

### 3.4 Clientes e histórico

| Sub-tela | Backend | Console | Estado | Lacuna |
|---|---|---|---|---|
| Ficha: tags, notas, bloqueio, preferências | S24 | F09 | 🔶 | nenhuma |
| Dossiê (pedidos e atendimentos) | S25 | F09 | 🔶 | nenhuma |
| Ocorrência e reembolso | S27 | F09 | 🔶 | reembolso no caixa: F14 |
| Avaliação | S26 (#1190) | sem leitura autenticada (`11-console-fechamento.md:145`) | ⬜ | **M3.5** (leitura) |
| Consentimento por canal | S38 | F02 | ✅ | nenhuma |
| **Busca e lista de clientes fora da conversa** | `GET api/clientes` e `GET api/clientes/buscar` (`ClientesController.cs:42-51,61-66`) | não existe | ❌ | **M3.5** |
| Linha do tempo do pedido | `GET api/pedidos/{id}` traz itens, eventos e pagamentos (`PedidosController.cs:58`) | não existe | ❌ | **M3.2** |

### 3.5 Lembrete de pedido agendado

| Peça | Fato | Lacuna |
|---|---|---|
| Job | `AgendamentoNotificacaoService` lê `Order` (`EasyStock.Worker/BackgroundServices/AgendamentoNotificacaoService.cs:74-89`), que é a tabela `mobile_orders` (`EasyStock.Domain/Entities/Mobile/Order.cs:9`) | pedido do site, do WhatsApp e da conversa (`Pedido`) fica fora |
| Destino | `usuarioDestinoId: null`, "empresa toda" (`AgendamentoNotificacaoService.cs:144`) | não vira lembrete da dona (S43) |
| Onde caberia | `Lembrete` já tem `PedidoId`, `Tipo`, `Referencia`, `VenceEm` (`Entities/Atendimento/Lembrete.cs:21-28`) | **M3.8** |

## 4. Fatias novas

Todas seguem as convenções do [README §4](README.md): issue, branch, worktree, `npm run qualidade` no
console, `gate.ps1` no backend, prova `ferramentas/prova-*.mjs` que falha antes (Red), e o aceite fixo
"sem `VITE_FONTE_DADOS`, o console abre como hoje".

### M3.1 · O balcão dentro do shell

**Problema.** O shell (M0.2) cria login, sala e menu por módulo. Se o balcão virar "um módulo com menu
lateral" genérico, a dona ganha um clique e perde largura nas 3 colunas: contraria D5.
**Abordagem.**
- Rotas do M3 na convenção da M0.2 (hash estendido, `dominio/rota.js`): `#/m/atendimento` (balcão, raiz), `#/m/atendimento/pedidos`, `#/m/atendimento/clientes`.
- O `Trilho` (`Moldura.jsx:303-345`) é o menu recolhido do módulo: entra Pedidos, Clientes e "módulos" (sala); sai Gestão (abas para os módulos, §2).
- Porta de entrada por perfil vem de M0.3 (`portaDeEntrada`): perfil Atendimento cai em `#/m/atendimento` direto após o login.
- O selo "cb" do trilho e as cores passam pelos tokens do M0.1; nenhum layout das 3 colunas muda.
**Aceite.**
- [ ] Login de perfil de atendimento abre o balcão sem passar pela sala (prova de rota).
- [ ] Captura do balcão em 1440, 1024 e 390 px sem mudança de largura das 3 colunas em relação ao master.
- [ ] Gestão não aparece no trilho; cada aba abre no seu módulo ou mostra "ainda não ligado" (F06).
- [ ] Perfil sem acesso ao M3 recebe 403 da API nas rotas de atendimento (a tela só esconde).
**Fora.** O shell em si (M0.2) e a matriz perfil × módulo (M0.3).
**Depende de.** M0.1, M0.2, M0.3; F13 no ar (ADR-0056 item 8: o redesenho não segura o go-live).
**Tamanho.** M · **Tier.** baixo · **Rollback.** revert do squash; o trilho volta ao de hoje.

### M3.2 · Pedidos fora da conversa

**Problema.** Venda de balcão, pedido de outro dia e pedido antigo sem conversa não têm onde ser vistos
no console; a dona precisa do `EasyStock.Web` para isso. (Pedido do checkout do site entra no balcão pela
SI.6; aqui ele aparece também, como qualquer pedido.)
**Fato medido.** A API já lista e detalha (`PedidosController.cs:36-55,58`); falta filtro por origem.
`Pedido.Origem` é texto livre e o comentário diz `"web" | "mobile" | "api"` (`Pedido.cs:81-82`), mas o
código grava também `whatsapp` (`CriarPedidoAtendimentoUseCase.cs:27`) e `storefront`
(`IPedidoStorefrontRepository.cs:76`).
**Abordagem.**
- API, aditivo: `origem` e `janela` (data) no `GET api/pedidos`; `conversaId` no resumo quando houver. Normalizar os valores de `Origem` num catálogo (`site`, `conversa`, `balcao`, `ifood` futuro) com leitura tolerante dos antigos.
- Console `#/m/atendimento/pedidos`: abas Hoje, Próximos dias, Todos; filtros status, origem, busca; cartão com número, cliente, janela, total, pago/pendente. Detalhe com itens, linha do tempo (`PedidoEvento`) e pagamentos; "Abrir conversa" quando houver (inclusive a `ChatSite` da SI.6).
**Aceite.**
- [ ] Venda de balcão (`POST api/pedidos/balcao`) aparece na lista em até 5 s (SSE da S18).
- [ ] Filtro por origem devolve só a origem pedida (teste de integração com 3 origens).
- [ ] Detalhe mostra a troca de status com hora e quem fez.
- [ ] Isolamento: pedido de outra empresa não aparece.
**Testes (Red).** `ListarPedidosUseCaseTests.FiltraPorOrigem`, `...OrigemLegadaVaiParaCatalogo`; `prova-m3-pedidos.mjs`.
**Fora.** Editar itens do pedido pela lista (fica na ficha). Relatórios (cada módulo, README §3).
**Depende de.** M3.1. **Tamanho.** M · **Tier.** baixo · **Rollback.** revert; campos aditivos.

### M3.3 · Esteira e pagamento do pedido pela ficha

**Problema.** Com o pedido criado, nove ações da comanda dizem "ainda não está ligado ao EasyStok (F04 em
diante). Use o EasyStok para isso" (`aplicacao/api/comanda.js:19-33`). A F04 não as ligou (ela cuidou da
tela Entregas) e nenhuma F seguinte as cita: a lacuna está órfã.
**Fato medido.** Todas têm rota: status (`PedidosController.cs:97`, `KdsController.cs:41`), cancelar
(`:113`), pagamento manual e desfazer (`:178,194`), aprovar e recusar (S12), troca de forma (S11).
**Abordagem.** Só console, sem endpoint novo: tirar de `SO_NO_EASYSTOK` e de `NAO_LIGADAS` as ações
`avancarEsteira`, `corrigirPasso`, `desfazerEsteira`, `confirmarPagamento`, `desfazerPagamento`,
`marcarComprovante`, `cancelarPedido`, `marcarEstorno`, `marcarRecebidoEntrega`, `refazerCobranca`,
`decidirAreaEntrega`; cada uma chama a rota e relê o pedido. Transição inválida mostra a mensagem da API.
**Aceite.**
- [ ] Avançar a esteira pela ficha muda o cartão na Cozinha (F05) sem recarregar.
- [ ] Pago à mão grava `PedidoPagamento` e aparece em "Pagamentos de pedidos hoje" (F14).
- [ ] Cancelar pedido pago obedece D3-04 (403 com texto para quem não pode).
- [ ] `prova-f06-honestidade.mjs` continua verde com as ações movidas para "ligada".
**Fora.** Trocar janela (M3.4) e endereço do pedido (M8.3).
**Depende de.** F14 para o lado do caixa. **Tamanho.** M · **Tier.** baixo (sem migração) · **Rollback.** revert; as ações voltam a avisar.

### M3.4 · Trocar a janela de um pedido criado

**Problema.** Reagendar pela API hoje só troca `AgendadoParaEm` (`AlterarAgendamentoPedidoUseCase.cs:41-42`).
Pedido com vaga continua preso à vaga antiga, porque o KDS manda pela vaga ativa
(`KdsPedidoQueries.cs:34-40`): a cozinha vê o horário velho e a janela velha continua ocupada.
**Abordagem.**
- `PATCH api/pedidos/{id}/janela {janelaId, data, avisarCliente}` (Operador): numa transação, libera a `VagaOcupada` ativa com motivo `reagendado`, ocupa a nova pela mesma regra da S16 (capacidade por COUNT, ADR-0014), recalcula o início previsto (S21) e grava `PedidoEvento`.
- `avisarCliente` usa o aviso de status (S13) com a faixa nova (RN-22, respiro).
- Console: o seletor de janelas da comanda (`features/ficha-cliente/SeletorJanelaApi.jsx`) passa a funcionar com pedido criado; "Alterar agendamento" da gaveta (`features/entregas/ModalAlterarAgendamento.jsx`) usa a mesma ação.
**Aceite.**
- [ ] Janela cheia recusa com 409 e a vaga antiga continua ocupada.
- [ ] Duas trocas simultâneas para a última vaga: só uma passa (teste com duas chamadas paralelas).
- [ ] KDS mostra a janela nova; atraso (S21) recalculado.
**Testes (Red).** `TrocarJanelaPedidoUseCaseTests.LiberaAntigaOcupaNova`, `...JanelaCheiaNaoMexe`, `...Concorrencia`.
**Depende de.** nada. **Tamanho.** M · **Tier.** baixo (sem migração) · **Rollback.** revert; volta a avisar.

### M3.5 · Clientes fora da conversa

**Problema.** Não há como achar um cliente sem abrir a conversa dele: a dona não consulta histórico de
quem não escreveu hoje nem começa contato.
**Abordagem.**
- Console `#/m/atendimento/clientes`: busca (`GET api/clientes/buscar`), lista paginada (`GET api/clientes`), filtro por tag (S24).
- Abrir cliente mostra a mesma Ficha da F09 (dossiê, tags, notas, ocorrências), sem conversa ao lado; "Conversar" abre a conversa existente ou cria contato pelo WhatsApp com modelo aprovado (M3.6).
- Avaliação (S26): leitura autenticada `GET api/clientes/{id}/avaliacoes` (Operador), aditiva, para o dossiê.
- Sinal de mesmo domicílio (RN-12, D10) só como selo, sem somar histórico (Q1 aberta, `03-ANALISE…md:550`).
**Aceite.**
- [ ] Busca por nome, telefone ou tag acha o cliente em até 1 s com 5 mil clientes (seed).
- [ ] A ficha aberta pela busca é a mesma da conversa (mesmo componente; prova de que não há segunda fonte).
- [ ] Avaliações aparecem no dossiê; de outra empresa, nunca.
**Fora.** Exportar lista; campanha a partir da lista (M6).
**Depende de.** F09 mergeada; M3.1. **Tamanho.** M · **Tier.** baixo · **Rollback.** revert.

### M3.6 · Modelo aprovado e reabrir conversa

**Problema.** Fora da janela de 24 h só modelo aprovado chega ao cliente, e o console não tem como mandar
um (#1287 deixou avisando); conversa encerrada não reabre.
**Fato medido.** A porta de canal já envia modelo (`ICanalMensageria.cs:22-33`; campanhas e S26 usam).
As rotas de mensagem são só texto e imagem (`AtendimentoConversasController.cs:99,112`).
`Conversa.RegistrarEntrada` exige conversa aberta (`Conversa.cs:225-228`).
**Abordagem.**
- `GET api/atendimento/modelos` (aprovados na Meta, cache curto) e `POST conversas/{id}/mensagens/modelo {nome, parametros}` com `AtenderConversas`.
- `POST conversas/{id}/reabrir`: `Conversa.Reabrir` no domínio (volta a `Assumida`, limpa `EncerradaEm`, guarda evento).
- Console: liga `ENVIOS_NAO_LIGADOS.modelo` e `reabrir`.
**Aceite.**
- [ ] Modelo enviado fora da janela chega ao WhatsApp (validação do Felipe) e aparece na thread.
- [ ] Texto livre fora da janela continua recusado (regra da S09 intacta).
- [ ] Reabrir põe a conversa em "Precisa de você" e a mensagem seguinte do cliente cai nela.
**Testes (Red).** `ConversaTests.ReabrirEncerrada`, `EnviarModeloConversaUseCaseTests.SoComAceitaModelo`.
**Fora.** Criar modelo na Meta pelo console (fica no Gerenciador da Meta).
**Tamanho.** M · **Tier.** baixo · **Rollback.** revert; volta a avisar.

### M3.7 · Atendentes: minhas, sem dono, todas

**Problema.** A S41 permite atribuir e transferir, mas o balcão não separa o que é de quem.
**Fato medido.** Atendente = usuário com `AtenderConversas` (`AtendentesUseCases.cs:24-31`). O console
sabe se a conversa é "minha" ou de "Outro atendente", sem o nome (`infra/api/traducaoConversas.js:70-81`).
No modo massa a atendente é fixa "Thatiane" (`Moldura.jsx:425`, `aplicacao/casos/caixa.js:23`,
`casos/encerramento.js:11`, `casos/cobranca.js:29`); no modo API vem da sessão.
**Abordagem.** Aditivo no resumo da conversa: `assumidaPorNome`. Filtro do balcão: Minhas, Sem dono, Todas
(padrão Todas, para a casa de uma pessoa só não mudar nada). Selo com o nome. Transferir é da F09.
**Aceite.**
- [ ] Com dois usuários, cada um vê "Minhas" com as suas e o nome do outro no selo.
- [ ] Com um usuário só, nada muda na tela (captura igual).
**Fora.** Distribuição automática; metas por atendente.
**Depende de.** F09; D3-03. **Tamanho.** P · **Tier.** baixo · **Rollback.** revert.

### M3.8 · Lembrete de pedido agendado sobre `Pedido`

**Problema.** O lembrete de agendado só enxerga `mobile_orders` (§3.5); encomenda do site ou do WhatsApp
para outro dia não lembra ninguém.
**Abordagem.**
- Job novo no Worker sobre `Pedido` com vaga ativa ou `AgendadoParaEm`, status não final: na véspera (início do expediente do dia anterior) e no início previsto (S21) cria `Lembrete` (S43) do tipo novo `PedidoAgendado`, com `PedidoId` e `Referencia` = `{pedidoId}:{marco}` como trava (sem coluna nova).
- Mesmo `PostgresAdvisoryLock`. O job antigo continua até a PWA sair (P05) e é removido junto.
**Aceite.**
- [ ] Encomenda para depois de amanhã gera um lembrete na véspera e só um (rodar o job duas vezes).
- [ ] Pedido cancelado não lembra.
- [ ] Lembrete aparece na lista da F10 e no SSE.
**Testes (Red).** `LembretePedidoAgendadoJobTests.VesperaUmaVez`, `...CanceladoNaoLembra` (relógio falso).
**Depende de.** F10 para a tela; M8.2 para existir encomenda pelo console. **Tamanho.** P · **Tier.** baixo (enum, sem migração; conferir se `TipoLembrete` é gravado como texto ou inteiro antes de fechar o tier) · **Rollback.** revert.

### M3.9 · iFood como canal de pedido (condicional a D3-02)

**Problema.** A Thati quer o iFood como canal, não como tela (D5, `02-ESTORIAS…md:232`). Não existe nada.
**Abordagem (esboço; a API do iFood não foi lida nesta sessão, confirmar antes de abrir a issue).**
- Porta `ICanalPedidoExterno` com adaptador iFood: recebe o pedido, cria `Pedido` com origem `ifood`, itens mapeados ao cardápio (M1), entra no KDS e no M3.2 com selo próprio; confirma e muda status de volta ao iFood.
- Chat do iFood como canal de conversa só se a API permitir; senão o pedido aparece sem conversa.
- Credencial por loja na F16 (`CategoriaIntegracao.Marketplace`).
**Aceite.** Definido quando D3-02 for respondida.
**Tamanho.** G · **Tier.** alto (integração externa, credencial, provável migração de mapeamento) · **Rollback.** desligar o canal na F16.

## 5. Ordem

```
F13 go-live ─► M0.1–M0.3 ─► M3.1 shell ─► M3.2 pedidos ─► M3.5 clientes (após F09)
SI.1 ─► SI.6 site como canal (pedido do checkout no balcão) ──────────────┘ (doc 10, em paralelo)
F09 ─► M3.3 esteira na ficha ─► M3.4 trocar janela ─► M3.6 modelo/reabrir ─► M3.7 atendentes
F10 ─► M3.8 lembrete agendado (junto com M8.2)            M3.9 só depois de D3-02
```

## 6. Decisões pendentes do Felipe

**D3-01 · Onde fica a configuração do atendimento (tom, saudações, respiro, horário)?**
- A) (Recomendado) "Loja aberta/fechada" fica no trilho do M3; o resto vai para M7 Configurações.
- B) Tudo fica no M3, num item "Ajustes do atendimento".
- C) Tudo vai para M7, inclusive abrir e fechar a loja.

**D3-02 · iFood entra quando e como?**
- A) (Recomendado) Fora até a casa ter conta e volume no iFood; M3.9 fica no backlog.
- B) Só o pedido do iFood entra (Cozinha e lista de pedidos), sem chat.
- C) Pedido e chat do iFood como canal do balcão (D5 inteiro).

**D3-03 · Atendentes múltiplos agora?**
- A) (Recomendado) Só filtro Minhas/Sem dono e nome no selo (M3.7, pequeno).
- B) Adiar até existir a segunda pessoa no atendimento.
- C) Distribuição automática das conversas entre atendentes.

**D3-04 · Quem cancela pedido já pago pela ficha (dispara estorno)?**
- A) (Recomendado) Operador cancela pedido não pago; pago só Gerente.
- B) Qualquer Operador.
- C) Só pelo EasyStok Web, nunca pelo console.

**Já decidido (01/10), registrado para não reabrir:** pedido do checkout do site entra no balcão pela
conversa `ChatSite` do cliente, com o pedido na Ficha, igual ao WhatsApp. Fatia dona: SI.6
([10-site-e-impressos.md](10-site-e-impressos.md)).
