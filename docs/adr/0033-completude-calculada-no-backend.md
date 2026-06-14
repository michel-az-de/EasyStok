# ADR-0033 — Completude do produto calculada no backend (supersede ADR-0027)

**Status:** Aceito
**Data:** 2026-06-14
**Supersede:** ADR-0027 (completude derivada Web-only) — na parte de ONDE a completude é computada.

## Contexto

O ADR-0027 (2026-06-06) decidiu que completude do produto é uma propriedade **derivada, computada no Web, nunca persistida** — exposta como `EstaCompleto`/`Pendencias` no `ProdutoResumo`. A motivação era frugalidade: zero migration, zero propagação por camadas, e "nunca diverge da realidade porque recomputa a cada leitura".

Na prática surgiram **três derivações Web independentes e divergentes** do mesmo conceito:

- **Lista** (`ProdutoResumo.EstaCompleto`): booleano simples — `PrecoReferencia > 0 && (tem foto || Servico)`.
- **Detalhe** (`ProdutoDetailViewModel.IntegrityScore`): **% ponderado** (fotos 20, descrição 15, custo 15, preço 15, código de barras 10, variações 10, marca 5, dimensões 5, nome 3, categoria 2; +nutricional 10 p/ alimento).
- **Wizard** (`Form.cshtml` `completionPercent`): % ponderado client-side, pré-save.

Resultado medido (QA v1.10 BUG-018, issue #582): a lista mostra o chip "INCOMPLETO" (booleano) enquanto o detalhe do mesmo produto mostra "Completude 50%" (ponderado). Dois números para a mesma verdade. A causa não é drift de persistência (o que o ADR-0027 evitou), e sim **falta de fonte única**: cada tela rederiva com regra própria. O ADR-0027 garantiu "não persiste", mas não garantiu "uma regra só".

## Decisão

Completude passa a ser **calculada uma vez no backend** e exposta no contrato, consumida igual por todas as telas.

1. **Domínio é a fonte única.** `Produto.CalcularCompletude()` retorna `(int Percent, IReadOnlyList<string> Pendencias)` com os pesos canônicos acima. Função pura sobre os campos da entidade — continua **não persistida** (recomputa por leitura), preservando a garantia central do ADR-0027 ("reflete a realidade sempre"). O que muda é só ONDE roda: Domínio, não Web.
2. **Exposto no contrato.** A listagem e o detalhe de produto ganham `CompletudePercent` (int) e `Pendencias` (string[]). A listagem já serializa a entidade `Produto` inteira (ADR-0027), então o custo extra é mínimo — exige garantir o eager-load de `Variacoes` na query da lista para o peso de variações não subnotificar.
3. **Web vira espelho.** `ProdutoResumo`/`ProdutoDetalhe` mapeiam `CompletudePercent`/`Pendencias`; lista e detalhe exibem o MESMO número. As derivações Web (`EstaCompleto`, `IntegrityScore`) são removidas.
4. **Wizard segue client-side.** O `completionPercent` do `Form.cshtml` é um preview de produto **ainda não salvo** (sem round-trip), então permanece no client — mas alinhado aos mesmos pesos canônicos. É um eixo distinto (pré-save), não a completude do produto persistido.

## Motivação / trade-off (por que reverter o ADR-0027)

O ADR-0027 trocou "fonte única" por "frugalidade" (Web-only, sem propagação). O preço apareceu: três regras divergentes e um bug recorrente de QA (BUG-018). Aceitamos agora o custo que o 0027 evitou — propagar `CompletudePercent` por Application→API→Web — em troca de **uma regra só**, consumida identicamente por lista, detalhe e (futuro) mobile/storefront. Continua sem migration (derivado em memória sobre a entidade já carregada); o que se paga é o campo novo no DTO público e o eager-load de `Variacoes` na lista.

### Alternativas rejeitadas

- **Manter Web-only e só unificar as duas derivações Web** (alinhado ao 0027): mais barato, mas mantém a regra fora do domínio e duplicada entre Web e o wizard; mobile/storefront teriam que rederivar. Rejeitada pelo Felipe em favor da fonte única no backend.
- **Persistir um flag de completude:** já rejeitada pelo ADR-0027 (mede proveniência, não completude; migration + drift). Continua rejeitada — completude segue derivada, só que no domínio.

## Consequências

- **Contrato público muda:** DTO de produto (lista e detalhe) ganha `CompletudePercent` + `Pendencias`. Consumidores (Web hoje; mobile/storefront no futuro) passam a ler o campo em vez de rederivar.
- **Query da lista** precisa eager-load de `Variacoes` (senão o peso de variações zera na lista). Medir o custo; se pesar, projetar só a contagem.
- **Sem migration, sem persistência** — preserva a garantia do ADR-0027 (recomputa por leitura, nunca inverte).
- **Wizard pré-save** mantém cálculo client, alinhado aos pesos canônicos; documenta-se que é eixo distinto (produto não-salvo).
- **Calibração dos pesos** (ADR-0027) continua válida e agora vive num lugar só (`Produto.CalcularCompletude()`), barata de ajustar.
