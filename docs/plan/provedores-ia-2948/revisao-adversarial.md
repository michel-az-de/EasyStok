# Revisão adversarial da spec #2948 (20/11/2026)

Três revisores independentes, somente leitura, contra o master `6c93bfe0a`: premissas contra o código, segurança e tenant, plano e rollout. Os achados B1, B2, B3 e C5 foram conferidos de novo à mão.

## Bloqueiam

| # | Achado | Evidência | Correção |
|---|---|---|---|
| B1 | A tabela `uso_ia` já existe: é o agregado mensal `UsoIa` (Ano, Mes, TotalGeracoes, TotalTokens), gravado a cada turno | `Domain/Entities/UsoIa.cs`, `UsoIaConfiguration.cs:7,18`, `AgenteAtendimentoService.cs:371-393`, ADR-0066:45 | Nova tabela `chamada_ia`. Definir uma fonte única de soma (hoje são `TurnoAgente` e `UsoIa`) |
| B2 | `Ia = 6` colide com `Mapas = 6` | `CategoriaIntegracao.cs:26` | `Ia = 7` |
| B3 | `IAgenteLlmClient.Disponivel` é global, sem loja, e é lido em 12 pontos | `IAgenteLlmClient.cs:17`, `PortoesDoAssistente.cs:138`, `ConversaChatSiteService.cs:80`, `AssistenteDoCardapioUseCase.cs:44,71`, `AssistenteDonaUseCase.cs:68`, `ObterFluxoAssistenteUseCase.cs:42` e outros | `DisponivelAsync(empresaId, ct)`. Listar os 12 pontos na S2 |
| B4 | SSRF: a `base_url` editável faz a API enviar a chave para qualquer host, inclusive a rede interna da VPS | Nenhuma validação de destino no código. O corpo do erro vai ao log (`AnthropicMessagesClient.cs:66-68`) | Só https na porta 443, sem redirect, bloqueio de IP privado e de metadata no `ConnectCallback`, `url_saldo` no mesmo host. Lista de hosts permitidos (Anthropic, DeepSeek, Fireworks); qualquer outro só por SuperAdmin |

## Corrigir na spec

| # | Achado | Correção |
|---|---|---|
| C1 | `EmpresaId` na requisição escolhe a chave sem o RLS proteger. Sob bypass (`AtendimentoFilaTurnoAgenteBackgroundService.cs:98`), qualquer loja lê qualquer chave | O roteador exige `req.EmpresaId == tenant atual`. Teste nos 3 caminhos: HTTP, webhook e job |
| C2 | A cota em US$ soma e depois compara. Turnos simultâneos estouram o orçamento N vezes | Reserva atômica antes da chamada, ou declarar cota "suave" com teto de estouro |
| C3 | O custo pelo preço de pico dobra o custo fora do pico, então a cota corta com cerca de metade do orçamento | Custo por faixa horária (pico ou fora), ou avisar na tela |
| C4 | O cliente atual não serve sem refatoração: tem `BaseAddress` e timeout fixos, opções globais e disjuntor estático | URI absoluta por requisição, `ResiliencePipelineRegistry` com chave por provedor. Decidir se o 429 abre o disjuntor |
| C5 | O gerador de descrições é streaming SSE, e `EnviarAsync` não é. A S5 também apaga o OpenAI (`gpt-4o-mini`), que não fala o formato Anthropic | O roteador ganha streaming, ou o gerador fica fora. Declarar a perda do OpenAI |
| C6 | Faltam chamadores: `SugerirReceita`, `AssistenteDoCardapio`, `SugerirCadastroConversa`, `ResumirCliente` e a ficha pelo CLI do Claude. Os options da S5 têm consumidores não citados (`AgenteAtendimentoService:165,185`, endpoint `Modelos`, `ModeloValidado`) | Inventário completo na S2. `EmpresaId` e `Finalidade` com valor padrão em `RequisicaoLlm` para não quebrar dezenas de testes |
| C7 | O cofre é um catálogo fechado: `provider_key` único por `(empresa, key, ambiente)`, e `ObterAsync<T>` exige ambiente | `provider_key = "ia:{provedorId}"`, ambiente `Production` |
| C8 | Constraints não estão declaradas | Índice único parcial `(empresa_id) WHERE padrao`; FK composta `(empresa_id, provedor_id)`; `custo_usd numeric`; migração de `modelo_agente` em 2 etapas, contando as lojas que caírem no padrão; semear o provedor atual (Fireworks) a partir do `.env` |
| C9 | O job de saldo é cross-tenant | O bypass só lista; a chave é lida no tenant de cada loja. Entra na allowlist de RLS com issue |
| C10 | Chave no erro: um host malicioso ecoa qualquer texto | Sanitizar o corpo. O Testar devolve só código e frase fixa |
| C11 | A S2 é grande e mexe no atendimento em produção. A S5 não tem rollback | S2a só registra uso (valor cedo). S2b liga o roteador atrás de flag por loja. S5a e S5b, com o gate `COUNT` de lojas sem provedor = 0 |
| C12 | Fatos da DeepSeek só da documentação: nomes de modelo, remapeamento, `tool_use` em `/anthropic` | Spike de uma chamada real com ferramentas antes da S1 |

## Cortes sugeridos (YAGNI)

| Item | Sugestão |
|---|---|
| Job de saldo e `url_saldo` | Adiar. O 402 já avisa |
| `prioridade` e troca automática de provedor | Adiar. Cota estourada passa para a equipe |
| `contexto_max` e `saida_max` | Cortar |

## Resistiu ao ataque

- Pool de conexão e RLS.
- Cache de credencial por loja.
- Cofre AES-GCM.
- S1 aditiva e implantável sozinha.
- Formato só Anthropic.
- Registro de uso do nosso lado.
- Fatos de `compose.yaml:67-71`, `AnthropicAgenteOptions`, `PrecosModelos`, disjuntor único e `DaCotaAsync`.
