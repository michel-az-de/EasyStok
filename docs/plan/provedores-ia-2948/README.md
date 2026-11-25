# Provedores e modelos de IA por loja (#2948)

- Status: proposta v2, revisada por uma revisão adversarial (`revisao-adversarial.md`); aprovada pelo Felipe em 21/11 ("Sim, aprovo", https://github.com/michel-az-de/EasyStok/issues/2948#issuecomment-7066321177). Cada PR de tier alto segue esperando a label `aprovado`
- Data: 2026-11-20
- Tier: alto (migração, cofre, `Program.cs` e DI)
- Relacionados: ADR-0010 (RLS), ADR-0061 (cofre e conexões), ADR-0066 (modelo por loja e cota), #1305 (DeepSeek via Fireworks), S67 (`TurnoAgente`)

## 1. Problema

A IA tem uma chave só, global, no `.env` da VPS. Por isso:

| Dor | Causa medida |
|---|---|
| Não dá para saber o gasto em US$ por provedor | O custo sai de `Anthropic:Precos`. Só o atendimento grava uso, em `TurnoAgente` e no agregado mensal `UsoIa` (`AgenteAtendimentoService.cs:371-393`) |
| Trocar de provedor ou de modelo exige deploy | `Anthropic__BaseUrl` e `Anthropic__ModeloAgente` ficam em `scripts/deploy/producao/compose.yaml:67-71` |
| Não dá para ter mais de uma API | `AnthropicAgenteOptions` tem uma `BaseUrl` e uma chave. `IAgenteLlmClient.Disponivel` vale para a plataforma inteira (`IAgenteLlmClient.cs:17`) |
| Parte do uso fica invisível | `GeradorAutoPreenchimentoClaude` e `OpenAI` usam clientes próprios; ficha, resumo e sugestões não registram uso |

## 2. Decisões

| # | Decisão | Origem |
|---|---|---|
| D1 | A chave é de cada loja, no cofre por loja (`CredencialIntegracao`, AES-256-GCM, RLS) | Felipe, 20/11 |
| D2 | A cota é em US$ por mês, por provedor. A `CotaTokensMes` atual continua como está | Felipe, 20/11 |
| D3 | Só o formato Anthropic Messages. O gerador OpenAI (`gpt-4o-mini`) sai na S5, e isso é perda declarada | Felipe, 20/11 |
| D4 | Toda chamada de IA entra no registro de uso | Felipe, 20/11 |
| D5 | Cortes: sem job de saldo, sem `url_saldo`, sem prioridade nem troca automática de provedor, sem `contexto_max` e `saida_max` (emenda da #3304, R8 da revisão do harness: `saida_max` voltou, por modelo, e o provedor ganhou `prazo_segundos`, ambos opcionais; `contexto_max` segue cortado). Orçamento estourado passa o atendimento para a equipe; o 402 avisa a falta de saldo | Felipe, 20/11 (revisão); emenda de 25/11 (#3304): Felipe mandou atuar em todos os achados da revisão do harness (#3271), R8 incluída |
| D6 | A cota é suave: a chamada só começa com gasto do mês abaixo do orçamento, sem reserva atômica. O estouro máximo é o custo das chamadas já em voo, e a tela diz isso | proposta |
| D7 | O custo usa o preço da faixa horária: pico ou fora do pico, por horário UTC. Sem isso a cota cortaria com cerca de metade do orçamento | proposta |
| D8 | A URL do provedor vem de uma lista fechada de hosts (Anthropic, DeepSeek, Fireworks). Host fora da lista só por SuperAdmin, com as travas de rede da seção 6 | proposta |

## 3. Fatos da DeepSeek (documentação lida em 20/11; a confirmar na S0)

| Item | Valor |
|---|---|
| URL no formato Anthropic | `https://api.deepseek.com/anthropic` (o cliente chama `v1/messages` relativo a ela) |
| Autenticação | `x-api-key` ou `Authorization: Bearer` |
| Modelos | `deepseek-flash` (contexto de 1M, com imagem) e `deepseek-v4-pro` (contexto de 1M, sem imagem) |
| Preço por 1M tokens, pico | flash: cache lido US$ 0,006, entrada 0,30, saída 1,20. pro: cache lido 0,044, entrada 1,32, saída 3,96 |
| Fora do pico | Metade. O pico vai das 01h às 04h e das 06h às 10h UTC, de segunda a sexta (22h às 01h e 03h às 07h em Brasília) |
| Limite | Concorrência por conta: 2500 no flash e 500 no pro; acima volta 429 |
| Sem saldo | HTTP 402 |
| Diferenças | `cache_control` e `anthropic-version` são ignorados; `cache_creation_input_tokens` vem 0; `claude-*` desconhecido vira `deepseek-flash` em silêncio |
| Histórico de uso por API | Não documentado; por isso o uso é registrado do nosso lado |

## 4. Modelo de dados

Todas as tabelas novas têm `empresa_id` e RLS (ADR-0010). Dinheiro é `numeric(12,6)`, nunca `double`.

```
credencial_integracao (existe)            provedor_ia
  categoria = Ia (nova, 7)     <──1:1──     id, empresa_id, nome
  provider_key = "ia:{provedor_id}"          base_url, autenticacao (x_api_key | bearer)
  ambiente = Production                      ligado, orcamento_mensal_usd (nulo = sem cota), prazo_segundos (nulo = o do .env)
  payload cifrado {apiKey}                   credencial_id (FK, único)
  mascara, ultimo_teste_*                         │1
                                                  │N   FK composta (empresa_id, provedor_id)
                                             modelo_ia
                                               id, empresa_id, provedor_id, id_externo, rotulo, ligado, padrao
                                               preco_entrada, preco_saida, preco_cache_lido (US$/1M, pico)
                                               fator_fora_pico (padrão 1; DeepSeek 0,5), saida_max (máximo do modelo; nulo = o teto do .env)
                                                  │1
                                                  │N
                                             chamada_ia (só inserção)
                                               id, empresa_id, provedor_id, modelo_id
                                               modelo_respondido, finalidade (enum), turno_agente_id (nulo)
                                               tokens entrada, saída, cache lido, cache escrito
                                               custo_usd (nulo sem preço), duracao_ms
                                               desfecho (ok, erro, sem_saldo_402, limite_429, cota_estourada)
                                               criado_em
```

| Regra | Como |
|---|---|
| Um modelo padrão por loja | Índice único parcial `modelo_ia (empresa_id) WHERE padrao`. A troca de padrão desmarca e marca na mesma transação |
| Sem referência entre lojas | FK compostas `(empresa_id, provedor_id)` e `(empresa_id, modelo_id)` |
| Soma do mês sem ler a tabela | Índice `chamada_ia (empresa_id, provedor_id, criado_em) INCLUDE (custo_usd)` |
| Nome | `chamada_ia`, porque `uso_ia` já existe: é o agregado mensal `UsoIa` (Ano, Mes, TotalGeracoes, TotalTokens), que continua como está (ADR-0066). A cota em US$ soma só `chamada_ia` |
| Retenção | Fica tudo até medir volume. Hoje são algumas centenas de chamadas por dia |
| O que não grava (S2b, #3064) | Chamada que não saiu (disjuntor aberto, cliente desligado, loja sem rota ou sem chave) não grava: não tem token nem custo e só inflaria a contagem; vira log. Cancelamento de quem chamou também não grava, porque os tokens são desconhecidos; o estouro possível é o das chamadas em voo (D6). Orçamento estourado grava `cota_estourada`, que é evento de negócio. Vale também para o caminho do `.env`, inclusive para loja sem flag: o disjuntor aberto deixa de contar como Erro, e o cliente desligado passa a lançar `ProvedorIaIndisponivelException` (antes `InvalidOperationException`; os chamadores já checam a disponibilidade antes). O retry do turno não repete essa exceção |
| Cota no portão (S2b, #3064) | Os chamadores checam `DisponivelAsync` antes de enviar, então é o portão que grava `cota_estourada`, uma vez por requisição e provedor (finalidade `outra`); o envio na mesma requisição não duplica. O portão avalia o modelo padrão da loja; um modelo pedido em outro provedor só é avaliado no envio. Telas que só checam o status (fluxo do assistente, diagnóstico) e cada mensagem do chat do site também gravam essa linha enquanto a cota estiver estourada; a S5 (painel) separa por finalidade ou passa a finalidade real ao portão |
| Fora da cota até a S5 | Auto-preenchimento (`GeradorAutoPreenchimentoClaude` e `OpenAI`) até a S5a, e a ficha pelo CLI do Claude até a S5b: o auto-preenchimento não grava `chamada_ia`, e a ficha pelo CLI grava sem provedor, então a soma do provedor não os vê. A tela da S4 diz isso |
| Modelo sem preço | Provedor com orçamento e modelo sem preço de entrada ou saída: a loja fica indisponível (falha fechada, log próprio), porque a cota ficaria muda. Sem orçamento, custo nulo é aceito. A S4 não deixa ligar modelo sem preço em provedor com orçamento |

Migração do modelo da loja, em duas etapas:
1. A S1 cria `configuracao_atendimento.modelo_ia_id` nulo e semeia, para cada loja que hoje usa IA, o provedor atual lido do `.env` (Fireworks, `deepseek-v4p1-flash`) sem chave, desligado. Sem esse seed a conversão casaria às cegas.
2. A S2b casa `modelo_agente` (texto) com `id_externo` em runtime: o roteador procura um modelo ligado da loja com aquele `id_externo` e, sem casamento, usa o padrão e registra no log cada queda no padrão. O seed do item 1 e o preenchimento de `modelo_ia_id` foram para a S4 (decisão de 21/11 na #3064): sem chave no cofre nada roda pelo roteador, e a chave só entra pela tela da S4. A coluna de texto só sai na S5b.

## 5. Fluxo da chamada

```
caso de uso ──RequisicaoLlm(EmpresaId, Finalidade)──> RoteadorIa (atrás de IAgenteLlmClient)
   0. EmpresaId tem de ser o tenant atual (ITenantContextAccessor); se divergir, falha
   1. modelo pedido ligado? senão o padrão da loja
   2. provedor do modelo ligado e gasto do mês < orçamento? senão Disponivel=false (a equipe atende)
   3. chave do cofre (IIntegrationCredentialResolver) -> AnthropicMessagesClient com URL e auth do provedor
   4. grava chamada_ia (ok, erro, 402, 429), com custo pela faixa horária
```

| Peça | Decisão |
|---|---|
| Disponibilidade | `bool Disponivel` vira `Task<bool> DisponivelAsync(Guid empresaId, CancellationToken)`. São 12 pontos de uso, entre eles `PortoesDoAssistente.cs:138`, `ConversaChatSiteService.cs:80`, `AssistenteDoCardapioUseCase.cs:44,71`, `AssistenteDonaUseCase.cs:68` e `ObterFluxoAssistenteUseCase.cs:42`. O inventário fecha na S2a |
| `RequisicaoLlm` | `EmpresaId` e `Finalidade` entram como propriedades com valor padrão, para não quebrar as dezenas de `new RequisicaoLlm(` nos testes |
| Tenant | O webhook chama `SetCurrentTenant` (`ProcessarEventoWhatsAppUseCase.cs:102`); a fila do turno roda por `empresaId` do job. Sob bypass de RLS (`AtendimentoFilaTurnoAgenteBackgroundService.cs:98`) o roteador recusa a chamada sem tenant definido |
| Cliente HTTP | `AnthropicMessagesClient` passa a usar URI absoluta por requisição. O timeout por provedor vem de um `CancellationTokenSource`, e as opções globais ficam só como reserva até a S5b |
| Disjuntor | Um por provedor, por `ResiliencePipelineRegistry.GetOrAddPipeline("ia:{provedorId}")`. O 429 não abre o disjuntor: é limite de concorrência, não queda |
| Streaming | O roteador ganha `EnviarStreamAsync` (SSE do formato Anthropic) na S5a, para o gerador de descrições continuar mostrando o texto enquanto gera |
| Chamadores | Todos passam pelo roteador com finalidade: atendimento, assistente da dona, assistente do cardápio, resumo do cliente, sugestão de cadastro, sugestão de receita, ficha (inclusive o caminho pelo CLI do Claude, que passa a gravar `chamada_ia`) e gerador de descrições |

## 6. Segurança

| Risco | Trava |
|---|---|
| SSRF pela `base_url` | Só `https` na porta 443, sem userinfo e sem seguir redirect. Host da lista fechada (D8). No `ConnectCallback` do `SocketsHttpHandler`, recusa IP privado, loopback, link-local e metadata, o que barra DNS rebinding. Resposta limitada em tamanho |
| Chave de outra loja | Passo 0 do fluxo. Teste de integração nos 3 caminhos: HTTP, webhook e job |
| Chave em log ou na tela | O corpo de erro é sanitizado (a chave vira `***`) antes do log. O botão Testar devolve só código e frase fixa, nunca o corpo cru. Teste que procura a chave em log, exceção e resposta |
| Quem muda | Política `Admin` existente (`IntegracoesController.cs:19`). `Gerente` só lê o painel. Host fora da lista só para `SuperAdmin` |

## 7. Fatias

| # | Entrega | Aceite mensurável | Rollback | Tier |
|---|---|---|---|---|
| S0 | Spike: uma chamada real à DeepSeek em `/anthropic` com ferramentas, pelo `.env` de homologação | `tool_use` volta parseado, `modelo_respondido` = `deepseek-flash`, tokens preenchidos | Nada a desfazer | Baixo |
| S1 | Domínio (`ProvedorIa`, `ModeloIa`, `ChamadaIa`), `CategoriaIntegracao.Ia = 7`, migração com RLS, constraints da seção 4, seed do provedor atual sem chave | Migração sobe e desce; teste de RLS impede ler provedor de outra loja; índice único parcial rejeita dois padrões | Descer a migração (só aditiva) | Alto |
| S2a | Registro de uso: todos os chamadores passam `EmpresaId` e `Finalidade`, e cada chamada grava `chamada_ia` com o cliente e o `.env` atuais | Teste de contagem: N chamadas geram N linhas; painel do mês já soma | Reverter o PR; a tabela fica vazia | Alto |
| S2b | Roteador por loja, `DisponivelAsync`, chave do cofre, travas da seção 6, cota suave (aviso no log a partir de 80%) e custo por faixa horária. Atrás da flag `ia.provedor_por_loja`; sem flag, a loja segue no `.env`. Lista fechada de hosts imposta em runtime; a exceção de SuperAdmin entra com o cadastro da S4 | Loja sem flag tem comportamento idêntico, provado por teste; orçamento estourado devolve `Disponivel=false` e grava `cota_estourada` | Desligar a flag | Alto |
| S4 | Console, em Integrações › IA: provedor (URL da lista, chave mascarada, Testar), modelos (ligar, desligar, padrão, preço) e uso do mês por provedor, modelo e finalidade. Linha por provedor na aba Uso e cota. Seed do provedor do `.env` e `modelo_ia_id` (vindos da S2b). Modelo sem preço não liga em provedor com orçamento | Teste de tela: cadastrar, testar, ligar e ver a soma do mês; seed idempotente com a contagem de lojas que caíram no padrão | Reverter o PR | Baixo, com spec |
| S5a | Gerador de descrições pelo roteador, com `EnviarStreamAsync` | O texto continua chegando aos poucos; a chamada grava `chamada_ia` com `descricao` | Reverter o PR | Alto |
| S5b | Corte do fallback global: saem `GeradorAutoPreenchimentoClaude` e `OpenAI`, `ModelosAprovadosAgenteOptions`, `PrecosModelosAgenteOptions` (com seus consumidores: `AgenteAtendimentoService.cs:165,185`, endpoint `Modelos`, `ModeloValidado`), a coluna `modelo_agente` e as seções `Anthropic` e `OpenAI` do `.env`. O custo de `TurnoAgente` passa a vir de `modelo_ia` | Gate antes do merge: `COUNT` de lojas com IA ligada e sem provedor ligado e testado = 0. O `.env` só sai do `compose.yaml` um deploy depois | Redeploy da versão anterior com o `.env` mantido | Alto |

Ordem: S0, S1, S2a (primeiro valor: o uso aparece), S2b, S4, S5a, S5b.

## 8. Riscos

| Risco | Mitigação |
|---|---|
| Loja sem chave fica sem IA depois da S5b | Gate do `COUNT` = 0 e `.env` mantido por um deploy |
| O custo estimado não bate com a fatura | Custo por faixa horária (D7). A tela diz que é estimativa |
| A DeepSeek troca `claude-*` em silêncio | O Testar compara `modelo_respondido` com `id_externo` e avisa |
| Cota suave estoura com chamadas em voo | Estouro limitado às chamadas já iniciadas (D6), escrito na tela |
| Perda do OpenAI no gerador | Declarada em D3 |

## 9. O que só o Felipe faz

1. Em `platform.deepseek.com`, recarregar o saldo. A conta é pré-paga, e sem saldo a API devolve 402.
2. Em API Keys, criar a chave e copiar na hora, porque ela só aparece uma vez.
3. Até a S4, dá para usar a DeepSeek já, sem código. Na VPS, configurar:
   - `Anthropic__BaseUrl=https://api.deepseek.com/anthropic/`
   - `Anthropic__ApiKeyAgente=<chave>`
   - `Anthropic__ModeloAgente=deepseek-flash`
   - os preços em `Anthropic__Precos__deepseek-flash__*`

   O `AutenticacaoBearer=true` atual também serve, porque a DeepSeek aceita Bearer.
4. Depois da S4, colar a chave na tela. Nunca no chat, em issue ou em commit.
5. Aplicar a label `aprovado` nas fatias de tier alto.
