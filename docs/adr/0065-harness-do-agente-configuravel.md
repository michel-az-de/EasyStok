# ADR-0065 · Harness do agente configurável por arquivo, com eval antes de mudar

- Status: Aceito (2026-11-11; Felipe aprovou no chat da sessão da #2281)
- Data: 2026-11-11
- Decisor: Felipe
- Relacionados: issue #2281, plano `docs/plan/atendimento-whatsapp/18-harness-do-agente.md` (Onda 13,
  S61 a S66), ADR-0050 (Meta Cloud API direta), ADR-0055 (tier de spec de plano aprovado), ADR-0062
  (ponte provisória), bateria de homologação `scripts/homologacao/` (#1318)

## Contexto

O agente de atendimento do WhatsApp já é um harness: monta o contexto, chama o modelo, executa
ferramentas em ciclo, aplica guardas e escala para a dona. Só que cada peça está presa no código.
Trocar modelo, ajustar o prompt ou acrescentar uma habilidade exige deploy, e nada prova que o
atendimento continua igual depois da mudança.

Fatos medidos em `origin/master` (0762f64b7, 11/11/2026):

| # | Fato | Fonte |
|---|---|---|
| 1 | A porta já isola o modelo: `IAgenteLlmClient.EnviarAsync(RequisicaoLlm)`, sem modelo, temperatura nem `tool_choice` na requisição | `Application/Ports/Output/Ai/IAgenteLlmClient.cs` |
| 2 | A única implementação fala HTTP cru com a API Messages; o vocabulário dos blocos é o da Anthropic | `Infra.Integrations/Ia/AnthropicMessagesClient.cs` |
| 3 | Um modelo para o agente, por configuração (`Anthropic:ModeloAgente`, padrão `claude-sonnet-5`); outro uso de IA tem modelo fixo no código | `AnthropicAgenteOptions.cs:25`, `GeradorAutoPreenchimentoClaude.cs:21` |
| 4 | O prompt é uma string em C# com o nome da loja escrito direto ("atendente virtual da Casa da Baba") | `Services/Atendimento/PromptAtendimento.cs:26` |
| 5 | Limites em constantes: 6 voltas de ferramenta e janela de 20 mensagens | `AgenteAtendimentoService.cs:46-47` |
| 6 | 19 ferramentas, todas enviadas em toda chamada | `Services/Atendimento/Ferramentas/` |
| 7 | Única prova automática do prompt é o snapshot do texto; não há eval de conversa | `PromptAtendimento.snapshot.txt` |
| 8 | Já existe bateria de clientes simulados contra a API local, com Meta e Mercado Pago falsos e um proxy que registra latência, tokens e ferramentas pedidas | `scripts/homologacao/` |
| 9 | `UsoIa` soma só tokens por empresa e mês; não guarda modelo nem custo | `Domain/Entities/UsoIa.cs` |
| 10 | Application depende só do Domain e de abstrações `Microsoft.Extensions.*`; ArchitectureTests guarda a fronteira | `EasyStock.Application.csproj`, `EasyStock.ArchitectureTests/` |

## Decisão

### D1 · O harness evolui dentro do EasyStok, sem reescrever

As ferramentas do agente são casos de uso do EasyStok e já rodam com tenant e RLS. O harness fica no
mesmo repositório, num namespace genérico, `Application/Services/Agentes`, separado do domínio
`Services/Atendimento`. Um teste de arquitetura impede que `Services.Agentes` dependa de
`Services.Atendimento` ou das entidades de atendimento, no molde de
`PlataformaNaoDependeDoAtendimentoTests`. Projeto próprio ou pacote só quando um segundo produto usar.

### D2 · A porta fica; `IChatClient` entra atrás dela

`IAgenteLlmClient` continua sendo a única porta do agente para o modelo. Um adaptador novo,
`ChatClientAgenteLlmClient`, em `Infra.Integrations`, traduz `RequisicaoLlm` para
`Microsoft.Extensions.AI` e escolhe o `IChatClient` registrado por chave de provedor. Application não
referencia Microsoft.Extensions.AI. `AnthropicMessagesClient` continua como está até o eval provar
paridade do adaptador novo (S63).

`RequisicaoLlm` ganha o modelo pedido (provedor, id, temperatura, teto de saída). Isso emenda o
comentário da porta: o modelo continua vindo de configuração, agora a do perfil do agente (D3), e não
de `Anthropic:ModeloAgente`.

### D3 · Perfil do agente em arquivo versionado

Cada agente tem uma pasta `agents/<id>/`:

| Arquivo | Conteúdo |
|---|---|
| `agent.json` | modelo e reserva, camadas do contexto na ordem, ferramentas permitidas, limites, gatilhos de escalada |
| `prompts/*.md` | texto do prompt, com marcadores preenchidos pelo tenant (ex.: `{{loja.nome}}`) |
| `skills/<nome>/SKILL.md` | habilidade carregada sob demanda (S65) |
| `evals/casos/*.json` | conversas de teste do agente (D5) |

O manifesto é JSON lido por `Microsoft.Extensions.Configuration` e validado no start
(`ValidateOnStart`): perfil inválido derruba o start, não o turno. `agents/agent.schema.json` serve ao
editor. Os arquivos vão para a imagem junto com o host e o hash do perfil entra no registro de cada
turno. Não há recarga a quente: a versão do perfil é a do commit implantado.

### D4 · O arquivo escolhe, o código executa

O perfil só escolhe entre o que o código já oferece. Nome de ferramenta que não existe no registro
derruba o start. O arquivo não cria ferramenta, não concede permissão, não muda RLS nem desliga
guarda de saída. O ciclo modelo e ferramenta continua explícito no EasyStok
(`GeracaoAgenteAtendimento`, depois `Services/Agentes`), com os limites vindos do perfil; não se usa
`UseFunctionInvocation`, para que guarda, escalada e telemetria continuem sob controle do código.

### D5 · Eval antes de mudar

Nenhuma mudança de agente entra sem eval. S61 cria a linha de base reaproveitando a bateria de
`scripts/homologacao/`: casos versionados em `agents/<id>/evals/`, cada um com asserções
determinísticas (ferramenta chamada ou não, escalou ou não, guarda limpa, palavra proibida ausente),
rodados k vezes. A métrica é pass^k (o caso passa nas k rodadas), mais tokens, custo e tempo por
turno. Conversa real só entra anonimizada e revisada; os casos de partida são sintéticos, como os da
bateria atual.

### D6 · Modelo por papel e custo por turno

O perfil declara o modelo de cada papel (atender, resumir, preencher) com um reserva. Cada turno
registra provedor, modelo, tokens de entrada e saída, custo estimado e hash do perfil (S64). Os
outros usos de IA migram para o mesmo esquema conforme forem tocados, sem onda própria.

### D7 · Tier das mudanças em `agents/**`

Emenda à ADR-0055, só para a pasta `agents/**`:

| Mudança | Tier | Prova na PR |
|---|---|---|
| Texto de prompt ou de skill | BAIXO | eval verde, pass^k sem queda |
| Caso de eval novo ou corrigido | BAIXO | o caso roda e o motivo está na PR |
| Troca de modelo ou provedor | ALTO | eval verde, pass^k e custo lado a lado |
| Ferramenta liberada, guarda, gatilho de escalada, limite | ALTO | eval verde e um caso novo que cobre a mudança |

Código, csproj, Dockerfile e migração continuam nas regras da ADR-0055.

## Alternativas descartadas

- **Projeto ou repositório separado para o harness.** Duplica tenant, RLS, deploy e CI para servir a um
  produto só. Fica para quando houver o segundo.
- **Microsoft Agent Framework com agente declarativo em YAML.** A parte declarativa ainda é prévia no
  C# e traria o ciclo para dentro do framework, tirando guarda e escalada do código do EasyStok.
- **`UseFunctionInvocation` do Microsoft.Extensions.AI no lugar do ciclo próprio.** Esconde cada volta
  de ferramenta, e é nela que a guarda, o limite e a escalada atuam.
- **Manifesto em YAML.** Exigiria pacote novo para ler e validar. O JSON já é lido e validado pelo
  `Options` que o projeto usa; o texto longo fica nos `.md`.
- **Prompt editável pelo console, no banco.** Perde revisão, histórico e eval por PR. Ajuste por loja
  entra depois (S66), dentro de limites que o perfil define.
- **Trocar o modelo antes do eval.** Sem linha de base não há como dizer se a troca piorou o
  atendimento.

## Consequências

- O prompt da Casa da Baba sai do C# sem mudar uma letra: o texto renderizado para ela é igual ao
  snapshot atual (S62).
- Trocar modelo vira PR de uma linha em `agent.json`, tier ALTO, com eval e custo lado a lado.
- O eval depende da pilha local da bateria e de uma chave de modelo, por isso roda fora do CI, com o
  resultado anexado à PR. Levar para o CI exige workflow e segredo, e é decisão à parte.
- Microsoft.Extensions.AI entra só no `Infra.Integrations` (S63, tier ALTO por csproj).
- Rollback por camada: o perfil volta por PR; o adaptador novo sai trocando o provedor do perfil para o
  cliente HTTP atual; a porta só ganha campos opcionais.
