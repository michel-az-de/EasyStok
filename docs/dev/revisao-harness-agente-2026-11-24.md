# Revisão do harness do agente de atendimento (24/11/2026)

- Issue: #3271
- Base medida: `master` `16c64c25b` (as 29 referências de arquivo e linha foram conferidas neste commit); log do `ez-api` de 24/11/2026, das 12:03 às 12:41 de Brasília (48 chamadas ao modelo, 2 conversas)
- Método: cinco eixos (correção, leitura, arquitetura, segurança, desempenho), só leitura, sem teste de mutação. Os dez achados principais passaram por uma banca independente que tentou refutá-los no código: 8 confirmados, 2 parciais (R7 e R10, já ajustados abaixo) e nenhum refutado
- Pedido do Felipe: revisar o desenho do harness da Casa da Baba com DeepSeek, apontar melhorias de código e de desenho, e propor o destino da ADR-0065

## 1. Veredito

O agente de atendimento **é um harness**, e o núcleo dele está certo: portões antes do modelo, laço próprio com limite, ferramentas sob trava da conversa, guarda de saída e escalada para a equipe. O que não se sustenta é o entorno:

1. O teto de saída (4096 tokens) não serve ao modelo que raciocina, e estourar o teto é tratado como resposta normal. Medido hoje: 3 de 48 chamadas (6%). O defeito está registrado desde 01/10 (A-07).
2. O uso é contado em três lugares, e por causa de um deles a loja inteira atende **uma conversa por vez**.
3. Não existe eval que barre mudança. A última medição formal (01/10) deu 61% de aderência e "go-live NÃO pronto", com veredito preenchido à mão.
4. A ADR-0065, que deveria descrever o harness, descreve um sistema que não foi construído.

## 2. O desenho medido

```
WhatsApp / chat do site
  -> webhook -> fila TurnoAgente (1 turno por LOJA por vez)
  -> AgenteAtendimentoService.ProcessarTurnoAsync
     1. PORTOES (11, sem modelo)
          situacao, vinculo, janela de 24 h         -> barra: so nao responde
          chave, modelo, canal, equipe, bloqueado,
          pausado, limite por hora, cota de tokens  -> barra: escala para a equipe
     2. CONTEXTO
          system estavel (prompt + roteiro + caderno, vai para o cache)
          system volatil (data, dossie) + 20 ultimas mensagens
     3. LACO (ate 6 voltas)
          modelo <-> ferramentas (23, menos as desligadas pela loja)
          cada efeito sob lock e revisao da conversa
     4. GUARDA de saida (regex)
          pedido, pagamento, frete, valor e link so passam com origem em ferramenta
     5. ENVIO e registro (TurnoAgente, UsoIa, chamada_ia)

IAgenteLlmClient
  RoteadorIaLlmClient: a loja tem a flag ia.provedor_por_loja?
    NAO (producao hoje) -> registro -> AnthropicMessagesClient -> .env global
                           Fireworks, deepseek-v4p1-flash, Bearer, 4096 tokens, 60 s
    SIM -> tenant confere -> modelo pedido ou padrao -> provedor ligado
           -> cota em US$ -> chave do cofre -> ClienteProvedorIa
              (hosts em lista fechada, trava de SSRF, disjuntor por provedor)
```

| Peça | Onde |
|---|---|
| Orquestração do turno | `EasyStock.Application/Services/Atendimento/AgenteAtendimentoService.cs` |
| Contexto e laço | `EasyStock.Application/Services/Atendimento/GeracaoAgenteAtendimento.cs` |
| Portões | `EasyStock.Application/Services/Atendimento/PortoesDoAssistente.cs` |
| Guarda de saída | `EasyStock.Application/Services/Atendimento/GuardaRespostaAgente.cs` |
| Porta do modelo | `EasyStock.Application/Ports/Output/Ai/IAgenteLlmClient.cs` |
| Roteador por loja | `EasyStock.Application/Services/Ia/RoteadorIaLlmClient.cs` |
| Clientes HTTP | `EasyStock.Infra.Integrations/Ia/AnthropicMessagesClient.cs`, `ClienteProvedorIa.cs` |
| Fila do turno | `EasyStock.Api/BackgroundServices/AtendimentoFilaTurnoAgenteBackgroundService.cs` |

