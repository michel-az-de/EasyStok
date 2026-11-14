# Onda 13 · Harness configurável do agente (S61–S66)

Issue: #2281 · Decisão: [ADR-0065](../../adr/0065-harness-do-agente-configuravel.md) · Origem: estudo do
harness do atendimento em 11/11/2026.

Objetivo: trocar modelo, ajustar prompt e acrescentar habilidade por arquivo versionado, com prova por
eval de que o atendimento não piorou. O agente atual evolui; nada é reescrito.

## Decisões

| Tema | Decisão |
|---|---|
| Ordem | Eval primeiro (S61). Nenhuma mudança no agente entra sem a linha de base medida. |
| Onde mora | No EasyStok. Harness genérico em `App/Services/Agentes`; o domínio continua em `App/Services/Atendimento`. |
| Perfil | `agents/<id>/agent.json`, `prompts/*.md`, `skills/<nome>/SKILL.md`, `evals/casos/*.json`. O arquivo escolhe, o código executa. |
| Manifesto | JSON lido por `Microsoft.Extensions.Configuration`, validado no start. Sem pacote novo. |
| Modelo | Porta `IAgenteLlmClient` mantida; adaptador `IChatClient` no `Integrations`, provedor por chave. |
| Ciclo | Explícito no EasyStok, limites vindos do perfil. Sem `UseFunctionInvocation`. |
| Tier de `agents/**` | Texto de prompt ou skill e caso de eval: BAIXO com eval verde. Modelo, provedor, ferramenta, guarda, gatilho, limite: ALTO (ADR-0065 D7). |

## Arquitetura

```
mensagem ──► AgenteAtendimentoService (domínio: conversa, dossiê, escalada)
                 │
                 ▼
        Services/Agentes (genérico)
        PerfilAgente ◄── IPerfisAgente ◄── agents/atendimento-whatsapp/
        RenderizadorPrompt                  agent.json · prompts/sistema.md
                 │ ciclo com limites e ferramentas do perfil
                 ▼
        IAgenteLlmClient ──► AnthropicMessagesClient (hoje)
                         └─► ChatClientAgenteLlmClient ──► IChatClient[provedor] (S63)

eval: scripts/homologacao/eval.py ──► pilha local da bateria ──► agents/<id>/evals/casos/*.json
```

Ordem: S61 → S62 → S63 → S64. S65 depois da S62. S66 depois da S65.

---

### S61 · Linha de base do eval do agente

**Problema.** Não há prova automática do comportamento da conversa. A bateria de homologação mede, mas
a nota é dada à mão (`matriz.py`) e os cenários vivem dentro dos scripts. Mudar prompt ou modelo hoje é
mudar às cegas.
**Abordagem.** Reaproveitar a pilha da bateria (`sim.py`, `fake_meta.py`, `fake_mp.py`,
`proxy_modelo.py`) num runner que lê casos versionados, roda cada caso k vezes e avalia asserções
determinísticas por código.
**Leitura mínima.** `scripts/homologacao/README.md`, `bateria1.py`, `bateria2.py`, `sim.py`,
`proxy_modelo.py`.
**Escopo.**
- `agents/atendimento-whatsapp/evals/casos/<id>.json`, de 20 a 30 casos. Partida: os 16 cenários da
  `bateria1.py` e os difíceis da `bateria2.py`. Formato: `id`, `descricao`, `falas[]` e `esperado` com
  `ferramentasChamadas[]`, `ferramentasProibidas[]`, `escala` (sim, não ou indiferente),
  `textoProibido[]` e `textoExigido[]` (expressões regulares).
- `scripts/homologacao/eval.py`: roda um caso por vez, para que as chamadas registradas pelo proxy
  pertençam ao caso sem ambiguidade; k rodadas por caso (padrão 5), cada uma com um telefone sintético
  novo; coleta as saídas (`sim.saidas`), as ferramentas pedidas (proxy) e a situação da conversa
  (`sim.sql`); grava `eval-<data>.json` e uma tabela Markdown com caso, pass^k, tokens médios e
  segundos médios.
- A avaliação fica em funções puras em `scripts/homologacao/avaliacao.py`.
- `agents/atendimento-whatsapp/evals/linha-de-base.md`: data, commit, modelo, k, pass^k por caso e total,
  tokens e tempo médios.
