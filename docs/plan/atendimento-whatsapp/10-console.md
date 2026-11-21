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

Complemento da candidata `95f915a7dfb5ca77a609bca5e650ecdab64270ea`: API
`1.11.0+95f915a7dfb5ca77a609bca5e650ecdab64270ea`, SHA-256
`71a96c4031613078951d72fdef4c523bffd7debc9d8db46a4e80def4a4f78a1d`.
Nesta tabela, `banca/` indica a pasta local de evidências independentes
`C:/Users/felip/.codex/visualizations/2026/11/12/01a1d012-e600-7dc3-b035-129f9186bf5e/acompanhamento-implementacao-r4/`.
Os resultados identificam a versão em que foram executados; não aprovam automaticamente versões posteriores.
Os retestes abaixo usam a candidata `d70b62b5340b9b4839e5c620d56e5965f1443c37`, API
`1.11.0+d70b62b5340b9b4839e5c620d56e5965f1443c37`, SHA-256
`97eab0f3211d8b563f78227d5583bd748b8f770d98e91c7380812254b90427a0`.
O operacional fecha sete recortes e a jornada mobile foi repetida nessa versão.
I01 e S01 foram retestados no WIP de integração sobre
`48325986293b9f02ad36eae334cd14b0af71bfc8`, base `02c704b0f75ce7c48caed0dff2ac403de83f48b2`.
Esse WIP não é uma árvore limpa: dez arquivos e onze assets estão identificados por hash em
`.build/r4-final-wip-runtime.json` (SHA-256 `f9571e3581b919237319d9541397f9aab385375504182e07771f23fddb045071`).
A API exercitada tem SHA-256 `afd622a29c8b5da83e247b67793150b8e3dfd10027517649cf18e9b548ade8f4`.
Cada artefato conserva a versão/origem da prova; não se atribuem as jornadas anteriores a um commit futuro.
O ajuste final dos rótulos, sem mudar EhCompra/EhLead, está em `.build/r4-final-rotulos-runtime.json`
(SHA-256 `5add5e253940531d2e7b10468286f2faf039845ad6ec7fd4b206ca401b56076c`), com doze arquivos e onze assets identificados.

Validação do WIP integrado: gate 69, Application 4137, APIUnit 2062, PostgreSQL 13 e HTTP/API 13,
sem falhas ou ignorados nesses filtros .NET. Os 13 HTTP incluem os três REDs da CI95, o controle,
sete C01, autoria J05 e origem/fila E02. A correção mantém o CAS externo: nova unidade de trabalho
após rollback de cobertura e cadastro/encaminhamento sob a mesma transação.
Logs: `.build/r4-{gate,app,api-unit,postgre,api}-final-integrado.log`.
Console: 213 provas executadas verdes e 71 excetuadas pelo runner, discriminadas em
`EasyStock.Console/.build/2395/qualidade-final-integrada.log`; não são 284 execuções. Domain/Conversa
teve 50 aprovações na d70 (`.build/r4-domain-onda3.log`).

Após integrar o master `601d48502`, o executor passou a fornecer `CONSOLE_URL` e
`CONSOLE_TEST_URL` para o mesmo Vite e incluiu as doze provas R4 com HTTP controlado.
As dez jornadas R4 de API/PostgreSQL reais continuam opt-in. A prova de navegação financeira
recebeu no mock da Dona as três permissões de leitura de Cadastros já expostas pela API;
preserva a exigência das seis entradas e seus destinos, sem ampliar permissões do produto.
Qualidade e build passaram com 229 provas, zero falhas, 55 exceções explícitas e zero puladas
por ausência de Chromium (`EasyStock.Console/.build/2395/qualidade-pos-master-601d.log`).
O gate do rebase passou com 69 testes (`.build/r4-gate-4653b901.log`). O delta do master
não altera .NET, migrations nem contratos financeiros; as evidências anteriores mantêm
suas versões e não são apresentadas como novas execuções.
O smoke do delta identificou alvos de 32 px nos botões de porção do Banco de imagens;
o ajuste usa `--toque` na mesma condição móvel dos tokens, preservando os 32 px no desktop com mouse.

