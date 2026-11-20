# Provedores e modelos de IA por loja (#2948)

- Status: proposta, aguardando aprovação do Felipe
- Data: 2026-11-20
- Tier: alto (migração, cofre, `Program.cs` e DI)
- Relacionados: ADR-0061 (cofre e conexões), ADR-0066 (modelo por loja e cota), #1305 (DeepSeek via Fireworks), S67 (`TurnoAgente`)

## 1. Problema

A IA tem uma chave só, global, no `.env` da VPS. Por isso:

| Dor | Causa medida |
|---|---|
| Não dá para saber o gasto por provedor | O custo sai de `Anthropic:Precos` na configuração. Só o turno do agente grava uso (`TurnoAgente`). |
| Trocar de provedor ou de modelo exige deploy | `Anthropic__BaseUrl` e `Anthropic__ModeloAgente` ficam em `scripts/deploy/producao/compose.yaml:67-71`. |
| Não dá para ter mais de uma API | `AnthropicAgenteOptions` tem uma `BaseUrl` e uma chave. |
| Parte do uso fica invisível | `GeradorAutoPreenchimentoClaude` e `OpenAI` (`Infra.Postgre/Services`) usam clientes próprios e não registram uso. |

## 2. Decisões do Felipe (20/11)

| # | Decisão |
|---|---|
| D1 | A chave é de cada loja e fica no cofre por loja (`CredencialIntegracao`, AES-256-GCM, RLS). |
| D2 | A cota é em US$ por mês, por provedor. A `CotaTokensMes` atual continua como está, sem crescer. |
| D3 | Aceita só o formato Anthropic Messages. DeepSeek, Fireworks e Anthropic falam esse formato, então o `AnthropicMessagesClient` atual serve. |
| D4 | Toda chamada de IA entra no registro de uso: atendimento, assistente, ficha, resumo, sugestões e gerador de descrições. |

## 3. Fatos da DeepSeek (docs oficiais, lidos em 20/11)

| Item | Valor |
|---|---|
| URL no formato Anthropic | `https://api.deepseek.com/anthropic` (o cliente chama `v1/messages` relativo a ela) |
| Autenticação | `x-api-key` ou `Authorization: Bearer` |
| Modelos | `deepseek-flash` (contexto de 1M, com imagem) e `deepseek-v4-pro` (contexto de 1M, sem imagem) |
| Preço por 1M tokens, horário de pico | flash: cache lido US$ 0,006, entrada US$ 0,30, saída US$ 1,20. pro: cache lido US$ 0,044, entrada US$ 1,32, saída US$ 3,96 |
| Fora do pico | Metade do preço. O pico vai das 01h às 04h e das 06h às 10h UTC, de segunda a sexta (22h às 01h e 03h às 07h em Brasília) |
| Saldo | `GET https://api.deepseek.com/user/balance` devolve `is_available` e `balance_infos[]` (`currency`, `total_balance`, `granted_balance`, `topped_up_balance`) |
| Lista de modelos | `GET https://api.deepseek.com/models` |
| Limite | Concorrência por conta: 2500 no flash e 500 no pro. Acima disso volta HTTP 429. Não há header de cota |
| Sem saldo | HTTP 402 |
| Diferenças | `cache_control` é ignorado (o cache é automático), `anthropic-version` é ignorado e `cache_creation_input_tokens` sempre vem 0. Nome `claude-*` desconhecido é trocado por `deepseek-flash`, e `claude-opus*` vira `v4-pro` |
| Histórico de uso por API | Não documentado. Por isso, o uso tem de ser registrado do nosso lado |

## 4. Modelo de dados

Todas as tabelas têm `empresa_id` e RLS (ADR-0010).

```
credencial_integracao (existe)          provedor_ia
  categoria = Ia (nova, 6)   <──1:1──    id, empresa_id, nome
  payload cifrado {apiKey}               base_url, autenticacao (x_api_key | bearer)
  mascara, ultimo_teste_*                url_saldo (nula), ligado, prioridade
                                         orcamento_mensal_usd (nulo = sem cota)
                                         saldo_usd, saldo_lido_em
                                              │1
                                              │N
                                         modelo_ia
                                           id, empresa_id, provedor_id, id_externo, rotulo
                                           ligado, padrao (um por loja)
                                           preco_entrada, preco_saida, preco_cache_lido (US$/1M)
                                           contexto_max, saida_max
                                              │1
                                              │N
                                         uso_ia (só inserção)
                                           id, empresa_id, provedor_id, modelo_id
                                           modelo_respondido, finalidade (enum)
                                           turno_agente_id (nulo), tokens entrada/saida/cache_lido/cache_escrito
                                           custo_usd (nulo quando não há preço), duracao_ms
                                           desfecho (ok, erro, sem_saldo_402, limite_429, cota_estourada)
                                           criado_em
```

