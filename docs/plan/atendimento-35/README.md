# Atendimento 35/36: plano de entrega única

> Uma issue, um worktree, uma branch, uma PR, um deploy. Começo, meio e fim descritos aqui antes da primeira linha
> de código. O que não está nesta página não entra na entrega.

**Origem:** crítica impeccable de 2026-11-26 (`.impeccable/critique/2026-11-26T13-54-19Z__…painelatendimento-jsx-a356de51.md`),
demo local do master, tema escuro, 1568 px. Nota **26/36** (H10 n/a). Rodada anterior em produção: 29/40
(`docs/dev/revisao-impeccable-atendimento-2026-11-25-producao-r2.md`).

**Meta:** nota ≥ 35/36 numa crítica nova, feita por subagente sem o contexto de quem implementou, nas 8 condições da
seção 2, antes do merge. Depois do deploy, a mesma crítica em produção.

**Tier:** ALTO, declarado desde o início (banca de 26/11: o plano entra na mesma PR, então não é "spec de plano já
mergeado" da ADR-0055). Segue a ADR-0070: CI verde, `/code-review` sem Critical por subagente sem contexto e aceite da
issue todo marcado, medidos no commit final, antes da label `aprovado`. Só frontend do Console: nada de API, banco,
migração, workflow, `Program.cs` ou `*.csproj`; se algum item exigir um desses, a entrega PARA (seção 4).

**Mudança de abordagem da #3265:** a issue aprovou 6 PRs em série. Com a decisão do Felipe de 26/11 (absorver), vira
uma PR só; isso fica registrado em comentário na #3265 antes do primeiro commit.

---

## 1. Pré-voo: resolver antes de codar

Cada linha precisa de resposta escrita aqui antes de abrir o worktree. Item sem resposta bloqueia o início.

| # | Pergunta | Como fechar | Resposta |
|---|---|---|---|
| P1 | A #3265 (grupos B, C, D, em 6 PRs) e o WIP do B2 em `.claude/worktrees/cartao-b2-3265` (10 arquivos sujos, parado há 47 h) se sobrepõem a esta entrega. Absorver ou esperar? | Decisão do Felipe | **ABSORVER** (Felipe, 26/11 13:03). Mapa na seção 1.1 |
| P2 | Massa da demonstração estável para a prova? | `git log -3 -- EasyStock.Console/src/demonstracao/` sem mudança aberta em outra branch | sim: último commit em 12/11 (#2398), nenhuma branch remota recente mexe nela |
| P3 | Tema claro alcançável na prova? | `data-theme='light'` em `tokens.css:214`: setar na raiz pela prova | sim, medido 26/11 |
| P4 | 1440 e 1920 px medíveis? | Playwright headless com `viewport` fixo | sim: padrão em `prova-3322-largura-navegador.mjs:23`, Playwright 1.62.1. Larguras **1440 e 1920** (as da #3265), mais 1280 só para o cabeçalho (selo) |
| P5 | H5 (prevenção) e H9 (recuperação) com nota medida? | Nota de partida | decidido: não bloqueia o início. A nota de partida sai da linha de base da C1; os itens da C12 já estão definidos sem ela |
| P6 | Aviso passageiro já existe? | `src/componentes/Toast.jsx` existe: reaproveitar, sem lib nova | sim, medido 26/11 |
| P7 | Quais arquivos a entrega toca? | Lista fechada no fim desta seção; arquivo fora dela = parada | lista na seção 1.2 |

### 1.1 Absorção da #3265 (medido em 26/11)

A entrega fecha a #3265 junto (`closes #3265`). Nada dela fica para PR separada.

| Item da #3265 | Estado hoje | Onde entra |
|---|---|---|
| C6 largura de leitura | feito (#3266) | fora |
| B2: R1 fileira de ações, R2 "Itens" secundário | WIP de 24/11 11:56, 10 arquivos, **sem conflito** com o master (master só mudou `ComandaDoCliente.jsx`, que o WIP não toca) | C0 |
| C5 selo do modo no cabeçalho | branch `feat/atendimento-modo-selo-3234` (`87801bc56`), 66 atrás do master, **conflito** em `BarraModo.jsx` e `PainelAtendimento.jsx` (o master mudou a barra na #3306) | C0, junto com a C8 |
| R3 notas recolhidas, R4 lista única de acessos | não feito | C5 |
| R5 Assistente sem bloco duplicado no cartão | não feito | C9 |
| L1 no máximo 1 selo de estado + contagem | não feito | C4 |
| L2 faixa "não entregues" neutra | não feito | C4 |
| G1 4 tamanhos de fonte | não feito | C7 (ver decisão abaixo) |
| G2 cor só para estado que pede ação | não feito | C2 e C3 |
| G3 Enviar e Gravar com borda sólida | Gravar feito (#3360); Enviar conferir na C1 | C3 |
| G4 "Há uma nova versão" sem cobrir a coluna | não feito | C14 |
| D do C4: "Só texto neste canal" como dica; faixa de notificação bloqueada | não feito | C14 |
| D da A2 e A2b: reanúncio do grupo, `aria-expanded`, "Reenviando…", frase de modelo sem Reenviar | não feito | C12 |

**Como absorver sem perder WIP:** o WIP do B2 é preservado com um commit `wip` na própria branch
`feat/atendimento-cartao-acoes-3265` e push, antes de qualquer outra coisa. A branch da entrega nasce do master,
recebe esse commit e o `87801bc56` por merge, e o conflito da barra se resolve com a versão do master como base
(o motivo de "precisa de você" da #3306 continua). O worktree antigo só é removido depois do merge da entrega.

**O selo foi adiado por medida, não por esquecimento** (crítica de 23/11, "Por que a A3 (C5) foi para o grupo B"): selo
e "Assumir" juntos quebram a linha do cabeçalho em 1280 e 1440 com a ficha aberta (de 64 para 77 px). Regra do C0: o
selo só entra se o cabeçalho continuar em uma linha em 1280, 1440 e 1920 com a ficha aberta. Se não couber, o C0 traz
só o domínio (`resumoDoModo.js`) e a forma do selo vai para a C8, com uma das duas saídas que a crítica deixou:
cabeçalho em duas linhas ou o botão do Assistente só com ícone. As provas `prova-1870`, `prova-2185-*` e
`prova-3234-modo-selo` rodam no C0.

**Decisão a confirmar na C7:** a G1 aprovada em 23/11 pedia 12, 14, 16 e 20 px. A escala de tokens do Console começa
em 13 px (`--t-micro`). O plano segue a escala: **13, 14, 16 e 20**. Se o Felipe preferir os 12 px, só a C7 muda.

### 1.2 Arquivos que a entrega pode tocar (fora disto = parada)

- `EasyStock.Console/src/features/atendimento/**`
- `EasyStock.Console/src/features/caixa-de-entrada/**` (fila, cartão da conversa, não entregues)
- `EasyStock.Console/src/features/ficha-cliente/**`
- `EasyStock.Console/src/features/assistente/**` (C9, R5)
- `EasyStock.Console/src/features/notificacoes/SinoDaSessao.jsx` (C14, faixa de notificação bloqueada)
- `EasyStock.Console/src/componentes/AjusteLargura.jsx` (C9), `Botao*` (C3, C10: tecla no botão)
- `EasyStock.Console/src/estilos/tokens.css` (só tokens NOVOS com prefixo `--atd-`; os tokens globais não mudam). Exceção: `--t-atd` (16 px do Atendimento), nome exigido pelo guarda `verificar-gramatica` (`--t-[a-z]+`); no `:root` ela vale `var(--t-corpo)` e só vira 1rem dentro de `.palco` e do painel do Assistente, para não vazar
- `EasyStock.Console/src/app/moldura.module.css` (C2: variáveis do palco `--fundo`, `--papel`, `--preenchimento*`, `--t-atd`; folga da fila sob o botão Simular)
- `EasyStock.Console/src/dominio/janela.js`, `resumoDoModo.js`, `falhaDeEnvio.js` (C12), `acoesDaConversa.js` (C12: `concluir` só depois de assumir), `aviso.js` (C11)
- `EasyStock.Console/src/app/AtualizacaoDoConsole.jsx` (G4), `PilhaDeAvisos.jsx` (C11), `Moldura.jsx` (C2, divisor)
- `EasyStock.Console/src/aplicacao/contextos/**` (C11, só se o aviso novo exigir)
- `EasyStock.Console/src/componentes/Toast.jsx` (só uso; mudança nele = parada)
- `EasyStock.Console/ferramentas/prova-*.mjs` (inclui atualizar `prova-3339-*` no C13), `verificar-tokens.mjs`,
  `rodar-provas.mjs`, `package.json` (script `qualidade`)
- `docs/dev/anexos-atendimento-35/**`, `docs/dev/critica-atendimento-3234-2026-11-23.md`, `docs/plan/atendimento-35/**`
- `changelog.d/<issue>.md`, `changelog.d/3265.md`

---

## 2. Linha de base congelada (primeiro commit)

- Script `ferramentas/prova-atendimento-35.mjs`, no padrão de `rodar-provas.mjs`, abre a demo em **8 condições**:
  claro e escuro × 1440 e 1920 px × (conversa aberta, Assistente aberto).
- **Red sem quebrar o `qualidade`:** `rodar-provas.mjs` roda todo `prova-*.mjs`. Por isso a prova sempre grava a
  medida e só cobra o alvo com `ALVO_35=1`. A C15 tira essa chave e o alvo passa a ser cobrado sempre; essa remoção é
  aceite da entrega.
- Em cada condição, ele mede e grava em JSON: botões laranja cheios visíveis, espaçamentos distintos, fundos distintos,
  tamanhos de fonte, cartões aninhados, contraste < 4,5, alvos < 24 px, borda só à esquerda.
- Mais 16 prints (8 antes, 8 depois), em `docs/dev/anexos-atendimento-35/`.
- Este commit entra com as asserções do alvo **falhando** (Red). A entrega termina quando todas passam (Green).

| Medida | Hoje (26/11, escuro 1568) | Alvo nas 8 condições |
|---|---|---|
| Laranjas cheios ao mesmo tempo | 2 + contorno do composer | 1 |
| Espaçamentos distintos | 19 | só `--e-1..--e-6` |
| Fundos distintos | 13 | 3 superfícies + estados |
| Tamanho mínimo de fonte | 12 px | 13 px |
| Cartão dentro de cartão | 1 | 0 |
| Contraste / alvos / borda esquerda | 0 / 0 / 0 | 0 / 0 / 0 |

**Decisão:** sem comparação de pixel (golden). Imagem gerada no Windows difere da do Linux do CI, e isso cria falha que
não é defeito. A guarda é a medida em número; os prints são evidência para olhar.

---

## 3. Ordem de execução (cada commit com `npm run qualidade` verde)

| Commit | O quê | Heurística | Aceite medível |
|---|---|---|---|
| C0 | Absorção: commit `wip` do B2 preservado, merge do B2 e do selo (`87801bc56`), conflito da barra resolvido | — | `qualidade` verde; provas `prova-3265-cartao-acoes` e `prova-3234-modo-selo` verdes |
| C1 | Prova + linha de base (seção 2) e `ferramentas/verificar-tokens.mjs` no `qualidade` | guarda | prova roda; asserções do alvo falham |
| C2 | 3 superfícies como tokens novos `--atd-*` com `light-dark()`, usados só no Atendimento (os globais `--fundo`, `--papel`, `--elevado` não mudam, para não mexer em caixa, cozinha e gestão); divisor neutro 1px entre colunas | H8 | fundos distintos ≤ alvo; `verificar-contraste` verde |
| C3 | Um laranja por vez: antes de assumir só `Assumir`; depois só `Enviar`; no fim só `Concluir`; composer vazio sem contorno laranja | H8, H4 | laranjas = 1 em todo estado |
| C4 | Fila: contador em `--marca-claro`/`--marca-forte`, no máximo 2 pílulas por linha | H8 | 0 laranja cheio na fila |
| C5 | Ficha: Pedido sai do cartão aninhado e vira linha da lista, com um ritmo só | H8, H4 | aninhados = 0 |
| C6 | Resposta numa cor só (selo "Assistente" distingue); entrada com contraste claro | H4 | 1 fundo de saída |
| C7 | Escala: espaçamentos só por token; rótulos do trilho em 13 px sem quebra | H4, H6 | `verificar-tokens` em erro, passa |
| C8 | Faixa de estado em uma linha, alinhada à grade | H1 | print |
| C9 | Assistente empurra a conversa, nunca cobre `Assumir` (reaproveita o controle de 320 a 560 px) | H3 | `Assumir` visível com painel aberto, 8 condições |
| C10 | `Esc` fecha painel; tecla mostrada no botão (`A` assume), sobre o teclado da #3363 | H7, H3 | prova de teclado |
| C11 | Ao responder, `Toast.jsx` e a faixa mostram que o pedido avançou | H1 | prova |
| C12 | `Concluir` só depois de assumir; não entregue com motivo e Reenviar no balão | H5, H9 | prova |
| C13 | "Janela aberta por 21 h 9 min" vira "Responda até 18h" (`src/dominio/janela.js:69`, a partir de `fimDaJanela`, `janela.js:20`) | H2 | teste de domínio com fuso da loja ≠ fuso do navegador; `prova-3339-*` atualizadas |
| C14 | Varredura de rótulo técnico na tela (a "Bancada de teste" já some no build: fora) | H2 | lista no PR |
| C15 | Tira a chave `ALVO_35`; crítica independente nas 8 condições, no máximo **2 rodadas**, mesmo prompt e rubrica | todas | gate objetivo: prova numérica verde. Meta: ≥ 35/36. Abaixo de 35 na 2ª rodada, os achados viram issue nova e a PR segue com a nota registrada |

---

## 4. Regras durante a execução (o que evita surpresa)

1. **Escopo fechado.** Achado novo fora da seção 3 vira issue separada e não entra. Nada de refatorar ao lado.
2. **Gatilhos de parada.** Para, comenta na issue e espera decisão se:
   - um item exigir API, banco, migração ou workflow;
   - tocar arquivo fora da lista P7;
   - quebrar uma prova existente de outra tela;
   - um commit custar mais que o dobro do estimado.
3. **Sem dependência nova de runtime.** Única dependência nova: `axe-core` em `devDependencies`, só se a C1 mostrar
   que o `verificar-contraste.mjs` não cobre nome acessível e foco. Se não precisar, nenhuma.
4. **Cada commit verde.** A branch fica mergeável em qualquer ponto. Push a cada commit, sem stash.
5. **Comunicação.** Ao fim de cada commit, uma linha na issue #3374 com a medida da prova (número antes → depois).
6. **Revisão por grupo, não só no fim.** A PR é grande. Um subagente revisor sem contexto lê o diff de cada grupo
   (C0-C1 absorção e guarda; C2-C8 visual; C9-C12 fluxo; C13-C14 texto) antes do grupo seguinte. Achado Critical para
   a execução até ser corrigido. O `/code-review` final cobre a PR inteira.

---

## 5. Definição de pronto (tudo marcado antes do merge, salvo o pós-merge)

- [ ] Seção 1 respondida e seção 3 inteira feita.
- [ ] Prova `atendimento-35` verde nas 8 condições.
- [ ] `verificar-tokens` em erro e passando.
- [ ] `npm run qualidade` verde local e no CI.
- [ ] Crítica independente ≥ 35/36, com o relatório em `docs/dev/`.
- [ ] `/code-review` por subagente sem contexto, sem Critical.
- [ ] 16 prints antes e depois na PR.
- [ ] Changelog `changelog.d/<issue>.md`.
- [ ] *(pós-merge)* Deploy por `scripts/deploy/console-deploy.sh`.
- [ ] *(pós-merge)* Crítica em produção ≥ 35/36, no Chrome deste PC, só leitura.
- [ ] *(pós-merge)* Limpeza de worktree e branch; issue fechada.

**Rollback:** um revert do squash devolve a tela inteira ao estado de hoje, e desfaz junto o B2 e o selo absorvidos
(eles não existem no master sem esta PR). Em produção, `console-deploy.sh` com a versão anterior segura até a correção.

---

## Fora da entrega (decidido)

| Item | Por quê |
|---|---|
| stylelint + strict-value | trocado por `verificar-tokens.mjs`, no padrão de `ferramentas/verificar-*.mjs`: mesma regra, zero dependência, já entra no `qualidade` |
| Comparação de pixel (golden) | falha entre Windows e Linux; a guarda é a medida em número |
| react-resizable-panels | o controle de largura próprio já existe; a C9 só muda de "cobrir" para "empurrar" |
| Radix / Base UI / React Spectrum | migração de todos os componentes |
| chatscope chat-ui-kit | parado desde 05/2025 |
| react-virtuoso (mensagens) | componente pago; 13 conversas, virtualizar só acima de ~200 |
| Lost Pixel | repositório arquivado |
| cmdk, motion, sonner | teclado já existe (#3363); animação em CSS; `Toast.jsx` já existe |
| Chatwoot | só estudo de padrão (fila, assumir, notas), sem dependência |

**Estimativa:** 3 a 4 sessões de execução (a absorção da #3265 soma R3, R4, R5, L1, L2, G4 e os itens da A2), mais
a crítica final. A previsão vale só depois do pré-voo fechado.