Na integração do master `93ab2afd5` (13/11), os quatro filtros `SyncPull*` focais passaram
com 23 testes; o PWA teve 492 aprovações e um skip por IndexedDB indisponível na VM, que também
não executa dois módulos ESM. Logs: `.build/r4-sync-pull-5cda7203.log` e `.build/r4-pwa-5cda7203.log`.
O novo prazo de escrita com keepalive revelou uma certeza indevida na Cozinha após perder o ACK.
A prova 2005 reproduziu o defeito (43/44); o ramo `SEM_RESPOSTA` passou a conservar o aviso de
incerteza, e a releitura confirma o estado aplicado sem repetir o PATCH (44/44).
A prova 2379 passou em 18 casos, incluindo timeout do recebimento, GET do saldo e repetição
explícita com a mesma chave. São transportes controlados; a persistência idempotente continua
coberta por `PagamentoRespostaPerdidaIntegrationTests` com HTTP/PostgreSQL.
Qualidade completa: 229 provas, zero falhas, 55 exceções e zero puladas por Chromium,
em `EasyStock.Console/.build/2395/qualidade-keepalive-final.log`.

O complemento `.build/2395/mobile-sync-b8c51734/resultado.json` passou em quatro etapas e 23
requisições HTTP/Kestrel/PostgreSQL reais, na API `1.11.0+b8c51734e9c842119342509e2114d4dc3547b621`.
O pagamento sintético pela rota usada pelo Console produz uma única `order.aReceber=0` no
aparelho autor; novo pull não repete a mutação nem reescreve pedido/pagamento/espelho mobile.
Outra empresa não recebe os dados, o pedido retorna 404 e o pull anônimo retorna 401.
O pagamento foi por HTTP, sem clique na UI; a preservação de edição local e a ausência de
criação de pedido no PWA são provas complementares da suíte de VM. Nenhum provedor externo foi chamado.

W5-01: `Agente_PedidoEVagaCommitadosSobrevivemAFalhaSqlDepoisDoProvedor` passou em PostgreSQL real,
com serviço de agente resolvido por DI e UoW reais; LLM e Mercado Pago controlados. Outra unidade
de trabalho vê pedido/vaga antes do retorno do provedor; SQL 22012 injetado no INSERT posterior
da cobrança não desfaz pedido/vaga/vínculo já commitados. Releitura confirma zero cobranças, zero
envios, uma chamada LLM e uma chamada MP. Não é uma chamada por endpoint HTTP do agente nem
homologação de provedor externo. Evidências: `.build/r4-w5-durabilidade-pg.log` e
`.build/r4-complemento-tests/w5-durabilidade-pg.trx` (1 aprovado, zero falhas/ignorados).

