# Onda 16 · Respostas do Assistente sem IA (#2879)

Issue #2879. Continua o polimento do fluxo (#2861, #2862, #2863, #2869). Tier alto: o código só começa depois da
label `aprovado` nesta PR. As decisões do fim já foram respondidas.

## Problema

Toda mensagem de texto do cliente vira um turno do modelo, mesmo quando a resposta é sempre a mesma. As palavras do
Felipe: "criar as respostas automáticas com templates pra gente não ter que toda hora gerar respostas e até economizar
token".

Medido em produção (Casa da Baba, 19/11/2026):

| Medida | Valor |
|---|---|
| Entradas de cliente em 45 dias | 182 |
| Entradas que são só saudação ("oi", "bom dia") | 24 (13%) |
| Dessas, as que viraram turno do modelo | 7 (9% das 76 respostas do Assistente no período) |
| Custo de um turno | cerca de 15 mil tokens (59,3 mil em 4 turnos, aba Uso) |
| Resposta do modelo a um "oi" | cerca de 2 s, depois da saudação que já saiu sem IA |
| Agradecimento | NÃO MEDIDO (a consulta de 19/11 falhou no acesso ao banco, causa não apurada) |
| Pedido de cardápio | cerca de 10, contagem frouxa em amostra com mensagens de teste |

O que muda para quem usa:

| Situação | Hoje | Com esta onda | Você percebe |
|---|---|---|---|
| Cliente novo manda só "Oi" | Sai a saudação com "já te respondo" e, 2 s depois, outra resposta do modelo | Sai uma mensagem só, a saudação, terminando em pergunta | Uma mensagem em vez de duas; 0 token |
| Cliente antigo volta com "Bom dia" depois de horas | Turno do modelo | Texto pronto da loja com o nome do cliente | Resposta na hora; 0 token |
| Cliente fecha com "Obrigada" | Turno do modelo para dizer "por nada" | Texto pronto da loja | 0 token |
| Cliente manda só "Cardápio" | Turno do modelo, que chama ferramenta e escreve | Texto pronto com o link | 0 token (fatia opcional, ver D1) |
| "Oi, quero 2 bolos para sábado" | Turno do modelo | Igual a hoje | Nada muda |
| Conversa com a equipe (caso `6bb73f77`) | Ninguém automático responde | Igual a hoje | Nada muda; devolver ao Assistente segue manual |

## O que já existe e é reaproveitado