**Testes (Red).** `python -m unittest scripts/homologacao/test_avaliacao.py`:
- caso que escala sem que o esperado peça escalada falha;
- ferramenta proibida chamada falha; ferramenta exigida ausente falha;
- texto proibido presente falha;
- pass^k é 0 quando uma das k rodadas falha.
**Aceite.** Testes verdes; eval roda contra a pilha local com k=5; linha de base gravada; nenhum dado
pessoal nos casos (nomes e telefones sintéticos, como os da bateria).
**Rollback.** Apagar a pasta `evals/` e os scripts novos. Nada em produção muda.
**Tier.** BAIXO (spec de plano; só script e dado de teste).

### S62 · Perfil do agente em arquivo, sem mudar comportamento

**Problema.** Prompt, nome da loja, limites e lista de ferramentas estão presos no código: mudar
qualquer um exige editar C# e não há como outra loja usar o agente.
**Abordagem.** Carregar um `PerfilAgente` de `agents/atendimento-whatsapp/` com os mesmos valores de
hoje. O prompt sai do C# para Markdown com marcadores; limites e ferramentas permitidas vêm do perfil.
**Leitura mínima.** `App/Services/Atendimento/PromptAtendimento.cs`, `AgenteAtendimentoService.cs`,
`GeracaoAgenteAtendimento.cs`, `App/DependencyInjection/ServiceCollectionExtensions.Atendimento.cs`,
`Integrations/Ia/AgenteLlmServiceCollectionExtensions.cs`, `Dockerfile`,
`EasyStock.ArchitectureTests/PlataformaNaoDependeDoAtendimentoTests.cs`.
**Escopo.**
- `agents/agent.schema.json` e `agents/atendimento-whatsapp/agent.json`: modelo atual, `maxPassos` 6,
  `janelaMensagens` 20, as 19 ferramentas, camadas na ordem atual (prompt, caderno, dossiê, resumo).
- `agents/atendimento-whatsapp/prompts/sistema.md`: o texto atual com `{{loja.nome}}` no lugar de
  "Casa da Baba".
- `App/Services/Agentes/PerfilAgente.cs` (record e validação) e `RenderizadorPrompt.cs` (marcadores
  `{{chave}}`; marcador desconhecido é erro).
- `App/Ports/Output/Ai/IPerfisAgente.cs`: `PerfilAgente Obter(string id)`.
- `Integrations/Ia/Perfis/PerfisAgenteEmArquivo.cs`: lê a pasta, valida no start contra o registro de
  ferramentas e calcula o SHA-256 do conteúdo.
- `AgenteAtendimentoService` e `GeracaoAgenteAtendimento`: limites, janela e ferramentas do perfil;
  prompt do arquivo com o nome da loja de `Empresa.NomeFantasia`, ou `Empresa.Nome` quando vazio.
  `PromptAtendimento.cs` sai.
- `agents/**` copiado para a saída do host que roda o turno e para a imagem (`COPY` no `Dockerfile`).
- `ArchitectureTests/AgentesNaoDependemDoAtendimentoTests.cs`: `Services.Agentes` não depende de
  `Services.Atendimento` nem das entidades de atendimento.
**Testes (Red).**
- `PerfilAgenteTests`: ferramenta fora do registro recusa; `maxPassos` fora de 1 a 12 recusa; camada
  desconhecida recusa.
- `RenderizadorPromptTests`: marcador desconhecido falha; nome com caractere especial entra literal.
- Snapshot: `sistema.md` renderizado para "Casa da Baba" é igual ao `PromptAtendimento.snapshot.txt`
  byte a byte.
- `AgenteAtendimentoServiceTests`: com perfil de 3 passos, ao atingir o limite faz o mesmo que hoje faz
  ao atingir 6; ferramenta fora da lista não vai na requisição.
- Teste de arquitetura verde.
**Aceite.** Testes verdes; gate verde; eval da S61 com pass^k por caso igual ou melhor que a linha de
base. Caso que piorar roda de novo com k=10 antes do merge e o resultado vai na PR.
**Rollback.** Reverter a PR; o prompt volta ao C#.
**Tier.** ALTO (csproj e `Dockerfile`).

### S63 · Adaptador `IChatClient` e modelo por papel

**Problema.** Trocar de provedor exige reescrever o cliente HTTP, e o mesmo modelo atende, resume e
preenche. Não há modelo reserva quando o principal cai.
**Abordagem.** `ChatClientAgenteLlmClient` em `Integrations` implementa `IAgenteLlmClient` sobre
`Microsoft.Extensions.AI`; provedores registrados por chave (`AddKeyedChatClient`); o perfil escolhe
provedor e modelo por papel, com reserva.
**Leitura mínima.** `App/Ports/Output/Ai/IAgenteLlmClient.cs`, `Integrations/Ia/AnthropicMessagesClient.cs`,
`AnthropicAgenteOptions.cs`, `AgenteLlmServiceCollectionExtensions.cs`.
**Escopo.**
- Pacote `Microsoft.Extensions.AI` e o cliente do provedor (SDK oficial `Anthropic`, que implementa
  `IChatClient`) só no `Integrations`. Application não referencia nenhum dos dois.