## 3. O que roda em produção

| Fato | Evidência |
|---|---|
| 100% das chamadas saem pelo caminho do `.env`, modelo `accounts/fireworks/models/deepseek-v4p1-flash` | 48 linhas `Anthropic uso: modelo=...` no log; nenhuma `IA uso: provedor=` |
| O cache de prefixo funciona | `cache_lido` de 1.920 a 18.304 tokens por chamada |
| 3 chamadas fecharam com `saida=4096`, o teto exato, e cada uma encerrou o turno | 12:03:02, 12:12:57 e 12:16:25 |
| A equipe perde a conversa em 60 s | 5 linhas `retomada pelo Assistente após 60 s sem resposta da equipe`, 4 delas na mesma conversa em 13 minutos |
| A entrada chega a 21,8 mil tokens na quinta volta de um turno | 12:12:32: `entrada=3501`, `cache_lido=18304` |

Não medido: a flag por loja, o custo no banco e o texto que saiu nos três turnos de 4096. O papel de leitura do banco não enxerga as tabelas de IA.

## 4. Achados de código

Severidade: **Critical** quebra ou expõe; sem prefixo é correção necessária; **Optional** vale considerar; **FYI** é contexto.

| # | Sev. | Achado | Evidência | Remédio |
|---|---|---|---|---|
| R1 | Necessária | Estourar `max_tokens` é tratado como resposta final. Com o modelo que raciocina, o texto vem vazio (escala com motivo técnico e 4096 tokens pagos) ou cortado (segue para o cliente se passar na guarda). | `GeracaoAgenteAtendimento.cs:157-163`; `AgenteAtendimentoService.cs:267-273`; teto em `AnthropicAgenteOptions.cs:28`; 6% das chamadas medidas; A-07 e H07a em `13-homologacao-organica.md:56,97`; `matriz.py:45`. Que texto cortado tenha chegado a cliente é hipótese, não medição | Tratar `max_tokens` como desfecho próprio: nunca enviar texto cortado, uma nova tentativa com teto maior e, falhando, a mensagem padrão de encaminhamento. Teto por modelo (ver R8). |
| R2 | Necessária | A nova tentativa repete qualquer falha, inclusive 400, 401 e 402, que não mudam em 300 ms. Cada repetição grava outra linha de erro em `chamada_ia`. | `GeracaoAgenteAtendimento.cs:199-204`; no caminho por loja o prazo estourado vira `HttpRequestException` e também se repete, mais 60 s (`ClienteProvedorIa.cs:70`) | Repetir só falha transitória (rede, prazo, 5xx, 408 e 429). 4xx definitivo sobe direto. |
| R3 | Necessária | Um turno por loja por vez, e sem prazo total. Pior caso de um turno: 6 voltas de até 60 s, cada uma com nova tentativa, perto de 12 minutos com as outras conversas da loja paradas. | `AtendimentoFilaTurnoAgenteBackgroundService.cs:43-45`; `RegistrarUsoAsync` lê, soma e grava `UsoIa` sem trava (`AgenteAtendimentoService.cs:517-538`); a banca de 01/10 já pedia "concorrência 4 por conversa" (`13-homologacao-organica.md:141`) | Incremento atômico em `UsoIa` (um `UPDATE` com soma no banco) ou tirar essa gravação do turno; chave do executor por conversa; prazo total do turno por `CancellationTokenSource`. |
| R4 | Necessária | A guarda tem "baba" escrito na regex e não recebe o nome da dona configurado. Loja com outro nome deixa passar "a Fulana vai combinar com você" sem escalada. | `GuardaRespostaAgente.cs:109`; `NomeDona` em `ConfiguracaoAtendimento` (ADR-0066) | Passar o nome da dona para `Avaliar` e montar a alternância com ele. Teste: nome diferente de "Baba" barra a promessa. |
| R5 | Optional | `chamada_ia.turno_agente_id` nunca é preenchido. Não dá para ligar o custo de uma chamada ao turno que a fez. | `RoteadorIaLlmClient.cs:192-194`; `RegistroChamadaIaLlmClient.cs:61` | Levar o id do turno na `RequisicaoLlm`, como já vão `EmpresaId` e `Finalidade`. |
| R6 | Optional | Três calendários para o mesmo mês: a cota de tokens fecha no mês de Brasília; a de US$ e o agregado `UsoIa` fecham no mês UTC. Três horas por mês em que os números não batem. | `PortoesDoAssistente.cs:103`; `RotasIaRepository.cs:38`; `AgenteAtendimentoService.cs:519` | Um calendário só, o de Brasília. |
| R7 | FYI | Dependências opcionais com padrão nulo deixam o controle de custo falhar aberto: sem o repositório de turnos, a cota vira "não se aplica" e o turno não é registrado. A produção registra todas; o risco é de teste ou de fiação futura. | `AgenteAtendimentoService.cs:44-51`; `PortoesDoAssistente.cs:91-92`; registro em `ServiceCollectionExtensions.cs:94` do `Infra.Postgre` | Tornar obrigatórias as que são regra de negócio (turnos, limite, preços). O teste injeta dublê, não nulo. |
| R8 | Optional | O caminho por loja usa teto de saída e prazo globais, não os do provedor nem do modelo. | `ClienteProvedorIa.cs:42,50,55` | `saida_max` por modelo e prazo por provedor. É o corte D5 do plano #2948, que a medição de R1 pede para rever. |
| R9 | Optional | Sem conseguir ler a flag, a loja cai na chave global do `.env` pelo resto da requisição. A cota de tokens continua valendo; fica de fora só o orçamento em US$. | `RoteadorIaLlmClient.cs:108-114` | Decisão registrada no código. Rever quando o `.env` sair (S5b): a falha passa a ser "indisponível". |
| R10 | Optional | O roteador soma o gasto do mês no banco em toda chamada: até 7 somas num turno de 6 voltas. A chave não pesa, porque o cofre tem cache de 5 minutos. | `RoteadorIaLlmClient.cs:47,71,146`; `IntegrationCredentialResolver.cs:34-37` | Memo do gasto no escopo, como já existe para a rota. |
| R11 | Optional | `ProcessarTurnoAsync` repete quatro vezes o mesmo bloco (loja, pessoa, limite e cota): portão barra, loga, escala, registra, comita e retorna. `RetomarTravadaAsync` já monta a mesma lista com `Concat` e `FirstOrDefault`. | `AgenteAtendimentoService.cs:118-120`, `137-139`, `148-150`, `158-160` e `368-369`; o arquivo tem 613 linhas e o construtor 20 dependências (`:31-51`) | Um método que devolve o primeiro portão que barra, usado pelos dois caminhos. Separar a finalização (guarda, envio, registro) em colaborador próprio. |
| R12 | Optional | O corpo de erro do caminho `.env` vai para o log sem sanitizar, com o comentário "o corpo de erro da Anthropic não contém chave". O provedor é a Fireworks. | `AnthropicMessagesClient.cs:66-68` | Reusar `ClienteProvedorIa.Sanitizar`. |
| R13 | FYI | Nomes dizem Anthropic e o provedor é Fireworks com DeepSeek: seção `Anthropic__*`, log "Anthropic uso", `AnthropicAgenteOptions`. | `compose.yaml:67-71` | Sai com a S5b do #2948. |
| R14 | FYI | A retomada em 60 s é o padrão do código. Com a equipe demorando mais de um minuto, a escalada quase não segura a conversa. | `RetomadaDoAssistente.cs:15`; log da seção 3 | Decisão do Felipe: confirmar o prazo ou subir. |

