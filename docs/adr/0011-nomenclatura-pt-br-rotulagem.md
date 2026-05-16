# ADR 0011 — Nomenclatura PT-BR para o módulo Rotulagem Nutricional

**Status:** Accepted (2026-05-16)
**Contexto do plano:** P-02 (Módulo Incremental de Rotulagem Nutricional) — `~/.claude/plans/pesquise-na-internet-sobre-iridescent-haven.md`.

## Decisão

**PT-BR para substantivos de negócio. EN apenas para sufixos de padrões técnicos consagrados ou para conceitos sem equivalente PT-BR natural.**

Aplicável a todo o módulo Rotulagem Nutricional (`EasyStock.Domain.Entities.Rotulagem`, `EasyStock.Application.Rotulagem`, `EasyStock.Infra.*.Rotulagem`) e a qualquer módulo novo deste ponto em diante. Código pré-existente do projeto que não segue a regra (`TenantFeatureFlag`, `EtiquetaTemplate`, `MobileProcessedMutation`, etc.) **não é renomeado retroativamente** — custo de churn maior que ganho.

## Por quê

O projeto EasyStok consolidou em PT-BR para entidades de negócio (`Empresa`, `Loja`, `Lote`, `Produto`, `Fornecedor`, `LoteItem`, `LoteEtiqueta`, `Categoria`, `ProdutoEmbalagem`, `ConfiguracaoLoja`, `ProdutoComposicao`) e usa EN apenas para conceitos técnicos sem equivalente natural (`RowVersion`, `Discriminator`, `Mutation`) e sufixos de padrões consagrados (`*Service`, `*Repository`, `*Handler`). Os primeiros rascunhos do P-02 tinham resíduos de EN em identificadores de negócio (`NutritionCalculator`, `NutritionPreviewPanel`, `FornecedorPrivacyConfig`, `RotuloPdfGenerator`). Esta ADR fixa a regra para evitar regressão.

Tentar 100% PT-BR (`Repositorio`, `Servico`, `Fabrica`, `Cache`) quebra a busca por padrões conhecidos, gera atrito com convenções de bibliotecas .NET (`IHostedService`, `IMiddleware`, `[FromBody]`) e não traz ganho comunicacional.

## Regras

### Categorias que devem usar PT-BR (obrigatório)

| Categoria | Exemplos |
|---|---|
| Entidades de domínio | `PerfilNutricional`, `Rotulo`, `Lote`, `FichaTecnicaProduto`, `Rascunho` |
| ValueObjects de domínio | `ValorNutricional`, `LimitesLupa`, `LupaAltoTeorFlags`, `CodigoEan` |
| DTOs/Records de aplicação | `Receita`, `Rendimento`, `Porcao`, `ResultadoNutricional`, `CalculoNutricionalInput`, `InsumoComFichaCongelada` |
| Use Cases (verbo imperativo) | `CalcularNutricaoPreview`, `AplicarNutricaoAoProduto`, `GerarRotulo`, `PublicarRotulo`, `ImportarFichaFornecedor`, `AprovarComoRt` |
| Services com nome de negócio claro | `CalculadoraNutricional`, `GeradorListaIngredientes`, `MarcaDaguaSimulacao`, `ImportadorTaco` |
| Domain Events | `ProdutoComposicaoAlteradoEvent`, `PerfilNutricionalAlteradoEvent`, `FichaTecnicaPublicadaEvent` |
| Adapters de domínio | `ProdutoComposicaoAdapter` |
| Enums e valores | `SubTipoAlimento.BebidaAlcoolica`, `Alergeno.Trigo`, `OrigemDadoNutricional.Manual` |
| Pastas semânticas | `Rotulagem/`, `Renderizadores/`, `Ai/` |

### Categorias onde EN é permitido por exceção

| Categoria | Justificativa | Exemplos |
|---|---|---|
| Sufixos de padrões consagrados | Comunidade .NET espera | `Repository`, `Service`, `UseCase`, `Handler`, `Factory`, `Adapter`, `Provider`, `Validator`, `Interceptor`, `Middleware`, `Filter`, `Job`, `HostedService`, `Generator`, `Renderer`, `Event` |
| Conceitos sem equivalente PT natural | Tradução perde sentido | `Snapshot`, `Hash`, `Cache`, `Stale`, `Hydrator`, `Marker` interface, `Versioning` |
| Bibliotecas externas e seus tipos | Não-renomeáveis | `QuestPDF`, `QRCoder`, `AspNetCoreRateLimit`, `Husky.Net`, `Verify.QuestPDF`, `NetArchTest.Rules`, `Resend`, `RazorLight` |
| Marker interfaces puramente técnicas | Sem semântica de negócio | `IGlobalCatalog`, `IClaudeStructuredExtractor<TInput, TOutput>` |
| Atributos EF Core e shadow properties | Convenção do framework | `RowVersion`, `Discriminator` |
| Padrões pré-existentes do projeto | Custo de churn > ganho | `TenantFeatureFlag`, `EtiquetaTemplate`, `EtiquetaTemplateSistema`, `EtiquetaEmpresaDefault`, `MobileProcessedMutation`, `MobileBatchId` |
| Generic type parameters | Convenção C# | `T`, `TInput`, `TOutput`, `TKey` |
| Configuration POCOs vindos do .NET/biblioteca | Schema externo | `IpRateLimiting`, `JwtBearerOptions` |

