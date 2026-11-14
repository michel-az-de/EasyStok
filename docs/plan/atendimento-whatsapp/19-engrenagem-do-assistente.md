# Onda 14 · Engrenagem do Assistente (S67–S70)

Issue: #2518 · Decisão: [ADR-0066](../../adr/0066-modelo-por-loja-e-cota-do-assistente.md) · Base:
[Onda 13](18-harness-do-agente.md) (ADR-0065). Esta onda SUBSTITUI a S66 da Onda 13 e antecipa a S64.

Objetivo: a dona ajusta o Assistente por uma engrenagem discreta, sem saber o que é prompt, e vê o que
ele respondeu, quanto gastou e com qual modelo. Critério de pronto da onda: a dona muda o nome dela,
o tom e uma resposta pronta, vê o efeito na próxima resposta do robô e entende o consumo do mês sem
ajuda.

## Decisões do Felipe (13/11/2026, 23:0x, múltipla escolha)

| Tema | Decisão |
|---|---|
| Lugar | Engrenagem no topo da lista do Atendimento. Ela abre um painel lateral com abas. |
| Prompt | Campos guiados. O prompt final aparece só para leitura, e as regras de preço, pagamento e entrega ficam protegidas no código. |
| Cota | Limite mensal de tokens por loja, com alerta em 80%. Ao estourar, a conversa passa para a dona e o robô não fica mudo. |
| Modelo | A loja escolhe entre modelos aprovados. Muda a ADR-0065, que dizia "nunca modelo" (ver ADR-0066). |
| Uso | Sutil e extremamente fácil: um clique para abrir, palavras do dia a dia, exemplo em cada campo, salvar com aviso "Salvo". |

## O que já existe (origin/master `a5712bc0c`)

