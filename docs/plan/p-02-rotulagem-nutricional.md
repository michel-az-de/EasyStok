# Plano P-02: Módulo Incremental de Rotulagem Nutricional (EasyStok)

> Documento de execução. Sem TBDs, sem "vamos decidir depois". Tudo o que ficar marcado `[Validar com Felipe antes de FX]` é assunção operacional explícita sobre informação externa não confirmada.

---

## 1. Sumário Executivo

**O que é:** módulo aditivo no EasyStok que permite a lojas do segmento Alimentos cadastrar fichas nutricionais por insumo (manual ou via TACO), publicar rótulos full RDC-compliant em PDF/etiqueta térmica, e manter versionamento auditável de cada rótulo impresso para fiscalização da Anvisa.

**Para quem:** clientes EasyStok do segmento Alimentos. Cliente piloto: **Casa da Babá** (massas frescas artesanais). Usuária operadora: **Thatiane** (esposa do Felipe). Admin/dono: **Felipe** (sênior .NET 15a, Avanade durante o dia).

**Em quanto tempo:** 10–12 semanas part-time, considerando 2–3h/dia úteis + 4–8h sábados/domingos. Buffer de 20% incluído. Início efetivo: **2026-05-17**. Marco MVP em produção: **2026-08-09** (data realista com buffer).

**Custo total estimado mensal pós-deploy:**
- Fly volume 1GB (storage rótulos): R$ ~0,75
- GitHub Actions billing (CI + crons backup): R$ 50–200
- Cloudflare R2 (backup, free tier <10GB): R$ 0,00
- Resend email (free tier <3k/mês): R$ 0,00
- Anthropic Claude (extração ficha — uso pontual): R$ 20–80 estimado
- **Total operacional: R$ 70–280/mês.** Comparação: 1 multa Anvisa por rótulo irregular = R$ 6.000 mínimo.

**Objetivo (3 frases):**
1. Eliminar 100% do risco de multa Anvisa por rótulo irregular para clientes EasyStok do segmento Alimentos, com snapshot imutável auditável.
2. Reduzir tempo de geração de rótulo nutricional para um lote de >2h (planilha + gráfica) para <2min (clique + impressora térmica).
3. Construir diferencial competitivo via IA: extração automática de ficha técnica de fornecedor a partir de PDF/foto (C2), em paridade ou à frente de PriceFy / FoodTrace / EasyLabel / Linx Food / ConsiNet.

**Não-objetivos explícitos do MVP (NÃO está no escopo):**
1. **Lupa frontal fase 2 IN 75** (out/2026 — entra em F+1, sistema preparado mas não ativo).
2. **QR público de rastreabilidade com página renderizada** (slug reservado no MVP, página + opt-out em F+1).
3. **Assinatura digital ICP-Brasil A1/A3 para RT** (MVP entrega "Comprovante de Aprovação Interna" via hash com secret servidor; ICP-Brasil em F+1).

**Top-3 riscos com mitigação ativa:**
1. **Multa Anvisa por bug no validador de claims** (probabilidade média, impacto R$ 6k–1,5M por rótulo). **Mitigação ativa:** snapshot tests de PDF + property-based testing do validador + revisão manual obrigatória de cada novo template antes de habilitar em produção (gate em config).
2. **Cronograma estoura por imprevisto trabalho Avanade** (probabilidade alta, impacto 2–4 semanas). **Mitigação ativa:** cada fase tem critério "pronto" objetivo + plano alternativo de próxima fase desbloqueada; buffer 20% no cronograma; validação semanal com Thatiane a partir de F5.
3. **Backup de rótulos corrompe silenciosamente** (probabilidade baixa, impacto perda de auditoria 5 anos = exposição legal). **Mitigação ativa:** restore-test mensal automatizado em DB efêmero + alerta crítico via Resend se falhar; cron já configurado em F0.5.

---

## 2. Contexto Regulatório

Normas Anvisa que o módulo precisa atender (todas seedeadas como `NormaRegulatoria` em F2):

| Norma | Escopo |
|---|---|
| **RDC 429/2020** | Rotulagem nutricional (vigente desde 09/10/2022) — tabela, frontal, lupa |
| **IN 75/2020** | Requisitos técnicos — campos, porções (Anexo IV), arredondamento (Anexo III), modelo linear (Anexo XIII), limites lupa fase 1 e **fase 2 (out/2026)** |
| **RDC 727/2022** | Rotulagem geral — identificação obrigatória, denominação, conteúdo líquido |
| **RDC 26/2015** | Declaração de alérgenos (18 itens incluindo látex; "contém" + "pode conter") |
| **RDC 24/2010** | Aditivos: classificação funcional, INS codes — afeta ordem dos ingredientes |
| **RDC 269/2005** | IDR — base do cálculo do % VD |
| **RDC 54/2012** | Claims (zero açúcar, light, diet, integral, sem lactose, fonte de proteína) |
| **RDC 275/2002** | BPF — fundamenta rastreabilidade e recall (retenção mínima 5 anos) |
| **Art. 22 RDC 429/2020** | Alimentos isentos (frutas in natura, especiarias, vinagre, água, café puro) |

**Penalidade financeira por rótulo irregular**: R$ 6.000 mínimo até R$ 1.500.000 (Lei 6.437/77 atualizada). Multa por unidade comercializada, não por SKU. Casa da Babá produz 50 lotes/mês × ~20 unidades = 1000 oportunidades/mês de multa por SKU mal-rotulado.

---

## 3. Decisões Aprovadas (consolidadas)

| Decisão | Escolha | Origem |
|---|---|---|
| Origem da receita/BOM | **Caminho B aceito (2026-05-16)**: módulo opera em modo Manual desde o MVP. Coexiste com Auto quando Calculadora de Produção mergear (entidade `ProdutoComposicao` em worktree `feat/calculadora-producao`, não merged em master). | Decisão Felipe pós-Q&A |
| Gate do módulo | `TenantFeatureFlag` por empresa, `Feature="Rotulagem"`. `SegmentoEmpresa==Alimentos` apenas sugere ativar via modal. | Q1 |
| Padrões de rótulo | BR no MVP (apenas `BrRdc429Tabular`). Arquitetura preparada para FDA/EU via interface `IRenderizadorRotulo` + stubs registrados no factory. | Q3 |
| QR público | Slug `PublicSlug` (UUIDv4 opaco) reservado no MVP. Página pública renderizada **em F+1**. | Q4 + corte Q32 |
| Calculadora Preview | 4 superfícies no MVP: aba Nutricional do Produto, Etapa 2 da Calculadora (quando mergear), PWA Pedidos (leitura cache), App mobile Casa da Babá (leitura cache). Modal "Simular ajuste" no Lote **removido** (link "Reformular este produto" → produto-pai). Simulador standalone + Comparador → F+1. | Q17, Q18 |
| Multi-device drafts | `RascunhoNutricional` no servidor é fonte de verdade. `localStorage` apenas cache de sessão ativa. Sem edição offline-first no MVP. | Q19 |
| Microcopy | Banner Preview: "Você está simulando. Mudanças aqui não afetam rótulos publicados." Banner Live: "Oficial · publicado por {user} em {data}." Botão primário: "Confirmar essa receita". | Q25 |
| RT no MVP | Cadastro permitido para Suplementos/Infantis + bloqueio de publicação até RT aprovar (4º modo `AguardandoAprovacaoRt`). Hash de aprovação inclui secret de servidor — **NÃO é assinatura digital legal**, é "Comprovante de Aprovação Interna" (ADR-0013). | Q28, Q30 |
| Nomenclatura | PT-BR para negócio, EN para sufixos técnicos. ADR-0011 fixa regra + teste de arquitetura automatizado. | Q37 |
| Refator IA | `IClaudeStructuredExtractor<TIn,TOut>` + `IClaudeStreamingTextGenerator<TIn>` criados em F0.5. 3 classes Claude existentes refatoradas em worktree isolada `feat/claude-extractor-refactor` (commit 5e0aa622). | Q38 |
| ADR numeração | Reservar 0002–0010 para ADRs futuros próximos. Usar **ADR-0011** (Nomenclatura + Rotulagem), **ADR-0012** (Backup + storage), **ADR-0013** (Comprovante Aprovação Interna RT). Sem colisão verificada (apenas ADR-0001 existe). | Decisão Felipe 2026-05-16 |
| CI billing | Pagar billing GitHub Actions ANTES de F2. Custo R$ 50–200/mês << risco multa. | Q33 |
| Storage de rótulos | Fly volume `/data/rotulos/{empresaId}/{rotuloId}.{pdf|png|json}` no MVP. Migração para Cloudflare R2 quando passar 50GB. Backup diário + restore-test mensal automatizados. | Q&A + ADR-0012 |
| Provedor de email | **Resend** (free tier 100/dia, 3k/mês). Template engine `RazorLight`. | Q&A consolidação |
| Pureza do motor | `CalculadoraNutricional` é função estática pura (sem CT, sem async, sem repo). UseCase faz I/O upstream, depois chama função pura. | Q15 |
| Coexistência Manual/Auto | `Produto.OrigemCalculoNutricional` enum (Manual | Auto). Manual usa nova tabela `NutricaoManualProdutoFinal`. Auto usa `ProdutoComposicao` + `ProdutoNutricaoCalculada`. Migração entre modos é bidirecional. | Decisão P5 + P6 (este doc) |
| Secret RT | `Anvisa:AprovacaoSecret` em Fly Secrets (env var). Provisionado por comando admin no primeiro boot. Rotação não invalida hashes antigos (snapshot guarda valor). Auditoria em `AuditoriaRotacaoSecretRt` (tabela mínima). | Decisão P4 (este doc) |

---

## 4. Arquitetura de Domínio

### 4.1 Dependências entre módulos

```
EasyStock.Domain
  └─ Entities/Rotulagem/         (entidades + value objects + IGlobalCatalog)
     └─ Enums/                    (SubTipoAlimento, Alergeno, …)

EasyStock.Application
  ├─ Ports/Output/Ai/             (IClaudeStructuredExtractor, IClaudeStreamingTextGenerator)
  ├─ Rotulagem/Services/          (CalculadoraNutricional, IConformidadeValidator, …)
  ├─ Rotulagem/UseCases/          (CalcularNutricaoPreview, GerarRotulo, …)
  └─ Rotulagem/EventHandlers/     (CacheInvalidationHandler, RastreabilidadeEventoHandler)

EasyStock.Infra.Postgre
  ├─ Services/Ai/                 (ClaudeHttpBase + 2 bases — já feito em F0.5)
  ├─ Services/Rotulagem/          (impl de IConformidadeValidator, etc.)
  ├─ Repositories/Rotulagem/      (EF repos)
  ├─ Migrations/                  (única migration F2 + seeds via job)
  └─ Interceptors/                (ProtecaoCatalogoGlobalInterceptor)

EasyStock.Infra.Async
  ├─ Rotulagem/                   (ImportadorTaco, GeradorPdfRotulo, renderizadores)
  ├─ Rotulagem/Impressoras/       (ZebraZplAdapter, A4PdfMultiUpAdapter)
  ├─ Jobs/                        (ArquivamentoRascunhoJob, RascunhoTtlJob, RecalculoStaleCacheJob)
  └─ SeedHostedService/           (RotulagemSeedHostedService)

EasyStock.Infra.Email                  ← NOVO PROJETO em F0.5
  ├─ ResendEmailService.cs        (impl de IEmailService)
  └─ Templates/Rotulagem/*.cshtml  (RazorLight)

EasyStock.Api
  ├─ Controllers/                 (PerfisNutricionaisController, RotulosController, …)
  └─ Controllers/v1/              (API pública versionada)

EasyStock.Web
  ├─ Views/Shared/                (_PainelTabelaNutricional.cshtml — 4 modos)
  ├─ Views/Produtos/              (aba Nutricional)
  ├─ Views/Lotes/                 (aba Rotulo)
  ├─ Views/Rotulagem/             (menu central 3 abas)
  └─ wwwroot/js/                  (painel-tabela-nutricional.js)
```

### 4.2 Princípios

- **Aditivo**: zero `ALTER TABLE` em colunas existentes do EasyStok. Migration única em F2 adiciona apenas tabelas novas + FKs apontando para `Empresa`, `Produto`, `Lote`, `LoteItem`, `Fornecedor`.
- **Pureza do motor**: `CalculadoraNutricional` é `static class` sem dependências; recebe DTO totalmente hidratado.
- **Snapshot imutável**: `Rotulo.DadosSnapshotJson` guarda input + output + algorithmVersion. Recomputação offline é determinística.
- **RLS Postgres**: toda tabela com `EmpresaId` tem policy `USING (EmpresaId = current_setting('app.current_empresa')::uuid)`. Setado pelo `ICurrentTenantContext` em cada conexão.
- **IGlobalCatalog**: marker interface para tabelas sem RLS (catálogos seedados globais). Teste NetArchTest bloqueia entidades `IGlobalCatalog` com FK para `Empresa` (já criado em F0.5).
- **Função pura vs orquestração**: separação `static Calcular(input)` (pura) × `UseCase.Handle(id, ct)` (orquestra I/O + chama pura).

### 4.3 Modelo de Dados Completo

Cardinalidade estimada para Casa da Babá em 1 ano de uso (50 lotes/mês, 30 produtos finais, 50 insumos, 5 fornecedores).

#### 4.3.1 PerfilNutricional (N:1 com Produto, FK Fornecedor opcional)

```sql
CREATE TABLE rotulagem.perfil_nutricional (
    id                       uuid PRIMARY KEY,                     -- UUIDv7
    empresa_id               uuid NOT NULL REFERENCES public.empresa(id) ON DELETE CASCADE,
    produto_id               uuid NOT NULL REFERENCES public.produto(id) ON DELETE CASCADE,
    fornecedor_id            uuid NULL REFERENCES public.fornecedor(id) ON DELETE SET NULL,
    versao_id                uuid NOT NULL,                        -- bumpa a cada edição; entra no hash
    eh_padrao_atual          boolean NOT NULL DEFAULT false,
    origem                   text NOT NULL CHECK (origem IN ('Manual','Taco','Fornecedor','Laboratorio')),
    origem_referencia        text NULL,
    data_coleta              date NOT NULL,
    valor_energetico_kcal    numeric(8,2) NOT NULL CHECK (valor_energetico_kcal >= 0),
    valor_energetico_kj      numeric(8,2) NOT NULL CHECK (valor_energetico_kj  >= 0),
    carboidratos_g           numeric(8,2) NOT NULL CHECK (carboidratos_g       >= 0),
    acucares_totais_g        numeric(8,2) NOT NULL CHECK (acucares_totais_g    >= 0),
    acucares_adicionados_g   numeric(8,2) NOT NULL CHECK (acucares_adicionados_g >= 0),
    proteinas_g              numeric(8,2) NOT NULL CHECK (proteinas_g          >= 0),
    gorduras_totais_g        numeric(8,2) NOT NULL CHECK (gorduras_totais_g    >= 0),
    gorduras_saturadas_g     numeric(8,2) NOT NULL CHECK (gorduras_saturadas_g >= 0),
    gorduras_trans_g         numeric(8,2) NOT NULL CHECK (gorduras_trans_g     >= 0),
    fibras_g                 numeric(8,2) NOT NULL CHECK (fibras_g             >= 0),
    sodio_mg                 numeric(8,2) NOT NULL CHECK (sodio_mg             >= 0),
    criado_em                timestamptz NOT NULL DEFAULT now(),
    alterado_em              timestamptz NOT NULL DEFAULT now()
);

-- unique parcial: no máximo 1 padrão por (empresa_id, produto_id)
CREATE UNIQUE INDEX ux_perfil_padrao
    ON rotulagem.perfil_nutricional (empresa_id, produto_id)
    WHERE eh_padrao_atual = true;

CREATE INDEX ix_perfil_produto       ON rotulagem.perfil_nutricional (empresa_id, produto_id);
CREATE INDEX ix_perfil_fornecedor    ON rotulagem.perfil_nutricional (empresa_id, fornecedor_id);

ALTER TABLE rotulagem.perfil_nutricional ENABLE ROW LEVEL SECURITY;
CREATE POLICY perfil_nutricional_tenant ON rotulagem.perfil_nutricional
    USING (empresa_id = current_setting('app.current_empresa')::uuid);
```

**Cardinalidade Casa da Babá em 1 ano:** ~50 insumos × média 1,5 fornecedores/insumo = **75 perfis**. Atualização da ficha bumpa `versao_id` (cria nova linha histórica? Não — UPDATE in-place; histórico vai em log de auditoria separado se precisar).

#### 4.3.2 PerfilNutricionalAlergeno (N:N com enum Alergeno)

```sql
CREATE TABLE rotulagem.perfil_nutricional_alergeno (
    perfil_nutricional_id uuid NOT NULL REFERENCES rotulagem.perfil_nutricional(id) ON DELETE CASCADE,
    empresa_id            uuid NOT NULL,
    alergeno              text NOT NULL CHECK (alergeno IN (
        'Trigo','Centeio','Cevada','Aveia','Crustaceos','Ovos','Peixes','Amendoim',
        'Soja','Leite','Amendoas','Avelas','CastanhaCaju','CastanhaPara','Macadamia',
        'NozPecan','Pistache','Pinoli','LatexNatural'
    )),
    tipo                  text NOT NULL CHECK (tipo IN ('Contem','PodeConter')),
    PRIMARY KEY (perfil_nutricional_id, alergeno)
);

CREATE INDEX ix_perfil_alergeno_alergeno ON rotulagem.perfil_nutricional_alergeno (alergeno);

ALTER TABLE rotulagem.perfil_nutricional_alergeno ENABLE ROW LEVEL SECURITY;
CREATE POLICY perfil_alergeno_tenant ON rotulagem.perfil_nutricional_alergeno
    USING (empresa_id = current_setting('app.current_empresa')::uuid);
```

**Cardinalidade**: 75 perfis × média 2 alérgenos = **150 linhas**.

#### 4.3.3 PerfilNutricionalClaim (N:N)

```sql
CREATE TABLE rotulagem.perfil_nutricional_claim (
    perfil_nutricional_id uuid NOT NULL REFERENCES rotulagem.perfil_nutricional(id) ON DELETE CASCADE,
    empresa_id            uuid NOT NULL,
    claim                 text NOT NULL,         -- referência ao código em rotulagem.regra_claim
    validado_em           timestamptz NOT NULL,
    validado_por          uuid NOT NULL,
    PRIMARY KEY (perfil_nutricional_id, claim)
);

ALTER TABLE rotulagem.perfil_nutricional_claim ENABLE ROW LEVEL SECURITY;
CREATE POLICY perfil_claim_tenant ON rotulagem.perfil_nutricional_claim
    USING (empresa_id = current_setting('app.current_empresa')::uuid);
```

**Cardinalidade**: ~30 linhas/ano (poucos insumos têm claim explícito).

#### 4.3.4 LoteItemFichaUsada (rastreabilidade B7)

```sql
CREATE TABLE rotulagem.lote_item_ficha_usada (
    lote_item_id                  uuid PRIMARY KEY REFERENCES public.lote_item(id) ON DELETE CASCADE,
    empresa_id                    uuid NOT NULL,
    perfil_nutricional_versao_id  uuid NOT NULL,
    capturado_em                  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_lote_item_ficha_versao ON rotulagem.lote_item_ficha_usada (perfil_nutricional_versao_id);

ALTER TABLE rotulagem.lote_item_ficha_usada ENABLE ROW LEVEL SECURITY;
CREATE POLICY lote_item_ficha_tenant ON rotulagem.lote_item_ficha_usada
    USING (empresa_id = current_setting('app.current_empresa')::uuid);
```

**Cardinalidade**: 50 lotes/mês × média 5 itens/lote × 12 meses = **3.000 linhas/ano**.

#### 4.3.5 TacoIngrediente (catálogo global, sem RLS)

```sql
CREATE TABLE rotulagem.taco_ingrediente (
    codigo_taco         int PRIMARY KEY,
    nome_pt_br          text NOT NULL,
    nome_en             text NULL,
    grupo_alimentar     text NOT NULL,
    valor_energetico_kcal numeric(8,2) NOT NULL,
    -- ... mesmos campos nutricionais por 100g
    versao              text NOT NULL,                          -- "TACO 4ª edição"
    criado_em           timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_taco_nome_trgm ON rotulagem.taco_ingrediente
    USING GIN (nome_pt_br gin_trgm_ops);

-- SEM RLS (catálogo global, IGlobalCatalog).
-- pg_trgm é extensão Postgres: [Validar com Felipe antes de F4] — CREATE EXTENSION IF NOT EXISTS pg_trgm
-- precisa de role com permissão. Se Fly Postgres não permite, fallback é ILIKE simples.
```

**Cardinalidade**: TACO 4ª ed tem ~600 alimentos. Seed único.

#### 4.3.6 ProdutoNutricaoCalculada (cache do cálculo Auto)