- `RequisicaoLlm` ganha `ModeloLlm? Modelo` (provedor, id, temperatura, teto de saída). Nulo mantém o
  comportamento atual.
- `MapeadorLlm`: `BlocoTextoLlm` e `TextContent`; `BlocoUsoFerramentaLlm` e `FunctionCallContent`;
  resultado de ferramenta e `FunctionResultContent`; `BlocoOpacoLlm` pela `RawRepresentation`, devolvido
  inalterado.
- `ChatFinishReason.ToolCalls` normalizado para `tool_use`.
- Reserva: falha transitória do principal (circuito aberto, 429 ou 5xx depois do retry) tenta o reserva
  uma vez no mesmo turno e registra a troca.
- `agent.json` ganha `papeis` (`atender`, `resumir`) com `provedor`, `modelo` e `reserva`.
**Testes (Red).**
- `MapeadorLlmTests`: ida e volta de cada bloco; bloco opaco intacto; schema da ferramenta preservado.
- `ChatClientAgenteLlmClientTests` com `IChatClient` falso: modelo e temperatura do perfil chegam no
  `ChatOptions`; tokens voltam na `RespostaLlm`; falha do principal chama o reserva uma vez; falha não
  transitória não chama.
- Os testes do `AnthropicMessagesClient` continuam verdes sem mudança.
**Aceite.** Testes e gate verdes; eval com o adaptador novo e o mesmo modelo: pass^k igual ou melhor que
a linha de base; tabela lado a lado (pass^k, tokens, custo, segundos) na PR.
**Rollback.** O perfil volta ao provedor do cliente HTTP atual. Os pacotes ficam.
**Tier.** ALTO (csproj e caminho do modelo).

### S64 · Registro e custo por turno

**Problema.** `UsoIa` só soma tokens por mês. Não dá para comparar modelos, saber o custo de um turno
nem achar qual versão do perfil gerou uma resposta ruim.
**Abordagem.** Um registro estruturado por turno, sem texto da conversa, com custo estimado por tabela
de preços versionada.
**Leitura mínima.** `AgenteAtendimentoService.cs`, `GeracaoAgenteAtendimento.cs`, `Domain/Entities/UsoIa.cs`.
**Escopo.**
- `agents/precos.json`: preço por milhão de tokens de entrada, saída e cache lido, por provedor e
  modelo, com a data da tabela.
- `App/Services/Agentes/RegistroTurno.cs`: empresa, id da conversa, perfil e hash, provedor, modelo,
  passos, tokens (entrada, saída, cache), custo estimado, escalou, duração. Nunca texto, telefone ou nome.
- Log estruturado no fim de cada turno, e um `Activity` com os atributos `gen_ai.*` quando a fonte já for
  coletada.
- `eval.py` passa a ler o custo médio por turno desse registro.
**Testes (Red).**
- `RegistroTurnoTests`: custo é tokens vezes preço; modelo sem preço registra custo nulo e avisa; o
  registro não contém texto da mensagem nem telefone.
**Aceite.** Testes e gate verdes; um turno do eval aparece no log com custo e hash do perfil.
**Rollback.** Reverter a PR; `UsoIa` não muda.
**Tier.** BAIXO (spec de plano, sem migração). Se ligar a fonte de telemetria exigir `Program.cs`, ALTO.

### S65 · Skills e roteamento (a especificar)

Habilidades em `agents/<id>/skills/<nome>/SKILL.md`, com nome e descrição no cabeçalho. Só o índice
(nome e descrição) vai no prompt; o corpo é carregado sob demanda por uma ferramenta, no mesmo molde
do `consultar_caderno` (S54). Cada habilidade pode restringir as ferramentas do turno. Roteamento por
intenção com modelo menor só se o eval mostrar ganho. Decidir aqui como ler o cabeçalho sem pacote novo.

### S66 · Ajuste por loja no console (substituída pela Onda 14, [19-engrenagem-do-assistente.md](19-engrenagem-do-assistente.md), ADR-0066)

A loja ajusta, dentro dos limites que o perfil define: tom, saudação, instruções curtas extras, liga e
desliga de habilidade. Nunca modelo, ferramenta, guarda nem gatilho de escalada. Guardado em
`ConfiguracaoAtendimento`, com migração (tier ALTO), e coberto por caso de eval.
