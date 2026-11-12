# Console do operador — matriz de paridade e ordem de ligação

Decisão: ADR-0054 · Issue: #1120 · Código: `EasyStock.Console/` · Medido em 2026-09-29 no master 69d9f9b3

Cada linha é um módulo do console. Ele é **ligado** quando troca o repositório simulado
(`src/infra/repositorio*.js`, massa em `dados/`) pela API real. Só se liga um módulo cuja spec
backend já está no master. Quando o módulo fica em paridade, a tela legada da última coluna sai.

## Matriz

| Módulo do console (`src/features/`) | Spec backend | No master? | Endpoint | Tela legada que sai |
|---|---|---|---|---|
| `caixa-de-entrada`, `atendimento` (lista, thread, assumir, responder, encerrar) | S04, S07, S34 | Sim | `api/atendimento/conversas` (+ `/mensagens`, `/assumir`, `/liberar-automatico`, `/encerrar`, `/marcar-lida`) | Nenhuma |
| `anexos` | S02 | Sim | `api/atendimento/conversas/{id}/mensagens/imagem` | Nenhuma |
| `agente` (painel do automático) | S06 | Sim | o agente roda na API; o console só mostra estado e sugestão | `servidor/agente.mjs` do próprio console |
| `assistente` (ligado na F02) | S47 | Sim | `api/atendimento/assistente` | Nenhuma |
| `gestao` (expediente, configuração; ligado na F02) | S08, S40 | Sim | `api/atendimento/configuracao`, `api/atendimento/expediente` | Nenhuma |
| `ficha-cliente` (consentimento; ligado na F02) | S38 | Sim | `api/atendimento/clientes/{id}/consentimentos` | Nenhuma |
| Mensagem programada (dentro de `atendimento`) | S39 | Sim | `api/atendimento/mensagens-programadas` | Nenhuma |
| Comanda da `ficha-cliente` (pedido e cobrança; ligada na F03) | S10, S11, S16 | Sim | `api/atendimento/comanda/cardapio`, `api/atendimento/comanda/janelas`, `api/atendimento/conversas/{id}/pedido` (GET e POST), `api/pedidos/{id}/cobranca/forma` | Nenhuma: medido em 2026-09-30, o PWA `Api/wwwroot/pwa` (Easy Stock Mobile) não cria pedido de conversa |
| `entregas` (ligado na F04) | S12, S14, S44, S45 | Sim | `api/kds/pedidos` (+ endereço e aprovação), `api/atendimento/entregadores`, `api/atendimento/viagens`, `api/atendimento/chamados-entregador`, `api/storefront/pedidos/{id}/aprovar` e `/recusar`, `api/minha-vitrine/entrega`, `api/operacao/eventos` (SSE) | Nenhuma |
| `cozinha` (ligado na F05) | S18, S19, S20, S21 | Sim | `api/kds/pedidos` (+ `/{id}/status`), `api/operacao/eventos` (SSE), `api/pedidos/{id}/canhoto`, `api/pedidos/{id}/reimprimir` | KDS atual (`Api/Mobile/Controllers/KdsController.cs`), sai na P05 |
| `cardapio`, `cardapio-link` | S45, S48 | Parcial | `api/minha-vitrine/cardapio`, `api/minha-vitrine/configuracao`, `api/storefront/{slug}/menu` | a medir |
| `ficha-cliente` (tags, notas, dossiê, bloqueio), `notas` (liga na F09) | S24, S25 | Sim (medido em 2026-09-30) | `api/clientes/{id}/tags`, `/notas`, `/dossie`, `/bloquear`, `/desbloquear`, `/preferencias`; `api/atendimento/conversas/{id}/dossie` | a medir |
| `respostas`, `automacoes` (liga na F10) | S42 | Sim | `api/atendimento/respostas-prontas` (+ `/{id}/arquivar`, `/{id}/render`), `api/atendimento/automacoes/{gatilho}` | Nenhuma |
| `lembretes` (liga na F10) | S43 | Sim | `api/atendimento/lembretes` (+ `/{id}/concluir`, `/vistos`) | Nenhuma |
| `lote-papel` (liga na F11) | S46 | Sim | `api/atendimento/esteira/lote` | Nenhuma |
| `gestao/caixa` (liga na F14, #1244) | caixa do EasyStok | Sim, com lacunas | `api/caixa/dia`, `/abrir`, `/fechar`, `/movimentos`, `/fechamentos` | PWA de caixa, sai na paridade |
| `gestao/fidelidade` (F15, #1245) | nova | Não | `api/fidelidade/*`, `api/clientes/{id}/pontos` (na spec) | Nenhuma |
| `gestao/integracoes` (F16, #1246; Lalamove na F17, #1247) | núcleo `credencial_integracao` | Parcial: tabela e cifra existem, sem uso, sem RLS | `api/integracoes` (na spec) | Nenhuma |
| `simulacoes` | nenhuma | N/A | fica só no modo demonstração | Nenhuma |

Correções e fatias F06 a F17: [11-console-fechamento.md](11-console-fechamento.md) (#1232).

Estoque, rotulagem e o restante do EasyStock.Web não têm tela no console. Continuam no legado até
existir decisão própria, depois das ondas acima.

## Ordem

```
F01 caixa de entrada real ──► F02 gestão + assistente + consentimento ──► F03 pedido e cobrança (ligada)
        │                                                                        │
        └── login JWT + tenant, deploy em app.easystok.online                     └──► F04 entregas ─► F05 cozinha (após S18–S21)
```

## F01 · Caixa de entrada na API real

**Problema.** A dona não tem onde ver e responder as conversas que já chegam pelo webhook da Meta.
A API (`api/atendimento/conversas`) está em produção desde 29/09; a tela só existe com dados falsos.

**Abordagem.** Um cliente HTTP em `src/infra/api/` com login (`api/auth`) e o token com a empresa;
`repositorioConversas.js` passa a ler da API quando `VITE_FONTE_DADOS=api` (e `VITE_API_BASE` para
outra origem; vazio = mesma origem) e cai na massa quando não está (o modo demonstração continua). Assumir, responder, liberar o automático, encerrar e
marcar como lida chamam os endpoints do S07. Atualização por polling curto até o SSE do S18 existir.

**Aceite.**
- [x] Com `VITE_FONTE_DADOS=api`, a lista mostra as conversas do tenant logado e nenhuma de outro
  tenant (validado pelo Felipe em 2026-09-30, Demonstração EasyStok).
- [x] Mensagem enviada pelo console chega ao WhatsApp do cliente e aparece na thread (validado pelo
  Felipe em 2026-09-30, envio e recebimento).
- [x] Sem `VITE_FONTE_DADOS`, o console abre como hoje, com a massa.
- [x] `npm run qualidade` verde; a fronteira de camadas continua valendo (`features` não chama
  `fetch`).
- [ ] Console publicado em `app.easystok.online` atrás do Caddy compartilhado (GO de deploy próprio).

**Fora.** SSE, cobrança, cozinha, agente no navegador.

## F02 · Gestão, assistente e consentimento na API real

Issue #1201. Base: a F01 (`feat/console-caixa-real-f01-1132`).

**Problema.** No modo API, abrir e fechar a loja, editar o horário, configurar o atendimento, perguntar
ao assistente e marcar os avisos da Ficha mudavam só a memória do navegador; recarregar perdia tudo.

**Abordagem.** Um módulo por endpoint em `src/infra/api/` (`expedienteApi`, `configuracaoApi`,
`assistenteApi`, `consentimentosApi`) e as ações em `src/aplicacao/api/`, compostas em `comApi`:
- Expediente (S40): carrega ao entrar e ao abrir a aba; o botão do topo grava `ForcarAberta` ou
  `ForcarFechada`; editar o horário em Mensagens automáticas grava a semana (espera de 800 ms);
  a aba **Atendimento** da Gestão (só no modo API) tem "Voltar a seguir o horário" e as mensagens de
  fora do horário e loja fechada. Dia sem turno na API é dia fechado no console.
- Configuração (S08): mesma aba, formulário com tom, sugestões, saudações, frases, respiro, preparo e
  a chave do automático; o que a API devolve no PUT volta para o formulário.
- Assistente (S47): o balão pergunta à API com o `conversaId`; o 503 sem chave da Anthropic aparece
  como erro no próprio balão.
- Consentimento (S38): os checkboxes E-mail e SMS da Ficha leem e gravam a finalidade Transacional
  daquele canal e mostram `podeEnviar` (transacional sai por padrão; desmarcar revoga). Só com
  `clienteId`; lead sem cadastro vê o aviso para cadastrar.

Erro de carga ou gravação aparece na FaixaApi (expediente) ou ao lado do controle (configuração,
avisos, assistente); no erro do controle manual o estado volta ao que a API tem.

**Aceite.**
- [x] `npm run qualidade` verde; fronteira de camadas ok.
- [x] Sem `VITE_FONTE_DADOS`, o console abre como hoje, com a massa (sem a aba Atendimento).
- [x] Abrir e fechar a loja pelo topo reflete no expediente (validado pelo Felipe em 2026-09-30).
- [x] A configuração salva e recarrega (validado pelo Felipe em 2026-09-30).
- [x] O assistente responde (validado pelo Felipe em 2026-09-30).
- [x] E-mail e SMS da Ficha gravam e voltam após recarregar (validado pelo Felipe em 2026-09-30).

**Fora.** Mensagem programada (S39), finalidade Marketing na Ficha, SSE do expediente.

## F03 · Pedido e cobrança da conversa na API real

Issue #1210. Medido em 2026-09-30: não havia endpoint autenticado para criar o pedido da conversa
(o S48 é anônimo, pelo token do link; `POST api/pedidos` é outro núcleo, sem vaga nem conversa).

**Problema.** No modo API a comanda da conversa era só memória do navegador: não virava pedido nem
cobrança, e a polling apagava a comanda a cada 5 s (`pedido: null` no merge).

**Abordagem.** Endpoint e tela na mesma PR (ADR-0054 item 5), sem regra de negócio no navegador:
- API (`AtendimentoComandaController`, policy `Operador`, empresa do token):
  - `GET comanda/cardapio`: o menu público da vitrine da empresa (o mesmo do agente e do site).
  - `GET comanda/janelas?itens=`: janelas com vaga no prazo dos itens (S16), pelo mesmo use case
    que a ferramenta `listar_janelas` passou a usar.
  - `POST conversas/{id}/pedido`: `GerarPedidoConversaUseCase` cria o pedido pelo S10, cobra pelo
    S11 (`online` ou `na_entrega`) e manda o resumo com o link ao cliente pela conversa. Exige
    `AtenderConversas`; recusa com pedido em andamento não finalizado na conversa.
  - `GET conversas/{id}/pedido`: pedido em andamento com a cobrança vigente (paga vence; senão a
    mais recente).
- Console: cardápio da vitrine no lugar da massa; seletor de janelas da API na comanda; "Enviar ao
  cliente" chama o POST; a polling relê o pedido aberto a cada ciclo e mostra pendente, pago e
  expirado pela mesma `situacaoDaCobranca` da demonstração. Trocar o meio com o pedido criado usa a
  troca de forma do S11. Confirmar à mão, estorno, cancelar e esteira ficam para a F04 e, com o
  pedido criado, avisam em vez de mudar só o navegador.

**Aceite.**
- [x] `npm run qualidade` verde; `ferramentas/prova-f03-pedido-api.mjs` com 12 verificações.
- [x] Testes .NET: `GerarPedidoConversaUseCaseTests`, `ObterPedidoConversaUseCaseTests`,
  `AtendimentoComandaControllerTests` (isolamento por empresa e permissão).
- [x] Sem `VITE_FONTE_DADOS`, o console abre como hoje, com a massa.
- [ ] Com a API publicada: pedido criado pela conversa aparece no EasyStok, o link chega ao
  cliente e o estado vira pago quando o Mercado Pago confirma (validação do Felipe, sandbox).

**Fora.** Esteira, cancelamento e estorno pela API (F04); reenvio avulso do link (o job da S11
reemite e envia sozinho); cupom.

## F05 · Cozinha na API real

Issue #1218. Sem endpoint novo: os quatro já estavam no master (S18 a S21).

**Abordagem.** No modo API, `#/cozinha` abre `TelaCozinhaApi` (pede login se a aba não tem sessão);
sem `VITE_FONTE_DADOS` segue a cozinha espelhada do Balcão.
- Fila: `GET api/kds/pedidos` (aguardando, em preparo, pronto e saiu para entrega), uma coluna por status.
- Ao vivo: `GET api/operacao/eventos` lido por `fetch` em stream com o JWT no header (o EventSource
  não manda header). Todo `ready` e `pedido.*` recarrega a fila. SSE caído: fila a cada 15 s e nova
  conexão a cada 10 s.
- S21: o cartão mostra "Começar em N min" ou "Atrasado N min" pelo `inicioPrevistoEm` e `atrasado`.
- Um toque: `PATCH api/kds/pedidos/{id}/status`; a transição inválida volta a mensagem da API.
- S20: "Canhoto" abre o HTML de 80 mm e chama a impressão do navegador; "Reimprimir na fila" põe o
  canhoto de novo na fila do bridge.
- Prova pura: `node ferramentas/prova-f05-cozinha-api.mjs` (próximo passo, aviso S21, leitor SSE).

**Aceite.**
- [x] `npm run qualidade` verde; fronteira de camadas ok.
- [x] Sem `VITE_FONTE_DADOS`, o console e a cozinha abrem como hoje.
- [ ] Pedido pago aparece na cozinha e muda de passo pela tela (validação do Felipe).

**Fora.** Arrastar cartão, filtro por linha e escolha de entregador no modo API; consumo automático
da fila de impressão pela aba (decisão da onda 0.6).

## F04 · Entregas na API real

Issue #1221. Sem endpoint novo. Medido: S44 e S45 já tinham todos os endpoints; faltava ao KDS o
endereço do cliente (S14) e o `RequerAprovacao` com o motivo (S12). Os três campos entraram de forma
aditiva em `KdsPedidoDto`, com `EmpresaId` no WHERE da leitura do cliente e teste de isolamento.

**Abordagem.** No modo API, a gaveta abre `GavetaEntregasApi` e `#/entregas` abre `TelaEntregasApi`
(pede login se a aba não tem sessão). Sem `VITE_FONTE_DADOS`, nada muda.
- Hoje: `GET api/kds/pedidos?status=aguardando_aprovacao_baba,pronto,saiu_para_entrega` e as viagens
  da S44. Recarrega a cada `ready` e `pedido.*` do SSE da F05; SSE caído, a cada 15 s.
- S12/S14: aprovação da exceção (motivo legível, "Entrega fora da área") com aprovar e recusar.
- S44: nova viagem, pôr pedido, subir, descer, tirar, escolher entregador, "Saiu para entrega"
  (bloqueado na tela sem entregador, RN-32), marcar entregue, desfazer, link de rota; entregadores
  (cadastrar e desativar) e chamados (abrir, atendido, cancelar).
- S45: janelas, zonas (CEP ou bairros) e bloqueios dos próximos 60 dias. Policy `Admin`: operador
  comum vê o 403 na faixa.
- Prova pura: `node ferramentas/prova-f04-entregas-api.mjs`.

**Aceite.**
- [x] `npm run qualidade` verde; fronteira de camadas ok.
- [x] Sem `VITE_FONTE_DADOS`, a gaveta e a janela de Entregas abrem como hoje.
- [ ] Viagem com entregador sai e os pedidos vão para "saiu para entrega" (validação do Felipe).

**Fora.** Arrastar parada, editar entregador, editar janela e zona já criadas (só ativar e desativar),
relatório por bairro e o endereço de `ClienteEndereco` (a leitura usa os campos primários do cliente).

## R4 · Cadastros no contexto do Atendimento (#2395)

Cadastros reutiliza os contratos de clientes, produtos, fornecedores, respostas prontas, etiquetas e
impressão. O menu compartilha o acesso ao Atendimento; cada recurso mantém sua autorização própria.
Leads são clientes persistidos sem pedidos. Conversas sem `ClienteId` aparecem como “Sem cadastro”.
Filtros de relacionamento e produto são aplicados no servidor antes da paginação.
Listagem e detalhe de clientes calculam quantidade e última compra pelos pedidos da empresa,
incluindo Atendimento; o contador legado não determina se a pessoa continua sendo lead.

- `api/cadastros/contas-bancarias` e `metodos-pagamento`: tenant do JWT, revisão concorrente e
  `requestId`. Gestão exige perfil de gerência e permissão financeira; recebimento usa apenas métodos
  ativos. Recibos repetidos e pagamentos confirmados pelo provedor continuam recuperáveis.
- `api/atendimento/conversas/{id}/vinculo-cliente`: retirada, restauração ou escolha explícita com
  revisão esperada, motivo e ator. Qualquer pedido histórico bloqueia a correção; conversas legadas
  sem histórico verificável também bloqueiam. Auditoria, expiração de links e revogação das sessões
  daquele chat são transacionais. Cadastros, OTP pessoal, mensagens e rascunhos são preservados.
- `candidatos-cliente`: telefone compartilhado não escolhe nem funde pessoas automaticamente.
- Criar pedido pela comanda exige `clienteId` e `vinculoRevisao` da proposta vista no Console.
  O servidor confere esse snapshot novamente sob lock, inclusive após retirar e restaurar a mesma pessoa.
- Salvar nome, telefone ou endereço pela ficha também exige a identidade capturada ao abrir a edição.
  `clienteId: null` é explícito para cadastro inicial; ausência dos campos recebe 400 e revisão obsoleta
  recebe 409 `VINCULO_CLIENTE_ALTERADO`. Reconferir o destino preserva o texto sem gravar automaticamente.
- Desfazer a edição de uma conta bancária gera uma nova alteração somente nessa conta, usando a revisão
  da gravação original. Uma edição posterior recebe 409; alterações independentes permanecem.
- Falha de armazenamento local mantém a proposta em memória e avisa que ela não será retomada após
  recarregar. Pagamento na entrega sem recebimento não dispara confirmação de pagamento.
- `api/atendimento/assistente/cadastro`: propõe até três campos com evidência literal de mensagens
  recebidas. Reutiliza `IMontagemFichaLlmClient`, sua configuração e o adaptador CLI restrito ou API;
  não grava cadastro nem envia mensagens. Mudança de sessão, conversa, cliente ou revisão invalida a
  sugestão e impede aplicar uma resposta atrasada.
- Modelos de notificação mantêm a versão publicada enquanto a próxima é rascunho. Aprovar a versão
  atual publica e desativa a anterior na mesma transação; `apenasAtuais=true` filtra antes da paginação.

Ao liberar pedido com cobrança online, a decisão é tomada sob lock, mas a troca de forma e a
conciliação usam suas próprias transações. A recusa de troca por pagamento já confirmado não pode
desfazer o recebimento. O caminho normal mantém status, estoque, auditoria e outbox atômicos.

Provas reproduzíveis: `prova-2395-cadastros.mjs`, `prova-2395-vinculo.mjs` e
`prova-2395-navegador.mjs` cobrem regras e componentes. `prova-2395-api-real.mjs` e
`prova-2395-jornada-api.mjs` usam a fixture opt-in `R4_FixtureLocalComLoginReal` com PostgreSQL e JWT.
Somente provedores externos são substituídos nessa fixture. `CadastroClaudeCliRealTests` é uma prova
opt-in separada do provedor real com dados sintéticos; não equivale à homologação de mensagens ou
pagamentos. Evidências de execução e aceite ficam na issue e na PR.

Auditoria factual dos 25 casos da matriz externa `aceites.json`, em 12/11/2026: as provas abaixo
demonstram os recortes descritos, não uma aprovação integral de 25/25. Artefatos locais estão em
`EasyStock.Console/.build/2395/` e não são versionados. `real/` usa UI, JWT, API e PostgreSQL reais,
com provedores de mensagem, pagamento e LLM controlados; resultados de componentes com HTTP
controlado e testes de contrato são identificados separadamente. A leitura desta tabela não reexecuta
as baterias anteriores ao rebase.

| Caso | Evidência existente e limite observado |
|---|---|
| J01 | `real/resultado.json`: retirada/restauração, três candidatos, escolha explícita e rascunho preservado. `real/excecoes/resultado.json`: transferência confirmada e retomada pela atendente. Não equivale a testar todos os estados da fila. |
| J02 | `real/jornada-ui-{1,2}-estado.json`: duas unidades, subtotal 64 + frete 5 = 69, endereço/janela e pedido persistido. Duplo gesto de criação e janela lotada não foram repetidos nessas jornadas Chromium. |
| J03 | Mesmas jornadas: KDS inicia preparo sem baixa; Pronto altera saldo 8→6 uma vez, confirmado após recarga. Estoque foi reconciliado por GET, sem operação pela tela Estoque. |
| J04 | Jornadas iniciais usam KDS. Complemento `real/entregas/resultado.json`: novo pedido, responsável cadastrado e atribuído, viagem despachada e entregue pelo módulo Entregas, recebimento na porta e reconciliação após reload. Não exercita todos os impedimentos de despacho. |
| J05 | Mesmas jornadas: um recebimento de 69, mensagem, encerramento e leitura posterior sem perda de histórico. Não foi percorrida toda a administração de custos/taxas/Caixa. |
| E01 | `real/excecoes/resultado.json`: falta visível, política padrão de descoberta, saldo 1→0 sem negativo; decisão de cancelar devolve só uma saída real e elimina a descoberta. Substituição/reposição e conselho de IA sobre estoque não foram exercitados. |
| E02 | Mesmo artefato: ocorrência, falha offline preservando vaga, troca Manhã→Tarde com capacidade reconciliada e comunicação posterior. Complemento `real/entregas/resultado.json` reagenda novo pedido pela própria tela Entregas e confere Tarde também em Cozinha e Atendimento antes de comunicar. |
| E03 | Mesmo artefato: atendente sem cancelamento pago, nota contextual, dona confere e cancela com motivo, atendente reassume e comunica. É solicitação por nota e ação autorizada, sem workflow formal de aprovação; ramo de recusa gerencial não executado. |
| E04 | Mesmo artefato: cancelamento e devolução total de 69 separados, recebimento preservado, estoque/capacidade liberados e uma devolução após reload. `real/jornada-api.json` complementa repetição idempotente; não é replay financeiro pelo Chromium nem inspeção visual do Caixa. |
| M01 | `real/mensagem-ui.json`: variável de pedido ausente bloqueia envio; perda da resposta de um 200 real e repetição mantêm ID/chave e um balão. `concorrencia/resultado.json`: prévia e publicação editorial persistidas. Envio de modelo Meta e indisponibilidade do catálogo/canal têm provas de contrato, sem jornada Chromium equivalente. |
| I01 | `real/resultado.json`: três pessoas no telefone, escolha, retirada/restauração e histórico. Guardas de revisão/ABA e tenant têm provas focais/backend. Não há bateria Chromium final completa para pessoa inativa e telefone divergente. |
| S01 | `real/resultado.json`: autocomplete real e telefone formatado em Sem cadastro; normalização tem prova de domínio/repositório. Busca global/local por todas as formas, acentos, IDs e papéis não foi inteiramente percorrida no navegador. |
| R01 | `real/resultado.json` e `resultado.json`: retorno, rascunho e filtros; componente real cobre contexto restaurado após clique/edição. Jornadas finais conferem Clientes/Leads após reload. Não há medição completa de posição de rolagem em todos os módulos. |
| R02 | `real/excecoes/resultado.json`: zero retira item, comanda vazia não confirma; vazio/zero/negativo/acima do limite recusados, 69 recebido e persistido. `recebimento-invalido/resultado.json` cobre moeda negativa e correção decimal; quantidade usa controles inteiros, sem campo livre fracionário. |
| R03 | `concorrencia/resultado.json`: Instituição bancária com 149/150/151 caracteres retorna 200/200/400, sem truncamento. Não substitui a prova limite−1/limite/limite+1 de resposta e observação pedida na matriz. |
| R04 | `concorrencia/resultado.json` e `real/resultado-financeiro.json`: rede indisponível preserva edição e retentativa grava uma revisão. `storage/resultado.json`: App real/HTTP controlado, quota local bloqueada, aviso e memória preservados; reload não restaura, conforme aviso. |
| C01 | `concorrencia/resultado.json`: contas distintas em duas abas preservadas; mesma conta obsoleta recebe 409 com proposta mantida. Não generalizar esse controle de revisão a todos os cadastros legados. |
| C02 | `desfazer-conta-real/resultado.json`: Desfazer A preserva B, A posterior recusa 409, resultado confirmado por GET/reload, ator/operação registrados. Reversão limitada à conta; efeitos financeiros usam compensação, não restauração de base. |
| P01 | `concorrencia/resultado.json`, `real/resultado-financeiro.json` e `real/jornada-api.json`: Dona/Atendimento, link direto, GET/escrita 403 e isolamento entre empresas. Não comprovam todas as combinações de papéis, busca ou revogação durante edição. |
| A01 | `real/resultado.json`: endpoint/harness real com LLM controlado e aplicação individual com evidência. Provas focais invalidam contexto obsoleto; CLI real opt-in é separada. Apoio operacional completo de IA a pedido com impedimento não foi percorrido nessa bateria Chromium. |
| A02 | Mesmo artefato: aviso de até 20 mensagens recebidas, três campos e aplicação individual; bancos não oferecem IA. Contrato minimiza entrada e não envia formulário. Não é homologação de provedor externo. |
| U01 | Capturas das jornadas e Cadastros, mapa real de ícones e provas de gramática; estados lead/cliente/sem cadastro, recebido/devolvido separados. Não houve medição exaustiva de contraste e tipografia de todos os módulos. |
| U02 | Jornadas reais 390/1440; complemento Entregas em 320/768 sem overflow global e com setas alterando aba/foco; componentes reais cobrem combobox e alvo 48 px mobile. Retorno de Gestão e aviso do aparelho têm provas focais. Sem teclado virtual físico, zoom/orientação ou auditoria integral de foco de todos os diálogos. |
| N01 | `real/resultado-financeiro.json`: módulo/tela inválidos mostram aviso e recuperação; acesso financeiro negado testado. Não foi percorrido todo link antigo ou entidade removida. |
| Q01 | Duas jornadas UI com produtos/pedidos distintos na mesma conversa, saldos 8→6, pagamentos únicos de 69, entrega e encerramento; exceção própria paga/cancelada/devolvida com saldo final 1. Complemento pelo módulo Entregas gera pedido de 37, uma baixa 2→1, um recebimento e viagem entregue com responsável. Leituras reconciliam IDs/valores; permanecem os limites dos demais casos acima. |

O complemento Entregas usou pedido `a26446ef-ecb6-437b-b92a-b021c4f6b5ba`, viagem
`a4f7f974-b993-41eb-9908-b1b0ea6ce8e3` e entregador `7d2c3e39-9ac4-45ef-ab4c-986fdadd2c56`.
Foi executado antes da integração posterior do fix de status/estoque #2403; não prova por si uma
versão futura do backend. Não houve mutação HTTP direta nas jornadas UI.