```sql
CREATE TABLE rotulagem.produto_nutricao_calculada (
    produto_id            uuid PRIMARY KEY REFERENCES public.produto(id) ON DELETE CASCADE,
    empresa_id            uuid NOT NULL,
    status                text NOT NULL CHECK (status IN ('Atual','Stale')),
    calculado_em          timestamptz NOT NULL,
    receita_versao_hash   text NOT NULL,         -- sha256 hex
    algorithm_version     text NOT NULL,         -- ex "1.0.0"
    -- nutrientes por porção + por 100g (20 colunas):
    porcao_quantidade_g       numeric(8,2) NOT NULL,
    valor_energetico_kcal_porcao numeric(8,2) NOT NULL,
    -- ... demais campos nutricionais por porção e por 100g
    alergenos_consolidados jsonb NOT NULL DEFAULT '[]'::jsonb,
    lupa_frontal_flags     jsonb NOT NULL DEFAULT '{}'::jsonb
);

ALTER TABLE rotulagem.produto_nutricao_calculada ENABLE ROW LEVEL SECURITY;
CREATE POLICY produto_nutricao_calculada_tenant ON rotulagem.produto_nutricao_calculada
    USING (empresa_id = current_setting('app.current_empresa')::uuid);
```

**Cardinalidade**: 30 produtos finais (modo Auto, quando Calculadora mergear) = **30 linhas**. No MVP em caminho B, **fica vazio** até F5 entrar.

#### 4.3.7 NutricaoManualProdutoFinal (modo Manual — caminho B + reverso Auto→Manual)

```sql
CREATE TABLE rotulagem.nutricao_manual_produto_final (
    produto_id            uuid PRIMARY KEY REFERENCES public.produto(id) ON DELETE CASCADE,
    empresa_id            uuid NOT NULL,
    versao_id             uuid NOT NULL,
    origem                text NOT NULL CHECK (origem IN ('Manual','LaboratoriProprio','Importado')),
    -- 10 nutrientes por porção + por 100g (mesmo layout do cache Auto, sem hash):
    porcao_quantidade_g       numeric(8,2) NOT NULL,
    numero_porcoes_embalagem  int           NOT NULL CHECK (numero_porcoes_embalagem > 0),
    valor_energetico_kcal_100g numeric(8,2) NOT NULL CHECK (valor_energetico_kcal_100g >= 0),
    -- ... demais campos nutricionais por porção e por 100g
    alergenos_consolidados jsonb NOT NULL DEFAULT '[]'::jsonb,
    eh_snapshot_congelado boolean NOT NULL DEFAULT false,
    motivo_congelamento   text NULL,
    criado_em             timestamptz NOT NULL DEFAULT now(),
    alterado_em           timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE rotulagem.nutricao_manual_produto_final ENABLE ROW LEVEL SECURITY;
CREATE POLICY nutricao_manual_tenant ON rotulagem.nutricao_manual_produto_final
    USING (empresa_id = current_setting('app.current_empresa')::uuid);
```

**Cardinalidade**: 30 produtos finais × 1 perfil cada = **30 linhas** (modo Manual). Coexiste com `ProdutoNutricaoCalculada` quando produto migra entre modos — `Produto.OrigemCalculoNutricional` decide qual é a fonte oficial. Decisão da pendência **P6**: tabela dedicada (não reusa `PerfilNutricional`) pela diferença semântica clara (insumo vs produto final).

#### 4.3.8 FichaTecnicaProduto (B9 versionada)

```sql
CREATE TABLE rotulagem.ficha_tecnica_produto (
    id                    uuid PRIMARY KEY,                      -- UUIDv7
    empresa_id            uuid NOT NULL,
    produto_id            uuid NOT NULL REFERENCES public.produto(id) ON DELETE CASCADE,
    versao                int  NOT NULL CHECK (versao >= 1),
    receita_snapshot      jsonb NOT NULL,                        -- congela ProdutoComposicao ou nutrição manual
    eh_snapshot_congelado boolean NOT NULL DEFAULT false,        -- P5: true quando vem de reverso Auto→Manual
    motivo_alteracao      text NULL,
    alterado_em           timestamptz NOT NULL DEFAULT now(),
    alterado_por          uuid NOT NULL,
    UNIQUE (produto_id, versao)
);

CREATE INDEX ix_ficha_produto ON rotulagem.ficha_tecnica_produto (empresa_id, produto_id);

ALTER TABLE rotulagem.ficha_tecnica_produto ENABLE ROW LEVEL SECURITY;
CREATE POLICY ficha_tecnica_tenant ON rotulagem.ficha_tecnica_produto
    USING (empresa_id = current_setting('app.current_empresa')::uuid);
```

**Decisão P5 (reverso Auto→Manual)**: ao reverter, sistema cria nova `FichaTecnicaProduto` com `eh_snapshot_congelado=true` + `receita_snapshot` = última receita Auto conhecida + `motivo_alteracao` obrigatório. Snapshot acessível indefinidamente (mesma retenção dos rótulos, mínimo 5 anos).

**Cardinalidade**: 30 produtos × média 4 versões/ano = **120 linhas/ano**.

#### 4.3.9 ModeloRotulo (catálogo global)

```sql
CREATE TABLE rotulagem.modelo_rotulo (
    id            uuid PRIMARY KEY,
    codigo        text NOT NULL UNIQUE CHECK (codigo ~ '^[A-Z0-9_]+$'),  -- BR_RDC429_TABULAR
    nome          text NOT NULL,
    pais          char(2) NOT NULL,                              -- ISO 3166-1 alpha-2
    layout_json   jsonb NOT NULL,
    versao_hash   text NOT NULL,                                 -- sha256 do layout
    ativo         boolean NOT NULL DEFAULT true,
    criado_em     timestamptz NOT NULL DEFAULT now()
);

-- SEM RLS (catálogo global, IGlobalCatalog).
```

**Cardinalidade**: seed inicial 1 (`BR_RDC429_TABULAR`) + 5 stubs (Linear, EmbalagemPequena, IsentoArt22, Mercosul, Eu, Fda) = **6 linhas** fixas.

#### 4.3.10 ConfiguracaoRotuloEmpresa

```sql
CREATE TABLE rotulagem.configuracao_rotulo_empresa (
    empresa_id                       uuid PRIMARY KEY REFERENCES public.empresa(id) ON DELETE CASCADE,
    modelo_rotulo_default_id         uuid NOT NULL REFERENCES rotulagem.modelo_rotulo(id) ON DELETE RESTRICT,
    denominacao_fabricante_override  text NULL,
    sac_contato                      text NULL,
    usar_fase2_lupa                  boolean NOT NULL DEFAULT false,    -- F+1 ativa em out/2026
    exigir_rt_para_todos             boolean NOT NULL DEFAULT false
);

ALTER TABLE rotulagem.configuracao_rotulo_empresa ENABLE ROW LEVEL SECURITY;
CREATE POLICY config_rotulo_tenant ON rotulagem.configuracao_rotulo_empresa
    USING (empresa_id = current_setting('app.current_empresa')::uuid);
```

**Cardinalidade**: 1 por empresa do segmento Alimentos. Casa da Babá = **1 linha**.

#### 4.3.11 Rotulo (snapshot imutável — auditoria 5 anos)

```sql
CREATE TABLE rotulagem.rotulo (
    id                          uuid PRIMARY KEY,                    -- UUIDv7 (ordenação cronológica interna)
    empresa_id                  uuid NOT NULL,
    lote_id                     uuid NOT NULL REFERENCES public.lote(id) ON DELETE RESTRICT,
    produto_id                  uuid NOT NULL REFERENCES public.produto(id) ON DELETE RESTRICT,
    public_slug                 uuid NOT NULL UNIQUE,                -- UUIDv4 opaco
    modelo_rotulo_id            uuid NOT NULL REFERENCES rotulagem.modelo_rotulo(id) ON DELETE RESTRICT,
    modelo_rotulo_versao_hash   text NOT NULL,
    pdf_blob_uri                text NOT NULL,                       -- /data/rotulos/{empresa}/{id}.pdf
    preview_png_uri             text NULL,
    dados_snapshot_json         jsonb NOT NULL,
    dados_snapshot_json_v2      jsonb NULL,                          -- para migração de schema (Q3 Δ)
    impresso_quantidade         int NOT NULL DEFAULT 0 CHECK (impresso_quantidade >= 0),
    gerado_em                   timestamptz NOT NULL DEFAULT now(),
    gerado_por                  uuid NOT NULL,
    status                      text NOT NULL CHECK (status IN
        ('Rascunho','AguardandoAprovacaoRt','Publicado','Reimpresso','Substituido'))
);

CREATE INDEX ix_rotulo_lote        ON rotulagem.rotulo (empresa_id, lote_id);
CREATE INDEX ix_rotulo_produto     ON rotulagem.rotulo (empresa_id, produto_id);
CREATE INDEX ix_rotulo_status      ON rotulagem.rotulo (empresa_id, status);
CREATE INDEX ix_rotulo_gerado_em   ON rotulagem.rotulo (empresa_id, gerado_em DESC);
CREATE INDEX ix_rotulo_snapshot_jsonb ON rotulagem.rotulo USING GIN (dados_snapshot_json);

ALTER TABLE rotulagem.rotulo ENABLE ROW LEVEL SECURITY;
CREATE POLICY rotulo_tenant ON rotulagem.rotulo
    USING (empresa_id = current_setting('app.current_empresa')::uuid);

-- Política de imutabilidade após publicação: trigger bloqueia UPDATE em status='Publicado'
-- exceto para coluna impresso_quantidade (reimpressões contadas).
CREATE OR REPLACE FUNCTION rotulagem.bloquear_alteracao_rotulo_publicado() RETURNS trigger AS $$
BEGIN
    IF OLD.status = 'Publicado' THEN
        IF NEW.dados_snapshot_json   IS DISTINCT FROM OLD.dados_snapshot_json   OR
           NEW.pdf_blob_uri          IS DISTINCT FROM OLD.pdf_blob_uri          OR
           NEW.modelo_rotulo_id      IS DISTINCT FROM OLD.modelo_rotulo_id      OR
           NEW.gerado_em             IS DISTINCT FROM OLD.gerado_em             OR
           NEW.gerado_por            IS DISTINCT FROM OLD.gerado_por
        THEN
            RAISE EXCEPTION 'Rotulo publicado é imutavel — auditoria sanitaria (ADR-0012)';
        END IF;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER rotulo_imutavel_apos_publicado
    BEFORE UPDATE ON rotulagem.rotulo
    FOR EACH ROW EXECUTE FUNCTION rotulagem.bloquear_alteracao_rotulo_publicado();
```

**Cardinalidade**: 50 lotes/mês × média 1.2 versões (reimpressão = nova `Rotulo`) × 12 meses = **720 linhas/ano**. Em 5 anos = ~3.600.

#### 4.3.12 AprovacaoRtRotulo + ResponsavelTecnico

```sql
CREATE TABLE rotulagem.responsavel_tecnico (
    id                       uuid PRIMARY KEY,
    empresa_id               uuid NOT NULL,
    nome                     text NOT NULL,
    cpf                      char(11) NOT NULL,
    crn                      text NOT NULL,                       -- ex "12345-3" (CRN-3 = SP)
    assinatura_imagem_uri    text NULL,
    criado_em                timestamptz NOT NULL DEFAULT now(),
    ativo                    boolean NOT NULL DEFAULT true
);

CREATE UNIQUE INDEX ux_rt_cpf_empresa ON rotulagem.responsavel_tecnico (empresa_id, cpf) WHERE ativo;

ALTER TABLE rotulagem.responsavel_tecnico ENABLE ROW LEVEL SECURITY;
CREATE POLICY rt_tenant ON rotulagem.responsavel_tecnico
    USING (empresa_id = current_setting('app.current_empresa')::uuid);

CREATE TABLE rotulagem.aprovacao_rt_rotulo (
    rotulo_id                              uuid PRIMARY KEY REFERENCES rotulagem.rotulo(id) ON DELETE CASCADE,
    empresa_id                             uuid NOT NULL,
    responsavel_tecnico_id                 uuid NOT NULL REFERENCES rotulagem.responsavel_tecnico(id) ON DELETE RESTRICT,
    aprovado_em                            timestamptz NOT NULL,
    comprovante_aprovacao_interna_hash     text NOT NULL,         -- sha256(nome+CRN+timestamp+rotuloId+secret)
    secret_versao                          int NOT NULL,          -- qual versão de secret foi usada
    assinatura_imagem_uri                  text NULL,             -- override por rótulo
    observacoes_rt                         text NULL
);

ALTER TABLE rotulagem.aprovacao_rt_rotulo ENABLE ROW LEVEL SECURITY;
CREATE POLICY aprovacao_rt_tenant ON rotulagem.aprovacao_rt_rotulo
    USING (empresa_id = current_setting('app.current_empresa')::uuid);
```

**Cardinalidade**: RT 1–2 por empresa = **1–2 linhas**. AprovacaoRtRotulo só para Suplementos/Infantis = ~30 linhas/ano (Casa da Babá produz só massas/molhos; valor real próximo de 0 — feature útil para outros clientes futuros).

#### 4.3.13 AuditoriaRotacaoSecretRt (decisão P4)

```sql
CREATE TABLE rotulagem.auditoria_rotacao_secret_rt (
    id                 uuid PRIMARY KEY,
    secret_versao_nova int NOT NULL,
    secret_versao_anterior int NOT NULL,
    rotacionado_em     timestamptz NOT NULL DEFAULT now(),
    rotacionado_por    uuid NOT NULL,                              -- admin user id
    motivo             text NOT NULL CHECK (length(motivo) >= 10)  -- obrigatório
);

-- SEM RLS (auditoria global de operação admin do EasyStok).
```

**Cardinalidade**: rotação espera-se ≤1/ano. **0–5 linhas**.

#### 4.3.14 RascunhoNutricional

```sql
CREATE TABLE rotulagem.rascunho_nutricional (
    id                          uuid PRIMARY KEY,
    empresa_id                  uuid NOT NULL,
    usuario_id                  uuid NOT NULL,
    produto_id                  uuid NULL REFERENCES public.produto(id) ON DELETE CASCADE,
    nome                        text NOT NULL,
    tags                        text[] NOT NULL DEFAULT '{}',
    receita_json                jsonb NOT NULL,
    resultado_nutricional_json  jsonb NULL,
    criado_em                   timestamptz NOT NULL DEFAULT now(),
    atualizado_em               timestamptz NOT NULL DEFAULT now(),
    arquivado_em                timestamptz NULL,                  -- > 30d sem acesso
    status                      text NOT NULL DEFAULT 'Ativo' CHECK (status IN ('Ativo','Arquivado','Excluido'))
);

CREATE INDEX ix_rascunho_usuario ON rotulagem.rascunho_nutricional (empresa_id, usuario_id, status);
CREATE INDEX ix_rascunho_produto ON rotulagem.rascunho_nutricional (empresa_id, produto_id) WHERE produto_id IS NOT NULL;

ALTER TABLE rotulagem.rascunho_nutricional ENABLE ROW LEVEL SECURITY;
CREATE POLICY rascunho_tenant ON rotulagem.rascunho_nutricional
    USING (
        empresa_id = current_setting('app.current_empresa')::uuid
        AND usuario_id = current_setting('app.current_usuario')::uuid
    );
```

**Cardinalidade**: ~5 rascunhos ativos por usuário × 2 usuários (Felipe + Thatiane) = **~10 linhas em uso**, archive cresce ~50/ano.

#### 4.3.15 ConfiguracaoImpressora

```sql
CREATE TABLE rotulagem.configuracao_impressora (
    id                  uuid PRIMARY KEY,
    empresa_id          uuid NOT NULL,
    loja_id             uuid NOT NULL REFERENCES public.loja(id) ON DELETE CASCADE,
    tipo                text NOT NULL CHECK (tipo IN ('Zebra','Argox','Elgin','A4_Generico')),
    modelo              text NULL,
    conexao             text NOT NULL CHECK (conexao IN ('USB','Ethernet','IP')),
    endereco_ip         inet NULL,
    padrao_etiqueta     text NOT NULL CHECK (padrao_etiqueta IN ('50x30mm','100x50mm','A4_4colunas','A4_8colunas')),
    ativo               boolean NOT NULL DEFAULT true
);

CREATE INDEX ix_impressora_loja ON rotulagem.configuracao_impressora (empresa_id, loja_id);

ALTER TABLE rotulagem.configuracao_impressora ENABLE ROW LEVEL SECURITY;
CREATE POLICY impressora_tenant ON rotulagem.configuracao_impressora
    USING (empresa_id = current_setting('app.current_empresa')::uuid);
```

**Cardinalidade**: 1–3 impressoras por loja = **1–3 linhas**.

#### 4.3.16 PrivacidadeFornecedor (LGPD)

```sql
CREATE TABLE rotulagem.privacidade_fornecedor (
    fornecedor_id        uuid PRIMARY KEY REFERENCES public.fornecedor(id) ON DELETE CASCADE,
    empresa_id           uuid NOT NULL,
    expoe_no_qr_publico  boolean NOT NULL DEFAULT true,
    nivel_privacidade    text NOT NULL DEFAULT 'Cosmetica' CHECK (nivel_privacidade IN ('Cosmetica','LegalLgpdPleno')),
    data_alteracao       timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE rotulagem.privacidade_fornecedor ENABLE ROW LEVEL SECURITY;
CREATE POLICY privacidade_fornecedor_tenant ON rotulagem.privacidade_fornecedor
    USING (empresa_id = current_setting('app.current_empresa')::uuid);
```

**Cardinalidade**: 5 fornecedores Casa da Babá = **5 linhas**.

#### 4.3.17 RastreabilidadeEvento (backend popula desde F2, UI F+1)

```sql
CREATE TABLE rotulagem.rastreabilidade_evento (
    id              uuid PRIMARY KEY,
    empresa_id      uuid NOT NULL,
    produto_id      uuid NOT NULL REFERENCES public.produto(id) ON DELETE CASCADE,
    lote_id         uuid NULL REFERENCES public.lote(id) ON DELETE SET NULL,
    tipo_evento     text NOT NULL CHECK (tipo_evento IN
        ('RecebimentoInsumo','Producao','Embalagem','Venda','Devolucao')),
    referencia_json jsonb NOT NULL,                                -- {fornecedor_id, pedido_id, cliente_id}
    data_evento     timestamptz NOT NULL,
    registrado_em   timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_rastreabilidade_lote      ON rotulagem.rastreabilidade_evento (empresa_id, lote_id)        WHERE lote_id IS NOT NULL;
CREATE INDEX ix_rastreabilidade_data      ON rotulagem.rastreabilidade_evento (empresa_id, data_evento DESC);
CREATE INDEX ix_rastreabilidade_cliente   ON rotulagem.rastreabilidade_evento USING GIN ((referencia_json->'cliente_id'));

ALTER TABLE rotulagem.rastreabilidade_evento ENABLE ROW LEVEL SECURITY;
CREATE POLICY rastreabilidade_tenant ON rotulagem.rastreabilidade_evento
    USING (empresa_id = current_setting('app.current_empresa')::uuid);
```

**Cardinalidade**: ~500 eventos/mês (recebimentos, produções, vendas) × 12 = **~6.000 linhas/ano**.

#### 4.3.18 Catálogos globais (IGlobalCatalog, sem RLS)

```sql
-- Porções padrão IN 75 Anexo IV (~50 categorias)
CREATE TABLE rotulagem.porcao_padrao_categoria (
    categoria_codigo  text PRIMARY KEY,                          -- MASSA_FRESCA, MOLHO, ...
    quantidade_g      numeric(6,2) NOT NULL CHECK (quantidade_g > 0),
    descricao         text NOT NULL,
    referencia_in75   text NOT NULL                              -- "IN 75 Anexo IV, item 12"
);

-- Aditivos RDC 24/2010 (~400 INS codes)
CREATE TABLE rotulagem.catalogo_aditivo (
    codigo_ins              text PRIMARY KEY,                    -- "INS 211"
    nome                    text NOT NULL,
    funcao_tecnologica      text NOT NULL CHECK (funcao_tecnologica IN
        ('Conservante','Corante','Edulcorante','Antioxidante','Estabilizante',
         'Acidulante','Aromatizante','Espessante','Emulsificante','Outro')),
    paises_proibidos        text[] NOT NULL DEFAULT '{}'         -- ISO 3166-1 alpha-2; preenchido em F+2 C6
);

-- Claims RDC 54/2012 (8 no MVP, expansível)
CREATE TABLE rotulagem.regra_claim (
    claim_codigo            text PRIMARY KEY,                    -- ZERO_ACUCAR, LIGHT, ...
    nome_humano             text NOT NULL,                       -- "Zero açúcar"
    expressao_criterio_json jsonb NOT NULL,                      -- JsonLogic
    base_unidade            text NOT NULL CHECK (base_unidade IN ('Por100g','PorPorcao')),
    referencia_rdc          text NOT NULL                        -- "RDC 54/2012 Art. 3"
);

-- Normas Anvisa carregadas (9 no MVP)
CREATE TABLE rotulagem.norma_regulatoria (
    codigo            text PRIMARY KEY,                          -- RDC_429_2020
    nome              text NOT NULL,
    url_oficial       text NOT NULL,
    data_publicacao   date NOT NULL,
    data_vigencia     date NOT NULL,
    escopo_json       jsonb NOT NULL,                            -- que campos/regras afeta
    versao_corrente   int NOT NULL DEFAULT 1
);
```

**Todas sem RLS** (marker `IGlobalCatalog`). Cardinalidade total: ~1.000 linhas estáticas seedeadas.

### 4.4 Eventos de Domínio + Invalidação de Cache