| Peça | Onde | Falta |
|---|---|---|
| Regra automática por gatilho, com chave, texto e variáveis | `RegraAutomatica`, `GatilhoAutomacao` (7 valores), tela Respostas automáticas (`EasyStock.Console/src/dominio/automacao.js`) | Gatilhos de mensagem curta, no enum e na lista da tela. A coluna é `int`: valor novo não pede migration |
| Resposta sem modelo no webhook | `OptOutPorPalavra.TentarAsync` (S38), `ProcessarEventoWhatsAppUseCase.cs:433` | Um segundo serviço no mesmo molde, logo depois dele |
| Saudação sem modelo | `SaudacaoAtendimento.Montar`, `EnviarSaudacaoAsync` (RN-01) | Não enfileirar o turno quando a entrada é só saudação |
| Link do cardápio | `SaudacaoAtendimento`, variável `{link}` (#2544) | Aceitar `{link}` nas regras automáticas |
| Portões do Assistente | `PortoesDoAssistente` (#2496), `AgenteAtendimentoService.cs:71` | Nada: a resposta sem IA passa pelos mesmos portões |
| Limite por hora contra laço | `DoLimiteAsync`, `ContarRespostasDoAgenteDesdeAsync` (#2285) | Contar a resposta sem IA (ver D2) |
| Entrada pendente | `Mensagem.MarcarProcessada`, `EntradasPendentes` | Marcar a entrada respondida sem IA |
| Registro do turno | `TurnoAgente.Registrar`, `DesfechoTurnoAgente` (S67) | Desfecho novo `SemIa = 6`, tokens 0 |

Peça nova de verdade: só o classificador de mensagem curta e o serviço que o usa. Nenhuma tabela nova.

## Fatias

### S75 · Saudação solta sem modelo

**Abordagem.**
1. `ClassificadorDeMensagemCurta`, função pura. Normaliza (minúsculas, sem acento, sem pontuação, sem emoji, espaços
   colapsados) e só classifica texto de até 4 palavras e 30 letras. A mensagem INTEIRA precisa ser composta de termos
   da lista; "contém" não vale. Devolve `Saudacao`, `Agradecimento`, `PedidoDeCardapio` ou `Nenhuma`. "sim", "ok",
   "não" e número ficam em `Nenhuma`: são respostas a pergunta e pertencem ao modelo.
2. `GatilhoAutomacao.SaudacaoSolta = 8`. `RegraAutomatica.Padrao` devolve a regra com texto sugerido e a chave conforme
   a D3. Variáveis: `{nome}` (primeiro nome, vazio se desconhecido).
3. `RespostaSemIa.TentarAsync(empresaId, conversa, mensagem, agora, ct)`, no molde de `OptOutPorPalavra`, chamado no
   webhook depois do opt-out e antes do `EnqueueAsync`. Devolve `true` quando respondeu. Responde só se TODAS valem:
   - a classificação não é `Nenhuma` e a regra do gatilho está ligada e com texto;
   - nenhum portão barra (`DaConversa`, `DaLojaAsync`, `DoLimiteAsync`, `DaCotaAsync`);
   - a mensagem é a única entrada pendente da conversa;
   - a conversa está parada: nenhuma saída nos últimos N minutos (D4).
   Qualquer condição falsa devolve `false` e o fluxo segue como hoje (o turno é enfileirado).
4. Ao responder: envia, grava a saída, marca a entrada com `MarcarProcessada` e registra `TurnoAgente` com
   `DesfechoTurnoAgente.SemIa = 6`, 0 token e modelo `sem-ia`.
5. Conversa nova (`GravarEntradaAsync` abre a conversa e só então há `identificacao`, linhas 281 a 296): quando a
   entrada que abriu a conversa é saudação solta e quem fala é a saudação RN-01, ela sai sem a `FraseEspera`, o turno
   não é enfileirado, a entrada é marcada como processada e o `TurnoAgente` `SemIa` é registrado, igual ao item 4. A
   regra `SaudacaoSolta` não dispara em conversa nova. O modelo só é dispensado quando a RN-01 saiu de fato: se o
   envio falha, o turno é enfileirado como hoje.
   Revisão na implementação (#2885): com a automática de entrada ligada (#2004, `DispararAutomacaoUseCase.cs:81`) o
   turno do modelo CONTINUA sendo enfileirado. A automática sai por outro caminho e, quando falha, cai na saudação de
   reserva com a frase de espera; dispensar o modelo ali deixaria o cliente com uma promessa sem resposta. Economizar
   esse turno fica para uma fatia própria, se o medidor da S78 mostrar que compensa.
6. Só texto puro é classificado: "oi" na legenda de foto segue para o modelo. "tudo bem", "tudo bom" e "opa" sozinhos
   são `Nenhuma` (concordância, como "ok"); só contam junto de uma saudação ("oi, tudo bem?").

**Aceite.**
- "Oi" em conversa nova, com a automática de entrada desligada, gera 1 saída, 0 turno do modelo e 1 registro `SemIa`.
  Com a automática ligada, ou se o envio da saudação falha, o turno do modelo é enfileirado como hoje.
- "Bom dia" em conversa parada há mais de N minutos gera a resposta da regra e 0 turno do modelo.
- "Oi, quero 2 bolos" e "oi" 5 minutos depois de uma pergunta do Assistente seguem para o modelo.
- Conversa com a equipe, pausada, bloqueada ou acima do limite por hora: nenhuma resposta sem IA.
- Regra desligada: comportamento idêntico ao de hoje.
- O turno seguinte do modelo não responde de novo a entrada já tratada e enxerga a resposta sem IA no histórico.

**Testes (Red).** Tabela do classificador com as 24 saudações reais de produção mais 20 contraexemplos ("oi quero",
"bom dia tem bolo?", "ok", "sim"). `RespostaSemIa`: um teste por condição do item 3, cada um falhando por falta do
serviço. Webhook: `EnqueueAsync` não é chamado quando `TentarAsync` devolve `true`, nem em conversa nova aberta por saudação
solta com a automática de entrada desligada; com ela ligada, ou com falha no envio da saudação, é chamado. Laço:
10 "oi" seguidos param no limite por hora.

**Depende de:** nada. **Tamanho:** M.

### S76 · Agradecimento e despedida

**Abordagem.** `GatilhoAutomacao.Agradecimento = 9`, mesma engrenagem da S75. Guarda própria no lugar da "conversa
parada": a última saída da conversa não termina em "?" (se o Assistente perguntou, "obrigada" pode ser resposta).
Antes do código, medir no banco quantas entradas de 45 dias a lista casa; abaixo de 5, a fatia não entra.

**Aceite.** "Obrigada" depois de "Seu pedido sai às 15h." gera a resposta da regra e 0 turno. "Obrigada" depois de
"Posso fechar assim?" segue para o modelo.

**Testes (Red).** Tabela do classificador para agradecimento; os dois casos do aceite.

**Depende de:** S75. **Tamanho:** P.

### S77 · Pedido de cardápio por palavra

**Abordagem.** `GatilhoAutomacao.PedidoDeCardapio = 10`, com `{link}` resolvido pelo mesmo caminho da saudação. Sem
link de cardápio configurado, a regra não dispara. A tela de Respostas automáticas passa a recusar, ao salvar, variável
que o sistema não preenche, dizendo quais existem: hoje a Primeira resposta da loja está com `{linkCardapio}`, que
sairia cru para o cliente.

**Aceite.** "Cardápio" e "me manda o cardápio" geram a resposta com o link e 0 turno. "Tem bolo de cenoura no
cardápio?" segue para o modelo. Salvar texto com `{linkCardapio}` devolve erro com a lista de variáveis válidas.

**Testes (Red).** Tabela do classificador; regra sem link não dispara; validação de variável desconhecida.

**Depende de:** S75. **Tamanho:** P. **Evidência fraca:** cerca de 10 mensagens em 45 dias, em amostra contaminada
por teste. Recomendo deixar para depois da medição da S78.

### S78 · Quanto deixou de gastar

**Abordagem.** A aba Uso e cota mostra, no mês, "Respostas sem IA: X de Y" ao lado dos tokens. Sai da contagem de
`TurnoAgente` por desfecho.
Na implementação (#2896): Y é o total de turnos do mês, com e sem IA. A estimativa de tokens poupados é X vezes a
média de tokens dos turnos que gastaram token no mesmo mês (a conta dos 15 mil por turno do Problema, refeita com o mês
corrente). Turno de cota esgotada ou de erro sem consumo fica fora da média. Sem turno com token no mês não há média e a
tela diz isso.

**Aceite.** Depois de 3 respostas sem IA e 2 turnos do modelo, a aba mostra "3 de 5".

**Testes (Red).** Consulta de uso com os dois desfechos; componente da aba.

**Depende de:** S75. **Tamanho:** P.

Ordem: S75 → S78 → S76 → S77. S75 e S78 juntas já entregam o ganho medido e o medidor: 7 turnos em 45 dias, perto de 105 mil tokens pela média de
15 mil por turno (estimativa sobre 4 turnos).

## Riscos

| Risco | Tratamento |
|---|---|
| Texto pronto responder o que pedia atenção | Lista fechada, mensagem inteira, até 4 palavras; guardas de conversa parada e de pergunta em aberto |
| Laço com robô de terceiros (a ouvidoria de operadora recebeu 16 respostas em 45 dias) | Conta no limite por hora (D2); barrando, a conversa vai para a equipe como hoje |
| Tela dizer uma coisa e o robô fazer outra | Mesmos `PortoesDoAssistente`; Checar o Assistente continua verdadeiro |
| Pedido em andamento receber "Olá, como posso ajudar?" | A janela de conversa parada cobre o caso comum. O webhook já sabe de pedido em andamento (`ReabrirSePedidoEmAndamentoAsync`); conversa reaberta por ele manda ao modelo |
| Valor novo de enum pedir migration | Conferido: `TurnoAgenteConfiguration` e `RegraAutomaticaConfiguration` não têm check constraint. Se o Red mostrar o contrário, a S75 ganha migration e a PR diz isso |
| Ganho menor que o esperado | É o cenário provável em volume baixo: 7 turnos em 45 dias. O ganho que não depende de volume é a mensagem duplicada que some. A S78 mede antes de S76 e S77 |

Fora de escopo: devolver a conversa ao Assistente por tempo. `Conversa.LiberarAutomatico` diz "nunca por tempo"
(`Conversa.cs:298`) e foi isso que deixou a conversa `6bb73f77` 13 dias sem resposta automática. É decisão de produto separada, com issue
própria.

## Decisões do Felipe

| # | Decisão | Opções (a = recomendada) |
|---|---|---|
| D1 | Quais fatias entram agora | a) S75 e S78, depois decidir S76 e S77 com o número na tela · b) as quatro · c) só S75 |
| D2 | Como a resposta sem IA aparece e conta no limite | a) como fala do Assistente (`AutorMensagem.Agente`): conta no limite sem mudar a consulta e entra no histórico do modelo como fala dele · b) como Sistema, igual à saudação: o histórico trata Sistema à parte (`GeracaoAgenteAtendimento.cs:243` e `:335`) e a contagem do limite precisa ser estendida |
| D3 | As regras novas nascem | a) desligadas, com texto sugerido; você liga na tela Respostas automáticas · b) ligadas com o texto sugerido |
| D4 | "Conversa parada" para a saudação | a) 30 minutos sem saída · b) 2 horas · c) só em conversa nova |

**Decidido pelo Felipe em 19/11/2026: opção a nas quatro.**

| # | Vale |
|---|---|
| D1 | Entram S75 e S78. S76 e S77 ficam para depois, decididas com o número da aba Uso |
| D2 | A resposta sem IA é gravada como fala do Assistente (`AutorMensagem.Agente`) e conta no limite por hora |
| D3 | As regras novas nascem desligadas, com texto sugerido |
| D4 | Conversa parada: 30 minutos sem saída |

## Prova

Para cada fatia: Red que falha pelo motivo esperado, Green, gate do commit. Em produção, depois do deploy: mandar "Oi"
de um número de teste (conversa nova, item 5 da S75) e conferir no Console 1 saída e, na aba Uso, 0 token novo e
"Respostas sem IA" somando 1. Depois, com a regra ligada, mandar "Bom dia" na mesma conversa passada a janela da D4.
