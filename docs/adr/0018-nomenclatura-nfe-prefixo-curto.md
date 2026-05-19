# ADR-0018 — Nomenclatura `Nfe*` em código vs `Nota Fiscal` em UI/REST

**Status:** Aceito (2026-05-17)
**Autores:** Felipe Azevedo
**Contexto do plano:** [`docs/plan/nota-fiscal/00-README.md`](../plan/nota-fiscal/00-README.md) — avanço da feature Nota Fiscal Eletrônica (NFC-e modelo 65).
**Relacionado:** [ADR-0011](0011-nomenclatura-pt-br-rotulagem.md) (PT-BR para substantivos de negócio), [incidente 2026-05-16](../dev/incidentes/2026-05-16-agentes-paralelos-trabalho-paralelo.md) (dual implementação `Nfe*` vs `NotaFiscal*`).

## Contexto

O módulo Fiscal foi implementado em duas frentes paralelas, descoordenadas (ver incidente referenciado):

- **Master** (commit `a1c27e28` em 2026-05-16, recuperado e estabilizado por sessões posteriores) — nomenclatura **`Nfe*`** (prefixo curto, derivado da sigla técnica usada pela SEFAZ): `NfeDocumento`, `NfeItem`, `NfeEvento`, `NfeRepository`, `EmitirNfceUseCase`, `NfeCertificadoA1Service`, `StatusNfe`, etc. Inclui 4 tabelas, 1 migration `20260507230251_AddNfeFundacao`, 6 use cases, adapter FocusNFe completo, 3 controllers, 5 relatórios fiscais.

- **PR #99** (`feature/nfce-domain-f1`, aberto desde 2026-05-08) — nomenclatura concorrente **`NotaFiscal*`** (substantivo PT-BR completo): `NotaFiscal`, `NotaFiscalItem`, `NotaFiscalPagamento`, `NotaFiscalEvento`, `NotaFiscalContador`, `NotaFiscalCertificadoA1`, `NotaFiscalInutilizacao`, `EmitirNotaFiscalConsumidorUseCase`, etc. 15063 inserts, 57 arquivos, modelo de dados mais rico (7 tabelas + enums tipados `ModeloDocumentoFiscal`, `TipoEmissao`, `AmbienteSefaz`, `FormaPagamentoFiscal`, `OrigemMercadoria`, `StatusInutilizacao`).