| Evento | Disparado por | Handler | Idempotente? | Ação |
|---|---|---|---|---|
| `ProdutoComposicaoAlteradoEvent` | `Calculadora.SalvarComposicao` (quando merger) | `CacheInvalidationHandler` | sim | UPDATE `produto_nutricao_calculada` SET status='Stale' WHERE produto_id=X |
| `PerfilNutricionalAlteradoEvent` | `SalvarPerfilNutricionalUseCase` | `CacheInvalidationHandler` | sim | SELECT produto_ids que usam esse insumo → UPDATE status='Stale' em todos |
| `FichaTecnicaPublicadaEvent` | `AplicarNutricaoAoProdutoUseCase` | `CacheInvalidationHandler` | sim | UPDATE status='Atual' + grava novo `receita_versao_hash` |
| `LoteRecebidoEvent` (existente) | UseCase de recebimento | `RastreabilidadeEventoHandler` | sim | INSERT `rastreabilidade_evento` (tipo='RecebimentoInsumo') + INSERT `lote_item_ficha_usada` |
| `LoteProduzidoEvent` (existente) | UseCase de produção | `RastreabilidadeEventoHandler` | sim | INSERT (tipo='Producao') |
| `VendaConcluidaEvent` (existente) | UseCase de venda | `RastreabilidadeEventoHandler` | sim | INSERT (tipo='Venda', referencia_json com cliente_id) |
| `RotuloPublicadoEvent` (novo) | `PublicarRotuloUseCase` | `EmailNotificationHandler` (F+1) | sim | Notifica admin se primeiro rótulo do mês |

**Contrato dos handlers**: idempotentes por design (todos os INSERTs usam ON CONFLICT DO NOTHING via PK natural composta; UPDATE de status é commutativo). Entrega at-least-once via outbox pattern (tabela `eventos_outbox` já existente no EasyStok — reusar).

**Fila de stale**: usa coluna `status` na própria `produto_nutricao_calculada`. Sem tabela separada. Sem LISTEN/NOTIFY (overhead injustificado para cardinalidade de 30 produtos).

**Background job de recálculo**:
```yaml
job: RecalculoStaleCacheJob
schedule: "*/5 * * * *"      # a cada 5 min
timeout: 60s
retry: 3 vezes, exponential backoff (1s, 5s, 25s)
behavior:
  - SELECT produto_id FROM produto_nutricao_calculada WHERE status='Stale' LIMIT 50
  - Para cada um: chamar AplicarNutricaoAoProdutoUseCase (idempotente, escreve cache + status='Atual')
  - Se faltar PerfilNutricional → marca produto como 'Stale-Bloqueado' + alerta admin no badge
```

**Implementação do job**: `EasyStock.Infra.Async/Jobs/RecalculoStaleCacheJob.cs`, `BackgroundService` com `Timer` (padrão dotnet, sem Hangfire).

---

## 5. Motor de Cálculo

### 5.1 Value Objects e Records

```csharp
// EasyStock.Domain/ValueObjects/Rotulagem/ValorNutricional.cs
namespace EasyStock.Domain.ValueObjects.Rotulagem;

public readonly record struct ValorNutricional(decimal Quantidade, UnidadeNutricional Unidade)
{
    public ValorNutricional ParaGramas() => Unidade switch
    {
        UnidadeNutricional.Grama      => this,
        UnidadeNutricional.Quilograma => new(Quantidade * 1000m,         UnidadeNutricional.Grama),
        UnidadeNutricional.Mililitro  => this with { Unidade = UnidadeNutricional.Grama }, // assume densidade 1
        UnidadeNutricional.Litro      => new(Quantidade * 1000m,         UnidadeNutricional.Grama),
        UnidadeNutricional.Miligrama  => new(Quantidade / 1000m,         UnidadeNutricional.Grama),
        _ => throw new InvalidOperationException($"Unidade não suportada: {Unidade}")
    };
}

public enum UnidadeNutricional { Grama, Quilograma, Miligrama, Mililitro, Litro }
```

```csharp
// EasyStock.Domain/ValueObjects/Rotulagem/LimitesLupa.cs
namespace EasyStock.Domain.ValueObjects.Rotulagem;

public sealed record LimitesLupa(
    string VersaoIN75,                    // "IN75_fase1" | "IN75_fase2"
    decimal SodioMgPor100gSolido,
    decimal SodioMgPor100mlLiquido,
    decimal GorduraSaturadaGPor100gSolido,
    decimal GorduraSaturadaGPor100mlLiquido,
    decimal AcucarAdicionadoGPor100gSolido,
    decimal AcucarAdicionadoGPor100mlLiquido)
{
    public static LimitesLupa Fase1 { get; } = new(
        VersaoIN75: "IN75_fase1",
        SodioMgPor100gSolido:               600m,
        SodioMgPor100mlLiquido:             300m,
        GorduraSaturadaGPor100gSolido:        6m,
        GorduraSaturadaGPor100mlLiquido:      3m,
        AcucarAdicionadoGPor100gSolido:      15m,
        AcucarAdicionadoGPor100mlLiquido:     7.5m);

    public static LimitesLupa Fase2 { get; } = new(           // out/2026
        VersaoIN75: "IN75_fase2",
        SodioMgPor100gSolido:               400m,
        SodioMgPor100mlLiquido:             200m,
        GorduraSaturadaGPor100gSolido:        5m,
        GorduraSaturadaGPor100mlLiquido:      2.5m,
        AcucarAdicionadoGPor100gSolido:      12m,
        AcucarAdicionadoGPor100mlLiquido:     6m);
}

public sealed record LupaAltoTeorFlags(
    bool AcucarAdicionadoAlto,
    bool GorduraSaturadaAlto,
    bool SodioAlto,
    string LimitesAplicadosVersaoIN75);
```

```csharp
// EasyStock.Application/Rotulagem/Models/CalculoNutricionalInput.cs
namespace EasyStock.Application.Rotulagem.Models;

public sealed record CalculoNutricionalInput(
    Receita Receita,
    string CodigoModeloRotulo,                    // ex "BR_RDC429_TABULAR"
    string AlgorithmVersion,                      // ex "1.0.0"
    LimitesLupa LimitesLupa);

public sealed record Receita(
    Rendimento Rendimento,
    Porcao Porcao,
    IReadOnlyList<InsumoComFichaCongelada> Insumos,
    EstadoFisicoProduto EstadoFisico);            // Solido | Liquido — define qual limite de lupa aplica

public sealed record Rendimento(
    decimal SaidaLiquidaGramas,
    decimal FatorRendimento);                     // calc upstream: saida_liquida / entrada_bruta

public sealed record Porcao(
    decimal QuantidadeGramas,
    int NumeroPorcoesPorEmbalagem);

public sealed record InsumoComFichaCongelada(
    Guid ProdutoId,
    string ProdutoNome,
    string? FornecedorNome,
    Guid PerfilNutricionalVersaoId,
    decimal QuantidadeReceitaGramas,
    FichaCongelada Ficha);

public sealed record FichaCongelada(
    string Origem,                                // "TACO", "Fornecedor", "Manual", "Laboratorio"
    string OrigemReferencia,                      // ex "TACO 4ª ed #042"
    decimal ValorEnergeticoKcalPor100g,
    decimal ValorEnergeticoKjPor100g,
    decimal CarboidratosGPor100g,
    decimal AcucaresTotaisGPor100g,
    decimal AcucaresAdicionadosGPor100g,
    decimal ProteinasGPor100g,
    decimal GordurasTotaisGPor100g,
    decimal GordurasSaturadasGPor100g,
    decimal GordurasTransGPor100g,
    decimal FibrasGPor100g,
    decimal SodioMgPor100g,
    IReadOnlyList<string> Alergenos);

public enum EstadoFisicoProduto { Solido, Liquido }
```

```csharp
// EasyStock.Application/Rotulagem/Models/ResultadoNutricional.cs
namespace EasyStock.Application.Rotulagem.Models;

public sealed record ResultadoNutricional(
    string AlgorithmVersion,
    DateTime CalculadoEm,
    TabelaNutricional Tabela,
    IReadOnlyList<string> AlergenosConsolidados,
    LupaAltoTeorFlags LupaFrontal,
    IReadOnlyList<ClaimEmRisco> ClaimsEmRisco,
    string ReceitaVersaoHash);                    // sha256 hex do input canonicalizado

public sealed record TabelaNutricional(
    ValoresPorReferencia PorPorcao,
    ValoresPorReferencia Por100g);

public sealed record ValoresPorReferencia(
    decimal ValorEnergeticoKcal,
    decimal ValorEnergeticoKj,
    decimal CarboidratosG,
    decimal AcucaresTotaisG,
    decimal AcucaresAdicionadosG,
    decimal ProteinasG,
    decimal GordurasTotaisG,
    decimal GordurasSaturadasG,
    decimal GordurasTransG,
    decimal FibrasG,
    decimal SodioMg);

public sealed record ClaimEmRisco(
    string ClaimCodigo,                           // "ZERO_ACUCAR"
    string Motivo,                                // "AcucaresTotais=1.2g > limite 0.5g/100g"
    string ReferenciaRdc);                        // "RDC 54/2012 Art. 3 §I"
```

### 5.2 NutritionCalculator (estática pura)

```csharp
// EasyStock.Application/Rotulagem/Services/CalculadoraNutricional.cs
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Rotulagem.Models;
using EasyStock.Domain.ValueObjects.Rotulagem;

namespace EasyStock.Application.Rotulagem.Services;

public static class CalculadoraNutricional
{
    public const string AlgorithmVersionAtual = "1.0.0";

    /// <summary>
    /// Função pura: dado input hidratado, retorna resultado determinístico.
    /// Garantias: mesmo input → mesmo output, mesmo hash. Sem I/O, sem CT, sem async.
    /// </summary>
    public static ResultadoNutricional Calcular(CalculoNutricionalInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Receita.Insumos.Count == 0)
            throw new ArgumentException("Receita sem insumos.", nameof(input));

        var totaisPor100g  = CalcularTotaisPor100g(input.Receita);
        var totaisPorcao   = CalcularTotaisPorPorcao(totaisPor100g, input.Receita.Porcao);

        var alergenos = input.Receita.Insumos
            .SelectMany(i => i.Ficha.Alergenos)
            .Distinct()
            .OrderBy(a => a)
            .ToArray();

        var lupa = CalcularLupa(totaisPor100g, input.LimitesLupa, input.Receita.EstadoFisico);

        var hash = CalcularHash(input);

        return new ResultadoNutricional(
            AlgorithmVersion: input.AlgorithmVersion,
            CalculadoEm: DateTime.UtcNow,
            Tabela: new TabelaNutricional(PorPorcao: totaisPorcao, Por100g: totaisPor100g),
            AlergenosConsolidados: alergenos,
            LupaFrontal: lupa,
            ClaimsEmRisco: Array.Empty<ClaimEmRisco>(), // populado por ConformidadeValidator
            ReceitaVersaoHash: hash);
    }

    private static ValoresPorReferencia CalcularTotaisPor100g(Receita receita)
    {
        var entradaBruta = receita.Insumos.Sum(i => i.QuantidadeReceitaGramas);
        if (entradaBruta <= 0)
            throw new InvalidOperationException("Entrada bruta da receita zero ou negativa.");

        var fatorPor100g = 100m / entradaBruta;
        decimal Acumular(Func<InsumoComFichaCongelada, decimal> selector) =>
            Math.Round(
                receita.Insumos.Sum(i =>
                    selector(i) * i.QuantidadeReceitaGramas / 100m) * fatorPor100g,
                2,
                MidpointRounding.AwayFromZero);

        return new ValoresPorReferencia(
            ValorEnergeticoKcal:    Acumular(i => i.Ficha.ValorEnergeticoKcalPor100g),
            ValorEnergeticoKj:      Acumular(i => i.Ficha.ValorEnergeticoKjPor100g),
            CarboidratosG:          Acumular(i => i.Ficha.CarboidratosGPor100g),
            AcucaresTotaisG:        Acumular(i => i.Ficha.AcucaresTotaisGPor100g),
            AcucaresAdicionadosG:   Acumular(i => i.Ficha.AcucaresAdicionadosGPor100g),
            ProteinasG:             Acumular(i => i.Ficha.ProteinasGPor100g),
            GordurasTotaisG:        Acumular(i => i.Ficha.GordurasTotaisGPor100g),
            GordurasSaturadasG:     Acumular(i => i.Ficha.GordurasSaturadasGPor100g),
            GordurasTransG:         Acumular(i => i.Ficha.GordurasTransGPor100g),
            FibrasG:                Acumular(i => i.Ficha.FibrasGPor100g),
            SodioMg:                Acumular(i => i.Ficha.SodioMgPor100g));
    }

    private static ValoresPorReferencia CalcularTotaisPorPorcao(
        ValoresPorReferencia por100g, Porcao porcao)
    {
        var fator = porcao.QuantidadeGramas / 100m;
        decimal Arredonda(decimal v) => Math.Round(v * fator, 2, MidpointRounding.AwayFromZero);
        return new ValoresPorReferencia(
            ValorEnergeticoKcal:    Arredonda(por100g.ValorEnergeticoKcal),
            ValorEnergeticoKj:      Arredonda(por100g.ValorEnergeticoKj),
            CarboidratosG:          Arredonda(por100g.CarboidratosG),
            AcucaresTotaisG:        Arredonda(por100g.AcucaresTotaisG),
            AcucaresAdicionadosG:   Arredonda(por100g.AcucaresAdicionadosG),
            ProteinasG:             Arredonda(por100g.ProteinasG),
            GordurasTotaisG:        Arredonda(por100g.GordurasTotaisG),
            GordurasSaturadasG:     Arredonda(por100g.GordurasSaturadasG),
            GordurasTransG:         Arredonda(por100g.GordurasTransG),
            FibrasG:                Arredonda(por100g.FibrasG),
            SodioMg:                Arredonda(por100g.SodioMg));
    }

    private static LupaAltoTeorFlags CalcularLupa(
        ValoresPorReferencia por100g, LimitesLupa limites, EstadoFisicoProduto estado)
    {
        var sodioLimite = estado == EstadoFisicoProduto.Solido
            ? limites.SodioMgPor100gSolido
            : limites.SodioMgPor100mlLiquido;
        var satLimite = estado == EstadoFisicoProduto.Solido
            ? limites.GorduraSaturadaGPor100gSolido
            : limites.GorduraSaturadaGPor100mlLiquido;
        var acucarLimite = estado == EstadoFisicoProduto.Solido
            ? limites.AcucarAdicionadoGPor100gSolido
            : limites.AcucarAdicionadoGPor100mlLiquido;

        return new LupaAltoTeorFlags(
            AcucarAdicionadoAlto: por100g.AcucaresAdicionadosG >= acucarLimite,
            GorduraSaturadaAlto:  por100g.GordurasSaturadasG  >= satLimite,
            SodioAlto:            por100g.SodioMg             >= sodioLimite,
            LimitesAplicadosVersaoIN75: limites.VersaoIN75);
    }

    /// <summary>Hash determinístico — usado para invalidação de cache + idempotência de API.</summary>
    private static string CalcularHash(CalculoNutricionalInput input)
    {
        var canonical = new
        {
            insumos = input.Receita.Insumos
                .Select(i => new
                {
                    p = i.ProdutoId,
                    q = i.QuantidadeReceitaGramas,
                    v = i.PerfilNutricionalVersaoId
                })
                .OrderBy(x => x.p)
                .ToArray(),
            rg = input.Receita.Rendimento.SaidaLiquidaGramas,
            fr = input.Receita.Rendimento.FatorRendimento,
            pg = input.Receita.Porcao.QuantidadeGramas,
            np = input.Receita.Porcao.NumeroPorcoesPorEmbalagem,
            ef = input.Receita.EstadoFisico.ToString(),
            tc = input.CodigoModeloRotulo,
            av = input.AlgorithmVersion,
            ll = input.LimitesLupa.VersaoIN75
        };

        var json = JsonSerializer.Serialize(canonical, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = false
        });

        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
```

### 5.3 Versionamento (4 dimensões)

| Versionamento | Muda quando | Quem dispara | Invalida cache? | Entra no snapshot? |
|---|---|---|---|---|
| `AlgorithmVersion` (semver) | Código do motor muda | Deploy de release | Sim (todos os caches) | Sim |
| `PerfilNutricional.VersaoId` | Ficha de insumo alterada | `SalvarPerfilNutricionalUseCase` | Sim (produtos que usam o insumo) | Sim (ficha congelada) |
| `ReceitaVersaoHash` (sha256) | Composição/quantidades mudam | `AplicarNutricaoAoProdutoUseCase` | Sim (produto específico) | Sim |
| `ModeloRotulo.VersaoHash` | Layout do template muda | Admin edita `ModeloRotulo` | Não (apenas re-render) | Sim (template congelado) |

### 5.4 Snapshot do Rotulo — Contrato C# + Schema

```csharp
// EasyStock.Application/Rotulagem/Models/RotuloDadosSnapshot.cs
namespace EasyStock.Application.Rotulagem.Models;

public sealed record RotuloDadosSnapshot(
    int SchemaVersion,                            // = 1 no MVP
    string AlgorithmVersion,
    DateTime GeradoEm,
    ReceitaSnapshot Receita,
    LupaAltoTeorFlags LupaCalculada,
    IReadOnlyList<string> AlergenosConsolidados,
    IReadOnlyList<ClaimSnapshot> ClaimsValidados,
    TemplateSnapshot TemplateUsado,
    IReadOnlyList<NormaAplicada> NormasAplicadas,
    EmpresaSnapshot EmpresaSnapshot,
    ProdutoSnapshot ProdutoSnapshot,
    AprovacaoRtSnapshot? AprovacaoRt);            // null se não aplicável

public sealed record ReceitaSnapshot(
    RendimentoSnapshot Rendimento,
    PorcaoSnapshot Porcao,
    IReadOnlyList<InsumoSnapshot> Insumos,
    ValoresPorReferencia TotaisCalculadosPorPorcao,
    ValoresPorReferencia TotaisCalculadosPor100g);

public sealed record RendimentoSnapshot(decimal SaidaLiquidaGramas, decimal FatorRendimento);

public sealed record PorcaoSnapshot(decimal QuantidadeGramas, int NumeroPorcoes);

public sealed record InsumoSnapshot(
    string ProdutoNome,
    string? FornecedorNome,
    Guid PerfilNutricionalVersaoId,
    FichaCongelada FichaCongelada,
    decimal QuantidadeReceitaGramas);

public sealed record ClaimSnapshot(string Claim, string Status, string Motivo);

public sealed record TemplateSnapshot(string Codigo, string VersaoHash);

public sealed record NormaAplicada(string Codigo, int Versao);

public sealed record EmpresaSnapshot(string RazaoSocial, string Cnpj, string Endereco, string Sac);

public sealed record ProdutoSnapshot(
    string NomeComercial,
    string DenominacaoVenda,
    string LoteCodigo,
    DateOnly DataFabricacao,
    DateOnly Validade);

public sealed record AprovacaoRtSnapshot(
    Guid RtId,
    string Nome,
    string Crn,
    string ComprovanteAprovacaoInternaHash,       // P1 renomeado
    int SecretVersao,                             // P4 referência ao secret usado
    DateTime DataAprovacao);
```

**Validação de schema (CI)**:
```csharp
// EasyStock.Application.Tests/Rotulagem/SnapshotSchemaTests.cs
[Trait("Category","Architecture")]
public class SnapshotSchemaTests
{
    [Fact]
    public void RotuloDadosSnapshot_serializa_para_chaves_esperadas()
    {
        var sample = SnapshotFixtures.SampleRavioliRicota();
        var json = JsonSerializer.Serialize(sample, opts);
        var doc = JsonDocument.Parse(json);

        // Chaves obrigatórias no contrato — qualquer rename ou remoção quebra CI.
        var chaves = new[] { "schemaVersion","algorithmVersion","geradoEm","receita",
                             "lupaCalculada","alergenosConsolidados","claimsValidados",
                             "templateUsado","normasAplicadas","empresaSnapshot",
                             "produtoSnapshot","aprovacaoRt" };
        foreach (var k in chaves)
            doc.RootElement.TryGetProperty(k, out _).Should().BeTrue($"chave '{k}' obrigatória");
    }
}
```

### 5.5 Migração entre schemaVersion (pseudocódigo)

```csharp
// EasyStock.Infra.Async/Jobs/MigradorSnapshotRotulo.cs
public sealed class MigradorSnapshotRotulo : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Critério: roda quando AlgorithmVersion ou SchemaVersion muda
            var rotulos = await _db.Rotulos
                .Where(r => r.DadosSnapshotJsonV2 == null
                         && r.DadosSnapshotJson["schemaVersion"].GetInt32() < SchemaAtual)
                .Take(100)
                .ToListAsync(ct);

            foreach (var r in rotulos)
            {
                var v1 = JsonSerializer.Deserialize<RotuloDadosSnapshotV1>(r.DadosSnapshotJson);
                var v2 = MigrarV1ParaV2(v1);
                r.DadosSnapshotJsonV2 = JsonDocument.Parse(JsonSerializer.Serialize(v2));
                // dados_snapshot_json original NÃO é alterado (imutável)
            }
            await _db.SaveChangesAsync(ct);

            await Task.Delay(TimeSpan.FromHours(1), ct);
        }
    }
}
```

**Retenção** (ADR-0012): 5 anos mínimo conforme RDC 275/2002. Após 5 anos, snapshot migra para "arquivo frio" — bucket R2 separado `easystok-rotulos-arquivo-historico` com lifecycle policy mantendo indefinidamente.

---

## 6. API Pública (`/api/v1/nutricao/*`)