| Peça | Onde | Lacuna |
|---|---|---|
| Configuração por loja | `ConfiguracaoAtendimento` (tom, nível de sugestão, saudações, espera, fora da área, escalada, encaminhamento, ativo) | Fica em Gestão > Atendimento automático, longe do chat |
| Prompt | `PromptAtendimento.Montar` | "Casa da Baba" está fixo (linha 26), e `EscalarParaDonaFerramenta` diz "a Baba vai continuar". A dona não tem nome configurável |
| Modelo | `AnthropicAgenteOptions.ModeloAgente`, global no appsettings | Não existe modelo por loja |
| Consumo | `UsoIa`: contador mensal por loja | Mistura Assistente e gerador de anúncios. Não tem limite nem leitura pela API |
| Histórico | `atendimento_mensagens` (Autor = Agente) | Não registra o turno: ferramentas chamadas, tokens, modelo e duração |
| Checagem | Modal "Checar o Assistente" (#2496) | Mostra o prompt e as ferramentas de uma conversa; não serve para configurar |

## O painel

```
Atendimento  [⚙]  ← engrenagem discreta no topo da lista; ponto âmbar quando o uso passa de 80%
┌ Assistente ────────────────────────────────────────── × ┐
│ [Geral] [Roteiro] [Respostas prontas] [Histórico] [Uso]  │
└──────────────────────────────────────────────────────────┘
```

| Aba | Conteúdo | Quem edita |
|---|---|---|
| Geral | Liga e desliga (já existe), nome da dona (ex.: "Michele"), tom, nível de sugestão, modelo (lista aprovada, com custo relativo "padrão", "econômico") e observações da loja (até 500 caracteres) | Admin |
| Roteiro | As etapas do atendimento, em ordem. Cada uma tem nome, instrução curta e liga e desliga, mais o botão "Voltar ao padrão". O conteúdo padrão vem da frente de conduta (#2518 só guarda e edita) | Admin |
| Respostas prontas | As mensagens da `ConfiguracaoAtendimento`, movidas de Gestão; Gestão passa a mostrar um link para cá | Admin |
| Histórico | Últimos turnos, com hora, cliente, pergunta → resposta, ferramentas em chips, tokens, modelo e o link "abrir conversa". Tem filtro por conversa e por "escalou" | Leitura |
| Uso | Barra do mês (tokens do Assistente ÷ cota), custo estimado, previsão de fim do mês e o campo da cota | Admin edita a cota |

Rodapé de Geral: o link "Ver instruções completas" abre só para leitura o prompt final, reaproveitando o
que a #2496 já mostra.

## Fatias

### S67 · Registro do turno do Assistente (antecipa a S64)

**Problema.** Sem o registro do turno não há histórico, consumo do Assistente separado dos anúncios,
nem como achar o motivo de uma resposta ruim.

**Abordagem.** É a S64 da Onda 13, gravada em tabela em vez de só em log, porque o painel precisa ler
os registros.

**Escopo.**
- Tabela `atendimento_turnos_agente`, com: `EmpresaId`, `ConversaId`, `MensagemClienteId`, `Modelo`,
  `TokensEntrada`, `TokensSaida`, `TokensCacheLido`, `CustoEstimado` (nulo sem preço), `Ferramentas`
  (jsonb, com nome e ok), `Passos`, `Resultado` (respondeu, escalou, cota, erro), `DuracaoMs` e
  `CriadoEm`.
  - A tabela não guarda texto nem telefone. O texto vem de `atendimento_mensagens` pelos ids.
  - Índice em (`EmpresaId`, `CriadoEm`).
- `agents/precos.json`, como na S64.
- `GET api/atendimento/assistente/historico` (paginado, filtro por conversa e por resultado) e
  `GET api/atendimento/assistente/uso` (mês, tokens, custo, cota e percentual).

**Testes (Red).**
- O turno grava um registro com os tokens que o cliente LLM devolveu.
- Custo é tokens vezes preço; modelo sem preço dá custo nulo.
- O registro não contém o texto.
- O uso soma só o Assistente.

**Aceite.** Um turno em produção aparece no histórico com ferramentas, tokens e modelo.

**Rollback.** Reverter a PR. A migração só cria a tabela, então dá para descer sem perda.

**Tier.** ALTO (migração).

### S68 · Configuração guiada por loja (substitui a S66)

**Escopo.**
- Campos novos em `ConfiguracaoAtendimento`, todos anuláveis (nulo = padrão):
  - `NomeDona` (60);
  - `ModeloAgente`;
  - `CotaTokensMes`;
  - `RoteiroJson` (jsonb);
  - `ObservacoesLoja` (500).
- Nome da loja e da dona:
  - `PromptAtendimento` e `EscalarParaDonaFerramenta` usam o nome da loja e `NomeDona` no lugar de
    "Baba";
  - sem nome configurado, usam "a responsável".
- Lista aprovada: `Anthropic:ModelosAprovados` (id, rótulo, custo relativo). O PUT recusa modelo fora da
  lista, e o turno usa o modelo da loja ou o padrão.
- Um modelo só entra na lista com eval verde (S61 da Onda 13), comparando pass^k e custo lado a lado. A
  lista nasce com o modelo atual.
- `GET api/atendimento/assistente/modelos`.
- O GET e o PUT de configuração ganham os campos novos.

**Testes (Red).**
- O prompt sem `NomeDona` não contém "Baba".
- Com `NomeDona`, a escalada cita o nome.
- O PUT com modelo fora da lista dá 400.
- O turno usa o modelo da loja.
- O snapshot de `PromptAtendimentoTests` é atualizado de forma consciente.

**Aceite.** Em produção, a dona grava o nome dela e a próxima escalada do robô cita esse nome.

**Rollback.** Reverter a PR. Colunas nulas mantêm o comportamento atual.

**Tier.** ALTO (migração, modelo e guarda, D7 da ADR-0065).

### S69 · Cota com alerta

**Escopo.**
- Antes de chamar o modelo, o turno lê o uso do mês (S67).
- Com o uso em 100% ou mais da `CotaTokensMes`:
  - o turno não chama o modelo;
  - a conversa passa para a dona pelo caminho da escalada que já existe (`MensagemEscalada` ao cliente);
  - entra a nota de sistema "Cota do Assistente esgotada no mês";
  - o registro do turno sai com `Resultado = cota`.
- Com o uso em 80% ou mais, o `GET uso` devolve `alerta = true`, e a engrenagem mostra o ponto âmbar.

**Testes (Red).**
- Em 99% responde normalmente.
- Em 100% escala sem chamar o LLM. Um dublê do cliente LLM prova que zero chamadas foram feitas.
- Sem cota, nunca bloqueia.
- O alerta liga em 80%.

**Aceite.** Uma cota baixa de teste em homologação escala a conversa com a nota de sistema.

**Rollback.** Reverter. Cota nula desliga a regra.

**Tier.** ALTO (limite, D7 da ADR-0065).

### S70 · Painel da engrenagem no console

**Escopo.**
- Ícone de engrenagem no topo da lista do Atendimento, que abre o painel lateral com as 5 abas acima.
- Salvamento por campo, com aviso "Salvo".
- Edição só para Admin; quem não é Admin vê os dados em modo de leitura.
- Funciona na largura do celular.
- Gestão > Atendimento automático vira um link para a engrenagem.
- A aba Roteiro entra só quando o formato do roteiro da frente de conduta estiver no master.

**Testes (Red).** Uma prova no estilo de `ferramentas/prova-2496-*.mjs` cobre:
- o mapeamento das abas;
- o PUT com só os campos alterados;
- o alerta a partir de 80%;
- a leitura sem a ação de editar.

**Aceite.**
- Print, no console real, das 5 abas com os dados da Casa da Baba.
- A dona troca o tom e vê o efeito na próxima resposta.

**Rollback.** Reverter. A API continua igual.

**Tier.** BAIXO (spec aprovada, sem migração).

## Ordem e dependências

```
S67 registro ──► S69 cota
     │
S68 config ────► S70 painel (Roteiro depois do formato da frente de conduta)
```

- S67 e S68 podem andar em paralelo, porque não tocam os mesmos arquivos. Exceção: `AgenteAtendimentoService`,
  que as duas tocam. Para resolver, S68 entra depois de S67.
- A frente de conduta do Assistente (sessão "Corrigir falhas de conduta") é dona do CONTEÚDO do prompt e do
  roteiro padrão. A S68 é dona do armazenamento e dos campos. Quem chegar primeiro em `PromptAtendimento.cs`
  rebaseia o outro.

## Fora de escopo

- Texto livre do prompt.
- Modelo fora da lista aprovada.
- Preço real faturado pelo provedor: o custo do painel é estimado pela tabela versionada.
- Habilidades (S65).