| Caso | Evidência existente e limite observado |
|---|---|
| J01 | `real/resultado.json`: retirada/restauração, três candidatos, escolha explícita e rascunho preservado. `real/excecoes/resultado.json`: transferência confirmada e retomada pela atendente. Não equivale a testar todos os estados da fila. |
| J02 | `real/jornada-ui-{1,2}-estado.json` e `real/operacao-complementos-candidata-d70b62b5/resultado.json`: duas unidades, subtotal 64 + frete 5 = 69, endereço/janela e pedido persistido. No d70, duplo gesto e resposta perdida resultam em um único POST/pedido; reload preserva o estado e o cadastro passa a ter um pedido. Janela lotada não foi repetida nessa prova. |
| J03 | Mesmas jornadas: KDS inicia preparo sem baixa; Pronto altera saldo 8→6 uma vez, confirmado após recarga. Estoque foi reconciliado por GET, sem operação pela tela Estoque. |
| J04 | Jornadas iniciais usam KDS. Complemento `real/entregas/resultado.json`: novo pedido, responsável cadastrado e atribuído, viagem despachada e entregue pelo módulo Entregas, recebimento na porta e reconciliação após reload. Não exercita todos os impedimentos de despacho. |
| J05 | `real/operacao-complementos-candidata-d70b62b5/resultado.json`: recebimento único de 69 pela UI, autoria Dona R4 no extrato da conversa, GET do pedido e Caixa; reload mantém os valores. O RED de autoria omitida na 95 foi corrigido e retestado. Jornadas anteriores cobrem mensagem, encerramento e leitura posterior. Custos/taxas não foram estimados nem auditados integralmente. |
| E01 | `real/excecoes/resultado.json`: falta visível e política de descoberta sem saldo negativo. No d70, `real/operacao-complementos-candidata-d70b62b5/resultado.json` registra produto com saldo zero retirado antes do pedido e alternativa de 32 + frete 5 = 37, com prazo/janela conferidos na UI. A escolha do cliente é explicitamente SIMULADA em nota, com responsável Dona; não é resposta real de cliente nem reposição de estoque. |
| E02 | `real/operacao-complementos-candidata-d70b62b5/resultado.json`: Dona registra atraso na Cozinha, ocorrência conserva pedido/conversa e aparece em Precisa de você sem responsável; o RED da 95 foi corrigido. Falha offline preserva janela e proposta; retry muda Manhã→Tarde uma vez, com capacidade reconciliada e estado igual em Atendimento/Cozinha/Entregas. `real/complementos-literais-d70b62b5/resultado.json` confirma a nova janela antes de comunicar por uma mensagem, sem alterar pedido/recebimentos/estoque. Usa capacidade da Dona, sem concedê-la ao perfil Cozinha; envio externo controlado. |
| E03 | `real/operacao-complementos-candidata-d70b62b5/resultado.json`: atendente sem privilégio registra solicitação contextual, gerente recusa com motivo e próximo passo visíveis e persistidos; a atendente retoma. `real/complementos-literais-d70b62b5/resultado.json` repete a negativa real com 403 FORBIDDEN, corpo completo e GET antes/depois iguais. `real/excecoes/resultado.json` cobre decisão autorizada de cancelar. São notas e ações autorizadas reais, sem workflow formal de aprovação. |
| E04 | `real/operacao-complementos-candidata-d70b62b5/resultado.json`: Pronto baixa uma unidade (8→7); cancelar repõe uma (7→8) e libera uma vaga. Devolução posterior de 37 conserva o recebimento e gera uma saída no Caixa. Resposta perdida/retry pela UI enviam a mesma operação duas vezes, com um estorno/saída e sem repetir estoque/capacidade. IDs e valores reconciliados após reload; provedores externos controlados. |
| M01 | `real/m01-intermediario/green-candidata-95f915a7/mensagem-ui.json`: 11/11 casos Chromium com JWT/API/PostgreSQL reais. Perda de resposta e ACK retido são controlados após gravação real; polling e replay mantêm um balão/ID por intenção e duas intenções de texto igual permanecem distintas. Variável ausente bloqueia envio; janela real encerrada bloqueia texto. Falha de transporte do catálogo é controlada; atualização real retorna 409 WHATSAPP_NAO_CONECTADO. Não houve autorização/envio de modelo pela Meta; publicação editorial não prova essa autorização. |
| I01 | `real/i01-excecoes/green-wip-final-integrado-390/identidade-ui.json`: 6/6 casos reais em 390. Compara três ativos do mesmo telefone com históricos 0/1/2 e datas, escolhe explicitamente a alternativa, confirma reload/ID/status do histórico e preserva cadastros. Inativo separado recebe 409 com motivo correto; cancelamento, telefone divergente e cadastro sem telefone passam. A fixture declara cancelados sintéticos, sem estoque/pagamento: candidatos contam todos os pedidos; dossiê conserva IDs/status e conta zero compras válidas, pela regra existente. `real/i01-rotulos-wip-390-1440/resultado.json` confere os rótulos finais “pedidos no histórico”/“compras válidas”, com candidatos reais e somente GET do vínculo controlado, sem escrita de negócio. Não redefine Cliente nem inventa compra entregue/paga. |
| S01 | `banca/s01-r01-candidata95/parecer-independente.json` prova busca global real e restrições por perfil; `banca/s01-local-d70/resultado.json` e complemento confirmam Cadastros por telefone/acentos/ID e filtros, mas revelaram divergência de telefone dentro de mensagens. O WIP corrigido passa em `real/s01-telefone/green-wip-final-integrado/resultado.json`: máscara, dígitos e +55 retornam o mesmo conjunto, incluindo contato sem cadastro encontrado pela mensagem, sem escrita e com assets/fontes estáveis. São preservadas substrings textuais; 188 casos puros complementam a prova. `banca/cadastros-w3-wip-48325986-1794532987651/resultado.json` W3-05 confirma parâmetros/filtros de produtos na UI com HTTP controlado; PostgreSQL cobre SQL antes do limite. |
| R01 | `memoria-atendimento/green/resultado.json`: App real/HTTP controlado restaura filtros e 700 px. `banca/s01-r01-candidata95/r01-e-restricoes.json` e `r01-comanda.json`: lista real filtrada, scroll 480 e primeiro cartão Fila 06 restaurados; Hall, reload e reabrir rota preservam conversa, resposta integral, quantidade 2 e observação após descarte. Mesma aba e sem pedido confirmado nesse recorte. W3-04 no artefato de Cadastros citado em S01 libera uma leitura antiga após abrir novo cadastro e preserva o novo nome/e-mail; é UI com HTTP controlado, sem escrita real. |
| R02 | `real/excecoes/resultado.json`: zero retira item, comanda vazia não confirma; vazio/zero/negativo/acima do limite recusados, 69 recebido e persistido. `recebimento-invalido/resultado.json` cobre moeda negativa e correção decimal; quantidade usa controles inteiros, sem campo livre fracionário. |
| R03 | `real/r03-textos-final-95f915a7/textos-ui.json`: 11/11 casos repetidos na candidata 95 com API/PostgreSQL reais. Resposta 4095/4096/4097/4001 preservada após navegação/recarga; 4097 bloqueia envio com aviso. Nota interna 499/500 salva integralmente; 501/4001 recebe 400 visível e mantém original recuperável. Observação de item, sem limite finito declarado, preserva 3999/4000/4001. Comprimentos, hashes e finais reconciliados, sem envio de mensagem ou criação de pedido. |
| R04 | `banca/r04-resposta-perdida-d70-1794530397683/resultado.json`: quatro casos com conta própria, UI/API/PostgreSQL reais; PATCH persiste, resposta perdida mostra erro e preserva texto; retry usa a mesma intenção e reload confirma efeito/revisão únicos. `storage-d70b62b5/resultado.json`: quota de comandas bloqueada preserva proposta na página; após restaurar armazenamento, quantidade/observação retornam no reload. Não promete recuperar dados não gravados antes de recarregar. `memoria-atendimento/green/resultado.json` cobre quota exclusiva de texto e retry após restauração. Somente as provas de quota usam HTTP controlado. |
| C01 | HTTP/PostgreSQL passou para duas pessoas, 409 obsoleto, 400 sem revisão, tenant, corrida entre contextos e rollback da auditoria; cinco escritas por ficha/endereço/telefone invalidam editor central aberto. `clientes-concorrencia-real/resultado.json` na 95 usa duas abas, JWT/API/PostgreSQL reais, sem interceptação HTTP: duas pessoas independentes, 409 preservando rascunho, reconferência somente leitura, outro 409 após nova alteração e save explícito preservando e-mail externo e a outra pessoa. |
| C02 | `desfazer-conta-real/resultado.json`: Desfazer A preserva B, A posterior recusa 409, resultado confirmado por GET/reload, ator/operação registrados. Reversão limitada à conta; efeitos financeiros usam compensação, não restauração de base. |
| P01 | `banca/p01-1794526958098/resultado.json`: 104 verificações, zero falhas, com navegador/API reais para Dona, Atendimento, Cozinha e combinações explícitas Consulta R4/Entrega R4. Menu, busca, link direto e GET/PATCH obedecem às capacidades observadas. Revogação do usuário exclusivo encerra sessão e recusa JWT antigo com 401 sem gravar. `banca/compilado-95f915a7/isolamento-jwt-real.json`: JWT de outra empresa recebe 404 em pessoa/produto/pedido/mensagens/vínculo/IA; empresa divergente recebe 400 sem alteração. Não generaliza a todos os perfis possíveis. |
| A01 | `real/operacao-complementos-candidata-d70b62b5/resultado.json`: use case/harness/consultar_pedido reais com LLM controlado identificam o pedido correto e saldo pendente de 37; prévia, descarte, 503 controlado, resposta retida durante troca de conversa e rascunho manual não alteram pedido/mensagens. A ferramenta não expõe a ocorrência de atraso. `real/resultado.json` cobre cadastro com aplicação individual e evidência. CLI real opt-in é prova separada, sem homologação externa nesta jornada. |
| A02 | Aviso informa até 20 mensagens recebidas com texto, três campos e aplicação individual; bancos não oferecem IA. HTTP real resolve o adaptador CLI com executor controlado: 25 entradas resultam em exatamente 20 objetos `{mensagemId,texto}`; outra empresa recebe 404 e não executa provedor. `.build/r4-a02-cli-payload.json` captura stdin e somente flags autorizadas de prompt/schema/tools/MCP para comparar com a divulgação da UI, sem ambiente ou credenciais. Não é homologação de provedor externo. |
| U01 | Capturas das jornadas e Cadastros, mapa real de ícones e provas de gramática; estados lead/cliente/sem cadastro, recebido/devolvido separados. Não houve medição exaustiva de contraste e tipografia de todos os módulos. |
| U02 | `banca/mobile-final-d70/resultado.json`: jornada d70 em 390×844 com toque, JWT/API/PostgreSQL reais; lead assistido e vínculo explícito, duas unidades de legumes (64 + 5 = 69), recebimento/autoria Dona, estoque 8→6 uma vez, entrega, mensagem, encerramento e consulta após reload, zero pageerrors. Navegador emulado; provedores externos/LLM controlados. Aviso: oito toques independentes em 320/390 (`banca/aviso-toque-d70b62b5/resultado.json`) e seis após o rebase no WIP, em 390/1440 sem remontagem/perda de texto (`.build/2395/avisos-final-wip-48325986/resultado.json` na raiz), com HTTP controlado e hashes estáveis. `u02-resultado.json` da banca95 cobre foco/orientação/ampliação nativa 2×; sem aparelho físico nem zoom da barra. |
| N01 | `real/resultado-financeiro.json`: módulo/tela inválidos mostram aviso e recuperação; acesso financeiro negado testado. `banca/compilado-95f915a7/n01-resultado.json`: Console compilado com HTTP integralmente controlado recupera alias antigo para Atendimento; cadastro ausente mostra erro, não abre editor e permite refazer busca. Não houve remoção real de entidade nem escrita no banco nessa prova. |
| Q01 | Jornadas iniciais reais preservam produtos/pedidos distintos, pagamentos e estoque; `real/entregas/resultado.json` acrescenta viagem com responsável. Na d70, operacional comprova pedido de 69 e exceção de 37 cancelada/devolvida com estoque 8→7→8; jornada mobile independente comprova duas unidades de legumes, 69, estoque 8→6, entrega e encerramento. I01/S01 e aviso foram retestados no WIP identificado por hashes. É evidência composta: versões, controles e limites de cada caso permanecem explícitos; não equivale a reexecutar todas as jornadas no commit posterior. |

O complemento Entregas usou pedido `a26446ef-ecb6-437b-b92a-b021c4f6b5ba`, viagem
`a4f7f974-b993-41eb-9908-b1b0ea6ce8e3` e entregador `7d2c3e39-9ac4-45ef-ab4c-986fdadd2c56`.
Foi executado antes da integração posterior do fix de status/estoque #2403; não prova por si uma
versão futura do backend. Não houve mutação HTTP direta nas jornadas UI.

A migração complementar `20261112225605_MensagensCorrelacionadasERevisaoClienteR4` foi aplicada
sobre a migração R4 anterior em PostgreSQL 17 descartável com cliente e mensagem preexistentes.
Contagens e hashes dos conteúdos permaneceram iguais; a mensagem legada mantém `ChaveEnvio`
nula. O SQL acrescenta somente essa coluna nullable de 120 caracteres. A revisão usa o `xmin`
de sistema: primeira atualização alterou a revisão e tentativa com revisão anterior afetou zero
linhas. `has-pending-model-changes` não detectou divergência. Logs locais:
`.build/r4-migration-{baseline,upgrade,dados-antes,dados-depois,pending}.log`.