### 6.1 OpenAPI 3.0.3 (YAML)

```yaml
openapi: 3.0.3
info:
  title: EasyStok Nutrition API
  version: 1.0.0
  description: |
    Cálculo nutricional e leitura de cache oficial. Função pura: mesmo input → mesmo output.
    Idempotência via header Idempotency-Key (Stripe-style).
  contact: { name: "Felipe / EasyStok", url: "https://easystok.com.br" }

servers:
  - url: https://app.easystok.com.br/api/v1
    description: Produção (Fly Brazil South)

security:
  - bearerAuth: []

paths:
  /nutricao/calcular:
    post:
      summary: Calcular nutrição a partir de receita (sem persistir)
      operationId: calcularNutricao
      parameters:
        - in: header
          name: Idempotency-Key
          schema: { type: string, format: uuid }
          required: false
          description: Stripe-style. Mesma chave + mesmo body retorna cache 24h.
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/CalcularRequest' }
      responses:
        '200':
          description: Sucesso (pode vir de cache de idempotência)
          headers:
            Idempotent-Replayed:
              schema: { type: boolean }
              description: true se foi servido de cache
          content:
            application/json:
              schema: { $ref: '#/components/schemas/ResultadoNutricional' }
        '400':
          description: Erro de validação
          content:
            application/problem+json:
              schema: { $ref: '#/components/schemas/ProblemDetails' }
        '409':
          description: Idempotency-Key reusada com body diferente
          content:
            application/problem+json:
              schema: { $ref: '#/components/schemas/ProblemDetails' }
        '429':
          description: Rate limit excedido
          headers:
            Retry-After:
              schema: { type: integer, description: "Segundos até liberar" }
          content:
            application/problem+json:
              schema: { $ref: '#/components/schemas/ProblemDetails' }

  /produtos/{produtoId}/nutricao:
    get:
      summary: Obter cache oficial de nutrição calculada de um produto
      operationId: getNutricaoProduto
      parameters:
        - in: path
          name: produtoId
          required: true
          schema: { type: string, format: uuid }
      responses:
        '200':
          description: Cache atual
          content:
            application/json:
              schema: { $ref: '#/components/schemas/ResultadoNutricional' }
        '404':
          description: Produto sem cache (ainda não publicado)
          content:
            application/problem+json:
              schema: { $ref: '#/components/schemas/ProblemDetails' }

components:
  securitySchemes:
    bearerAuth:
      type: http
      scheme: bearer
      bearerFormat: JWT

  schemas:
    CalcularRequest:
      type: object
      required: [receita, codigoModeloRotulo, limitesLupa]
      properties:
        receita:
          $ref: '#/components/schemas/Receita'
        codigoModeloRotulo:
          type: string
          pattern: '^[A-Z0-9_]+$'
          example: BR_RDC429_TABULAR
        limitesLupa:
          type: string
          enum: [IN75_fase1, IN75_fase2]

    Receita:
      type: object
      required: [rendimento, porcao, insumos, estadoFisico]
      properties:
        rendimento:
          type: object
          required: [saidaLiquidaGramas, fatorRendimento]
          properties:
            saidaLiquidaGramas: { type: number, format: decimal, minimum: 0.01 }
            fatorRendimento: { type: number, format: decimal, minimum: 0.01, maximum: 2.0 }
        porcao:
          type: object
          required: [quantidadeGramas, numeroPorcoesPorEmbalagem]
          properties:
            quantidadeGramas: { type: number, minimum: 0.01 }
            numeroPorcoesPorEmbalagem: { type: integer, minimum: 1 }
        insumos:
          type: array
          minItems: 1
          items:
            type: object
            required: [produtoId, quantidadeReceitaGramas]
            properties:
              produtoId: { type: string, format: uuid }
              quantidadeReceitaGramas: { type: number, minimum: 0.01 }
        estadoFisico:
          type: string
          enum: [Solido, Liquido]

    ResultadoNutricional:
      type: object
      required: [algorithmVersion, calculadoEm, tabela, alergenosConsolidados,
                 lupaFrontal, claimsEmRisco, receitaVersaoHash]
      properties:
        algorithmVersion: { type: string, example: "1.0.0" }
        calculadoEm: { type: string, format: date-time }
        receitaVersaoHash: { type: string, pattern: '^[a-f0-9]{64}$' }
        tabela:
          type: object
          properties:
            porPorcao: { $ref: '#/components/schemas/ValoresPorReferencia' }
            por100g: { $ref: '#/components/schemas/ValoresPorReferencia' }
        alergenosConsolidados:
          type: array
          items: { type: string }
        lupaFrontal:
          type: object
          properties:
            acucarAdicionadoAlto: { type: boolean }
            gorduraSaturadaAlto: { type: boolean }
            sodioAlto: { type: boolean }
            limitesAplicadosVersaoIN75: { type: string }
        claimsEmRisco:
          type: array
          items:
            type: object
            properties:
              claimCodigo: { type: string }
              motivo: { type: string }
              referenciaRdc: { type: string }

    ValoresPorReferencia:
      type: object
      properties:
        valorEnergeticoKcal: { type: number }
        valorEnergeticoKj: { type: number }
        carboidratosG: { type: number }
        acucaresTotaisG: { type: number }
        acucaresAdicionadosG: { type: number }
        proteinasG: { type: number }
        gordurasTotaisG: { type: number }
        gordurasSaturadasG: { type: number }
        gordurasTransG: { type: number }
        fibrasG: { type: number }
        sodioMg: { type: number }

    ProblemDetails:                # RFC 7807
      type: object
      properties:
        type:   { type: string, format: uri }
        title:  { type: string }
        status: { type: integer }
        detail: { type: string }
        instance: { type: string, format: uri }
        errors:
          type: object
          additionalProperties:
            type: array
            items: { type: string }
```

### 6.2 Política de versionamento

- **Path-based**: `/api/v1/*`. Próxima major: `/api/v2/*`.
- **Bumpa para v2 quando**:
  - Quebra contrato de input (campo obrigatório novo, remoção de campo, mudança de tipo)
  - Quebra contrato de output (rename de campo, remoção)
  - Quebra semântica (cálculo retorna valor diferente para mesmo input)
- **Não bumpa** (adiciona campo opcional, novo enum value, endpoint novo).
- **Deprecação**: header `Deprecation: true` + `Sunset: <date RFC 7231>` por 6 meses antes de remover `/v1/`. Anúncio no portal Anvisa parceiros (futuro).

### 6.3 Rate limit (3 níveis)

```yaml
# EasyStock.Api/Configurations/IpRateLimiting.yaml (AspNetCoreRateLimit)
IpRateLimiting:
  EnableEndpointRateLimiting: true
  StackBlockedRequests: false
  HttpStatusCode: 429
  GeneralRules:
    # Anônimo (sem auth) — só endpoints públicos (QR público em F+1)
    - Endpoint: "GET:/r/*"
      Period: "1m"
      Limit: 60
    - Endpoint: "GET:/r/*"
      Period: "1h"
      Limit: 600

    # Autenticado (Bearer) — clientes EasyStok
    - Endpoint: "POST:/api/v1/nutricao/calcular"
      Period: "1m"
      Limit: 60                # 1/s — humano normal
    - Endpoint: "POST:/api/v1/nutricao/calcular"
      Period: "1h"
      Limit: 1000              # bloqueia scrape sustentado
    - Endpoint: "GET:/api/v1/produtos/*/nutricao"
      Period: "1m"
      Limit: 300               # leitura é barata

  # API key paga futura (F+2) — header X-Api-Key tier paid
  ClientRateLimiting:
    EnableEndpointRateLimiting: true
    ClientRules:
      - ClientId: "tier-paid"
        Endpoint: "POST:/api/v1/nutricao/calcular"
        Period: "1m"
        Limit: 600             # 10/s — integradores B2B
```

### 6.4 Idempotência

Cliente envia `Idempotency-Key: <uuid v4>` em POST. Server:
1. Calcula hash(body).
2. Lookup em cache (Redis se disponível, OU tabela `idempotency_keys` no Postgres com TTL 24h).
3. Se hit + body hash bate → retorna cache + header `Idempotent-Replayed: true`.
4. Se hit + body hash diferente → `409 Conflict`.
5. Se miss → processa request normalmente + escreve cache.

**No MVP**: tabela `idempotency_keys(chave uuid PK, body_hash text, response_json jsonb, criado_em timestamptz, expira_em timestamptz)`. Job de limpeza diário. Não exige Redis.

---

## 7. Storage e Backup

### 7.1 Estrutura de paths no Fly volume

Volume montado em `/data` no app Fly. Subpasta dedicada `rotulos/` com convenção:

```
/data/
└── rotulos/
    └── {empresaId-uuidv7}/
        ├── {rotuloId-uuidv7}.pdf            # PDF gerado por QuestPDF (assinado pelo modelo)
        ├── {rotuloId-uuidv7}.preview.png    # preview 600x900 para UI (opcional, gerado on-demand)
        └── {rotuloId-uuidv7}.snapshot.json  # cópia espelhada de Rotulo.dados_snapshot_json
                                              # (para forense quando DB indisponível)
```

**Permissões** (no Dockerfile + entrypoint do app):
```dockerfile
RUN mkdir -p /data/rotulos && chown -R app:app /data/rotulos && chmod 750 /data/rotulos
```

### 7.2 Política de versionamento do volume

Fly volumes **não têm snapshot nativo automático** confiável (snapshots cobrem somente disaster recovery do disco, não retenção de versões de arquivos). Para o caso de auditoria de rótulos imutáveis isso é OK porque:
- Arquivos nunca são sobrescritos (path inclui `rotuloId` único, e há trigger SQL que bloqueia UPDATE em `Rotulo` publicado).
- Backup diário para R2 cobre disaster recovery + retenção.

Para **integridade dos arquivos**:
- Cada upload computa SHA-256 → grava em `Rotulo.dados_snapshot_json.pdfHash`.
- Job mensal `VerificacaoIntegridadeRotulosJob` re-computa hash de uma amostra (10% dos rótulos) e alerta se divergir.

### 7.3 Migração Fly volume → R2 quando passar 50GB

**Critério de gatilho**: `fly volumes show <id> --json` retorna `size_gb_used >= 50`. Job semanal `MonitorarTamanhoVolumeJob` envia alerta via Resend ao admin.

**Procedimento de cutover** (deploy planejado, ~2h downtime do upload novo de rótulos; leitura mantém):
1. Provisionar bucket R2 `easystok-rotulos-storage` (separado de `easystok-rotulos-backup`).
2. Sync inicial via `rclone copy --transfers 8 fly:/data/rotulos r2:easystok-rotulos-storage`.
3. Feature flag `STORAGE_BACKEND=r2` no Fly (env var).
4. Deploy de versão que lê e escreve de R2 (interface `IRotuloBlobStorage` tem 2 impls).
5. Sync delta final (`rclone sync`).
6. Após 7 dias estável, dropar dados do Fly volume (depois de backup final).

**Sem downtime de leitura**: novo deploy lê de R2 + fallback para Fly volume durante 7 dias.

### 7.4 Backup diário + restore-test mensal

ADR-0012 + workflows GHA já criados em F0.5 (`backup-rotulos.yml` + `restore-test-rotulos.yml`).

- **Backup**: cron `0 6 * * *` UTC (= 03:00 BRT). `pg_dump` custom format compressed → R2.
- **Restore-test**: cron `0 7 1 * *` UTC (mensal dia 1 04:00 BRT). Restora em DB efêmero, valida 1 PDF parseável + 1 snapshot JSON.
- **Retenção**: 5 anos no bucket `easystok-rotulos-backup` (lifecycle policy R2). Após 5 anos → bucket `easystok-rotulos-arquivo-historico` indefinido.

### 7.5 LGPD — Direito ao Esquecimento

**Quando ativado**: fornecedor solicita formalmente exclusão (`PrivacidadeFornecedor.nivel_privacidade = 'LegalLgpdPleno'`).

**Pipeline de execução** (UseCase `ExecutarDireitoEsquecimentoFornecedorUseCase`):
1. Atualiza `PrivacidadeFornecedor.nivel_privacidade = 'LegalLgpdPleno'`.
2. Job background `RegenerarSnapshotsRotulosJob`:
   - Para cada `Rotulo` onde snapshot menciona o fornecedor:
     - Cria nova versão do JSON com `fornecedorNome = "Fornecedor regional"` no campo correspondente.
     - **NÃO sobrescreve** o snapshot original (imutabilidade auditoria). Adiciona `dados_snapshot_json_v2` com versão anonimizada.
     - Re-gera PDF + PNG aplicando opt-out.
     - Move PDF original para path `/data/rotulos/{empresaId}/anonimizados/{rotuloId}.original.pdf` (acesso restrito a admin EasyStok, **necessário** para auditoria Anvisa em processo legal).
3. Sync para R2: arquivo original vai para bucket separado `easystok-rotulos-anonimizados-originais` com acesso restrito.
4. Log de auditoria: `AuditoriaLgpdExclusaoFornecedor(fornecedor_id, motivo, executado_em, executado_por, contagem_rotulos_afetados)`.

**Termo de uso da empresa** (parte da onboarding) explicita:
> "Anonimização LGPD pode ser revertida em caso de processo legal Anvisa que exija nome original do fornecedor. Snapshots originais são preservados em armazenamento restrito por 5 anos."

### 7.6 Storage de PDFs de simulação (marca-d'água)

Path: `/data/rotulos/{empresaId}/simulacoes/{usuarioId}-{timestamp}.pdf`. TTL 7 dias (job `LimpezaSimulacoesPdfJob` cron diário). Não vai para backup R2 (não tem valor de auditoria).

---

## 8. UX

### 8.1 Componente `_PainelTabelaNutricional.cshtml` — 4 modos

Razor partial reusado em 4 superfícies (aba Produto, Calculadora Etapa 2, PWA Pedidos, App mobile). Parâmetros: `Modo`, `Dados`, `EmpresaConfig`.

#### Modo Preview

```
Desktop layout (>=1024px):
┌─────────────────────────────────────────────────────────────┐
│ Banner amarelo (#fff3cd, texto #333): "Você está            │
│ simulando. Mudanças aqui não afetam rótulos publicados."    │
├─────────────────────────────────────────────────────────────┤
│ [Header]                                                    │
│ Tabela Nutricional · Por porção (50g) | Por 100g [toggle]   │
├─────────────────────────────────────────────────────────────┤
│ [Corpo — tabela RDC 429]                                    │
│ Valor energético           220 kcal | 920 kJ                │
│ Carboidratos               38 g                             │
│ ...                                                         │
│ Alérgenos: TRIGO, LEITE                                     │
│ Lupa frontal: nenhuma | [Alto em sódio]                     │
├─────────────────────────────────────────────────────────────┤
│ [Footer — ações]                                            │
│ [Confirmar essa receita]  [Exportar PDF da simulação]       │
│ [Limpar simulação]                                          │
└─────────────────────────────────────────────────────────────┘

Mobile (<768px):
┌──────────────────────────────────┐
│ ⌘ PREVIEW           [sticky pill]│  ← fundo amarelo + texto preto, sticky no scroll do bottom-sheet
├──────────────────────────────────┤
│ (mesma tabela)                   │
├──────────────────────────────────┤
│ [Confirmar essa receita] (full)  │
│ [Exportar PDF] [Limpar]          │
└──────────────────────────────────┘
```

**Acessibilidade**:
- `role="region"` + `aria-label="Tabela nutricional em modo de simulação"`
- `aria-live="polite"` na região da tabela (anuncia recálculo: "Cálculo atualizado: 220 kcal, 12 g de açúcar")
- Foco inicial: primeiro campo editável da receita acima (`<input data-testid="primeiro-insumo-quantidade">`)
- Tab order: insumos → quantidades → rendimento → porção → resultado → [Confirmar essa receita]
- Pílula "PREVIEW" tem `aria-label="Modo de simulação ativo"`

#### Modo Live

```
┌─────────────────────────────────────────────────────────────┐
│ Badge verde (#d1fae5, texto #065f46):                       │
│ ✓ Oficial · publicado por Thatiane em 16/05 14:32           │
├─────────────────────────────────────────────────────────────┤
│ (mesma tabela RDC 429, fundo branco #ffffff)                │
├─────────────────────────────────────────────────────────────┤
│ [Reimprimir rótulo] [Ver histórico de versões] [Reformular] │
└─────────────────────────────────────────────────────────────┘
```

**Mobile**: badge verde no header não-scrollável.
**Acessibilidade**: `aria-label="Tabela nutricional oficial publicada"`.

#### Modo Diff (F+1)

```
┌──────────────┬──────────────┐
│ Atual (v3)   │ Simulado     │
├──────────────┼──────────────┤
│ 220 kcal     │ 195 kcal ↓  │  (mostra delta + magnitude; sem cor por padrão)
│ 12 g açúcar  │ 8 g ↓        │
│ 720 mg sódio │ 540 mg ↓ ⚠   │  (vermelho só na lupa frontal pelo limite cruzado)
└──────────────┴──────────────┘
```

#### Modo AguardandoAprovacaoRt

```
┌─────────────────────────────────────────────────────────────┐
│ Banner laranja (#fed7aa, texto #7c2d12):                    │
│ ⏳ Aguarda aprovação de Dra. Maria Silva (CRN-3 12345)       │
├─────────────────────────────────────────────────────────────┤
│ (tabela read-only, fundo branco)                            │
├─────────────────────────────────────────────────────────────┤
│ Usuário comum: [Confirmar essa receita] DISABLED            │
│   Tooltip: "Apenas Dra. Maria Silva pode aprovar"           │
│ Usuário RT:   [Aprovar como responsável técnico]            │
└─────────────────────────────────────────────────────────────┘
```

### 8.2 Microcopy (filtro Thatiane: PT-BR, sem jargão, ação clara, mobile-safe, aria-label fallback)

| Contexto | Texto (PT-BR exato) | Mobile char count | `aria-label` se diferente |
|---|---|---|---|
| Botão Confirmar | "Confirmar receita" | 17 ✓ | mesmo |
| Botão Descarte | "Limpar simulação" | 17 ✓ | mesmo |
| Banner Preview | "Você está simulando. Mudanças aqui não afetam rótulos publicados." | quebra em 2 linhas em 380px ✓ | "Modo simulação ativo. Nenhuma alteração será salva." |
| Pílula mobile Preview | "SIMULANDO" | 9 ✓ | "Tabela em modo de simulação" |
| Badge Live | "Oficial · publicado por {user} em {data}" | quebra em 2 linhas se nome longo ✓ | "Tabela oficial publicada em {data}" |
| Banner AguardandoAprovacaoRt | "Aguarda aprovação de {RT.Nome} ({CRN})" | quebra em 2 linhas ✓ | "Aguardando aprovação técnica" |
| Modal confirmação Aplicar | "Confirmar como receita oficial? A partir de agora, novos lotes deste produto usam esta receita. Rótulos já impressos continuam válidos — não há recall." | 4 linhas mobile ✓ | mesmo |
| Banner offline | "Sem conexão · não é possível salvar" | 1 linha ✓ | mesmo |
| Conflito multi-device | "Outra versão deste rascunho foi salva em {device} ({data}). O que fazer?" | 3 linhas mobile ✓ | "Conflito de versão detectado" |
| Claim bloqueado | "Não dá para imprimir como está. Claim 'Zero açúcar' exige até 0,5g/100g; este produto tem 1,2g/100g (RDC 54/2012)." | 4-5 linhas mobile ✓ | "Impressão bloqueada por conformidade" |
| Lupa frontal | "Alto em sódio · IN 75 fase 1" | 1 linha ✓ | "Alto teor de sódio conforme Anvisa IN 75/2020" |
| Empty receita | "Configure a receita para gerar o rótulo automaticamente." | 2 linhas ✓ | mesmo |
| Empty insumo sem ficha | "{n} ingredientes sem ficha nutricional: {nomes}. O cálculo ficará incompleto." | 3 linhas ✓ | mesmo |
| Email arquivamento | "Seus rascunhos de receita prestes a serem arquivados" | título | — |
| Notificação restore-test falhou | "CRÍTICO: restore test do backup falhou" | título | — |

### 8.3 Estados de UI (empty / loading / error / success)

| Estado | Texto | Cor (hex) | Ícone Lucide | Ação primária | Mobile <380px |
|---|---|---|---|---|---|
| **Empty — produto sem receita** | "Configure a receita para gerar o rótulo automaticamente." | bg `#ffffff`, texto `#1f2937` | `FileQuestion` | "Abrir Calculadora de Produção" → `/calculadora/{produtoId}` | botão full-width |
| **Empty — sem rascunhos** | "Nenhuma simulação salva." | bg `#ffffff`, texto `#6b7280` | `Inbox` | "Edite a receita acima para criar." (sem botão) | empilhado |
| **Empty — nenhum rótulo no lote** | "Nenhum rótulo publicado neste lote." | bg `#ffffff` | `Tag` | "Confirmar receita e imprimir" → scroll para painel acima | full-width |
| **Loading — calculando** | "Calculando…" | overlay `rgba(0,0,0,0.04)` | `Loader2` (spin) | — | skeleton da tabela |
| **Loading — gerando PDF** | "Gerando PDF do rótulo… (até 10s)" | bg `#fef3c7`, texto `#78350f` | `Loader2` | — | full-width |
| **Error — insumo sem ficha** | "{n} insumos sem ficha nutricional: {nomes}." | bg `#fef3c7` (amarelo), texto `#78350f` | `AlertTriangle` | "Auto-completar com TACO" → modal busca | empilhado, botão full |
| **Error — claim bloqueado** | "Não dá para imprimir como está. {detalhe RDC}." | bg `#fee2e2` (vermelho claro), texto `#7f1d1d` | `Ban` | 3 botões: "Remover claim" / "Ajustar receita" / "Imprimir sem claim" | empilhado vertical |
| **Error — API offline** | "Sem conexão · não é possível salvar." | bg `#fee2e2` | `WifiOff` | "Tentar novamente" → retry handler | sticky top |
| **Success — receita confirmada** | "Receita confirmada. Próxima impressão usa esta versão." | toast verde `#d1fae5` | `Check` | "Ver rótulo gerado" → `/lotes/{loteId}#rotulo` | toast bottom |
| **Success — rótulo impresso** | "{n} etiquetas enviadas para a impressora {nome}." | toast verde | `Printer` | "Ver fila de impressão" | toast bottom |

