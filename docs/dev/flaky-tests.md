# Testes Flaky Conhecidos

Inventário de testes que falham de forma transiente (timing, ordem de execução, race condition aceito). Cada entrada precisa ter:
- **Por que é flaky** (causa raiz)
- **Como confirmar** (re-run, condição específica)
- **Por que não foi corrigido** (custo vs valor)
- **Quando promover a "corrigir"** (gatilho de cancelamento da tolerância)

Sem este inventário, próxima sessão tropeça no teste e perde 30 min investigando algo já conhecido.

---

## ✅ CORRIGIDO (#3250): `ExecutorPorChaveTests` fixava qual de duas chaves pega a vaga primeiro

Falhou 1 vez no job "Unidade, arquitetura e Inventario" da PR #3247 (run 45286414101, tentativa 1, commit
`8ec22604c`), que não tocava em `.cs`: `O_limite_de_paralelismo_faz_chaves_diferentes_rodarem_uma_por_vez` esperava
`A inicio, A fim, B inicio` e viu `B inicio, A inicio, A fim`.

- **Por que era flaky:** `ExecutorPorChave.ExecutarAsync` começa com `await Task.Yield()`, então A e B, agendados
  juntos, voltam em ordem não garantida e disputam o `SemaphoreSlim(1)`. O contrato só promete ordem dentro da mesma
  chave. A exclusão mútua se manteve; o teste fixava a mais quem começa.
- **Como se confirmou:** o teste antigo falhou 39 vezes em 2000 repetições em sequência no mesmo processo, sempre com a
  mensagem do CI. Com um contexto de teste que roda B antes de A, falhou sempre.
- **Correção (#3250):** B só é agendado com A já na única vaga, sob uma `FilaManual` (contexto de sincronização do
  teste) que leva B até o semáforo na thread do teste antes de liberar A. Com o limite certo, B para no `WaitAsync`; se
  o limite deixasse duas chaves juntas, B rodaria ali e o teste falharia sempre (com `paralelismo: 2`, 200 de 200
  falhas). Sem espera de tempo e sem mudar o `ExecutorPorChave`. O teste novo passou em 2000 de 2000 em sequência,
  2000 de 2000 juntas (`Task.WhenAll`) e sob o contexto que, no teste antigo, rodava B antes de A.
- **Lição:** teste de concorrência não fixa a ordem entre trabalhos que o contrato deixa livres. Quem precisa começar
  primeiro entra antes de o outro existir, e o "não começou" se prova parando o outro num ponto conhecido, não com
  espera de tempo.

---

## ⚠️ TOLERADO (#3221): três provas de navegador do Console estouram 10 s na primeira tentativa do CI

Três falhas em três execuções do job `qualidade` (workflow `console`), em 23/11/2026, cada uma numa prova
diferente e sempre por espera de 10 s vencida. Nenhuma se repetiu: o job passou em seguida sem mudança de código.

| Execução | Commit | Prova e linha | Erro |
|---|---|---|---|
| master, run 45128292422, 1ª tentativa | `6b76d58d3` | `prova-2853-painel-assistente-navegador:116` | `locator.click` no botão Subir, 10 s |
| master, run 45135404512, 1ª tentativa | `a31803667` | `prova-2855-assistente-na-conversa-navegador:174` | `waitFor` detached, 10 s |
| PR 3222, run 45140513161 | `da36629c3` | `prova-1743-tamanhos-navegador:124` | `waitFor` visible da linha "Anotação para ... 1 kg", 10 s |

- **Por que é flaky:** causa não provada. O que se mediu: as três passam localmente na mesma árvore; a 2853 e a
  2855 passam com a CPU do navegador limitada em 1x, 4x, 8x e 12x (`Emulation.setCPUThrottlingRate`) e na suíte
  inteira local (425 ok); a 1743 passa com 5x (98 s) e, com 10x, não chega à linha 124: o executor a encerra no
  limite de 180 s, que é outra falha. Hipótese ainda sem prova para a 2853: `CanvasDoFluxo.jsx` reenquadra o canvas
  150 ms depois de abrir ou fechar o grupo e o clique pega o nó em movimento. Na 1743 a espera da linha 124 é a
  correção da #2982 (logo abaixo): se ela vence, a linha de 1 kg não apareceu em 10 s depois do clique em "Somar".