- Índice `uso_ia (empresa_id, provedor_id, criado_em)`. A cota do mês é a soma de `custo_usd` nesse índice.
- `custo_usd` usa o preço cheio, o de pico. Assim o valor fica conservador: a fatura real da DeepSeek pode sair até 50% menor.
- `ConfiguracaoAtendimento.ModeloAgente` (texto) vira `modelo_ia_id`. A conversão casa o texto com `id_externo`, e sem casamento usa o modelo `padrao`.

## 5. Fluxo da chamada

```
caso de uso ──RequisicaoLlm(+EmpresaId, +Finalidade)──> RoteadorIa
   1. modelo pedido ligado? senão o padrão da loja
   2. provedor ligado e dentro do orçamento? senão o próximo por prioridade; sem nenhum, Disponivel=false (a equipe atende)
   3. chave do cofre (IIntegrationCredentialResolver) ─> AnthropicMessagesClient (URL e auth do provedor)
   4. grava uso_ia (ok ou erro), com custo pelo preço do modelo
```

A `RequisicaoLlm` ganha `EmpresaId` e `Finalidade` explícitos, porque o webhook roda sem tenant HTTP. Há um disjuntor por provedor, e não mais um só da Anthropic.

## 6. Fatias

| # | Entrega | Tier |
|---|---|---|
| S1 | Domínio (`ProvedorIa`, `ModeloIa`, `UsoIa`), categoria `Ia` no cofre, migração com RLS e repositórios. Sem efeito em produção | Alto |
| S2 | `RoteadorIa` atrás de `IAgenteLlmClient`, com `EmpresaId` e `Finalidade` em todos os chamadores, gravação em `uso_ia` e custo pelo `modelo_ia`. Loja sem provedor cadastrado continua no `.env` global | Alto |
| S3 | Orçamento em US$ por provedor com troca por prioridade, leitura de saldo (job por hora em quem tem `url_saldo`) e aviso de saldo baixo e de 402 | Alto |
| S4 | Tela no Console, em Conexões: cadastrar provedor (URL, chave mascarada, botão Testar), modelos (ligar, desligar, padrão, preço) e painel do mês por provedor, modelo e finalidade, com o saldo | Baixo, com spec |
| S5 | O gerador de descrições passa pelo roteador. Saem `GeradorAutoPreenchimentoClaude` e `OpenAI`, `ModelosAprovadosAgenteOptions`, `PrecosModelosAgenteOptions` e as seções `Anthropic` e `OpenAI` do `.env`. Corte do fallback global | Alto |

## 7. Riscos

| Risco | Mitigação |
|---|---|
| Depois da S5, loja sem chave fica sem IA | A S5 só entra depois que a Casa da Baba tiver o provedor cadastrado e testado |
| O custo estimado não bate com a fatura (pico e fora do pico) | O valor é conservador, e a tela diz que é estimativa. O saldo lido do provedor serve de contraprova |
| Nome de modelo inválido some na DeepSeek, que troca `claude-*` em silêncio | O botão Testar compara `modelo_respondido` com `id_externo` e avisa quando divergem |
| Chave em log | Já é regra no cliente. O teste do roteador confere que a chave não aparece em log nem em exceção |
| Volume de `uso_ia` | Uma linha por chamada (hoje são algumas centenas por dia). Particionar só quando medir |

## 8. O que só o Felipe faz

1. Em `platform.deepseek.com`, entrar e recarregar o saldo. A conta é pré-paga, e sem saldo a API devolve 402.
2. Em API Keys, criar a chave e copiar na hora, porque ela só aparece uma vez.
3. Até a S4 sair, dá para usar a DeepSeek já, sem código: na VPS, `Anthropic__BaseUrl=https://api.deepseek.com/anthropic/`, `Anthropic__ApiKeyAgente=<chave>` e `Anthropic__ModeloAgente=deepseek-flash`, mais o preço em `Anthropic__Precos__deepseek-flash__*` para o custo não sair nulo.
4. Depois da S4, colar a chave na tela. Nunca no chat, em issue ou em commit.