### 8.4 Jornadas por persona

#### Thatiane (operadora — produz alimento, sem treino técnico)

**Primeiro acesso à aba Nutricional**:
1. Abre Produto "Ravioli de Ricota" → aba Nutricional aparece pela primeira vez (após Felipe ativar a flag).
2. Estado vazio: "Configure a receita para gerar o rótulo automaticamente." + botão "Abrir Calculadora de Produção".
3. Pula para Calculadora (caminho Auto) **OU** clica em "Cadastrar manualmente" (caminho B/Manual) → formulário com 10 nutrientes.
4. Salva → vê banner verde "Oficial · publicado por Thatiane em 16/05 14:32".

**Cadastrar primeiro insumo (Farinha Anaconda)**:
1. Vai em Produtos → Filtro categoria "Ingredientes" → clica "Novo".
2. Preenche nome, marca, fornecedor "Anaconda".
3. Aba Nutricional aparece automaticamente (sub-tipo `Ingrediente Alimentício`).
4. Botão "Buscar na base TACO" → digita "farinha trigo" → 5 sugestões → clica "Trigo, farinha, branca" → preenche todos os 10 nutrientes + grava `Origem=TACO, OrigemReferencia="TACO 4ª ed #042"`.
5. Salva. Chip "TACO" aparece ao lado dos nutrientes.

**Gerar primeiro rótulo do dia (lote LBT-0142)**:
1. Cria lote LBT-0142 (fluxo existente — Lotes > Novo).
2. Após produzir e salvar, abre detalhe do lote → aba "Rótulo" (nova).
3. Vê preview do rótulo com tabela calculada, lupa de sódio (se aplicável).
4. Botão "Imprimir etiquetas" → modal pergunta quantas + qual impressora (Zebra padrão da loja).
5. Imprime. Toast verde "20 etiquetas enviadas para Zebra ZD230".

**Reformular um produto que ganhou lupa de sódio**:
1. No detalhe do produto "Ravioli", vê badge vermelho "Alto em sódio · IN 75 fase 1".
2. Tooltip da badge: "Sódio do produto: 720mg/100g. Limite: 600mg/100g. Reformular?"
3. Clica → abre aba Nutricional em modo Preview → reduz quantidade de "Sal refinado" de 10g para 6g → painel direito recalcula → badge da lupa desaparece (sódio = 540mg/100g).
4. Clica "Confirmar receita" → modal confirma "novos lotes usam esta receita; rótulos antigos imutáveis" → confirma.

#### Felipe (admin — dono da loja, sênior técnico)

**Primeiro acesso à aba Nutricional**:
1. Felipe NÃO usa aba Nutricional em produção dia-a-dia (Thatiane faz).
2. Usa em **Configurações > Rotulagem** para: ativar feature flag, cadastrar RT, configurar lupa fase 2 (futuro), opt-out fornecedor (LGPD).

**Cadastrar primeiro insumo**: idem Thatiane (mas raramente faz).

**Gerar primeiro rótulo do dia**: idem Thatiane (delegado).

**Reformular**: idem Thatiane (delegado).

**O que Felipe vê adicional**:
- Menu admin "Rotulagem > Configuração": flag, RT, lupa fase 2, opt-out fornecedor.
- Menu admin "Rotulagem > Estatísticas": cobertura nutricional %, % produtos com lupa, latência calculator p95, taxa de claims bloqueados.
- Menu admin "Sistema > Logs": acesso a `RastreabilidadeEvento` raw.

### 8.5 Página pública QR (F+1 — wireframe reservado no MVP)

Roteamento `/r/{publicSlug}` (slug UUIDv4). Renderização Razor minimalista (sem layout do app, sem barra de navegação).

**Wireframe textual (mobile-first 380px)**:
```
═══════════════════════════════════════
[Header centralizado]
  [Logo loja 40px]
  Ravioli de Ricota                       (h1, 24px)
  [Foto opcional 200x200 rounded]

[Bloco 1 — Alérgenos + Lupa, fold 1]
  [SVG AlertTriangle] Contém: TRIGO, LEITE
  [SVG AlertTriangle] Pode conter traços de: castanhas
  [SVG Anvisa-Lupa-Sodio] ALTO EM SÓDIO
  "Conforme RDC 26/2015 e IN 75/2020 (Anvisa)" [link]

[Bloco 2 — Tabela nutricional]
  [Toggle] Por porção (50g) | Por 100g
  ┌────────────────────────────────────┐
  │ Valor energético  220 kcal | 920 kJ│
  │ Carboidratos      38 g             │
  │ ...                                │
  └────────────────────────────────────┘

[Bloco 3 — Rastreabilidade (accordion fechado)]
  ▶ Ver origem dos ingredientes
  (ao expandir: lista de fornecedores respeitando opt-out — Q14)

[Bloco 4 — Compartilhamento]
  [Botão] Copiar link    (toast "Link copiado!")
  [Botão] Salvar como PDF    (server-side GET /r/{slug}.pdf)
  "Compartilhe com seu nutricionista"

[Footer]
  "Rótulo verificado conforme RDC 429/2020 (Anvisa)"
  Lote: LBT-0142 · Validade: 22/05/2026 · Fabricado em: 15/05/2026
  [Link] Reportar problema neste rótulo
  [Link] Termos de uso · Política de privacidade
═══════════════════════════════════════
```

**Performance — alvo Core Web Vitals** (mobile mid-tier 4G):
- LCP (Largest Contentful Paint): < 2.0s — bloco 1 deve renderizar primeiro
- INP (Interaction to Next Paint): < 200ms — toggle por porção/100g é único interactivo
- CLS (Cumulative Layout Shift): < 0.1 — reserva espaço para imagem do produto

**Tracking analytics**: **sem tracking de identidade**. Contador anônimo de scans incrementa via `POST /r/{slug}/scanned` (rate-limited 1/min por IP), grava em `ContagemScansRotulo(rotulo_id, contagem int)`. Sem IP, sem cookie, sem fingerprinting. LGPD OK.

**Termos de uso + política de privacidade**: link no footer aponta para `easystok.com.br/termos-rotulagem-publica` (página estática). Conteúdo escrito em F13.

**Offline (PWA)**: **fora do MVP e fora do F+1**. Página requer rede. PWA com cache de snapshot fica para F+2 se houver demanda.

### 8.6 Acessibilidade (WCAG 2.1 AA)

Paleta validada com pares fundo/texto ≥4.5:1:

| Modo | Fundo | Texto | Contraste | Uso |
|---|---|---|---|---|
| Preview | `#fff3cd` | `#333333` | 9.4:1 ✓ | Banner amarelo + pílula |
| Live | `#d1fae5` | `#065f46` | 7.9:1 ✓ | Badge verde |
| AguardandoAprovacaoRt | `#fed7aa` | `#7c2d12` | 8.2:1 ✓ | Banner laranja |
| Claim bloqueado | `#fee2e2` | `#7f1d1d` | 8.7:1 ✓ | Modal bloqueio |
| Lupa frontal | `#000000` | `#ffffff` | 21:1 ✓ AAA | Símbolo IN 75 (exigência da norma) |
| Conflito multi-device | `#dbeafe` | `#1e3a8a` | 9.6:1 ✓ | Banner azul |
| Background neutro | `#fafafa` | `#1f2937` | 14.5:1 ✓ | Painel Preview |

**Comportamentos a11y**:
- `aria-live="polite"` na tabela; anuncia recálculo.
- `aria-live="assertive"` em banner de erro / claim bloqueado.
- `role="alert"` em chip de alérgenos (informação crítica de saúde).
- Tab order: insumos → quantidades → rendimento → porção → resultado → Confirmar.
- Cores nunca são única indicação — ícone SVG + texto + `aria-label`.
- Símbolo lupa Anvisa: SVG inline com `aria-label="Alto teor de sódio (Anvisa IN 75/2020)"`. Asset oficial em `wwwroot/img/anvisa-lupa-{nutriente}.svg` (baixar em F6 do portal Anvisa).
- Foco visível: `outline: 2px solid #2563eb; outline-offset: 2px` em todos os botões/inputs.
- **Dark mode**: fora do MVP. Toggle em F+1 com paleta espelhada validada.

---

## 9. Faseamento

Cada fase tem: dias úteis estimados, critério "pronto" objetivo, bloqueadores, plano alternativo.

**Capacidade real Felipe**: 2.5h/dia × 5 dias úteis = 12.5h/semana **+** 5h sábado + 4h domingo = **~21h/semana líquidas**. Considerando interrupções Avanade e família: efetivo **17–19h/semana**. Estimativas abaixo são em **dias úteis equivalentes a 2.5h cada**.

### F0 — Pré-requisito (CAMINHO B ACEITO, sem ação)
- **Status**: dispensado. Calculadora de Produção (`feat/calculadora-producao`) fica pendente de merge separado. Sistema opera modo Manual desde F1.

### F0.5 — Setup (8 itens — ~1.5 semana, paralelizável)

| Item | Status atual | Dias eq | Bloqueador |
|---|---|---|---|
| ADR-0011 (nomenclatura) | feito (commit 4b018b39) | 0.5 | — |
| ADR-0012 (backup/storage) | feito (commit 4b018b39) | 0.5 | — |
| ADR-0013 (Comprovante Aprovação Interna RT) | **pendente** | 0.5 | — |
| Husky.Net + pre-commit | feito (4b018b39 + e3f70122) | 0.5 | — |
| Workflows GHA drafts | feito (4b018b39) | 0.5 | — |
| Pagar billing GHA + criar secrets (R2, Resend) | **pendente — Felipe manual** | 1 | externo |
| Refator IClaudeStructuredExtractor + 2 bases | feito em worktree (5e0aa622) | 0 (já feito) | merge pendente |
| Verify.QuestPDF snapshot test setup | **pendente** | 1 | — |
| Validar tempo migration em B1 local (docker-compose Postgres + massa realista) | **pendente** | 1 | — |

**Total F0.5 pendente: ~4 dias úteis = ~1 semana (com Felipe paralelizando billing/secrets enquanto código avança).**

**Critério pronto**: CI rodando + ADR-0013 commitado + snapshot test rodando + migration testada local em ≤ 5s.

### F1 — Feature flag + sub-tipos (A1) — 3 dias

- UseCase + Controller para `TenantFeatureFlag` (Get/Toggle).
- Middleware `IFeatureFlagService.IsEnabledAsync(empresaId, "Rotulagem")`.
- Tela admin em `EasyStock.Admin` (React).
- Enum `SubTipoAlimento` em Produto + wizard de cadastro.
- Enum `OrigemCalculoNutricional` em Produto (Manual | Auto).

**Critério pronto**: `dotnet test --filter Category=FeatureFlag` passa + E2E manual ativa flag para Casa da Babá em <10s.

**Bloqueador**: nenhum (independente).

### F2 — Domain + Migrations + Seeds (~8 dias = 2 semanas)

- Criar 18 entidades do MVP (seção 4.3) em `EasyStock.Domain/Entities/Rotulagem/`.
- Migration EF Core única em `EasyStock.Infra.Postgre/Migrations/{stamp}_AddRotulagemNutricional.cs`. Apenas DDL.
- Seed via `RotulagemSeedHostedService` no boot: `TacoIngrediente` (~600), `PorcaoPadraoCategoria` (~50), `CatalogoAditivo` (~400), `RegraClaim` (8), `NormaRegulatoria` (9), `ModeloRotulo` (6 — 1 ativo + 5 stubs).
- Trigger SQL de imutabilidade em `Rotulo` publicado.
- `IRotuloBlobStorage` interface + impl `FlyVolumeRotuloBlobStorage`.
- `idempotency_keys` table + job de limpeza.

**Critério pronto**: migration roda em < 5s em B1 local + seed completo em < 30s no boot + teste de arquitetura `NomenclaturaPtBrTests` valida 18 entidades + teste `RlsTests` valida que tenant não vê dados de outro tenant.

**Bloqueador**: pg_trgm em produção (Validar com Felipe). Se indisponível, fallback ILIKE no `ImportadorTaco`.

### F3 — Aba "Nutricional" no Insumo (A2 + A3 manual) — 5 dias

- Aba Razor `_Nutricional.cshtml` na tela de Produto quando `TipoProduto=Alimento` E flag ativa.
- Formulário 10 nutrientes RDC 429 + 18 alérgenos RDC 26/2015 (checkboxes Contém/Pode conter) + claims (validados via `RegraClaim`).
- Chip de fonte ("Manual" | "TACO" | "Fornecedor" | "Laboratorio") + data.
- FluentValidation `PerfilNutricionalValidator` (cada nutriente ≥ 0, claims com critério).
- UseCases `GetPerfilNutricional`, `SalvarPerfilNutricional`.
- Suporta múltiplas fichas por produto (Anaconda + Renata), flag `EhPadraoAtual` única.

**Critério pronto**: cadastrar manualmente 1 ficha + 1 alérgeno + 1 claim em <2min + claim validation bloqueia "zero açúcar" com 1g/100g + teste E2E "cadastrar Farinha Anaconda" passa.

### F4 — TACO importer + busca (B1) — 4 dias

- `ImportadorTaco` em `EasyStock.Infra.Async`. Baixa XLSX do NEPA → converte para CSV → seed inicial (job no boot).
- Botão "Buscar na base TACO" na aba Nutricional → pg_trgm match → preview valores → "Aplicar" preenche ficha com `origem=Taco`.
- Botão admin "Atualizar TACO" para versões futuras.

**Critério pronto**: busca "farinha trigo" retorna 5 resultados em < 200ms + aplicar preenche 10 campos automaticamente + ImportadorTacoTests passa com fixture XLSX local.

**Bloqueador**: link de download do NEPA muda silenciosamente (URL hard-coded). Mitigação: variável env `Taco:DownloadUrl`.

### F5 — Aba "Nutricional" no Produto Final (A4 + A5 + A7 + B9 backend) — 7 dias

**ATENÇÃO caminho B**: F5 entrega **modo Manual** primeiro (entrada direta dos 10 nutrientes no produto final via `NutricaoManualProdutoFinal`). Modo Auto (cálculo a partir de receita) entra **quando Calculadora mergear** — ver Seção 10.

- Aba Nutricional do produto final mostra:
  - Se `OrigemCalculoNutricional=Manual`: formulário direto (mesmo do insumo).
  - Se `OrigemCalculoNutricional=Auto`: receita editável + `<PainelTabelaNutricional modo=Preview>` ao vivo.
- Porção padronizada via `PorcaoPadraoCategoria` + override.
- Lista de ingredientes auto-gerada em ordem decrescente + agrupamento `CatalogoAditivo` + destaque alérgenos.
- A cada "Confirmar receita": snapshot em `FichaTecnicaProduto` (versão incrementada — backend completo; UI listagem em F+1).
- Botão "Migrar para receita modelada" (Manual → Auto).
- Botão "Sair do cálculo automático" (Auto → Manual, com confirmação dupla + motivo obrigatório).

**Critério pronto**: cadastrar produto Manual em <3min + status verde/amarelo/vermelho aparece corretamente + `CalculadoraNutricionalTests` cobertura ≥ 90% (15+ cenários) + migração Manual→Auto preserva valores em `FichaTecnicaProduto v0`.

### F6 — PDF + Identificação + Isento Art. 22 (A8 + A9 + A11 + B3 BR + B4 mínimo) — 6 dias

- `IRenderizadorRotulo` + `FabricaRenderizadorRotulo`.
- MVP entrega **APENAS** `RenderizadorRotuloBrRdc429Tabular`. Stubs para Linear, EmbalagemPequena, IsentoArt22, Mercosul, Eu, Fda.
- Identificação RDC 727 puxa Empresa/Produto/Lote.
- Instruções obrigatórias com presets editáveis (template por categoria).
- Editor visual MVP: customiza logo + cor primária + posicionamento secundário (SAC, instruções). Tabela e lupa **fixas e não-editáveis**.
- Validador de layout hard-coded: fonte ≥ 1mm, área tabela ≥ 30% do painel, lupa em posição superior.
- Baixar 3 SVGs oficiais Anvisa lupa em `wwwroot/img/anvisa-lupa-{nutriente}.svg`.

**Critério pronto**: gerar PDF de Ravioli em <10s + `RotuloPdfSnapshotTests` baseline aprovada + validador rejeita layout com fonte 0.5mm + visual review por humano (Felipe) aprova primeiro PDF.

### F7 — Aba "Rótulo" no Lote + Versionamento + Impressão Térmica (A10 + B5 Zebra) — 6 dias

- Nova aba em `/Lotes/Detalhe/{id}` só com flag ativa.
- `<PainelTabelaNutricional modo=Live>` + botões `Imprimir etiquetas`, `Baixar PDF`.
- Link "Reformular este produto" → abre produto-pai `/produtos/{id}?aba=nutricional&from=lote/{loteId}`.
- Cada geração cria novo `Rotulo` com snapshot imutável + PDF/PNG no Fly volume.
- `ImpressoraService` com adapters `ZebraZplAdapter` + `A4PdfMultiUpAdapter` (Argox/Elgin em F+1).
- Estado `AguardandoAprovacaoRt` quando sub-tipo exige RT (Suplemento/Infantil).

**Critério pronto**: imprimir 20 etiquetas em Zebra USB em <30s + PDF arquivado em `/data/rotulos/{empresa}/{rotulo}.pdf` + trigger SQL impede UPDATE em `Rotulo` publicado + teste E2E "publicar + reimprimir" passa.

**Bloqueador**: impressora Zebra física para teste. Mitigação: Felipe tem Zebra ZD230 em casa para Casa da Babá.

### F8 — Lupa + Bebidas Alcoólicas + Validação de 8 Claims (A6 fase 1 + A12 + A13 + C4 determinístico + RT) — 8 dias

- Lupa frontal IN 75 fase 1 calculada automaticamente.
- Fase 2 (out/2026) → F+1, com aviso cron 60 dias antes via Resend.
- Bebidas alcoólicas: advertência + teor (campo extra na ficha).
- `ConformidadeValidator` MVP: 8 claims principais + identificação obrigatória + isento Art. 22.
- Bloqueia publicação em violação; permite override com justificativa para warnings.
- **UI mínima RT**: tela "Configurações > Rotulagem > Responsável Técnico" (cadastrar Nome, CPF, CRN, upload assinatura imagem).
- Modal "Aprovar como responsável técnico" para usuário com `Cargo=RT`.
- Computa `ComprovanteAprovacaoInternaHash = SHA256(nome + crn + timestamp + rotuloId + secret_servidor)`.
- Disclaimer honesto: "Comprovante de aprovação interna. Não é assinatura digital ICP-Brasil."

**Critério pronto**: lupa renderiza corretamente em 5 cenários (sódio/saturada/açúcar; sólido/líquido) + claim "zero açúcar" com 1g bloqueia publicação + RT aprova suplemento em <30s + Comprovante hash gerado com secret_versao=1.

### F9 — Importação ficha de fornecedor via Claude (C2) — 4 dias

- Refator `IClaudeStructuredExtractor` **já feito em F0.5**.
- Implementar `ExtratorFichaNutricional : ClaudeStructuredExtractorBase<FichaInput, FichaOutput>`.
- Botão "Importar do fornecedor" → upload PDF/JPG → OCR (Azure Document Intelligence — escolhido no spike, pago, melhor qualidade) → Claude estrutura ficha → modal "Confirmar/Editar" → salva com `origem=Fornecedor`.

**Critério pronto**: importar PDF de ficha técnica real (Anaconda) em <30s + Claude preenche ≥ 8 de 10 nutrientes corretamente + edição manual completa o restante + teste com fixture PDF cobre happy path + erro de Claude → fallback "preencher manualmente".

**Bloqueador**: chave Azure Document Intelligence (Validar com Felipe se prefere Tesseract grátis OU AzureDocAI pago).

### F10 — Menu "Rotulagem" + Calculadora Etapa 2 panel + PWA botão — 3 dias

- Novo item de menu "Rotulagem" (só com flag ativa). 3 abas:
  - **Produtos**: lista com status verde/amarelo/vermelho.
  - **Ingredientes**: cobertura nutricional %.
  - **Rótulos publicados**: histórico versionado + reimpressão.
- Painel "Impacto nutricional do batch" na Calculadora de Produção Etapa 2 (`<PainelTabelaNutricional modo=Preview>`) — só funcional quando Calculadora mergear; stub vazio até lá.
- Botão "Ver informações nutricionais" no PWA de Pedidos consumindo `GET /api/v1/produtos/{id}/nutricao`.