- **O que não é:** não é a corrida da `prova-1738` com a sugestão do Assistente, corrigida na #3223; aquela falhava
  por asserção e reproduz com CPU 10x. Também não há evidência de que as provas visuais novas saturem o Vite.
- **Como confirmar:** a mesma prova passa na execução seguinte do job no mesmo commit, sem mudança.
- **Por que não foi corrigido:** sem reprodução não há Red. A 2853 e a 2855 gravam `falha.png` em
  `EasyStock.Console/.build/`, mas o workflow `console` não publica artefato, então a tela da falha se perde.
- **Quando promover a "corrigir":** na próxima falha de qualquer uma das três. Antes disso, o passo que destrava
  é publicar `EasyStock.Console/.build/` como artefato do job `qualidade` quando ele falha (mudança de workflow,
  tier alto) e fazer a 1743 gravar screenshot na falha, como as outras duas.

---

## ✅ CORRIGIDO (#3223, registrado na #3221): `prova-1738-pedido-navegador` contava a sugestão do Assistente como escrita

Falhou 1 vez no workflow `console` do master em `a31803667` (23/11/2026), linha 103: a asserção "Montar e
recarregar não envia pedido nem inicia conversa na API" viu uma chamada que não era GET nem `marcar-lida`.

- **Por que era flaky:** a sugestão proativa do Assistente (`src/aplicacao/api/agente.js`) faz
  `POST /api/atendimento/conversas/{id}/sugestao` sozinha, 1,2 s depois de a conversa parar. Em máquina lenta a
  espera vencia no meio da prova.
- **Como se confirmou:** CPU do navegador 10x mais lenta: 5 de 5 falhas, a asserção listando só essa rota. Com a
  preferência do complemento desligada: 2 de 2 verdes.