## 5. Revisão do desenho

| # | Tema | Hoje | Proposta |
|---|---|---|---|
| D1 | Onde mora a configuração | Em três lugares (constante no código, `.env` global, banco por loja) e a ADR-0065 pede um quarto (arquivo) | Dois lugares. **Código**: o que protege o cliente (portões, guardas, limites). **Banco por loja**: o que a dona ajusta (nome, tom, roteiro, modelo, ferramentas, cota). Sem perfil em arquivo. |
| D2 | Contabilidade de uso | `UsoIa` (agregado mensal), `TurnoAgente` (por turno) e `chamada_ia` (por chamada), com dois cálculos de custo que podem divergir | `chamada_ia` é a fonte única, ligada ao turno (R5). `TurnoAgente` vira leitura do turno. `UsoIa` sai do caminho do turno, o que destrava a concorrência por conversa (R3). |
| D3 | Cotas | Tokens por mês (ADR-0066) e US$ por provedor (#2948), com calendários diferentes | Uma só protege dinheiro: a de US$. A de tokens vira derivada dela ou sai na S5b. |
| D4 | Robustez por modelo | Um teto e um prazo globais; `stop_reason` só distingue `tool_use` do resto | Teto e prazo por modelo; `max_tokens` e recusa como desfechos próprios; falha transitória separada da definitiva (R1, R2, R8). |
| D5 | Prova antes de mudar | Simulador em `scripts/homologacao/`, com veredito digitado à mão em `matriz.py`, uma rodada, última medição formal em 01/10 | Eval mínimo como portão: poucos casos com asserção determinística (ferramenta chamada, escalou, guarda limpa), k rodadas, resultado gravado. Roda antes de trocar modelo, prompt ou ferramenta. Reaproveita `sim.py`, `fake_meta.py` e `proxy_modelo.py`. **É a peça que mais falta.** |
| D6 | Formato do provedor | Só Anthropic Messages (#2948, D3) | Manter. Com um formato só, o adaptador `IChatClient` da ADR-0065 não se paga. |
| D7 | Harness genérico | O laço recebe `Conversa` e mora em `Services/Atendimento` | Não generalizar agora: só existe um agente. Separar orquestração de regra dentro do próprio atendimento (R11) já prepara o segundo. |
| D8 | Contexto | Janela fixa de 20 mensagens, sem orçamento de tokens | Manter, e medir a entrada por turno no eval antes de mexer. |

## 6. Proposta: substituir a ADR-0065

A ADR-0065 está como "Aceito" desde 11/11, mas nenhuma decisão estrutural dela existe no código, duas foram emendadas (ADR-0066 e ADR-0067) e uma foi contrariada sem citação (#2948).

| Decisão da 0065 | Estado medido | Destino proposto |
|---|---|---|
| D1 · Namespace genérico `Services/Agentes` | A pasta não existe | **Sai.** Volta quando houver o segundo agente. |
| D2 · `IChatClient` atrás da porta | Nenhum `.csproj` referencia `Microsoft.Extensions.AI` | **Sai.** Vale a D3 do #2948: só Anthropic Messages. |
| D3 · Perfil em `agents/<id>/agent.json` | A pasta `agents/` não existe | **Sai.** A configuração é por loja, no banco (ADR-0066 e ADR-0067). |
| D4 · O código executa, laço explícito | É o que o código faz | **Fica**, sem a parte do arquivo. |
| D5 · Eval antes de mudar | Não existe; a ADR-0067 registra que entrou sem eval | **Fica e vira a decisão central**, na versão mínima de D5 acima. |
| D6 · Modelo por papel e custo por turno | Só o custo por turno | **Fica o custo**, pela fonte única (D2 acima). Modelo por papel sai. |
| D7 · Tier de mudanças em `agents/**` | Regula uma pasta que não existe | **Reescrita**: tier ALTO para modelo, provedor, guarda, portão e limite; BAIXO para texto de prompt com eval verde. |

Rascunho da ADR nova. Fica só neste documento: a substituição de verdade é issue própria, de tier ALTO, aberta depois da decisão do Felipe.

> **Harness do agente de atendimento: código protege, loja ajusta, eval barra**
>
> 1. O harness é o do atendimento: portões, laço explícito, guarda e escalada ficam no código e não se desligam por configuração.
> 2. O que a loja ajusta mora no banco, por loja: nome, tom, roteiro, modelo, ferramentas e cota. Não há perfil em arquivo.
> 3. Um formato de provedor, Anthropic Messages. Provedor e modelo por loja seguem o plano #2948.
> 4. `chamada_ia` é a fonte única de uso e custo, ligada ao turno.
> 5. Mudança de modelo, provedor, prompt, ferramenta, guarda ou limite só entra com o eval verde anexado à PR.
> 6. Esta ADR substitui a ADR-0065. As ADR-0066 e ADR-0067 continuam valendo.

## 7. Ordem sugerida

| # | Fatia | Achados | Tier provável |
|---|---|---|---|
| 1 | `max_tokens` como desfecho próprio e teto por modelo | R1, R8 | Baixo (fix) com teste; alto se criar coluna |
| 2 | Nova tentativa só em falha transitória | R2 | Baixo (fix) |
| 3 | Guarda com o nome da dona | R4 | Alto (guarda) |
| 4 | `UsoIa` atômico, fila por conversa e prazo do turno | R3 | Alto (concorrência e limite) |
| 5 | Id do turno em `chamada_ia` | R5 | Baixo |
| 6 | Eval mínimo como portão | D5 | Baixo (script e dado de teste) |
| 7 | ADR nova no lugar da 0065 | seção 6 | Alto (ADR) |

A fatia 6 pode vir antes de todas: ela é a prova das outras.

## 8. Limites desta revisão

- Sem leitura das tabelas de IA em produção: a flag por loja, o custo no banco e o texto enviado nos turnos de 4096 não foram medidos.
- A amostra do log é de 38 minutos, 48 chamadas e 2 conversas, com cara de sessão de teste. Os 6% de R1 valem para essa janela.
- O plano `18-harness-do-agente.md` foi lido por trechos. Os planos `19` e `provedores-ia-2948` e as ADR-0065, 0066 e 0067 foram lidos inteiros.
- Não li as 23 ferramentas uma a uma, nem `PromptAtendimento.cs`.

## 9. Estado em 25/11/2026

Marcação feita depois que as fatias entraram na `master`. As seções 1 a 8 ficam como foram escritas, na base `16c64c25b`: este quadro só diz o que mudou desde então e o que continua aberto. A PR é a mergeada; a issue da fatia vai entre parênteses. As provas de cada uma estão na descrição da PR.

| # | Estado | PR (issue) |
|---|---|---|
| R1 | Tratado: `max_tokens` é desfecho próprio e o texto cortado nunca vai ao cliente; se a saída cortada ficou abaixo de 16384 tokens (o teto que ela usou) e o modelo não tem `saida_max` cadastrado, repete uma vez pedindo 16384; estourando de novo, ou se já tinha 16384 ou mais, ou se o teto já era o `saida_max` do modelo, a conversa vai para a equipe com o motivo `teto_saida` | #3274 (#3273), #3326 (#3313) |
| R2 | Tratado: a nova tentativa repete uma vez a falha sem código HTTP (rede, prazo, resposta ilegível), 5xx, 408 e 429, menos a repetição depois de `max_tokens`, que não tem outra; os outros 4xx (400, 401, 402 e afins) e o provedor indisponível (disjuntor, cota, sem chave) sobem direto; no histórico do turno, o status que não se repete (400, 401, 402, 403, 404, 3xx) grava `falha_llm_definitiva` e o resto, `falha_llm` (#3347) | #3274 (#3273), #3347 |
| R3 | Tratado: a fila ordena por conversa, até 2 turnos por loja e 4 no total (valores padrão); o prazo do turno, 180 s por padrão (0 desliga), conta o tempo das ferramentas, mas só corta a espera pelo modelo: ferramenta em curso e envio terminam e a volta seguinte não chama o modelo | #3293 (#3291) |
| R4 | Tratado: a guarda monta a alternância com o nome da dona configurado | #3280 (#3278) |
| R5 | Tratado: `chamada_ia.turno_agente_id` é preenchido nas chamadas enviadas dos dois caminhos; a linha de cota estourada, que nem chega a sair, fica sem turno | #3288 (#3287) |
| R6 | Tratado: a cota em US$ e a soma do mês de `chamada_ia` fecham no mês de Brasília | #3296 (#3295) |
| R7 | Tratado: turnos, limite de respostas e preços são obrigatórios no serviço do agente | #3305 (#3298) |
| R8 | Tratado no caminho por loja: cada modelo ganha `saida_max` (256 a 200000), que vira o teto de saída de toda chamada dele no lugar do global do `.env`; com ele cadastrado não há repetição depois de `max_tokens`, porque a chamada já saiu com o máximo do modelo; cada provedor ganha `prazo_segundos` (5 a 300 s) no lugar do prazo global; os dois são opcionais e nulo usa o global; nenhum valor foi gravado: até a tela de cadastro (S4 da #2948) o valor entra por SQL | #3312 (#3304), #3326 (#3313) |
| R9 | Tratado: a decisão de seguir pelo `.env` quando a flag não lê ficou escrita no código; rever com a S5b | #3288 (#3287) |
| R10 | Tratado: o roteador lê o gasto do mês uma vez por escopo e provedor | #3288 (#3287) |
| R11 | Parcial: um método devolve o primeiro portão do cliente e do custo (pessoa, limite e cota); os portões da conversa e da loja seguem em chamadas à parte; a finalização do turno tem colaborador próprio (`FinalizacaoDoTurno`) | #3305 (#3298) |
| R12 | Tratado: o log de erro do caminho do `.env` sai sem a chave | #3274 (#3273) |
| R13 | Fora desta série: sai com a S5b do plano #2948 | sem PR (#2948) |
| R14 | Aberto: decisão do Felipe sobre o prazo de retomada de 60 s | sem issue |
| D1 | Parcial: teto e prazo por modelo e provedor passam a morar no banco (R8); o `.env` global só sai com a S5b e a ADR nova, que fixa "dois lugares, sem perfil em arquivo", espera o GO | #3312 (#3304) |
| D2 | Parcial: o turno não grava mais `UsoIa` (#3293) e a chamada de IA carrega o id do turno (#3288); o `TurnoAgente` ainda guarda consumo e custo próprios e a cota de tokens lê dele, então `chamada_ia` ainda não é a fonte única | #3293 (#3291), #3288 (#3287) |
| D3 | Fora desta série: a cota de tokens vira derivada da de US$, ou sai, na S5b | sem PR (#2948) |
| D4 | Parcial: `max_tokens` (`teto_saida`, #3274) e a recusa (`refusal`, `recusa_modelo`) são desfechos próprios, e a recusa nunca repete nem leva o texto dela ao cliente; teto e prazo existem por modelo e provedor no caminho por loja (#3312); a falha definitiva (a que a nova tentativa não repete: 400, 401, 402 e afins) grava `falha_llm_definitiva` no turno, e a transitória e o provedor indisponível (disjuntor, cota, sem chave) seguem `falha_llm` (#3347) | #3274 (#3273), #3312 (#3304), #3326 (#3321), #3347 |
| D5 | Parcial: o eval mínimo entrou (casos com asserção determinística, k rodadas, veredito pelo código de saída); falta a primeira rodada real contra a bancada, que calibra o mínimo de 0,8 e os casos; nenhum workflow do GitHub roda o eval | #3276 (#3275) |
| D6 | Sem mudança: manter só o formato Anthropic Messages | sem PR |
| D7 | Sem mudança: não generalizar; a separação de R11 já prepara o segundo agente | #3305 (#3298) |
| D8 | Parcial: a janela fica como está e o eval mede a entrada por turno com cache; a medida real espera a primeira rodada | #3276 (#3275) |
| ADR | Aberto: a ADR nova no lugar da ADR-0065 (seção 6) espera o GO do Felipe e é issue própria, de tier ALTO | sem issue |

Surgiu durante a série:

- A prova `prova-2543-chip-do-motivo.mjs` lia texto de C# que o refactor da #3298 moveu e deixou o workflow `console` da `master` vermelho. Corrigida na #3311 (#3310). O filtro de caminhos do `console.yml` não inclui `EasyStock.Application`, então uma PR só de backend não roda esse workflow: mudar o filtro é de tier ALTO e fica como sugestão ao Felipe.
- A conferência E32 da `prova-2185-efeitos-tela.mjs` falhou em duas execuções entre 00:01 e 00:05 de Brasília de 25/11 e passou às 00:15, sem mudança de código. A hipótese é a dependência do relógio, ainda não reproduzida nem provada: issue #3307.
