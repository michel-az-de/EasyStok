# Fila pelo teclado e atalhos do Atendimento (#3358)

Spec S do console desktop, tela `#/m/atendimento/balcao`. Vem do plano de usabilidade de 25/11/2026 (uma revisão em duas rodadas, 35 achados F01 a F35; o plano e os anexos ficam em `docs/dev/` sem versionar, então o que importa para as PRs está repetido aqui). Esta spec cobre os achados da fila e do teclado (F01 a F05, F10, F29), o aviso que chega (F24, F27) e registra o modo compacto (F17). Base: `master` d93dfbfbc. Medição de partida em modo API: [medicao-modo-api.md](medicao-modo-api.md).

Cada item S abaixo é uma PR. PR que implementa um S-número desta spec, dentro do escopo dele, é tier baixo pela ADR-0055 e entra por squash com CI e revisão verdes. Migração, autenticação, RLS, policy e processo continuam tier alto; nenhum S abaixo toca neles.

## 1. Decisões de base

| # | Decisão | Quem e quando |
|---|---|---|
| D1 | **Ctrl+Enter envia e abre a próxima conversa "Precisa de você"**, com o foco no campo. Enter só envia; Shift+Enter quebra linha | Felipe, 25/11 10:02 |
| D2 | **Modal "Enviar e assumir?" fica**, com o foco no botão de confirmar (`data-foco-inicial`; já entregue na PR 1, #3317, e coberto pela `prova-3317`) | Felipe, 25/11 17:44 |
| D3 | **A fila ordena só pelo início da espera**, sem score de valor ou prazo de entrega. Fecha a #2530 | Felipe, 25/11 17:44 |
| D4 | **Modo compacto da lista automático abaixo de 1400 px**, sem preferência da atendente | Felipe, 25/11 17:44 |
| D8 | **Ctrl+Enter com o campo vazio assume a conversa** | Felipe, 25/11 17:44 |
| D9 | Nome acessível do cartão fica curto (exceção em `docs/dev/excecoes-acessibilidade-console.md`) | Felipe, 25/11 14:19 |

Fonte das datas: respostas do Felipe na sessão de 25/11 (D1 em mensagem às 10:02; D2, D3, D4 e D8 em "ok nas recomendadas" às 17:44; D9 às 14:19), gravadas na seção 9 do plano.

Sem resposta ainda, fora desta spec: **D5** "Conferir pagamento" como botão primário (dinheiro, F09), **D6** rótulos novos (F11) e **D7** Enviar sempre à direita (F13). Eles travam as PRs 7 e 3 do plano, não os itens abaixo.

## 2. Medida de partida

Toda meta é projeção até a PR medir. T1, T3, T10 e T13 são as tarefas do plano. "Demonstração" é o console sem API, aba "Precisa de você" com 11 cartões; "API" é a medição desta spec, aba "Todas" com 35.

| Tarefa | Hoje, só teclado | Meta | Item |
|---|---|---|---|
| T1/T13 abrir a próxima da fila | 16 teclas na demonstração, 13 na API (11 Shift+Tab, 1 Tab, 1 Enter) | 3 teclas pela lista (Esc, J, Enter); 1 quando está respondendo (Ctrl+Enter, que vai ao topo da fila) | S1, S2, S3 |
| T3 assumir do Assistente | 5 teclas | 1 (Ctrl+Enter, campo vazio) | S5 |
| T10 buscar uma conversa | 24 teclas na demonstração (19 de Shift+Tab até a busca); na API, 43 Shift+Tab do campo até "Buscar conversa" (só a subida, não o total) | 3 a partir do campo (Esc, `/`, Enter) e 2 a partir da lista, sem contar o texto digitado | S6 |
| Paradas de Tab do início do documento até o campo | 35 na demonstração, 56 na API | tantas quanto as de hoje menos (cartões - 1): a lista vira 1 parada. Com 35 cartões, 56 caem para 22 | S1 |
| Cartões que se deslocam quando a atendente responde | 2 saltos na demonstração: o cartão respondido desce 365 px e cada um dos 6 de baixo sobe 93,2 px | 0 px até trocar de conversa | S4 |

O Ctrl+K já existe no modo API e abre a Busca na Casa (pessoas, conversas, pedidos, produtos e telas; escolher uma conversa a abre, `BuscaNaCasa.jsx`). A medição não viu resultado de conversa porque a rede simulada devolveu lista vazia, então a abertura por Ctrl+K em modo API fica por medir na PR 6. Na demonstração ele não monta de propósito (`App.jsx`).

## 3. Modelo e mapa de teclas

Duas zonas, uma tecla para trocar. **Lista** (foco num cartão): setas e letras. **Campo** (foco na mensagem): só Enter, Ctrl+Enter, `/`. Esc vai do campo para o cartão da conversa aberta; Enter vai do cartão para o campo (já é assim hoje).

Regra de letra (WCAG 2.1.4, atalho de caractere): letra e `?` só valem **com o foco dentro da lista**. Fora dela, nenhuma tecla de caractere é interceptada. Combinação com Ctrl vale onde está documentada. Toda tecla nova entra na folha `?` e no `title` do botão que ela aciona (S7).

| Zona | Tecla | Ação | Estado |
|---|---|---|---|
| Lista | ↑ ↓ ou `k` `j` | Cartão anterior ou seguinte; a lista é 1 parada de Tab | novo (S1) |
| Lista | Home, End | Primeiro e último cartão | novo (S1) |
| Lista | Enter | Abre a conversa, foco no campo | já existe |
| Lista | `/` | Foca "Buscar conversa" | novo (S6) |
| Lista | `?` | Abre a folha de atalhos | novo (S7) |
| Lista | `a`, `n`, `d` | Assumir, Nota, Devolver ao Assistente na conversa aberta | **proposta** (S6b), Felipe confirma |
| Campo | Enter | Envia | já existe |
| Campo | Shift+Enter | Quebra linha | já existe |
| Campo | **Ctrl+Enter** com texto | Envia e abre a próxima "Precisa de você" | novo (S3, D1) |
| Campo | **Ctrl+Enter** vazio | Assume a conversa, se o Assistente atende | novo (S5, D8) |
| Campo | Esc | Devolve o foco ao cartão da conversa aberta; o rascunho fica | novo (S2) |
| Campo | `/` | Abre as respostas prontas | já existe |
| Modal, menu, popover, sugestão de texto, barra de respostas | Esc, Enter | Fecha e confirma só o que está aberto; têm precedência sobre S2 e S3 (no campo, `barra.aoTeclar` e `sugestao.aoTeclar` já rodam antes do Enter) | já existe |
| Qualquer | Ctrl+K | Busca na Casa (só no modo API) | já existe |

Conflito com o navegador: Ctrl+Enter, setas, J, K, A, N, D, `/` e `?` não constam da lista de atalhos do Chrome e do Edge dentro da página (a PR 5 confere no Chromium). No Firefox, `/` abre a busca rápida quando o foco não está num campo: o handler chama `preventDefault`, e Firefox segue fora do alvo (plano, Lacunas). Ficam de fora de propósito Alt+Seta, Alt+F, Alt+E, Alt+D, Ctrl+T, Ctrl+W, Ctrl+N, Ctrl+L, Ctrl+Shift+N.

## 4. Itens

Cada item traz a prova que falha primeiro (Red), em `ferramentas/prova-<issue>-*.mjs`, rodando no Chromium em modo API com rede simulada e na demonstração. A primeira ação da PR 5 é medir a fila "Precisa de você" com motivo, que a medição desta spec não cobriu.

| S | PR | Achado | O que muda | Red |
|---|---|---|---|---|
| S1 | 5 | F01 | A lista de cartões é uma parada de Tab (foco itinerante: o cartão aberto, ou o primeiro). ↑ ↓, `j` `k`, Home e End movem o foco; o atalho de letra só vale com o foco na lista. Nome acessível do cartão fica como está (D9). **Leitor de tela:** os cartões são `button` em `role="list"` e NVDA e JAWS, em modo navegação, consomem setas e letras (`k`, `n`, `d`). Por isso nenhuma ação existe só por letra (Tab e botão seguem como caminho) e o aceite da PR 5 pede teste manual com NVDA ou o registro de que não foi feito. `listbox`/`option` com `aria-selected` mudaria a #2232 e o nome acessível (D9): fora desta spec | Tab do início ao campo cai de 56 para 22 (API, 35 cartões); ↓ muda o foco; `j` com o foco no cartão move e `j` digitado no campo continua escrevendo `j` |
| S2 | 5 | F04 (Esc) | Esc no campo devolve o foco ao cartão da conversa aberta, sem apagar o rascunho. Com modal, menu, popover, sugestão de texto ou barra de respostas aberta, Esc continua só fechando o que está aberto | Esc com texto: foco no cartão e `value` intacto; com modal, sugestão de texto ou barra de respostas aberta, Esc só fecha isso e o foco fica onde está |
| S3 | 5 | F02 | Ctrl+Enter com texto, ou com anexo e o campo vazio, envia pelo mesmo caminho do Enter (`enviarComposer`) e, **depois do envio aceito**, abre a conversa do **topo da fila "Precisa de você"** que não seja a atual (fila na ordem da lista, qualquer que seja a aba exibida), com o foco no campo. "Aceito" = `enviarComposer` passou as guardas (variável sobrando, limite, confirmação), assumiu e chamou `aoEnviar` ou `enviarMidia`; hoje a função não devolve resultado, e a PR passa a devolver `true` nesses casos e `false` nos outros. Falha do servidor depois disso segue o tratamento atual do balão e não reabre a conversa. Sem outra conversa na fila, fica onde está. Com o modal "Enviar e assumir?" (1ª vez da sessão), Ctrl+Enter abre o modal e o Enter que confirma envia **e avança**: `confirmarEnvioAssumindo` hoje chama `enviarComposer(true)` e perde a intenção, que passa a morar no estado da confirmação | Do campo, 1 combinação leva à próxima da fila; Ctrl+Enter, modal, Enter envia e avança; envio recusado (limite, variável, guarda) não avança; Enter continua só enviando |
| S4 | 5 | F29 | Ao responder, a conversa aberta mantém o cartão no lugar, atenuado quando sai de "Precisa de você", até a atendente trocar de conversa. Só então a lista reordena, num quadro. "Atenuado" usa um token existente, sem `transform`, e o estado também sai em texto no nome acessível do cartão (texto a definir na PR; D9 vale para o resto). A regra "ordem congelada com o ponteiro ou o foco na lista" (`PainelConversas.jsx`) continua e pesa em S1 e S3: com o foco no cartão a ordem já fica parada | Deslocamento dos cartões abaixo = 0 px entre responder e trocar; hoje o respondido desce 365 px e cada um dos 6 de baixo sobe 93,2 px |
| S5 | 6 | F05 (Assumir) | Ctrl+Enter com o campo vazio e o Assistente atendendo executa o mesmo que o botão "Assumir conversa", com o foco no campo. Campo vazio em conversa já assumida, ou de outra atendente (nesta o botão pede "Assumir esta conversa?" e a tecla não decide por ela): nada acontece | T3 em 1 combinação; sem POST de envio; conversa já assumida ou de outra atendente não gera chamada |
| S6 | 6 | F03 | `/` com o foco na lista foca "Buscar conversa"; Esc na busca volta ao cartão. Limpar a busca volta à aba em que a atendente estava (hoje buscar troca para "Todas" e a aba não volta) | T10 em 3 teclas a partir do campo (Esc, `/`, Enter); aba anterior restaurada; Ctrl+K em modo API medido com resposta de busca simulada |
| S6b | 6 | F05 | **Proposta, sem aceite: não entra em PR até o Felipe confirmar.** `a`, `n`, `d` na lista (Assumir, Nota, Devolver ao Assistente). Programar e Passar não têm tecla: não rodam na demonstração (T8 e T11) e entram quando houver medida. Anexar fica fora: é a F14 (colar e soltar, PR 9) e o botão | Cada tecla aciona a mesma ação do botão; no campo, `a`, `n` e `d` continuam digitando |
| S7 | 6 | F04 | `?` com o foco na lista abre a folha de atalhos (a Ajuda existe, `AjudaDoConsole.jsx`, e ganha a seção do Balcão, com `<kbd>`); botão visível "Atalhos" para quem não usa a tecla. A dica do `/` **não volta ao placeholder**: `Composer.jsx` a tirou de lá porque a frase quebrava em três linhas e o botão Respostas já mostra o caminho; a dica vai para o `title` desse botão e para a folha | Folha lista todas as teclas deste mapa; nenhuma tecla do mapa fica fora dela |
| S8 | 8 | F10 | O cartão mostra "Esperando há N min"; a ordem "Urgência" usa o início da espera, não `ultimaEm` (`ordenarBalcao`); "janela fecha em X" aparece antes de fechar. Fecha a #2530 (`refs #2530` até validar em produção). **Fonte do início:** com as mensagens carregadas, a mais antiga das sem resposta real (`mensagensSemRespostaReal`). Só com o resumo da API (`ultimaSemResposta`, `automatico.js`), vale `ultimaEntradaEm`, que é a **última** entrada: a espera mostrada é menor ou igual à real. **Limite:** no modo API a lista não tem as mensagens, então a ordem fica pela última fala do cliente até o servidor expor a primeira sem resposta. Esse campo é mudança de API: vira S8b, tier alto, fora desta spec e só com decisão do Felipe | Com 3 conversas de esperas conhecidas (mensagens carregadas), a ordem é a da espera; cartão traz o tempo; só com o resumo da API, ordena e mostra por `ultimaEntradaEm`; sem mensagem pendente, por `ultimaEm` |
| S9 | 2 | F24, F27 | Notificação do sistema também com a aba visível e sem foco; o clique abre a conversa. Erro do painel da conversa ganha "Tentar de novo", como a lista; a falha da ficha do cliente deixa de cair calada e mostra o mesmo botão | Notificação com `document.hasFocus() === false` e aba visível; clique chama a abertura; erro do painel e da ficha mostram o botão |
| S10 | 10 | F17 | Só registro: lista compacta automática abaixo de 1400 px (D4). Espera o B1 (C6, largura de leitura) da #3265 por mexer nas mesmas larguras de `Moldura.jsx`. Sem PR até lá, e sem Red escrito: **não é item de PR desta spec**; vira S novo, com prova, quando a #3265 liberar | n/a |

## 5. Conflitos (D04) e limites

- `modo-3234` e a B6 da #3265 mexem em `resumoDoModo.js` e `BarraModo.jsx`: nenhum S acima os toca.
- `cartao-b2-3265` (`features/ficha-cliente/`): nenhum S toca. S8 deve conferir o cartão da lista depois que a B2 mergear.
- #3339 e #3355 (frentes ativas em `Composer.jsx` e `PainelAtendimento.jsx`): S2, S3 e S5 entram no `onKeyDown` do campo e no envio. Rebasear no master do dia e comentar na issue de quem estiver aberto.
- Já coberto pela #3265, não recriar: R1 a R5 do cartão do cliente, C5, C6, L1, L2, G1 a G4.
- Não propor `content-visibility` na lista (quebrou a volta à posição, prova 2395) nem alça de redimensionamento de 24 px dentro da área de rolagem.
- A medição de partida é de rede simulada: tempos de servidor e o SSE real não entram. Teclas e paradas de Tab entram.

## 6. Prova e rollback

- Cada PR: Red, Green, `npm run qualidade`, `impeccable detect --json --changed`, axe (wcag2a e wcag2aa) no estado novo, prints antes e depois em 1440 claro, 1920 escuro e 1280. Provas vizinhas que não podem quebrar: 1442, 2232 (5 arquivos), 2395, 3009, 3317, 3330.
- Revisão por subagente sem o contexto da sessão autora, sem Critical.
- Pós-merge: CI do master, `console-deploy.sh`, leitura em produção sem alterar nada, repetindo T1, T3, T10 e T13 com a mesma contagem de teclas.
- Rollback: squash, um commit por PR. Produção: `console-deploy.sh` do sha anterior. Código: `revert` da PR (exige GO).
