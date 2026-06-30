# ADR-0039 — Fonte única de elegibilidade e sugestão de reposição de estoque

**Status:** Aceito
**Data:** 2026-06-30
**Refs:** issue #748

## Contexto

O sintoma relatado era de incoerência entre superfícies: o Dashboard mostrava "N críticos"
enquanto a tela "Gerar do estoque baixo" dizia "Tudo abastecido".

A medição refutou a hipótese inicial (não era "qtd 0 excluída" nem "default 99/5 quebrado": os
defaults são 5/2 e o `LimiarEstoqueResolver` já normaliza `crítico<mínimo`; o esgotado já entrava
nas listas — BUG-05, commit `7826f064`). A causa-raiz real, medida no código, é a **ausência de
fonte única de elegibilidade**: o conceito "estoque baixo / reposição" era derivado de **9 a 11
predicados distintos** em **≥3 bases incompatíveis** (Status computado; `qty < coluna congelada`;
`qty < constante`), espalhados por **≥6 superfícies** (Dashboard, badge do menu, Análises, "Gerar",
o job de notificações `GeradorNotificacoesAutomaticas` e o endpoint admin `InteligenciaLojas`).

Dois agravantes estruturais: (a) as colunas `ItemEstoque.QuantidadeMinima/QuantidadeCritica` são um
**snapshot tirado só na entrada do lote** (`RegistrarEntradaEstoqueUseCase`), nunca re-sincronizado
quando o limiar do produto/categoria/loja muda; o `Status` (usado pelo Dashboard) deriva delas,
enquanto as listas usam a config viva. (b) Tudo era avaliado **por lote**, não por produto, com
falso-positivo de múltiplos lotes pequenos e invisibilidade de produto **nunca estocado** (sem linha
em `ItensEstoque`).

## Decisão

Centralizar elegibilidade e sugestão numa **fonte única, por produto, com limiar resolvido ao vivo**,
consumida por todas as superfícies. Nenhuma superfície recalcula elegibilidade por fora.

- **Função pura de domínio** `AnalisadorReposicao` (`EasyStock.Domain/Services`): recebe uma projeção
  por-produto e devolve `ItemReposicao` classificados (R1/R2), com quantidade sugerida (R3), motivo
  legível (R4) e ordenados (R5). Reusa `LimiarEstoqueResolver` (hierarquia + clamp `crítico<mínimo`)
  e `CalculadoraReposicaoEstoque` (migrada para `decimal`).
- **Predicado canônico:** `vigente == 0 → ESGOTADO`; `vigente <= NivelCritico → CRITICO`;
  `vigente < NivelMinimo → ATENCAO` (operador `<` deliberado, alinhado ao domínio). "Precisa repor"
  (card/badge/sino) = `{ESGOTADO, CRITICO}`; a lista exibe os três estados.
- **Projeção** (`EstoqueAnalyticsQueries.GetSnapshotReposicaoAsync`, exposta por `IAnalyticsRepository`):
  parte de **`FROM Produtos` (ativos) LEFT JOIN** agregação de `ItensEstoque` vigentes, de modo que
  produto nunca-estocado entra com `QuantidadeVigente = 0` (vira ESGOTADO na função pura). Vigente
  exclui lotes vencidos, bloqueados e descartados. Velocidade recomputada por produto de
  `MovimentacoesEstoque` com `Natureza == Venda` (decimal). Limiares passados **brutos**
  (produto/categoria/config) para o domínio resolver. Implementada em **2 passos EF-translatable**
  (agregações no banco → materializa → combina em memória) para garantir tradução e o LEFT JOIN
  lógico. `EmpresaId` no WHERE de cada query (defesa-em-profundidade) além do RLS/global query filter.
- **Orquestração** `ObterReposicaoUseCase`: carrega a política da loja (`DiasCoberturaAlvo`,
  `LeadTimePadraoDias`, limiares de config — Fatia 2) e delega à função pura.
- **Contrato** `ItemReposicao { ProdutoId, VariacaoId?, Nome, QuantidadeVigente, NivelMinimo,
  NivelCritico, Estado, QuantidadeSugerida, Confianca, Motivo, DiasAteRuptura, FornecedorId? }`.
- **Validação R6** na persistência de Configurações (bloquear salvar `crítico>=mínimo`): validator
  FluentValidation + guarda no `AtualizarConfiguracaoLojaUseCase`.

Decisões de granularidade e parâmetros (confirmadas com o Felipe): por produto (variação herda,
`VariacaoId=null` no MVP); lead time = `Fornecedor.LeadTimeEstimadoDias` do lote vigente mais recente
`?? ConfiguracaoLoja.LeadTimePadraoDias ?? OperacionalDefaults`; `DiasCoberturaAlvo`/`LeadTimePadraoDias`
defaults 7, marcados como **premissa de negócio a validar**.

## Consequências

- A contagem do Dashboard/badge **muda** (passa de por-lote/Status para por-produto/limiar-vivo). É
  regressão intencional de KPI; coberta por teste e comunicada.
- I-1 foi **redefinida** (a forma original, "contagem == tamanho da lista completa", é impossível com
  itens ATENCAO): `contagem_card == nº de itens da lista em {ESGOTADO,CRITICO}`, e todas as superfícies
  derivam do mesmo use case.
- Pior caso de performance (catálogo grande): agregação no banco (1 linha/produto) + cache mitigam.
- **Risco residual:** a tradução em runtime da projeção EF foi desenhada sobre padrões já provados no
  repo e revisada adversarialmente, mas **deve ser confirmada por teste de integração (Postgres real)
  antes de cabear as superfícies** (Fatia 5).
- Entregue em fatias build-verdes: 1 (`AnalisadorReposicao` + contrato), 2 (parâmetros de config +
  migration), 3a (validação R6), 4 (projeção + `ObterReposicaoUseCase`). Pendentes: teste de integração
  da projeção; Fatia 5 (migrar as 6 superfícies, incl. sino e InteligenciaLojas, aposentando os
  predicados antigos); 3b (UI de Configurações) / 3c (telemetria ao normalizar); Fatia 6 (UI "Criar
  lista" pré-preenchida). Issues separadas: venda avulsa sem identificação; auditoria do erro 409.