ADR-0011 (Aceito) define a regra geral do projeto: **PT-BR para substantivos de negócio + EN apenas para sufixos técnicos consagrados**. Pela letra da ADR-0011, a nomenclatura correta seria `NotaFiscal*` (PR #99) — não `Nfe*` (master).

Mas o **custo de refactorar master para `NotaFiscal*`** é alto:
- ~50 arquivos `Nfe*` espalhados em Domain/Application/Infra/Api/Tests
- Migration já aplicada em DB (renomear tabelas `nfe_*` → `notas_fiscais*` exige migration destrutiva)
- 5 relatórios fiscais já consomem o schema atual
- Adapter FocusNFe + webhook em uso
- Risco de regressão em código pré-produção
- 1-2 dias de trabalho dedicado só pra renomear, sem entregar valor novo

E o **custo de NÃO formalizar** é deixar a confusão entre as duas nomenclaturas como gatilho de dúvida em toda sessão futura.

## Decisão

**Adotar `Nfe*` em código + `Nota Fiscal` em UI PT-BR + `notas-fiscais` em rotas REST.** Estabelecer **exceção formal à ADR-0011** restrita ao módulo Fiscal.

### Regras

| Camada | Convenção | Exemplo |
|---|---|---|
| Domain entities/VOs | `Nfe*`, `Empresa*Fiscal` | `NfeDocumento`, `NfeItem`, `NfeEvento`, `EmpresaConfiguracaoFiscal` |
| Domain enums | `*Nfe` ou `*Fiscal` | `StatusNfe`, `RegimeTributario` |
| Application use cases | `*Nfe*`, `*Nfce*` (NFC-e modelo 65) | `EmitirNfceUseCase`, `CancelarNfeUseCase`, `InutilizarNumeracaoUseCase` |
| Application ports | `INfe*`, `IGateway*` | `INfeRepository`, `INfeCertificadoA1Service`, `IGatewayFiscal` |
| Infra (EF, Focus) | `Nfe*`, `FocusNFe*` | `NfeRepository`, `FocusNFeAdapter`, `FocusNFePayloadMapper` |
| Tabelas DB | `nfe_*`, `empresa_configuracao_fiscal` | `nfe_documentos`, `nfe_itens`, `nfe_eventos` |
| API rotas REST | `notas-fiscais`, `configuracao-fiscal`, `webhooks/focus-nfe` | `GET /api/notas-fiscais`, `POST /api/configuracao-fiscal/certificado` |
| API DTOs request/response | `Nfe*Request`, `Nfe*Response`, `Nfce*Request` | `EmitirNfceRequest`, `CancelarNfeRequest`, `NfeResponse`, `NfeDetalheResponse` |
| UI Web ERP (PT-BR) | "Nota Fiscal", "Notas Fiscais", "NFC-e" | Sidebar: "Notas Fiscais"; título de página: "Notas Fiscais Emitidas" |
| UI Admin (PT-BR) | "Configuração Fiscal", "Certificado Digital", "CSC (NFC-e)" | Wizard: "Configurar Empresa para Emitir NFC-e" |
| Microcopy (status, badges) | "Autorizada", "Cancelada", "Rejeitada", "Em contingência", "Aguardando autorização", "Inutilizada" | Tabela de status na listagem |
| Mensagens de erro (PT-BR) | Substantivo completo "Nota Fiscal" | "Nota fiscal não encontrada" (não "NFe não encontrada") |
| Documentação técnica | `Nfe*` ao se referir a classe/tabela; "Nota Fiscal" ao se referir a conceito de negócio | ADRs, plano docs/plan/nota-fiscal/, comentários inline |

### Por que `Nfe` (e não `Nfce`) como prefixo geral

`Nfe` cobre tanto NFC-e (modelo 65) quanto NF-e (modelo 55). Hoje o sistema só implementa NFC-e, mas a entidade `NfeDocumento` já tem propriedade `Modelo` preparada pra suportar 55 no futuro (F+2). Usar `Nfce*` como prefixo geral fecharia a porta para NF-e B2B. Use cases específicos de NFC-e mantêm sufixo `Nfce` (ex: `EmitirNfceUseCase`); use cases que valem para ambos modelos usam `Nfe` (ex: `CancelarNfeUseCase`, `ConsultarNfeUseCase`, `InutilizarNumeracaoUseCase`).

### Por que exceção à ADR-0011

`Nfe` e `NFC-e` são **siglas consagradas pela SEFAZ/Receita Federal** que entram no jargão de qualquer desenvolvedor brasileiro que toca fiscal. Análogo aos exemplos já permitidos em ADR-0011 (`RowVersion`, `Hash`, `Cache` — termos técnicos sem equivalente PT-BR natural). Escrever `NotaFiscalEletronica*` em código não traz ganho comunicacional sobre `Nfe*` para esse domínio específico, e custa caracteres em cada identificador.

A UI PT-BR resolve a expectativa do usuário final ("Nota Fiscal") — quem nunca vê os identificadores de código.

## Trade-offs explícitos

**Positivas:**
- Zero refactor de master (50+ arquivos preservados, 1 migration preservada, schema DB intacto)
- 1-2 dias economizados → vai pra entrega de UI (F2/F3 do plano)
- Coerência interna do módulo Fiscal (já 100% `Nfe*`)
- Compatibilidade preservada com webhooks FocusNFe já assinados (HMAC), DTOs já em uso

**Negativas:**
- Inconsistência com ADR-0011 (módulo Fiscal vira a 1ª exceção formalizada — abre porta para mais exceções no futuro)
- Desenvolvedor novo no projeto precisa lembrar que "no Fiscal a regra é diferente" (mitigado por este ADR + ArchitectureTest)
- Hipotético: caso a Receita Federal mude a sigla NFe no futuro (improvável), refactor seria necessário

## Verificação automática

Teste de arquitetura em `EasyStock.ArchitectureTests/FiscalArchitectureTests.cs` (a ser criado em F1.6 do plano):

```csharp
[Fact]
public void Entidades_de_dominio_Fiscal_devem_usar_prefixo_Nfe_ou_Empresa()
{
    var prefixosPermitidos = new[] { "Nfe", "Empresa", "Regime", "Status", "Ambiente" };

    var entidades = Types.InAssembly(typeof(NfeDocumento).Assembly)
        .That().ResideInNamespace("EasyStock.Domain.Fiscal")
        .GetTypes();

    foreach (var t in entidades)
    {
        var temPrefixoValido = prefixosPermitidos.Any(p => t.Name.StartsWith(p));
        temPrefixoValido.Should().BeTrue(
            $"tipo {t.Name} em EasyStock.Domain.Fiscal deve começar com Nfe/Empresa/Regime/Status/Ambiente (ADR-0018). " +
            "Se for um conceito novo, atualize esta lista ou o ADR.");
    }
}

[Fact]
public void Tabelas_fiscais_devem_usar_prefixo_nfe_ou_empresa()
{
    // Lê snapshot EF Core e valida nomes de tabela
    var tabelas = ObterNomesDeTabelaDoSnapshot()
        .Where(t => t.StartsWith("nfe_") || t.StartsWith("empresa_configuracao_fiscal"));

    tabelas.Should().NotBeEmpty("módulo Fiscal deve ter ao menos uma tabela mapeada (ADR-0018)");
}
```

## Consequências para PR #99

PR #99 (`feature/nfce-domain-f1`) é **superseded** por commit `a1c27e28` + recuperações posteriores em master. Aspectos do PR #99 que **valem ser retomados em fases futuras** (sem renomear, adaptando para `Nfe*`):

| Item do PR #99 | Quando incorporar | Como (mantendo `Nfe*`) |
|---|---|---|
| `NotaFiscalPagamento` (pagamentos múltiplos por nota) | F+1 | Criar `NfePagamento` entity + tabela `nfe_pagamentos` |
| `NotaFiscalContador` (controle de numeração por loja) | F+1 (quando 2ª loja entrar) | Criar `NfeContador` entity OU estender `EmpresaConfiguracaoFiscal` com `Series` jsonb |
| Enums tipados (`ModeloDocumentoFiscal`, `TipoEmissao`, `AmbienteSefaz`, etc.) | F1 oportunisticamente | Adicionar enums novos sem renomear o `StatusNfe` existente |
| `NotaFiscalInutilizacao` (entity dedicada) | F+2 | Hoje inutilização cria evento em `nfe_eventos` — entity separada só se ficar grande |
| `RenovacaoCertificadoA1Job` | F+1 | Job novo, sem refactor |
| Documentação detalhada do PR body | F0 | Anexar como referência no `docs/plan/nota-fiscal/00-README.md` |

PR #99 é fechado com comentário apontando para este ADR.

## Reversão

Para reverter (adotar `NotaFiscal*`):
1. Migration de rename: `nfe_documentos` → `notas_fiscais`, `nfe_itens` → `notas_fiscais_itens`, etc. (4 tabelas)
2. Renomear ~50 arquivos C# (refactor IDE)
3. Atualizar DI registrations, EF configurations
4. Reescrever este ADR
5. Atualizar ArchitectureTest

Custo estimado: 2-3 dias. **Não recomendado sem motivação forte** (ex: requisito legal novo, restrição de tooling, decisão estratégica de UI mudando jargão).

## Caminho futuro

- Considerar criar ADR meta para "como decidir exceções a ADR-0011" se mais módulos pedirem (ex: módulo de Logística pode querer `Nf*` vs "NotaTransporte").
- Avaliar se vale criar glossário em `docs/dev/glossario.md` mapeando sigla técnica ↔ termo PT-BR para todos os módulos.