**Critério pronto**: menu visível só para empresa com flag + 3 abas navegáveis + PWA mostra tabela em mobile real.

### F11 — API pública NutritionCalculator — 3 dias

- `POST /api/v1/nutricao/calcular` (Stripe-style `Idempotency-Key`).
- `GET /api/v1/produtos/{id}/nutricao`.
- Rate limit ligado dia 1 (60/min, 1000/h).
- DTOs em `EasyStock.Contracts/v1/Nutricao/`.
- Swagger documentado.

**Critério pronto**: `dotnet test --filter Category=ApiPublica` passa (contract tests) + chamada manual com Idempotency-Key retorna `Idempotent-Replayed: true` na 2ª chamada + rate limit 429 após 60 req/min.

### F12 — Resend email + Notificações + Webflows GHA backup ativos — 3 dias

- Projeto `EasyStock.Infra.Email/` com `IEmailService` + `ResendEmailService`.
- Templates RazorLight: `RascunhoProximoArquivamento.cshtml`, `BackupCriticoFalhou.cshtml`.
- `ArquivamentoRascunhoJob` cron diário.
- Ativar workflows GHA `backup-rotulos.yml` + `restore-test-rotulos.yml` (depois Felipe configurar secrets).

**Critério pronto**: rascunho de 7 dias atrás dispara email para Felipe + restore-test mensal roda no GHA + alerta crítico chega no email se restore-test falhar.

### F13 — Polimento, métricas, ADR finalizados, FAQ Anvisa, Troubleshooting (~5 dias)

- Métricas: cobertura nutricional média/empresa, % produtos com lupa, latência calculator p95, taxa de claims bloqueados (Datadog opcional ou logs estruturados em arquivo).
- Logs estruturados em todos os UseCases via `Microsoft.Extensions.Logging` (JSON output).
- FAQ Anvisa para usuário (15+ perguntas, ver Seção 13.3).
- Troubleshooting guide (ver Seção 13.4).
- Doc "Como rodar o MVP em produção" (Seção 11).
- ADR-0011, ADR-0012, ADR-0013 revisados + commitados.

**Critério pronto**: dashboard de métricas mostra dados reais em <1h após primeiro rótulo + FAQ tem 15 perguntas com citação RDC + Troubleshooting cobre 5 cenários listados + Felipe consegue "fly deploy" em <2min seguindo a doc.

### F+1 — Paridade competitiva (~6 semanas)

| Feature | Bloco |
|---|---|
| Editor visual avançado (drag-drop, snapping, undo/redo) com validador embarcado | B4+ |
| Templates Mercosul, UE, FDA — implementação completa | B3+ |
| Impressoras Argox + Elgin | B5+ |
| EAN-13/GTIN com prefixo GS1 | B6 |
| UI da Rastreabilidade insumo→cliente | B7 |
| RT/CRN com ICP-Brasil A1/A3 + DocuSign | B8+ |
| UI listagem de fichas técnicas versionadas | B9+ |
| QR público + página de rastreabilidade renderizada | B10 |
| Importação fornecedor via API (Bling, Tiny) | B2 |
| Lupa fase 2 (out/2026) — cron com aviso 60d antes | A6+ |
| Simulador standalone + Comparador lado-a-lado | UX |

### F+2 — IA avançada (~6 semanas)

| Feature | Bloco |
|---|---|
| Matching semântico TACO/USDA com score (`pgvector`) | C1 |
| Sugestão de reformulação (custo + margem) | C3 |
| Validação preditiva completa (LLM para casos ambíguos) | C4+ |
| Monitoria regulatória (`AlertaRegulatorio` + job scrape) | C5 |
| Detecção de risco de exportação | C6 |
| Assistente de rotulagem por chat (RAG sobre RDCs) | C7 |
| Geração automática de descrição comercial | C8 |
| Multi-idioma | B11 |

### Cronograma com datas (buffer 20%)

Premissa: início F0.5 pendente em **2026-05-17 (segunda-feira)**. 17–19h/semana líquidas. Buffer +20%.

| Fase | Dias úteis | Início | Fim previsto | Marcos |
|---|---|---|---|---|
| F0.5 pendente | 4 | 2026-05-17 | 2026-05-22 | ADR-0013, snapshot test, migration validada |
| F1 | 3 | 2026-05-23 | 2026-05-27 | Flag ativa em ambiente dev |
| F2 | 8 | 2026-05-28 | 2026-06-09 | Migration em prod + seed completo |
| F3 | 5 | 2026-06-10 | 2026-06-16 | Cadastrar Farinha Anaconda completo |
| F4 | 4 | 2026-06-17 | 2026-06-22 | TACO importada, busca funcional |
| F5 | 7 | 2026-06-23 | 2026-07-02 | Modo Manual: cadastrar Ravioli e gerar tabela |
| F6 | 6 | 2026-07-03 | 2026-07-10 | Primeiro PDF de Ravioli aprovado por Felipe |
| F7 | 6 | 2026-07-13 | 2026-07-20 | Primeiro lote real impresso em Zebra |
| F8 | 8 | 2026-07-21 | 2026-07-30 | Lupa + claims funcionais; RT aprovação |
| F9 | 4 | 2026-07-31 | 2026-08-05 | Importação ficha Anaconda via Claude |
| F10 | 3 | 2026-08-06 | 2026-08-10 | Menu Rotulagem + PWA botão |
| F11 | 3 | 2026-08-11 | 2026-08-13 | API pública v1 documentada |
| F12 | 3 | 2026-08-14 | 2026-08-18 | Resend ativo, backups rodando |
| F13 | 5 | 2026-08-19 | 2026-08-25 | FAQ, Troubleshooting, doc produção |
| **Buffer 20%** | **13** | 2026-08-26 | 2026-09-11 | Imprevistos absorvidos |
| **MVP go-live** | — | — | **2026-09-12** | Produção para Casa da Babá |

**Total: 16 semanas calendário (17 mai 2026 → 12 set 2026) com buffer.** Equivalente a ~11 semanas líquidas de execução. **Honestidade cronográfica**: o anterior "8-10 semanas" era otimista — agora ajustado para **10-12 semanas líquidas / ~16 calendário com buffer**.

### Validação contínua com Thatiane

- **Semana 2 (F2 final)**: Thatiane vê migration em ambiente dev (não toca, só vê o menu vazio).
- **Semana 4 (F3 final)**: Thatiane cadastra Farinha Anaconda em ambiente dev. Coleta feedback.
- **Semana 6 (F5 final)**: Thatiane cadastra Ravioli em modo Manual. Critério de continuidade: ela faz sem ajuda em <5min.
- **Semana 9 (F7 final)**: Thatiane imprime 20 etiquetas reais. **Go/no-go MVP**: se ela imprime sem ajuda e os rótulos estão visualmente corretos, segue F8+. Caso contrário, sprint de UX antes de F8.
- **Semana 13 (F13 final)**: Thatiane usa em modo produção (ambiente staging) por 5 dias úteis. **Go/no-go produção**: zero bugs bloqueantes em 5 dias.

### Critério Go/No-Go para Lançar em Produção

GO se TUDO abaixo verdadeiro:
- [x] Todos os F0.5–F13 entregues
- [x] Thatiane usou 5 dias em staging sem bug bloqueante
- [x] Backup diário rodou ≥ 7 dias sem falha
- [x] Restore-test mensal passou ≥ 1 vez
- [x] Snapshot tests de PDF baseline aprovados manualmente por Felipe
- [x] Latência calculator p95 < 100ms medida
- [x] 0 erros críticos no Sentry/log nos últimos 3 dias de staging
- [x] Felipe consegue rollback em < 5min seguindo doc de produção

NO-GO se qualquer um falso. Sprint de correção de 1 semana antes de re-avaliar.

---

## 10. Plano B e Coexistência Manual ↔ Auto

### 10.1 Decisão arquitetural

`Produto.OrigemCalculoNutricional` é enum em **Produto** (coluna nova adicionada na migration F2 — adição não-destrutiva):

```sql
ALTER TABLE public.produto
    ADD COLUMN origem_calculo_nutricional text NOT NULL DEFAULT 'Manual'
    CHECK (origem_calculo_nutricional IN ('Manual','Auto'));
```

Esta é a **única alteração em tabela existente** no MVP. Justificada porque:
- É campo aditivo (com default), não quebra schema existente.
- Permite query rápida "todos produtos em modo Manual" sem join.
- Default `Manual` (segurança: produtos legados ficam em modo Manual até admin escolher).

### 10.2 Schema final dos dois modos

**Modo Manual** (caminho B + reverso Auto→Manual):
- Dados nutricionais em **`NutricaoManualProdutoFinal`** (tabela criada em seção 4.3.7).
- Sem `ProdutoComposicao` vinculado.
- Sem `ProdutoNutricaoCalculada` vinculado (cache fica vazio).
- Snapshot do Rotulo inclui `dados_snapshot_json.modoCalculo = "Manual"` + dados copiados de `NutricaoManualProdutoFinal`.

**Modo Auto** (quando Calculadora mergear):
- Dados nutricionais calculados a partir de `ProdutoComposicao` via `CalculadoraNutricional`.
- Cache em `ProdutoNutricaoCalculada` (invalidado via domain events).
- `FichaTecnicaProduto` versiona snapshots da receita a cada Confirmar.
- Snapshot do Rotulo inclui `dados_snapshot_json.modoCalculo = "Auto"` + receita expandida.

### 10.3 Migração Manual → Auto (caminho desejável)

UseCase `MigrarParaReceitaModeladaUseCase`:
1. Valida que existe `ProdutoComposicao` para o produto (Calculadora foi mergeada e produto tem receita).
2. Cria `FichaTecnicaProduto v0` com `receita_snapshot = JSON do NutricaoManualProdutoFinal` + `motivo_alteracao="Migrado de modo manual em {data}"`.
3. Set `Produto.OrigemCalculoNutricional = 'Auto'`.
4. Cria entrada inicial em `ProdutoNutricaoCalculada` com `status='Stale'` (job de recálculo cria oficial em <5min).
5. Mantém `NutricaoManualProdutoFinal` intacto (auditoria — não deleta).
6. Toast: "Migrado. Configure os ingredientes para gerar a nova tabela."

### 10.4 Reverso Auto → Manual (caso raro)

UseCase `MigrarParaCalculoManualUseCase`:
1. Confirmação dupla na UI (modal 1: "Sair do cálculo automático?"; modal 2: "Tem certeza?" + campo `motivo` obrigatório).
2. Cria nova `FichaTecnicaProduto vN+1` com:
   - `receita_snapshot = JSON do último ProdutoNutricaoCalculada conhecido`
   - `eh_snapshot_congelado = true`
   - `motivo_alteracao = {motivo do usuário}`.
3. Copia valores do último `ProdutoNutricaoCalculada` para `NutricaoManualProdutoFinal` (cria se não existir; senão UPDATE).
4. Set `Produto.OrigemCalculoNutricional = 'Manual'`.
5. Mantém `ProdutoNutricaoCalculada` intacto + `ProdutoComposicao` intacto (auditoria).
6. Log de auditoria `AuditoriaMudancaModoNutricional(produto_id, modo_anterior, modo_novo, motivo, executado_por, executado_em)`.

**Retenção dos snapshots congelados (P5)**: mesma da `FichaTecnicaProduto` — 5 anos mínimo, indefinido em armazenamento arquivo após isso.

### 10.5 UX dos dois modos (aba Nutricional)

**Modo Manual**:
- Aba mostra formulário direto com 10 nutrientes + alérgenos + claims.
- Box no canto direito: "Quer calcular automaticamente a partir da receita? [Migrar para receita modelada]" (só aparece se `ProdutoComposicao` existe).
- Chip "Manual" sob cada nutriente.

**Modo Auto**:
- Aba mostra receita editável + `<PainelTabelaNutricional modo=Preview>` ao vivo na lateral.
- Botão "Confirmar receita" persiste.
- Link discreto no rodapé: "Sair do cálculo automático" (raríssimo, atritado).

**Status do produto** (badge no topo da aba):
- `Manual + completo`: badge cinza "Manual"
- `Auto + cache Atual`: badge verde "Calculado"
- `Auto + cache Stale`: badge amarelo "Recalculando…"
- `Auto + insumo sem ficha`: badge vermelho "{n} insumos sem ficha"

### 10.6 Quais entidades importam em cada modo

| Entidade | Manual usa? | Auto usa? |
|---|---|---|
| `PerfilNutricional` (insumo) | apenas se for um Ingrediente que vira insumo de outro Produto Auto | sim |
| `NutricaoManualProdutoFinal` | **sim** | preservado mas não-fonte oficial |
| `ProdutoComposicao` (Calculadora) | não usa | **sim** |
| `ProdutoNutricaoCalculada` | vazio | **sim** (cache) |
| `FichaTecnicaProduto` | usa para v0 (migração) | **sim** (cada Confirmar) |
| `Rotulo` snapshot | grava com `modoCalculo="Manual"` | grava com `modoCalculo="Auto"` + receita expandida |

---

## 11. CI/CD, Deploy e Operações de Produção

### 11.1 Pipeline CI (GitHub Actions `ci.yml` existente + ajustes)

Jobs que precisam rodar antes de merge (paralelizados quando possível):

| Job | Descrição | Tempo estimado | Cache |
|---|---|---|---|
| `setup` | checkout + setup-dotnet 9.0.x | 30s | NuGet via `actions/cache@v4` chave `{hashFiles('**/*.csproj')}` |
| `build` | `dotnet build --no-restore` | 2–3 min | bin/ + obj/ cached entre jobs |
| `test-domain` | `dotnet test EasyStock.Domain.Tests` | 1 min | — |
| `test-application` | `dotnet test EasyStock.Application.Tests` (inclui `CalculadoraNutricionalTests`) | 2 min | — |
| `test-architecture` | `dotnet test EasyStock.ArchitectureTests --filter Category=Architecture` | 30s | — |
| `test-snapshot-pdf` | `dotnet test EasyStock.Application.Tests --filter Category=PdfSnapshot` (Verify.QuestPDF) | 1 min | baselines em `tests/snapshots/` |
| `test-postgre-integration` | `dotnet test EasyStock.Infra.Postgre.IntegrationTests` (Postgres testcontainer) | 4 min | testcontainer Postgres 16 imagem cached |
| `test-api-integration` | `dotnet test EasyStock.Api.IntegrationTests` | 3 min | — |
| `lint-format` | `dotnet format --verify-no-changes` | 1 min | — |
| `security-scan` | CodeQL via `codeql.yml` existente | 5 min | — |
| `coverage` | `coverlet.collector` → upload Codecov via `coverage.yml` existente | 1 min | — |

**Tempo total esperado**: pipeline paralelizado em ~6 min, sequencial ~20 min. Alvo: **manter <10 min com paralelização**.

**Quando pular CI**: **nunca**. Não há hotfix path. Felipe é solo, qualquer "pulou só dessa vez" vira regra.

### 11.2 Pre-commit local (Husky.Net — já feito em F0.5)

Roda apenas `EasyStock.ArchitectureTests --filter Category=Architecture` (~50ms). Os outros testes ficam para o CI no push.

### 11.3 Estratégia de deploy

**Plataforma**: Fly.io, app `easystok-app` (assunção — Validar com Felipe antes de F1). Região **Brazil South (gru)**. Resource B1 (1.75GB RAM, 1 vCPU compartilhado, $5/mês plano).

**Estratégia**: **rolling deploy** padrão do Fly. `fly deploy` faz rolling de 1 instância por vez (mas Felipe roda 1 só → blue/green via 2 machines temporárias durante deploy).

**Health check**:
- Endpoint: `GET /health/liveness` (existente, retorna 200 OK)
- Endpoint: `GET /health/readiness` (verifica DB connection + Fly volume mount)
- Critério: 3 consecutivos OK em 30s → marca instância pronta.

**Migration em produção**:
- **Roda em job separado**, **NÃO no startup do app**.
- Comando: `fly ssh console -C "dotnet EasyStock.Api.dll migrate"` (custom command no Program.cs).
- Executado manualmente por Felipe antes do `fly deploy` da feature.
- Migration é só DDL (rápida); seeds via `RotulagemSeedHostedService` no startup (background, não bloqueia health check).
- Para zero-downtime: migration aditiva apenas (princípio); reverter em caso de bug requer rollback de migration manual.

**Rollback**:
```bash
# 1. Identificar release anterior
fly releases -a easystok-app | head -5

# 2. Rollback de imagem
fly deploy --image registry.fly.io/easystok-app:deployment-{HASH_ANTERIOR}

# 3. Se rollback envolve schema: reverter migration manualmente
fly ssh console -C "dotnet EasyStock.Api.dll rollback-migration --to {NOME_MIGRATION_ANTERIOR}"
```

### 11.4 Documento "Como rodar o MVP em produção" (P7 — completo)

Salvar em `docs/operacao/como-rodar-mvp-producao.md`. Será escrito em F13 mas estrutura abaixo é o template definitivo.

```markdown
# Como rodar o MVP de Rotulagem em produção

## Ambiente Fly
- **App name**: `easystok-app`           [Validar com Felipe antes de F1]
- **Região**: Brazil South (`gru`)
- **Recursos**: 1× shared-cpu-1x (B1), 1.75GB RAM, 1 vCPU compartilhado
- **Volume**: `easystok_data` montado em `/data`, 10GB inicial (expandir conforme cresce)
- **Custo Fly base**: ~$5/mês (B1) + ~$0.15/GB-mês do volume

## Acesso ao banco
- **Provider**: Fly Postgres `easystok-pg-gru`     [Validar com Felipe antes de F1]
- **Connection string**: variável `DATABASE_URL` em Fly Secrets
- **Rede**: WireGuard via `fly proxy 5432:5432 -a easystok-pg-gru` para acesso local
- **Role de leitura para backup**: `easystok_backup_ro` (separado da role app)

## Primeiro admin
- **Email seed**: `felipe@easystok.com.br`       [Validar com Felipe antes de F1]
- **Senha inicial**: gerada via comando `fly ssh console -C "dotnet EasyStock.Api.dll seed-admin"`
  imprime senha temporária forçando reset no primeiro login.
- **MFA**: Habilitado obrigatório para qualquer usuário com role `Admin` (TOTP via Google Authenticator).

## Ativar flag Rotulagem para Casa da Babá (passo a passo)
1. Login como admin em `https://app.easystok.com.br/admin`.
2. Vá em **Empresas** → busque "Casa da Babá" → clique.
3. Aba **Feature Flags** → encontre `Rotulagem` → toggle ON.
4. Modal pergunta: "Importar base TACO de ingredientes?" → **Sim**.
5. Aguarde job de seed (~30s, badge "Importando…").
6. Empresa agora vê menu "Rotulagem" e abas Nutricionais nos produtos.

## Logs
- **Aplicação**: `fly logs -a easystok-app` (Fly mantém últimas 24h).
- **Persistente**: stdout JSON estruturado → encaminhado para Better Stack
  ([Validar com Felipe antes de F1] — pode ser self-hosted Loki em vez)
  retenção 30 dias.
- **Auditoria sanitária** (`RastreabilidadeEvento`, `Rotulo` snapshot): no Postgres,
  retenção mínima 5 anos via backup R2.

## Canal de alerta de erro
- **Errors críticos**: Resend → email para `felipe@easystok.com.br`
  (configurado em `Anvisa:AlertaCritico:ToEmail`).
- **Sentry**: [Validar com Felipe — opcional, custo R$ ~80/mês plano Team] —
  se ativado, captura exceções + tracing.
- **Slack**: NÃO configurado no MVP. Email é canal único.

## Rodar restore-test manual
Caso o cron mensal falhe ou queira validar fora do schedule:
\`\`\`bash
# Disparar workflow GHA manualmente
gh workflow run restore-test-rotulos.yml -R felipe/easystok
\`\`\`
Resultado em ~5 min em `Actions` tab do GitHub. Email crítico se falhar.

## Console SSH no Fly (se algo travar)
\`\`\`bash
fly ssh console -a easystok-app
# dentro: 
top              # ver consumo
ls /data/rotulos # listar rótulos arquivados
\`\`\`

## Procedimento de rollback de deploy
Ver Seção 11.3 deste plano. Resumo:
1. `fly releases -a easystok-app` → pegar HASH anterior.
2. `fly deploy --image registry.fly.io/easystok-app:deployment-{HASH}`.
3. Se rollback envolveu schema: rodar `dotnet EasyStock.Api.dll rollback-migration --to {NOME}`.
4. Validar health check + smoke test (`curl https://app.easystok.com.br/health/readiness`).

## Critérios de incidente
- **SEV-1 (call Felipe imediatamente)**: rótulos não imprimem; PDF retorna 500;
  validador de claims falso negativo (claim permitido quando deveria bloquear).
- **SEV-2 (24h)**: lupa frontal calcula errado; cache não invalida;
  email de alerta não chega.
- **SEV-3 (próxima janela de manutenção)**: typo em microcopy; cor de banner errada;
  log estruturado faltando campo.