- **Correção (#3223):** a prova roda com a preferência do complemento desligada e a asserção, sem afrouxar, diz
  método e rota do que viu.
- **Lição:** prova que proíbe escrita na API precisa desligar o que o Console dispara por temporizador.

---

## ✅ CORRIGIDO (#2982, registrado na #2973): `prova-1743-tamanhos-navegador` conta a linha de 1 kg antes de ela aparecer

Falhou 1 vez no workflow `console` do master em `925309989` (20/11/2026, 18:08Z), linha 127:
`Retirar 800 g preserva 1 kg`, esperado 1 e veio 0. A asserção de 600 g, logo antes, passou. O rerun do mesmo
SHA (attempt 2, 18:28Z) passou sem mudança nenhuma; a mesma prova também passou na PR #2963.

- **Por que é flaky:** depois de clicar "Somar Lasanha Bolonhesa Clássica 1 kg no pedido", a prova não espera a
  linha nova. Em seguida tira uma unidade de 800 g e conta "Anotação para ... 1 kg" com `.count()`, que lê o DOM
  na hora e não espera. Se a comanda ainda não renderizou a linha de 1 kg, a contagem dá 0. A linha de 600 g
  entrou antes do arrasto e por isso não sofre o mesmo atraso.
- **Como confirmar:** rodar de novo o job `console` no mesmo SHA. Passar no rerun sem mudança é o sinal.
- **Por que não foi corrigido:** a issue pediu só o registro. A correção é uma linha:
  `await page.getByRole('button', { name: 'Anotação para Lasanha Bolonhesa Clássica 1 kg', exact: true }).waitFor()`
  logo depois do clique de 1 kg.
- **Quando promover a "corrigir":** na segunda falha com a mesma mensagem, ou antes, se alguém mexer nessa prova.
- **Correção (#2982):** o `waitFor()` acima entrou logo depois do clique de 1 kg.

---

## ✅ CORRIGIDO (#2871): `prova-2395-ficha-navegador` lia o botão antes da troca de pessoa

Falhou no CI da PR #2874 em 19/11/2026 (17:27Z, linha 52, `true !== false`) e 1 vez em 21 rodadas locais. A prova
chamava `window.trocarPessoa()` e lia `isEnabled()` de "Manter texto para esta pessoa" logo em seguida; o `setState`
ainda não tinha sido aplicado e o botão às vezes seguia habilitado. Agora a prova espera o botão desabilitado
(`getByRole(..., { disabled: true }).waitFor()`) antes de afirmar.

---

## ✅ CORRIGIDO (#2770): provas do Caixa do dia falhavam nos primeiros minutos depois da meia-noite de São Paulo

Visto em `EasyStock.Infra.Postgre.IntegrationTests/Workflows/CaixaDoDiaNumerosIntegrationTests.cs`, em 16/11/2026
às 03:02:20Z (00:02 em São Paulo), em duas PRs sem relação com o caixa. Três provas falharam:
`Dia_fechado_mostra_o_fechamento_e_o_que_entrou_depois_fica_em_pos_fechamento` ("Expected caixa.PosFechamento to
be 80M ... but found 0.00M"), `Lancamento_gravado_depois_do_fechamento_some_do_retrato_e_o_estorno_diz_o_motivo_certo`
("Expected caixa.PosFechamento to be 30M, but found 0.00M") e
`Linha_de_pagamento_de_pedido_traz_numero_do_dia_cliente_e_quem_registrou` (lista sem a primeira linha).

- **Por que era flaky:** as provas derivavam os instantes de `DateTime.UtcNow` menos 5, 3, 2 ou 1 min e calculavam
  o dia operacional a partir de um deles. Nos primeiros minutos depois das 03:00Z parte dos lançamentos caía no
  dia anterior, e o dia consultado não continha o que o teste semeou. Os use cases usam o relógio real
  (`HorarioBrasil.Hoje()`, `DateTime.UtcNow`), então deslocar tudo para outro horário não resolvia.
- **Como foi confirmado:** `InstantesDoDiaOperacionalTests` com a derivação antiga (o próprio `agora`, sem
  empurrar para dentro do dia) falhou em 3 de 3 para `03:02:20Z`, `02:59:58Z` e `03:00:00Z`, com os lançamentos
  espalhados por 15/11 e 16/11.
- **Fix (#2770):** `InstantesDoDiaOperacional.Ancora` empurra `agora` para dentro do dia operacional dele, com
  folga para o maior recuo e o maior avanço da prova. Todas as provas do arquivo derivam daí, e a ordem e o
  espaçamento entre os instantes não mudam. A prova do helper roda sem banco e sem relógio, com `agora` fixo.
- **Se voltar a falhar:** ver se algum use case novo carimba ou filtra por um relógio que não seja o dia
  operacional de agora, ou se a prova voltou a semear com `DateTime.UtcNow.AddMinutes(-N)` solto. Instante
  semeado depois de `FecharCaixa` precisa ser derivado depois dele, como `Ancora(DateTime.UtcNow, ...)` de novo.
- **Limite conhecido:** as provas com banco só rodaram em horário comum; a virada é provada pelo helper, com
  `agora` fixo. Se a meia-noite real cair entre o semeio e o `FecharCaixa` da mesma prova (milissegundos a poucos
  segundos por dia), o fechamento vale para o dia novo e a prova ainda pode falhar. Isso só some com relógio
  injetável no use case, que é mudança de produção e ficou fora da #2770.
- **Classes vizinhas conferidas (#2769):** `CaixaFechamentoTravaIntegrationTests`,
  `SangriaSimultaneaIntegrationTests` e `MobileCashPromoverIntegrationTests` também semeiam com "agora menos N",
  mas nenhuma prova delas depende de a abertura cair no dia de hoje; não falham na virada.

---

## ✅ CORRIGIDO (#2814): três provas de navegador do Console dependiam de tempo e falhavam com a máquina carregada

Visto em 16/11/2026, em `npm run qualidade` na #2803, com outra medição de navegador na mesma máquina. Sozinhas,
as 3 passaram no mesmo commit.

| Prova | Falha | Causa |
|---|---|---|
| `prova-1728-teste-email-navegador.mjs` | `page.goto` passou de 10 s | o teto de 10 s da prova valia também para abrir a página |
| `prova-1743-tamanhos-navegador.mjs` | `5 !== 4` em "Tentativa bloqueada não escreve na API" | o POST `/sugestao`, que o aplicativo manda sozinho ao abrir a conversa, chegou depois da contagem |
| `prova-hub-navegador.mjs` | `waitFor` do `data-estado="carregando"` passou de 30 s | a leitura demorava 900 ms de relógio real; com a máquina lenta, acabava antes de a prova olhar |

- **Como foi confirmado:** com 8 processos ocupando CPU numa máquina de 12, a 1728 falhou de novo no `goto` de
  10 s e a 1743 falhou no próprio `goto`. As outras duas causas saem da mensagem da falha e do código: a da 1743
  nomeia o POST `/sugestao` a mais, e a do hub espera um estado que dura 900 ms.
- **Fix (#2814):** o hub segura a leitura num portão que a prova solta depois de conferir o "Carregando…" (três
  pontos da prova usavam os 900 ms). A 1743 conta só as escritas que mexem no pedido; `/marcar-lida` e `/sugestao`
  já eram aceitas pela asserção do fim da prova, mas a contagem de escritas não as ignorava. A 1728 e a 1743 dão
  60 s só à navegação; as esperas da tela seguem em 10 s. O hub não mexeu na navegação.
- **Se voltar a falhar:** `goto` acima de 60 s é máquina sem CPU, não a prova. No hub, ver se alguma leitura
  nova ficou fora do portão. Na 1743, ver se o aplicativo passou a mandar outra escrita ao abrir a conversa.
- **Limite conhecido:** com o portão fechado, a leitura presa conta os 25 s de `AbortSignal.timeout` do
  `usePulsoDoHall.js`, que o relógio falso da prova não controla. Se a prova levar mais de 25 s entre fechar e
  soltar o portão, o cartão vira "erro" em vez de "carregando". A entrada do hub mais abaixo (arrasto por toque) é
  outro ponto e segue tolerada. As provas da entrada #2809 não foram mexidas.

---

## ⚠️ TOLERADO (#2809): provas de navegador do Console estouram o tempo no gate local carregado

Visto em 16/11/2026, no head `777ffef10` da PR #2806, em duas rodadas completas de `npm run qualidade` com outras
sessões usando a máquina. Cada rodada falhou em uma prova diferente, sempre por timeout do Playwright:

| Rodada | Prova | Falha |
|---|---|---|
| 1 | `prova-1847-nota-resumo.mjs` | `click` em "Abrir Atendimento" passou de 5 s |
| 1 | `prova-2478-pedidos-navegador.mjs` | `waitFor` da navegação "Telas de Cadastros" passou de 12 s |
| 2 | `prova-1685-sinal-navegador.mjs` | `page.goto` passou de 8 s |

- **Por que é flaky:** causa provável, não medida. O runner roda as provas em paralelo, e cada prova de navegador
  abre seu Chromium (a 1847 sobe também seu próprio Vite). Com a CPU disputada, o `goto` e o primeiro render
  passam dos timeouts fixos de 5 a 12 s. Isolada, a 1847 levou 68 s.
- **Como confirmar:** `node ferramentas/rodar-provas.mjs <nome>` no mesmo commit. As 3 passaram isoladas, e cada
  uma passou na outra rodada completa. O CI do mesmo head ficou verde (job `qualidade`).
- **Por que não foi corrigido:** só aparece com a máquina carregada por fora. Subir timeout sem medir esconderia
  lentidão real da tela.
- **Quando promover a "corrigir":** se a mesma prova falhar em duas rodadas completas seguidas, ou se aparecer no CI.

---

## ⚠️ TOLERADO (#2697): impresso entrega/a5 falhou uma vez na suíte local carregada

Visto em `EasyStock.Api.UnitTests/Services/Impressao/PedidoImpressoHtmlTests.cs:107`,
`TodoDocumentoSaiEmTodoPapelComONumeroEOQr(documento: "entrega", modelo: "a5")`, em 15/11/2026. A suíte
local deu 2177/1 com os testes de integração Postgres rodando em paralelo na mesma máquina. A mensagem da
falha não foi guardada. Isolado, o teste passou 3 de 3.

- **Por que é flaky:** causa não confirmada. O teste usa dados fixos. A suspeita é o `RenderTimeout` do
  `ScribanRenderer`, que vale 500 ms fora do CI (o CI usa 2 s, ver #910 abaixo). Com
  `EASYSTOK_SCRIBAN_TIMEOUT_MS=1`, o teste falha com `TimeoutException`, então o mecanismo existe. Duas
  rodadas de carga não reproduziram: 3 e depois 8 suítes completas em paralelo, 0 falhas em 11.
- **Como confirmar:** rodar de novo. Se voltar, guardar a mensagem. `TimeoutException: Renderizacao de
  template excedeu 500ms` confirma a suspeita.
- **Por que não foi corrigido:** apareceu uma vez, só localmente e sob carga externa. Subir o timeout de
  produção ou mexer no teste sem reproduzir seria chute.
- **Quando promover a "corrigir":** na segunda falha, ou se aparecer no CI (lá o timeout já é 2 s).

---

## ⚠️ TOLERADO: prova do hub não vê a troca de ordem depois do arrasto por toque

Visto em `EasyStock.Console/ferramentas/prova-hub-navegador.mjs:161`, no job `qualidade` da PR #2616
(run 43667676409, head `276a3b392`). A tentativa 1 falhou com `page.waitForFunction: Timeout 30000ms
exceeded`, logo depois do log "Produção movido para a posição 2 de 6". O rerun do mesmo job no mesmo SHA
passou, e localmente a prova passou 4 de 4 vezes nesse head. A PR não toca `features/hall`.

- **Por que é flaky:** causa não confirmada. A prova usa `page.clock` falso, e o arrasto por toque
  depende de timers e quadros. A suspeita é que, com o runner carregado, a reordenação só aplique depois
  de um `runFor` que vem depois da espera da linha 161.
- **Como confirmar:** rerun do job no mesmo SHA. Para reproduzir, rodar a prova com CPU limitada
  (`cdp.send('Emulation.setCPUThrottlingRate', { rate: 6 })`) e ver se a espera expira.
- **Por que não foi corrigido:** apareceu uma vez, numa PR fora do hall. Corrigir sem reproduzir
  seria chute.
- **Quando promover a "corrigir":** na segunda falha em qualquer branch, ou se reproduzir com CPU
  limitada.

---

## ✅ CORRIGIDO (#2357, #2010): stream do chat do site media relógio de parede abaixo de 2 s

Visto em `ChatSiteControllerTests.RespostaDaLojaChegaPeloStreamEmMenosDeDoisSegundos`: 2,43 s, 2,74 s e 3,25 s
em PRs de 23/10 (#1929, #1931), 2,05 s na #2310 e 2,275 s no master `d3c31ea72` (run 43175092348). Todas
passaram no rerun do mesmo SHA, em PRs que não tocam o chat.

- **Por que era flaky:** o stream lê a cada `IntervaloStream` (1 s) e o teste exigia a resposta em menos de 2 s
  de parede. Com o runner carregado, uma leitura lenta somada ao ciclo passava do limite.
- **Como foi confirmado:** com a leitura que acha a resposta atrasada em 1,5 s, o teste antigo falhava em 1 de 1.
- **Fix:** `RespostaDaLojaSaiNoStreamNaLeituraQueAEncontra` afirma a ordem, não o tempo: a resposta sai no
  stream antes da leitura seguinte, e `IntervaloStream` fica em até 1 s. O atraso de 1,5 s ficou no teste, então
  quem voltar a medir parede vê a falha sempre. 20 de 20 verdes localmente.
- **Se voltar a falhar:** não é mais carga do runner. Ver se o controller passou a escrever a mensagem depois
  da leitura seguinte ou se o intervalo subiu.

---

## ✅ CORRIGIDO (#2433): prova do Financeiro contava o Sair da tela de carregamento

Visto em `EasyStock.Console/ferramentas/prova-financeiro-navegador.mjs:69`, no job `console` do master em
`7edefc785` (run 43349117808). A tentativa 1 falhou com `1 !== 0` e a 2 passou com o mesmo SHA.

- **Por que era flaky:** a prova passou a rodar no CI com a #2410. Logo depois do `page.reload()`, o App
  mostra "Carregando seus módulos / Aguarde um instante.", e essa tela tem um botão Sair próprio
  (`App.jsx`, `if (!dados)`). Depois do segundo reload, a prova só esperava textos sumirem. Esses textos
  também não existem na tela de carregamento, e a `conferirSessao` contava Sair solto antes de a
  moldura montar. O log da falha mostra "Aguarde um instante.".
- **Como foi confirmado:** com `/api/auth/me/modulos` atrasado em 1,5 s, a prova falhava em 1 de 1, na
  mesma linha e com o mesmo `1 !== 0`.
- **Fix (#2433):**
  - A `conferirSessao` espera o avatar da conta à vista antes de contar. A tela de carregamento e a
    moldura nunca aparecem juntas, e com a tela pronta o Sair solto continua proibido.
  - O atraso de 1,5 s ficou na prova, no segundo reload. Assim, quem tirar a espera vê a falha sempre,
    não de vez em quando.
- **Se voltar a falhar:** não é mais a carga dos módulos. Ver qual tela tinha o Sair na captura.

---

## ✅ CORRIGIDO (#1680) — teste do canário de telemetria dependia do timeout de produção

Visto em `TelemetriaGeocodingTests.Sem_a_protecao_o_canario_vaza_no_span_e_no_log` (os três testes da
classe tinham a mesma exposição).

- **Por que era flaky:** o teste (I8.2, #1661) usa os clientes reais de CEP e de geocodificação contra
  um servidor local, e eles vinham com o timeout de produção (1 s no ViaCEP, 2 s no Nominatim). No run
  38318901505 (master `8831b2a9`) a primeira chamada, fria, passou de 1 s; o cliente do ViaCEP devolveu
  nulo, como manda o contrato dele, e o teste caiu em `NullReferenceException` na linha 93, com duração
  de 1 s. Já tinha falhado uma vez na máquina de dev antes do merge e foi mal diagnosticado como
  contagem de spans.
- **Como foi confirmado:** com o servidor local respondendo em 1,5 s, os três testes falhavam em 3 de 3.
- **Fix (#1680):** `PrazoLargoSoNoTeste` sobrepõe o timeout dos `HttpClient` só dentro do teste, e o
  servidor local passou a demorar 1,2 s de propósito. Assim o teste é determinístico nos dois sentidos:
  quem tirar o prazo largo vê a falha sempre, não de vez em quando.
- **Se voltar a falhar:** não é mais prazo. Olhar o que foi para span ou log.

---
## ✅ CORRIGIDO (#1822) — OTP em rajada e trava de outro host nos jobs

Vistos em PRs só de console, em 14/10/2026, e passando no master minutos antes.

- **`LoginTentativasConcorrenciaTests.RajadaDeCodigosCertosConsomeOOtpUmaVezSo`** (run 38642604450, PR #1815):
  uma das quatro requisições com o código certo recebeu `OtpTentativasExcedidasException`. **Era bug do código**,
  não do teste: a requisição que leu o OTP antes do consumo da outra tinha a reserva de tentativa recusada pelo
  `NOT Consumido` do WHERE, e o caso de uso tratava toda reserva recusada como tentativas esgotadas. Fix: reserva
  recusada com o OTP já consumido (lido sem rastreamento, `ConsumidoAsync`) responde `OtpInvalidoException`.
  Teste unitário determinístico: `ExecuteAsync_ReservaRecusadaPorqueOutraRequisicaoConsumiu_LancaOtpInvalido`.
- **`JobsEmDoisHostsIntegrationTests.*_ComATravaDoOutroHost_PulaARodada`** (run 38643283929, PR #1818): a rodada
  com a trava livre não registrou nada. **Era corrida do teste**: `DeterTravaAsync` soltava a trava fechando a
  conexão sem pool, e fechar não espera o backend do PostgreSQL sair; o `pg_try_advisory_lock` da rodada seguinte
  ainda achava a trava presa e pulava. Fix: `pg_advisory_unlock` explícito antes de fechar (`TravaDeOutroHost`).
- **Medição:** no master, 19 de 19 verdes numa máquina ociosa (a corrida não aparece fora da carga do CI; o OTP foi provado pelo teste unitário). Na branch, 40 de 40 verdes nos quatro testes. No CI a falha do OTP apareceu 3 vezes em 14/10 (#1815, #1818, #1821).
- **Se voltar a falhar:** no OTP, olhar quem mais recusa a reserva; nos jobs, conferir se outra conexão do
  mesmo teste segura a chave.

---
## ✅ CORRIGIDO (#1117) — gate de worktrees paralelos compartilhava a pasta de build

Visto em `MigrationDesignerHygieneTests` (qualquer arch-test pode ser a vitima).

- **Por que era flaky:** `gate.ps1` e `build-check.ps1` (ADR-0040/ADR-0029) compilavam com
  `-o %TEMP%\easystok-build-check`, pasta unica para todos os worktrees da maquina. Em 2026-09-29, com
  sessoes paralelas, o pre-commit de `chat-site-1097` falhou com `ReflectionTypeLoadException`
  (`ISessaoChatSiteRepository`/`SessaoChatSite` nao encontrados) e o stack trace apontava para
  `gate-zero-arch-tests`: o build do outro worktree sobrescreveu a saida entre o build e o robocopy.
  Reexecutar passava. Corrida, nao regressao.
- **Fix (#1117):** `scripts/poka-yoke/build-out-dir.ps1` deriva a pasta de um hash do caminho do repo:
  `%TEMP%\easystok-build-check-<hash8>`. Estavel no mesmo worktree (incremental preservado), distinta
  entre worktrees. As pastas antigas/orfas em `%TEMP%` podem ser apagadas a mao sem risco.
- **Se voltar a falhar:** confira se o stack trace aponta para outro caminho de repo; se sim, alguem
  reintroduziu pasta de saida fixa.

---

## ✅ CORRIGIDO (#910) — timeout 500ms do ScribanRenderer no CI

Afetava dois testes, mesma causa: `ScribanRendererTests.Templates_diferentes_geram_resultados_independentes`
e `EmailTemplateRenderSmokeTests.Template_de_email_renderiza_sem_erro_de_sintaxe`.

- **Por que era flaky:** os testes exercitam o `ScribanRenderer`, que impunha `RenderTimeout = 500ms`
  hardcoded. Em runner do CI (primeiro run pós-JIT, GC, testes em paralelo) a primeira renderização
  passava de 500ms e disparava `TimeoutException : Renderizacao de template excedeu 500ms`. Visto no
  run 28685566400 (triagem #822) e depois **2/2 consecutivos, sem carga concorrente**, no run
  29111058019 (master pós-#909) — o gatilho ">5%/sprint" deste próprio doc, cruzado.
- **Fix (#910):** `ScribanRenderer.RenderTimeout` passou a ler `EASYSTOK_SCRIBAN_TIMEOUT_MS` (fallback
  500ms); `ci.yml` seta `2000` no step de teste. **Prod e dev local seguem em 500ms** — a guarda de
  sanidade não foi diluída, só o piso do runner lento do CI.
- **Se voltar a falhar mesmo com 2s:** aí é renderização de fato lenta (regressão), não timing —
  investigar o template, não subir mais o teto.

---

## ⚠️ AMBIENTE (#1110) — Smart App Control bloqueia a DLL dos arch-tests no gate

Não é teste flaky, é bloqueio da máquina local. Fica aqui porque aparece como "gate estranho" no pre-commit.

- **Sintoma:** em 2026-09-29 o Windows Smart App Control bloqueou
  `.build\arch-gate\EasyStock.ArchitectureTests.dll` ("Uma política de Controle de Aplicativo bloqueou
  este arquivo", `0x800711C7`). O vstest pulou o assembly, disse "Nenhum teste corresponde ao filtro
  `Category=Architecture`" e **saiu 0**. O `gate.ps1` confiava só no exit code e dava VERDE falso.
- **Fix (#1110):** o `gate.ps1` grava TRX em `.build\arch-gate-results\arch.trx` e exige
  `Counters/@executed > 0`. Zero testes executados agora é VERMELHO com **exit 4**.
- **Se o gate der exit 4:** confira a mensagem do vstest. Com `0x800711C7`, o bloqueio é do Smart App
  Control (Segurança do Windows > Controle de aplicativos e navegador); rode de novo ou valide pelo CI.
  **Não** contorne com `--no-verify` nem afrouxe a checagem do TRX.

---

## Política geral

- **Não marcar teste como flaky sem documentar aqui.** Sem entrada neste arquivo + comment no próprio teste, o "sabe-se que é flaky" não é compartilhável (e some quando a memória do dev some).
- **`[Trait("Category","Flaky")]`** não é usado intencionalmente — flaky deve ser visível em qualquer run, não escondido em categoria opcional.
- **Re-run automático em CI:** não há. Teste desta lista que voltar a falhar é corrigido, não repetido.
