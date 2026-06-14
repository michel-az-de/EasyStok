# ADR-0027 — Cadastro rápido de produto e completude derivada

**Status:** Parcialmente superseded por ADR-0033 — a completude migrou de Web-derived para calculada no backend (fonte única no domínio). O cadastro rápido/lote desta ADR permanece vigente. Original: Aceito  
**Data:** 2026-06-06

## Contexto

O cadastro de produto é um wizard de 4 etapas (Essencial, Fotos e Preços, Detalhes, Revisão) em `EasyStock.Web/Views/Produtos/Form.cshtml`. A etapa Essencial já contém só o obrigatório real: nome e categoria. Para um varejista pequeno pondo dezenas de itens no sistema, passar por 4 telas por produto é cansativo.

Queremos atender dois perfis sem perder nenhum:

- **Cadastro caprichado** — mantém o wizard completo intacto (stepper, botões "Gerar" de SKU/código de barras).
- **Pôr o item para começar a vender** — um **cadastro rápido** (1 item, só o mínimo) e um **cadastro em lote** (planilha), completando fotos/detalhes depois.

Já existe a infraestrutura para o caminho rápido: o endpoint `POST /produtos/quick.json` (`QuickCriar`) roda em produção no modal "Novo pedido", aceita nome + categoria (preço/custo/qtd opcionais) e converge no mesmo `ProdutosService.CriarAsync()` do wizard. A regra de status já trata "produto sem `PrecoReferencia > 0`" como `Inativo`.

## Decisão

1. **Cadastro rápido = modal na lista de Produtos.** Botão "Cadastro rápido" ao lado de "Novo produto", reusando `quick.json` + o padrão `formModal`/`fm-*`. Não toca o wizard. Expõe nome + categoria + preço (opcional); **não** expõe quantidade inicial (ver Consequências).
2. **Cadastro em lote = importar planilha** (colar Excel/TSV ou CSV), com preview e validação por linha antes de criar qualquer coisa. Feature maior, entregue numa segunda fase.
3. **Completude = propriedade DERIVADA, nunca persistida.** Um produto está "Incompleto" quando lhe falta o mínimo para vender bem. Isso é **computado** a cada leitura a partir dos campos que o produto já tem, exposto como propriedade derivada no `ProdutoResumo` (ao lado de `StatusNome`/`PrimeiraFotoUrl`):

   ```
   EstaCompleto = PrecoReferencia?.Valor > 0 && (tem foto || Tipo == Servico)
   ```

   Serviço não exige foto. Não há campo novo, migration, propagação por camadas, nem string de estado cruzando o boundary Web↔API.
4. **A regra "sem preço → Inativo" permanece.** `Status` (Ativo/Inativo) responde "pode vender?"; completude responde "falta o quê para o cadastro ficar pronto?". São eixos ortogonais de verdade — porque completude é computada, não um segundo flag persistido.

## Motivação

Completude é, por natureza, **estado derivado** (função da presença de campos). Persistir isso como um flag separado o condena a divergir da realidade:

- Um flag setado por "qual formulário salvou" mede **proveniência, não completude**. Ele inverte o chip: um produto do cadastro rápido com preço viraria "incompleto", enquanto um produto criado pelo wizard só com o Essencial (sem preço/foto) viraria "completo" — o item com mais dado marcado como tendo menos.
- Um flag de intenção com botão "Finalizar cadastro" tem semântica honesta, mas custa migration (com risco de lock no `UPDATE` da tabela viva), propagação por 5 camadas, default seguro, fonte única para a string no boundary, e adiciona um passo que o usuário precisa lembrar — atrito, o oposto do objetivo.
- Derivar custa apenas recomputar campos já em memória no `ProdutoResumo`. Em troca: zero migration, zero call-site, zero drift, e nunca inverte.

A API de listagem serializa a entidade `Produto` inteira (sem projeção), então `FotosJson` e `PrecoReferencia` sempre chegam na lista — a derivação é 100% no Web, sem estender a API.

### Alternativas rejeitadas

- **Flag virado por qual-formulário-salvou** — proveniência fantasiada de completude; inverte o chip. Descartada.
- **Flag de intenção + botão "Finalizar"** — honesto, mas caro (migration + 5 camadas) e adiciona atrito. Rejeitada em favor do derivado.
- **Reusar `Status = Inativo` como completude** — mistura "incompleto" com "inativado de propósito". Rejeitada.
- **Filtro "Incompletos" novo na lista** — duplicaria o filtro "Sem preço" existente. Reusa-se "Sem preço".

## Consequências

- **Web-only, sem migration.** A Fase 1 (completude + chip + modal) toca apenas `EasyStock.Web`. `ProdutoResumo` ganha `Tipo` (já serializado pela API) + `EstaCompleto` + `Pendencias`.
- **O chip "Incompleto" reflete a realidade sempre.** Apagar o preço de um produto completo o torna incompleto de novo automaticamente; editar só o nome não o "completa" por engano.
- **Cadastro rápido não mexe em estoque.** Expor quantidade inicial dispararia entrada de estoque com efeitos colaterais: produto sem preço nasce `Inativo` e a entrada **rejeita** produto inativo (`ProdutoInativoException`); sem custo, a entrada usaria custo R$0,01, poluindo o custo médio. Quantidade fica no fluxo "Registrar entrada".
- **Calibração pendente (data-driven).** Como a régua é retroativa, se a maior parte do catálogo real não tiver foto, "Incompleto" vira ruído. Medir a distribuição (% sem preço, % sem foto, por tipo) antes de generalizar; se necessário, foto sai do `EstaCompleto` (ajuste de 1 linha).
- **Vocabulário canônico.** "Incompleto" = produto persistido faltando dado de venda. Nunca "Rascunho" (que o wizard usa para `sessionStorage` local — conceitos distintos).
- **Lote (Fase 2) entra como dado externo.** A importação de planilha exige tratamento de parsing PT-BR (vírgula decimal), deduplicação (a unicidade de nome é case-sensitive), resolução de categoria por nome **tenant-scoped**, e validação antes de criar — detalhado na issue da Fase 2.