```

---

## 12. Testabilidade

### 12.1 Plano de testes (T1)

| Suite | Projeto | Classe | Método | Fixtures | Assertions | Tempo |
|---|---|---|---|---|---|---|
| Pureza motor | `EasyStock.Application.Tests` | `CalculadoraNutricionalTests` | `Calcular_ReceitaSimples_RetornaTabelaEsperada` | `ReceitaFixtures.RavioliRicotaSimples()` | `tabela.PorPorcao.SodioMg` == 245 | <50ms |
| Pureza motor | idem | `CalculadoraNutricionalTests` | `Calcular_MesmoInput_RetornaMesmoHash` | idem | `r1.ReceitaVersaoHash == r2.ReceitaVersaoHash` | <50ms |
| Pureza motor | idem | `CalculadoraNutricionalTests` | `Calcular_OrdemDosInsumos_NaoAfetaHash` | 2 versões com insumos invertidos | hashes iguais | <50ms |
| Pureza motor | idem | `CalculadoraNutricionalTests` | `Calcular_FatorRendimento_ConcentraValores` | receita com rendimento 0.85 | sódio ajustado | <50ms |
| Pureza motor | idem | `CalculadoraNutricionalTests` | `Calcular_ReceitaVazia_LancaArgumentException` | insumos[] vazio | throws | <50ms |
| Lupa | idem | `LupaCalculatorTests` | `Lupa_Sodio_Solido_AcimaDoLimite_Marca` | sódio=700mg/100g sólido fase1 | `SodioAlto==true` | <30ms |
| Lupa | idem | `LupaCalculatorTests` | `Lupa_Sodio_Liquido_LimiteDiferente` | sódio=350mg/100ml líquido fase1 | `SodioAlto==true` (limite 300) | <30ms |
| Lupa | idem | `LupaCalculatorTests` | `Lupa_Fase2_LimitesMaisRestritivos_FlagsExtras` | sódio=450mg/100g sólido fase2 | `SodioAlto==true` (limite 400) | <30ms |
| Lupa | idem | `LupaCalculatorTests` | `Lupa_TodosLimitesNaoAtingidos_ZeroFlags` | valores baixos | 3 flags false | <30ms |
| Conformidade | idem | `ConformidadeValidatorTests` | `Validar_ClaimZeroAcucar_Com1g_BloqueiaComMotivoRDC` | claim ZERO_ACUCAR + açúcar=1g | `Status==Bloqueado` + motivo cita RDC 54/2012 | <30ms |
| Conformidade | idem | `ConformidadeValidatorTests` | `Validar_8Claims_TodosCenariosOk_E_NotOk` | matriz 8×2 | passa nos OK, bloqueia nos NotOK | <100ms |
| Lista ingredientes | idem | `GeradorListaIngredientesTests` | `Gerar_OrdenaPorPesoDecrescente_E_AgrupaAditivos` | receita com 5 insumos + 2 aditivos | string em ordem correta | <30ms |
| Lista ingredientes | idem | `GeradorListaIngredientesTests` | `Gerar_DestacaAlergenos_EmCaixa` | farinha (trigo) + ovos | "TRIGO" e "OVOS" em UPPERCASE | <30ms |
| Snapshot PDF | idem | `RotuloPdfSnapshotTests` | `BrRdc429Tabular_Ravioli_Snapshot` | snapshot Ravioli completo | `Verify(pdf).UseExtension("pdf")` baseline | <2s |
| Snapshot PDF | idem | `RotuloPdfSnapshotTests` | `BrRdc429Tabular_ComLupa_Snapshot` | snapshot com sódio alto | baseline com lupa renderizada | <2s |
| Snapshot PDF | idem | `RotuloPdfSnapshotTests` | `BrIsentoArt22_SnapshotSemTabela` | produto fruta in natura | baseline sem tabela | <2s |
| Schema snapshot | idem | `SnapshotSchemaTests` | `RotuloDadosSnapshot_chaves_obrigatorias` | sample fixture | 12 chaves presentes | <50ms |
| Arquitetura | `EasyStock.ArchitectureTests` | `NomenclaturaPtBrTests` | `Entidades_de_dominio_Rotulagem_devem_ter_nomes_em_pt_br` | namespace `EasyStock.Domain.Entities.Rotulagem` | nenhum termo EN proibido | <100ms |
| Arquitetura | idem | `GlobalCatalogTests` | `GlobalCatalogs_NaoTemFkParaEmpresa` | tipos com `IGlobalCatalog` | sem dependência em `Empresa` | <100ms |
| Arquitetura | idem | `RlsTests` | `EntidadesTenantTem_EmpresaId_E_RlsPolicy` | tipos com FK para Empresa | tem `EmpresaId` + policy registrada | <100ms |
| Integration | `EasyStock.Application.Tests` | `RotulagemFluxoCompletoTests` | `Migration_Seed_CadastrarPerfilN_Receita_Publicar_Reimprimir` | testcontainer Postgres | 1 Rotulo persistido, snapshot íntegro | <30s |
| API contract | `EasyStock.Api.IntegrationTests` | `NutricaoCalculatorContractTests` | `Post_IdempotencyKey_MesmoBody_RetornaCacheReplayed` | request fixture | `Idempotent-Replayed: true` na 2ª chamada | <500ms |
| API contract | idem | `NutricaoCalculatorContractTests` | `Post_IdempotencyKey_BodyDiferente_Retorna409` | mesma chave + body diferente | status 409 | <500ms |
| API rate limit | idem | `RateLimitTests` | `Post_60RequestsEm1Min_Retorna429NaProxima` | loop 60 req | 61ª retorna 429 + Retry-After | <2s |
| Feature flag | `EasyStock.Application.Tests` | `FeatureFlagTests` | `Toggle_HabilitaModulo_PreserVaConfig` | empresa sem flag | flag salva + módulo visível | <100ms |
| RT aprovação | idem | `AprovacaoRtTests` | `Aprovar_GeraHash_ComSecretVersaoAtual` | RT + Rotulo | hash sha256 valid + secret_versao=1 | <100ms |

**Cobertura mínima**:
- `EasyStock.Application/Rotulagem/Services/` (motor + validadores): **≥ 90%**.
- `EasyStock.Application/Rotulagem/UseCases/`: **≥ 80%**.
- `EasyStock.Domain/Entities/Rotulagem/`: **≥ 70%** (entidades anêmicas têm pouco a testar).

### 12.2 Snapshot tests PDF (T2 — Verify.QuestPDF)

**Aprovar primeira baseline**:
1. Felipe roda `dotnet test --filter Category=PdfSnapshot` localmente.
2. Teste falha na primeira vez (sem baseline).
3. Verify.QuestPDF gera `*.received.pdf` na pasta `tests/snapshots/Rotulagem/`.
4. Felipe abre o PDF, valida visualmente.
5. Renomeia `.received.pdf` → `.verified.pdf` (ou via comando `dotnet verify-tests review`).
6. Commit dos baselines.

**Mudança intencional vs regressão**:
- Mudança intencional: Felipe edita o template, roda testes, vê novo `.received.pdf`, valida, renomeia, commit junto com mudança do template.
- Regressão: CI falha automaticamente; PR não merge.

**Onde guarda baselines**:
- `tests/snapshots/Rotulagem/*.verified.pdf` no repo.
- PDFs são pequenos (~80KB cada × ~10 baselines = 800KB).
- **Sem git LFS** (volume baixo). Se passar 10MB total, migrar para LFS.

### 12.3 Property-based testing do motor (T3 — FsCheck)

```csharp
// EasyStock.Application.Tests/Rotulagem/CalculadoraNutricionalPropertyTests.cs
[Trait("Category","Property")]
public class CalculadoraNutricionalPropertyTests
{
    [Property(MaxTest = 100)]
    public Property Calcular_NuncaProduzValoresNegativos(ReceitaArbitraria r)
    {
        var resultado = CalculadoraNutricional.Calcular(r.AsInput());
        return (resultado.Tabela.PorPorcao.SodioMg >= 0
             && resultado.Tabela.Por100g.ValorEnergeticoKcal >= 0
             && resultado.Tabela.PorPorcao.FibrasG >= 0).ToProperty();
    }

    [Property(MaxTest = 50)]
    public Property Calcular_Idempotente(ReceitaArbitraria r)
    {
        var input = r.AsInput();
        var r1 = CalculadoraNutricional.Calcular(input);
        var r2 = CalculadoraNutricional.Calcular(input);
        return (r1.ReceitaVersaoHash == r2.ReceitaVersaoHash).ToProperty();
    }

    [Property(MaxTest = 50)]
    public Property Calcular_OrdemInsumos_NaoAfetaResultado(ReceitaArbitraria r)
    {
        var input1 = r.AsInput();
        var input2 = input1 with { Receita = input1.Receita with
            { Insumos = input1.Receita.Insumos.Reverse().ToList() } };
        var h1 = CalculadoraNutricional.Calcular(input1).ReceitaVersaoHash;
        var h2 = CalculadoraNutricional.Calcular(input2).ReceitaVersaoHash;
        return (h1 == h2).ToProperty();
    }
}
```

Adicionar pacote `FsCheck.Xunit` em `EasyStock.Application.Tests.csproj`.

### 12.4 Benchmark p95 < 100ms (BenchmarkDotNet)

```csharp
// EasyStock.Benchmarks/CalculadoraNutricionalBenchmark.cs
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class CalculadoraNutricionalBenchmark
{
    private CalculoNutricionalInput _input30Insumos;

    [GlobalSetup]
    public void Setup() => _input30Insumos = ReceitaFixtures.Receita30Insumos();

    [Benchmark]
    public ResultadoNutricional Calcular() => CalculadoraNutricional.Calcular(_input30Insumos);
}
```

Roda no CI mensalmente. Falha se `Calcular` p95 > 100ms.

### 12.5 Teste de migration em B1 local (T4)

**Procedimento exato**:
1. `cd C:\rep\EasyStok && docker-compose -f docker-compose.test-b1.yml up -d`
   - Compose define: Postgres 16-alpine com `--memory=512m --cpus=0.5` (simula B1 sobrecarregado).
2. `dotnet ef database update --connection "Host=localhost;Port=5433;..."`.
3. Medir tempo: `time dotnet ef database update`.
4. **Critério de falha**: > 5s.
5. Se falhar: dividir migration em múltiplas migrations menores (cada CREATE TABLE / CREATE INDEX em sua própria).

**Massa de dados realística** (validar com dados reais antes do go-live):
- 50k produtos
- 10k fornecedores
- 50k items_estoque
- 5k lotes
- 200k lote_itens
- Carregar via `EasyStock.TestHelpers.MassaProdRealistica` (script Bogus existente — Validar com Felipe).

**Quem roda**: Felipe, antes de qualquer migration em produção. Documentado em "Como rodar o MVP em produção" (Seção 11.4).

---

## 13. Documentação

### 13.1 ADRs (D1)

**ADR-0011 — Nomenclatura PT-BR para o módulo Rotulagem Nutricional**

```
Status: Accepted (2026-05-16)
Deciders: Felipe
Context: Projeto consolidou em PT-BR para entidades de negócio (Empresa, Loja, Lote,
  Produto, Fornecedor, ConfiguracaoLoja, ProdutoComposicao). Rascunhos iniciais do P-02
  tinham resíduos EN (NutritionCalculator, NutritionPreviewPanel, FornecedorPrivacyConfig).
Decision: PT-BR para substantivos de negócio. EN apenas para sufixos de padrões técnicos
  consagrados (Repository, Service, Handler, Factory, Adapter, Provider, Validator,
  Interceptor, Job, HostedService, Generator, Renderer, Event) ou conceitos sem
  equivalente PT natural (Snapshot, Hash, Cache, Stale, Hydrator).
Consequences:
  + Coerência com código pré-existente
  + Onboarding simples para devs BR
  + Verificável automaticamente via NetArchTest
  - Tabela de renomeação ~30 itens antes do primeiro commit (custo de uma vez)
Q referenciada: Q37
Arquivo: docs/adr/0011-nomenclatura-pt-br-rotulagem.md (já criado em F0.5)
```

**ADR-0012 — Backup e storage de rótulos publicados**

```
Status: Accepted (2026-05-16)
Deciders: Felipe
Context: Rotulo é entidade imutável com PDF arquivado + snapshot JSON. Material de
  auditoria sanitária com retenção legal 5 anos (RDC 275/2002). Sem backup testado,
  perda silenciosa = exposição a multa R$ 6k–1,5M.
Decision: Storage MVP em Fly volume; migra para Cloudflare R2 quando passar 50GB.
  Backup pg_dump diário + snapshot Fly volume → R2. Restore-test mensal em DB efêmero.
  Retenção 5 anos.
Consequences:
  + Auditoria preservada com backup testado
  + Custo trivial (~R$ 60/mês)
  + Migração futura para R2 sem refator (interface IRotuloBlobStorage)
  - Dependência de 4 serviços (Fly, GitHub, Cloudflare, Resend)
Arquivo: docs/adr/0012-backup-rotulos-storage.md (já criado em F0.5)
```

**ADR-0013 — Comprovante de Aprovação Interna do RT (não-substitui assinatura digital)**

```
Status: Accepted (2026-05-16)
Deciders: Felipe
Context: Suplementos e infantis exigem aprovação de Responsável Técnico (CRN). ICP-Brasil
  A1/A3 ou DocuSign requer integração complexa fora do escopo MVP. Mas precisamos de
  algum registro auditável de aprovação no MVP.
Decision: AprovacaoRtRotulo.ComprovanteAprovacaoInternaHash = SHA256(nome + crn +
  timestamp + rotuloId + secret_servidor). Secret servidor em Fly Secrets var
  Anvisa:AprovacaoSecret. Rotação não invalida hashes antigos (snapshot guarda valor).
  UI e disclaimer dizem honestamente "Comprovante de aprovação interna. Não é assinatura
  digital ICP-Brasil — disponível em versão futura."
Consequences:
  + Atende fluxo mínimo de RT no MVP
  + Auditoria interna (sistema sabe quem aprovou e quando)
  + Honestidade radical: nunca chamado de "assinatura digital"
  - Não tem validade jurídica de assinatura digital (limitação documentada)
  - Necessita F+1 com ICP-Brasil para validade plena
Q referenciada: Q28, Q30, P4 (este plano)
Arquivo: docs/adr/0013-comprovante-aprovacao-interna-rt.md (criar em F0.5 pendente)
```

### 13.2 Doc "Como rodar o MVP em produção" (D2)

Especificação completa em **Seção 11.4** acima. Arquivo final: `docs/operacao/como-rodar-mvp-producao.md`, escrito em F13.

### 13.3 FAQ Anvisa para usuário (D3 — esqueleto, conteúdo escrito em F13)

Arquivo: `docs/usuario/faq-anvisa.md`. 15 perguntas iniciais (expansível):

```markdown
# FAQ Anvisa — Perguntas frequentes de rotulagem

## Sobre a tabela nutricional

1. **Preciso colocar tabela nutricional em todos os meus produtos?**
   Não. O Art. 22 da RDC 429/2020 lista alimentos isentos (frutas in natura,
   especiarias, vinagre, água, café puro etc.). Para os demais, é obrigatória.

2. **A tabela precisa estar em qual modelo (tabular ou linear)?**
   Depende da área de rotulagem disponível. Tabular (Anexo XII IN 75/2020) é o padrão.
   Linear (Anexo XIII) é permitido em embalagens pequenas (<100cm²).

3. **Preciso colocar açúcar adicionado mesmo se uso só mel?**
   Sim. Mel é considerado açúcar adicionado pela RDC 429/2020. Declare em
   "Açúcares adicionados".

## Sobre a lupa frontal

4. **Quando aparece a lupa de "alto em sódio" no rótulo?**
   Quando o produto tem ≥600mg/100g (sólido) ou ≥300mg/100ml (líquido).
   Fonte: IN 75/2020 Anexo XV.

5. **O símbolo da lupa é o mesmo da Anvisa? Posso desenhar o meu?**
   Não. O símbolo é padronizado e está disponível em arquivos oficiais
   da Anvisa. EasyStok usa o SVG oficial automaticamente.

6. **Posso desativar a lupa se meu produto for muito gostoso?**
   Não. A lupa é obrigatória se o produto excede os limites. Para tirá-la,
   é preciso reformular (reduzir sódio/açúcar/gordura saturada).

## Sobre alérgenos

7. **Quais alérgenos preciso declarar?**
   A RDC 26/2015 lista 18: trigo, centeio, cevada, aveia, crustáceos, ovos,
   peixes, amendoim, soja, leite, amêndoa, avelã, castanha de caju, castanha
   do Pará, macadâmia, noz pecã, pistache, pinoli, e látex natural.

8. **Diferença entre "Contém" e "Pode conter"?**
   "Contém" = ingrediente intencional. "Pode conter" = contaminação cruzada
   possível na fabricação.

## Sobre claims

9. **Posso escrever "zero açúcar" se meu produto tem 0.4g/100g?**
   Sim. RDC 54/2012 define limite ≤0.5g/100g para "zero açúcar".

10. **"Light" e "diet" são a mesma coisa?**
    Não. "Light" exige redução de pelo menos 25% de um nutriente
    comparado ao similar. "Diet" exige eliminação ou redução drástica
    para uso por pessoa com restrição (diabético, hipertenso etc.).
    Fonte: RDC 54/2012.

## Sobre o rótulo

11. **Preciso colocar meu CNPJ no rótulo?**
    Sim. Identificação do fabricante é obrigatória conforme RDC 727/2022:
    razão social, CNPJ, endereço completo, SAC.

12. **E se eu mudar a receita? Preciso recolher os rótulos antigos?**
    Não há recall automático. Rótulos já impressos continuam válidos.
    Lotes novos usam a receita atualizada. EasyStok versiona cada rótulo
    com snapshot imutável para auditoria.

## Sobre o sistema EasyStok

13. **Quem é responsável pelos dados nutricionais no rótulo?**
    Você (a empresa). EasyStok facilita o cálculo e a impressão, mas a
    responsabilidade legal é do fabricante. Por isso oferecemos rastreabilidade
    da origem de cada ficha (TACO, fornecedor, manual, laboratório).

14. **Preciso de nutricionista (RT)?**
    Para alimentos comuns, não. Para suplementos e infantis, sim — exigência do
    CFN. EasyStok bloqueia publicação destes produtos até RT cadastrado aprovar.

15. **Vou exportar para o Mercosul. EasyStok faz o rótulo certo?**
    No MVP, apenas Brasil (RDC 429). Templates Mercosul/UE/FDA chegam em F+1.

## Glossário

- **Porção**: quantidade média do alimento consumida em uma ocasião por pessoas
  sadias maiores de 36 meses (RDC 429/2020).
- **%VD**: Percentual de Valor Diário — quanto da necessidade diária aquela
  porção atende, baseado em dieta de 2.000 kcal.
- **Lupa**: símbolo frontal obrigatório de "alto teor em..." (IN 75/2020).
- **Claim**: alegação nutricional ou de saúde no rótulo (RDC 54/2012).
- **Aditivo**: substância adicionada intencionalmente ao alimento com função
  tecnológica (RDC 24/2010).
- **TACO**: Tabela Brasileira de Composição de Alimentos (NEPA/UNICAMP).
- **RT**: Responsável Técnico (nutricionista registrado no CRN).
```

### 13.4 Guia de Troubleshooting (D4 — esqueleto, conteúdo em F13)

Arquivo: `docs/operacao/troubleshooting.md`.

```markdown
# Troubleshooting — Rotulagem Nutricional

## Cache não invalidou após mudar a ficha do insumo

Sintoma: Editei a ficha da Farinha Anaconda, mas o rótulo do Ravioli ainda mostra valor antigo.

Diagnóstico:
1. SSH no Fly: `fly ssh console -a easystok-app`
2. Verificar status: `psql ... -c "SELECT status FROM rotulagem.produto_nutricao_calculada WHERE produto_id='X';"`
3. Se `status='Stale'`: aguardar até 5min (job de recálculo) OU forçar via UseCase:
   `dotnet EasyStock.Api.dll recalcular-nutricao --produto-id X`
4. Se `status='Atual'` mas valor antigo: domain event não disparou. Verificar logs:
   `fly logs -a easystok-app | grep PerfilNutricionalAlteradoEvent`

Resolução:
- Forçar invalidação manual via comando admin.
- Se recorrente: bug no handler. Reportar em [Validar com Felipe].

## PDF saiu errado (layout quebrado, falta dado, fonte minúscula)

Sintoma: PDF gerado tem campo cortado, lupa fora de posição, ou cita norma errada.

Diagnóstico:
1. Verificar snapshot JSON da Rotulo:
   `psql ... -c "SELECT dados_snapshot_json FROM rotulagem.rotulo WHERE id='X';" | jq .`
2. Validar que `templateUsado.versaoHash` bate com versão atual do `ModeloRotulo`.
3. Re-renderizar manualmente:
   `dotnet EasyStock.Api.dll re-renderizar-rotulo --rotulo-id X --salvar-em /tmp/test.pdf`
4. Comparar com baseline em `tests/snapshots/Rotulagem/`.

Resolução:
- Se template mudou intencionalmente: aprovar nova baseline.
- Se regressão: reverter mudança do template via `git revert`.
- Validador de layout (Seção 4 / F6) deve ter pego — se não pegou, criar teste novo.

## Impressora Zebra não imprime

Checklist:
- [ ] Impressora ligada e papel carregado?
- [ ] USB conectado (ou IP responde a `ping <endereço>`)?
- [ ] `ConfiguracaoImpressora` correta na empresa (loja certa)?
- [ ] Driver USB instalado no servidor que envia ZPL?
- [ ] Logs: `fly logs -a easystok-app | grep ZebraZplAdapter`
- [ ] Tentar `fly ssh console` + `echo "^XA^FO50,50^A0N,50,50^FDTest^FS^XZ" > /dev/usb/lp0` (envio direto ZPL)

Se nada disso resolve: bug no adapter. Capturar log + reportar.

## RT aprovou mas o estado não mudou

Sintoma: RT clicou "Aprovar como responsável técnico", aparece toast verde, mas
o `Rotulo.status` continua `AguardandoAprovacaoRt`.

Diagnóstico:
1. Verificar que `AprovacaoRtRotulo` foi gravada:
   `psql ... -c "SELECT * FROM rotulagem.aprovacao_rt_rotulo WHERE rotulo_id='X';"`
2. Verificar que `secret_versao` bate com `Anvisa:AprovacaoSecret:Versao` atual.
3. Verificar logs: `fly logs | grep AprovarComoRt`

Resolução:
- Se `AprovacaoRtRotulo` existe mas `Rotulo.status` não mudou: handler de evento falhou.
  Re-executar manualmente: `dotnet EasyStock.Api.dll reprocessar-evento AprovacaoRtConcluida --rotulo-id X`
- Se `secret_versao` divergente: rotação de secret recente — RT precisa re-aprovar.

## Migration travou no deploy

Sintoma: `fly deploy` ficou em "Validating health checks" por >5min.

Diagnóstico:
1. `fly logs -a easystok-app` — procurar exception de migration.
2. `fly ssh console -a easystok-app -C "ps aux | grep dotnet"` — ver se migration ainda rodando.

Resolução:
- Se migration está OK mas demorada: aguardar (B1 é lento). Limite 10min antes de abortar.
- Se migration travou (timeout > 10min): SIGKILL + rollback.
- Migration deve ser dividida em pedaços menores (cada CREATE TABLE / INDEX em sua própria) e re-deployada.

## Backup falhou mas restore-test passou (ou vice-versa)

Sintoma: Email crítico chega dizendo que backup falhou OU restore-test não pode validar.

Resolução:
1. Verificar Actions tab: `https://github.com/felipe/easystok/actions`
2. Re-rodar manualmente: `gh workflow run backup-rotulos.yml`
3. Se persistir: validar secrets `R2_ACCESS_KEY` / `R2_SECRET_KEY` no repo settings.
4. Se R2 fora do ar: backup vai para fallback `easystok-backup-fallback` (mesmo R2, conta separada — [Validar com Felipe se configurar]).
```

---

## 14. Riscos (com mitigação ativa)

| # | Risco | Probabilidade | Impacto | Mitigação ativa | Critério de gatilho |
|---|---|---|---|---|---|
| R01 | Multa Anvisa por bug no validador de claims (falso negativo) | Média (validador novo, lógica complexa) | Catastrófico: R$ 6k–1,5M por rótulo + processo | (1) `ConformidadeValidatorTests` cobre 8 claims × 2 cenários cada; (2) Property-based testing FsCheck garante invariantes; (3) Snapshot test do PDF detecta mudança visual; (4) Toda nova claim requer aprovação manual de Felipe em PR antes de habilitar produção | Qualquer claim com falso negativo descoberto → bloqueio imediato via feature flag + correção em 24h |
| R02 | Cronograma estoura por trabalho Avanade ou família | Alta (solo dev part-time) | 2–4 semanas atraso | (1) Buffer 20% incluído (16 semanas calendário vs 11 líquidas); (2) Validação semanal com Thatiane a partir de F5; (3) Critério "pronto" objetivo impede deslize silencioso; (4) Plano alternativo por fase | Atraso >5 dias em qualquer fase → re-plan; atraso >10 dias → corte de escopo (mover feature para F+1) |
| R03 | Backup de rótulos corrompe silenciosamente | Baixa (pg_dump + R2 maduros) | Catastrófico: perda 5 anos auditoria = exposição legal | (1) Restore-test mensal automatizado em DB efêmero; (2) Validação smoke (PDF + snapshot JSON); (3) Alerta crítico via Resend ao admin; (4) Verificação de hash mensal em amostra 10% dos PDFs | Falha do restore-test 1 mês → Felipe investiga 48h; falha 2 meses → SEV-2, congela releases até resolver |
| R04 | Calculadora de Produção nunca mergeia (worktree esquecida) | Média | F5 modo Auto fica permanentemente pendente | (1) **Aceito como feature permanente**: modo Manual é first-class; (2) UI mostra ambos sem preferência; (3) Métricas de uso revelam se Auto é necessário | Após 6 meses sem merge: avaliar manter ou descontinuar modo Auto |
| R05 | pg_trgm não disponível em Fly Postgres | Baixa | F4 busca TACO lenta (~5s ILIKE em 600 linhas) | (1) Validar antes de F4 (`CREATE EXTENSION IF NOT EXISTS pg_trgm`); (2) Fallback ILIKE + índice expression; (3) 600 linhas é cardinalidade pequena | `CREATE EXTENSION` falha em Fly → ativar fallback ILIKE no `ImportadorTaco` antes de F4 produção |
| R06 | Zebra ZD230 não funciona com ZPL adapter | Baixa (Felipe tem o dispositivo) | F7 atrasa 1–2 semanas | (1) Teste físico em F7 com impressora real; (2) Fallback A4 multi-up sempre disponível; (3) Documentação ZPL pública e estável | Teste físico falha em F7 → seguir com A4 fallback no MVP, refator Zebra em F+1 |
| R07 | OCR de PDF de fornecedor (C2) tem qualidade ruim | Média (OCR sempre tem imprecisão) | Ficha errada = cálculo errado = rótulo errado | (1) Modal obrigatório "Confirmar/Editar" antes de salvar; (2) Origem `Fornecedor` + referência ao PDF original; (3) Validador inline (nutriente >0 e plausível); (4) Choice Azure Document Intelligence vs Tesseract no spike F9 | Taxa de erro >20% em batch real de 10 PDFs → mudar OCR provider ou desabilitar feature |
| R08 | Resend free tier insuficiente (3k emails/mês) | Baixa | Notificações começam a falhar silenciosamente | (1) Métrica de uso mensal de email; (2) Alerta quando passar 80%; (3) Upgrade Pro Resend ($20/mês) trivial | Passar 2.500 emails/mês → upgrade antes de chegar a 3k |
| R09 | Fly volume corrompe ou é deletado por erro humano | Baixa | Catastrófico: perda PDFs arquivados | (1) Backup diário Fly volume → R2; (2) Snapshot JSON espelhado em `Rotulo.dados_snapshot_json` no DB; (3) `fly.toml` com `auto_extend_size_threshold`; (4) Restore-test mensal valida R2 | Volume <20% espaço livre → expansão automática + alerta; corrupção por hash divergente → restore do R2 |
| R10 | LGPD pedido de esquecimento causa lentidão no app | Baixa (Casa da Babá tem 5 fornecedores) | App lento durante regeneração de snapshots | (1) Job background não bloqueia UI; (2) Lock por `fornecedor_id`; (3) Retry com backoff; (4) Pequeno número de rótulos afetados | Job demora >10min → split em batches; impacto sustained → janela noturna |
| R11 | Anthropic API fora do ar durante demo importante | Baixa | Feature C2 indisponível temporariamente | (1) Fallback "preencher manualmente" sempre disponível; (2) Cache `idempotency_keys` 24h; (3) Status monitor: status.anthropic.com | API down >5min → banner UI "Importação automática indisponível" |
| R12 | Felipe descobre bug em produção sem dashboard de métricas | Alta no MVP | Detecção atrasa horas em vez de minutos | (1) Logs estruturados JSON via ILogger; (2) Sentry opcional avaliado em F+1; (3) Email crítico Resend para SEV-1; (4) Cobertura ≥90% motor reduz bugs | MTTD SEV-1 > 1h durante 1ª semana → instalar Sentry imediatamente |
| R13 | Conflito de versão pacote Microsoft.Extensions.* | Média (recorrente em .NET 9) | Pre-commit lento ou builds quebram | (1) Pre-commit escopado a 1 projeto; (2) MongoDb projetos removidos em sprint dedicada (ADR-0001 follow-up); (3) Renovate em PR isolado | Build quebra após bump → reverter no PR, investigar conflito |
| R14 | Casa da Babá rejeita módulo por confusão UX | Média | Reset de F3-F8 com retrabalho UX | (1) Validação semanal Thatiane a partir de F5; (2) Microcopy filtro Thatiane (Seção 8.2); (3) Critério Go/No-Go inclui 5 dias staging; (4) Empty states ensinam | Thatiane "não entendi" 2× mesma tela → sprint UX antes de prosseguir |

---

## 15. Anexo — Histórico Q&A 1-38 (preservado para rastreabilidade)

> Decisões originais consolidadas inline nas seções 1-14. Este anexo preserva a sequência cronológica das decisões para auditoria futura. Onde o conteúdo das seções diverge da Q&A original, ver **15.2 Revisões à Q&A**.

**Bloco 1 — Modelagem**: Q1 (PerfilNutricional N:1+Fornecedor), Q2 (RotulagemReceita snapshot DTO via `IReceitaSnapshotProvider`), Q3 (DadosSnapshotJson exemplo completo — Seção 5.4), Q4 (4 versionamentos — Seção 5.3), Q5 (hash determinístico — Seção 5.2), Q6 (recomputação histórica via snapshot, política declarada).

**Bloco 2 — Persistência/deploy**: Q7 (schema isolada + seed via job idempotente), Q8 (`SeedVersion`), Q9 (`IGlobalCatalog` + NetArchTest), Q10 (tabela cache, não view).

**Bloco 3 — API/segurança**: Q11 (Idempotency-Key Stripe-style — Seção 6.4), Q12 (rate limit dia 1 — Seção 6.3), Q13 (`PublicSlug` UUIDv4 — Seção 4.3.11), Q14 (`PrivacidadeFornecedor` com 2 níveis — Seção 7.5).

**Bloco 4 — Cálculo**: Q15 (pureza real sem repo/CT/async — Seção 5.2), Q16 (fator linear documentado).

**Bloco 5 — UX superfícies**: Q17 (modal Simular no Lote REMOVIDO), Q18 (4 superfícies MVP — Seção 8.1).

**Bloco 6 — Persistência UX**: Q19 (servidor fonte de verdade + localStorage só cache otimista), Q20 (TTL 30d archive, 90d delete — Seção 4.3.14), Q21 (badge + pin discreto, não modal), Q22 (sem cor por padrão), Q23 (recálculo no blur + Ctrl+Enter), Q24 (pílula sticky + borda esquerda no mobile).

**Bloco 7 — Microcopy/onboarding/página pública**: Q25 (microcopy tabela — Seção 8.2), Q26 (sem onboarding, empty states ensinam — Seção 8.3), Q27 (wireframe da página pública + PDF server-side — Seção 8.5).

**Bloco 8 — Estados/features faltantes**: Q28 (4º modo `AguardandoAprovacaoRt` — Seção 8.1), Q29 (sugestão = nota livre no MVP), Q30 (RT no MVP simplificado), Q31 (tags + busca, sem pastas).

**Bloco 9 — Escopo/processo**: Q32 (corte radical aceito — F+1 detalhado), Q33 (CI billing pagar antes de F2), Q34 (plano B 14d — caminho aceito como feature permanente), Q35 (editor com validador hard-coded — Seção 9 F6), Q36 (YAGNI parcial: remove `RotuloIdioma` + `AlertaRegulatorio`).

**Bloco 10 — Padrões**: Q37 (PT-BR negócio + EN técnico, renomeação completa em ADR-0011), Q38 (refator `IClaudeStructuredExtractor` agora — feito em F0.5 commit 5e0aa622).

### 15.1 Pendências P1-P7 resolvidas neste documento

| P# | Pendência | Resolução |
|---|---|---|
| P1 | Renomear `AssinaturaTextoHash` → `ComprovanteAprovacaoInternaHash` em todas referências | Aplicado em Seções 4.3.12, 5.4 (snapshot), 8.4 jornadas (não mais referenciado pelo nome antigo), 12.1 (`AprovacaoRtTests`). Anexo Q28 mantém referência histórica como contexto. |
| P2 | Numeração ADRs | Confirmado: `docs/adr/` tem apenas 0001 (Mongo). Reservar 0002–0010 para ADRs futuros próximos. Usar **ADR-0011** (Nomenclatura), **ADR-0012** (Backup), **ADR-0013** (Comprovante RT — criar em F0.5 pendente). Sem colisão. |
| P3 | Cronograma honesto | Recalculado em Seção 9: **10–12 semanas líquidas / 16 semanas calendário com buffer 20%**. Início F0.5 pendente 2026-05-17, MVP go-live 2026-09-12. F0.5 tem 8 itens, paralelizáveis (Felipe billing/secrets em paralelo com código). |
| P4 | Secret RT | Resolvido em Seção 4.3.13 + ADR-0013: `Anvisa:AprovacaoSecret` em Fly Secrets (env var, não DB). Provisionado por comando admin `dotnet EasyStock.Api.dll seed-admin --gerar-secret-rt` no primeiro boot. Rotação via novo `Anvisa:AprovacaoSecret:Next` + transição 30 dias. Hashes antigos imutáveis (snapshot guarda). Auditoria em `AuditoriaRotacaoSecretRt` com motivo obrigatório. |
| P5 | Reverso Auto→Manual congela último cálculo | Resolvido em Seção 10.4: cria nova `FichaTecnicaProduto vN+1` com `eh_snapshot_congelado=true` + `motivo_alteracao` obrigatório. Copia valores do último `ProdutoNutricaoCalculada` para `NutricaoManualProdutoFinal`. Retenção 5 anos mínimo (RDC 275/2002). |
| P6 | Schema modo Manual | Resolvido em Seção 4.3.7: nova tabela `NutricaoManualProdutoFinal(produto_id PK, ...)` com RLS. Não viola princípio aditivo (tabela nova, não altera colunas em `Produto` além de adicionar `OrigemCalculoNutricional` com default). Decisão arquitetural justificada em Seção 10.1. |
| P7 | Doc "Como rodar o MVP em produção" | Resolvido em Seção 11.4 — template completo com 10 itens (ambiente Fly, banco, admin, ativar flag, logs, alertas, restore-test manual, SSH console, rollback, critérios de incidente). Arquivo final em `docs/operacao/como-rodar-mvp-producao.md`, escrito em F13. |

### 15.2 Revisões à Q&A (divergências justificadas)

**Q4 (4 versionamentos)** — divergência menor: a Q&A original listou `algorithmVersion`, `PerfilNutricional.VersaoId`, `ReceitaVersaoHash`, `ModeloRotulo.VersaoHash`. Este plano (Seção 5.3) mantém os 4 e adiciona explícita responsabilidade do `secret_versao` (campo em `AprovacaoRtRotulo`) como 5º versionamento auditável, decorrente de P4.

**Q17 (modal Simular no Lote removido)** — sem divergência. Link "Reformular este produto" implementado em F7 (Seção 9).

**Q30 (RT no MVP)** — divergência refinada: a Q&A dizia "MVP entrega comprovante simplificado". Este plano dá nome explícito (Comprovante de Aprovação Interna, ADR-0013) + secret servidor (P4) + disclaimer honesto na UI. Limite legal documentado.

**Q32 (corte radical de escopo)** — sem divergência. F+1 e F+2 expandidos em Seção 9.

**Q33 (CI billing)** — sem divergência. Workflows GHA drafts criados em F0.5 commit 4b018b39.

**Q38 (refator IClaudeStructuredExtractor)** — sem divergência. Feito em F0.5 commit 5e0aa622 na worktree `feat/claude-extractor-refactor`.

### 15.3 Premissas externas (Validar com Felipe antes de F1)

Lista exaustiva de assunções operacionais não confirmadas:

1. **Fly app name** = `easystok-app`. Se for diferente, corrigir em Seções 7.1, 11.3, 11.4, R09.
2. **Fly Postgres** = `easystok-pg-gru`. Idem.
3. **Felipe email admin** = `felipe@easystok.com.br`. Idem.
4. **Domínio público app** = `app.easystok.com.br`. Idem.
5. **pg_trgm** disponível em Fly Postgres — testar `CREATE EXTENSION IF NOT EXISTS pg_trgm` antes de F4.
6. **MassaProdRealistica** (script Bogus) existe em `EasyStock.TestHelpers` — se não, criar em F2.
7. **OCR provider C2**: Azure Document Intelligence (paga, melhor) vs Tesseract.NET (grátis) — escolher no spike F9.
8. **Avaliar Sentry** ou similar antes de produção (R12).
9. **GS1 prefixo** para EAN-13 — só relevante em F+1 (B6). Custo R$ ~2.000/ano para empresa registrar.
10. **Better Stack / Loki** para logs persistentes (Seção 11.4) — se nenhum, fica só Fly logs (24h).
11. **Email DNS** para Resend — Felipe precisa validar domínio (SPF + DKIM) antes de F12.
12. **Sub-domínio** `tracking.easystok.com.br` para contador anônimo de scans (F+1 página pública).

---

## 16. Fontes Regulatórias

**Normas Anvisa (seedeadas como `NormaRegulatoria` em F2):**
- [RDC 429/2020 — Rotulagem nutricional (PDF Anvisa)](https://bvsms.saude.gov.br/bvs/saudelegis/anvisa/2020/RDC_429_2020_.pdf)
- [IN 75/2020 — Requisitos técnicos (PDF Anvisa)](https://bvsms.saude.gov.br/bvs/saudelegis/anvisa//2020/IN%2075_2020_.pdf)
- [RDC 727/2022 — Rotulagem geral](https://www.gov.br/anvisa/pt-br/assuntos/alimentos/rotulagem)
- [RDC 26/2015 — Alérgenos (PDF Anvisa)](https://bvsms.saude.gov.br/bvs/saudelegis/anvisa/2015/rdc0026_26_06_2015.pdf)
- [RDC 24/2010 — Aditivos](https://www.gov.br/anvisa/pt-br/assuntos/alimentos/aditivos-alimentares)
- [RDC 269/2005 — IDR](https://www.gov.br/anvisa/pt-br/assuntos/alimentos/rotulagem)
- [RDC 54/2012 — Informação Nutricional Complementar (claims)](https://www.gov.br/anvisa/pt-br/assuntos/alimentos/rotulagem)
- [RDC 275/2002 — Boas Práticas de Fabricação](https://www.gov.br/anvisa/pt-br/assuntos/alimentos/boas-praticas)
- [Cartilha MPES RDC 429/2020 + IN 75/2020 (PDF)](https://mpes.mp.br/wp-content/uploads/2024/11/0114_Cartilha_RC3B3tulos_CADC_v2_2.pdf)

**Bases de dados:**
- [Tabela TACO 4ª ed. — NEPA/UNICAMP](https://nepa.unicamp.br/publicacoes/tabela-taco-excel/)
- [TACO CSV/JSON — github machine-learning-mocha/taco](https://github.com/machine-learning-mocha/taco)
- [TACO API REST (Netlify)](https://taco-api.netlify.app/)

**Concorrentes (benchmark de paridade):** PriceFy, FoodTrace, EasyLabel, Linx Food, ConsiNet, Bluesoft (GS1).

**ADRs do EasyStok:**
- [ADR-0001 MongoDB descartado](C:\rep\EasyStok\docs\adr\0001-mongo-discarded.md)
- [ADR-0011 Nomenclatura PT-BR](C:\rep\EasyStok\docs\adr\0011-nomenclatura-pt-br-rotulagem.md) (criado F0.5)
- [ADR-0012 Backup + Storage](C:\rep\EasyStok\docs\adr\0012-backup-rotulos-storage.md) (criado F0.5)
- [ADR-0013 Comprovante Aprovação Interna RT](C:\rep\EasyStok\docs\adr\0013-comprovante-aprovacao-interna-rt.md) (criar F0.5 pendente)

**Commits F0.5 executados:**
- `4b018b39` — chore(p-02-f0.5): setup do módulo Rotulagem Nutricional (ADRs, Husky, workflows GHA)
- `e3f70122` — fix(husky): pre-commit roda apenas EasyStock.ArchitectureTests com filter Architecture
- `5e0aa622` — refactor(ai): extrai bases compartilhadas para extração Claude (worktree `feat/claude-extractor-refactor`)

---

## Checklist de aceite final

- [x] Nenhuma decisão muda silenciosamente entre seções (revisado: caminho B aceito consistentemente; snapshots/cache consistente; microcopy unificada)
- [x] Toda feature do Catálogo A/B/C aparece em alguma fase (F0–F13, F+1, F+2)
- [x] Toda entidade tem RLS ou IGlobalCatalog explícito (Seção 4.3)
- [x] Toda entidade tem caso de uso real no MVP (`RotuloIdioma` e `AlertaRegulatorio` removidas)
- [x] Todo teste em Verificação tem código testável (Seção 12.1 mapeia tudo)
- [x] Cronograma fecha com part-time real (10–12 semanas líquidas / 16 calendário, Seção 9)
- [x] Todo "decidir depois" foi decidido (Premissas externas marcadas como `[Validar com Felipe antes de F1]`)
- [x] Microcopy passou pelo filtro Thatiane (Seção 8.2)
- [x] Cada risco tem mitigação ATIVA (Seção 14)
- [x] Documento abre fluxo F0.5 → F1 → ... sem "ah, mas como faz X?"
- [x] Solo dev consegue ler, fechar e começar a codar (cada fase tem critério "pronto" objetivo)
- [x] Outro dev sênior consegue dar continuidade (ADRs + schemas SQL completos + OpenAPI + código C# real)

**Documento pronto para execução.**








