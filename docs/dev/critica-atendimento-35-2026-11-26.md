# Críticas do Atendimento 35 (#3374)

Três críticas independentes (heurísticas de Nielsen, 9 itens, 0 a 4 cada, total de 36), sobre a tela do balcão em modo demonstração. A entrega fecha com **24/36**, abaixo do alvo de 35. O que sobrou vai para a #3381.

## Notas

| H | Heurística | r1 | r2 | r3 |
|---|---|---|---|---|
| H1 | Visibilidade do estado | 3 | 3 | 3 |
| H2 | Linguagem do mundo real | 3 | 3 | 3 |
| H3 | Controle e liberdade | 3 | 2 | 3 |
| H4 | Consistência e padrões | 2 | 2 | 2 |
| H5 | Prevenção de erros | 2 | 3 | 2 |
| H6 | Reconhecer em vez de lembrar | 3 | 3 | 3 |
| H7 | Flexibilidade e eficiência | 3 | 3 | 3 |
| H8 | Estética e minimalismo | 2 | 2 | 2 |
| H9 | Recuperar de erros | 2 | 3 | 3 |
| | **Total** | **23** | **24** | **24** |

## O que cada rodada corrigiu

- **r1:** Assistente toma o lugar da ficha abaixo de 1308 px, barra do compositor cabe, aviso diz onde fica o cadeado e a prova vigia sobreposição e texto coberto.
- **r2:** restrição alimentar fica até fechar a conversa, motivo do cabeçalho quebra em 2 linhas, Devolver ao Assistente à vista e uma regra de envio (Enter pergunta, Shift+Enter quebra).
- **r3:** Assistente com 560 px ou mais na conversa, Enviar desabilitado com contorno contínuo, filtros sem selo com contagem 0 (o "0" de Todas ficou, pois separa vazio de carregando).

## Pendente

Achados P1 e P2 da r3 e P2 da r2 não feitos: issue #3381 (https://github.com/michel-az-de/EasyStok/issues/3381).

Prints antes e depois: `docs/dev/anexos-atendimento-35/`.