### Padrão híbrido aceito (substantivo PT + sufixo EN)

| Padrão | OK | NÃO ok |
|---|---|---|
| `{Substantivo PT}{SufixoEN técnico}` | `ConformidadeValidator`, `RotuloHandler`, `ProdutoComposicaoAdapter` | ~~`NutritionValidator`~~ (substantivo principal em EN) |
| `{Verbo PT}{SufixoEN}` | `MigradorSnapshotRotulo`, `ArquivamentoRascunhoJob` | ~~`SnapshotMigrator`~~ (verbo principal em EN) |

**Caso-limite aceito**: `IReceitaSnapshotProvider` — substantivo PT (`Receita`) + dois sufixos EN encadeados (`Snapshot` sem equivalente PT, `Provider` padrão técnico). Permitido pela regra, mas evitar encadear mais sufixos EN no futuro.

### Convenção de arquivos

| Tipo | Convenção | Exemplo |
|---|---|---|
| Classes C# (.cs) | PascalCase | `CalculadoraNutricional.cs` |
| Razor partials (.cshtml com prefixo `_`) | PascalCase | `_PainelTabelaNutricional.cshtml` |
| Razor pages/views | PascalCase | `Index.cshtml`, `Detalhe.cshtml` |
| JavaScript (.js) | kebab-case | `painel-tabela-nutricional.js` |
| TypeScript admin (.tsx/.ts) | PascalCase componente, camelCase utils | `FeatureFlagsToggle.tsx`, `featureFlagsApi.ts` |
| CSS/SCSS | kebab-case | `painel-tabela-nutricional.css` |
| Migrations EF | timestamp_PascalCase | `20260516000000_AddRotulagemNutricional.cs` |
| Pastas no Domain | PascalCase em PT | `Rotulagem/`, `Renderizadores/` |
| Pastas no Infra (técnico) | PascalCase EN | `Interceptors/`, `Jobs/`, `Migrations/` |

JS em kebab-case é padrão moderno do ecossistema web, alinha com CSS, evita problemas de case-sensitivity entre Windows dev / Linux produção.

## Verificação automática

Teste de arquitetura em `EasyStock.ArchitectureTests/Rotulagem/NomenclaturaPtBrTests.cs`:

```csharp
[Fact]
public void Entidades_de_dominio_Rotulagem_devem_ter_nomes_em_pt_br()
{
    // Lista de palavras EN proibidas em nomes de entidades de negócio do módulo Rotulagem.
    var palavrasProibidas = new[] {
        "Profile", "Recipe", "Label", "Batch", "Allergen",
        "Claim", "Supplier", "Source", "Draft", "Privacy",
        "Snapshot" // permitido só como sufixo (caso-limite); não como nome principal
    };

    var entidades = Types.InAssembly(typeof(PerfilNutricional).Assembly)
        .That().ResideInNamespace("EasyStock.Domain.Entities.Rotulagem")
        .GetTypes();

    foreach (var t in entidades)
        foreach (var p in palavrasProibidas)
            t.Name.Should().NotContain(p,
                $"entidade {t.Name} contém termo EN proibido '{p}' — usar equivalente PT (ver ADR-0011)");
}
```

Falha CI ao introduzir `NutritionLabel`, `RecipeIngredient`, etc. Roda em `dotnet test --filter "Category=Architecture"`.

## Consequências

**Positivas:**
- Coerência entre módulos novos e código existente do projeto.
- Onboarding mais simples para desenvolvedor BR.
- Padrão verificável automaticamente (não depende de revisão humana).

**Negativas:**
- Tabela de renomeação completa precisa ser aplicada **antes** do primeiro commit do módulo. A tabela está no plano P-02 (~30+ itens, ex: `NutritionCalculator` → `CalculadoraNutricional`, `RotuloPdfGenerator` → `GeradorPdfRotulo`, `ConfigRotuloEmpresa` → `ConfiguracaoRotuloEmpresa`).
- Híbrido sempre cria zonas cinzas — caso-limite documentado (`IReceitaSnapshotProvider`); novos casos exigem decisão explícita aqui.

## Mudanças aplicadas

- Documento ADR criado.
- Plano P-02 atualizado com nomes finais em todas as seções ativas (Modelo de Dados, Faseamento, Arquivos Críticos, Verificação).
- Teste de arquitetura a ser criado em `EasyStock.ArchitectureTests/Rotulagem/NomenclaturaPtBrTests.cs` no próximo commit de setup do módulo (F0.5).

## Reversão

Excluir este ADR + remover o teste `NomenclaturaPtBrTests` libera futura introdução de nomes em EN. Não recomendado: quebra coerência com módulos pré-existentes.

## Caminho futuro

Avaliar extensão da regra a outros módulos novos do EasyStok no próximo planejamento (P-03+). Considerar análise estática mais ampla via `EditorConfig` ou Roslyn analyzer customizado se palavras proibidas começarem a aparecer em outros lugares.
