using EasyStock.Domain.Exceptions;

namespace EasyStock.Domain.Entities.Pagamentos;

/// <summary>
/// Regra de roteamento configuravel: para um (<c>Metodo</c>, <c>Moeda</c>,
/// <c>Pais</c>) decide qual <c>Provedor</c> usar e em que ordem
/// (<see cref="Prioridade"/> menor = preferido).
///
/// <para>
/// <b>Granularidade</b>: <see cref="EmpresaId"/> NULL = regra global (default
/// pra todos os tenants). <see cref="EmpresaId"/> preenchida = override
/// especifico de tenant — tem precedencia sobre regras globais quando
/// existe pelo menos uma regra do tenant para o metodo.
/// </para>
///
/// <para>
/// <b>Multi-currency / multi-region (provisionado)</b>: hoje o EasyStok e
/// BR-only com <c>"BRL"</c>/<c>"BR"</c> hardcoded. Os campos <see cref="Moeda"/>
/// e <see cref="Pais"/> existem para o dia em que abrir multi-pais — algoritmo
/// do router ja filtra por eles.
/// </para>
///
/// <para>
/// <b>Multi-tenant</b>: <see cref="EmpresaId"/> nullable + pode ser global
/// → tipo isento do Global Query Filter (igual a <c>TenantFeatureFlag</c>);
/// repository filtra manualmente <c>EmpresaId == tenant OR EmpresaId IS NULL</c>.
/// </para>
/// </summary>
public class GatewayRoutingRule
{
    public Guid Id { get; set; }

    /// <summary>NULL = regra global. Preenchida = override de tenant.</summary>
    public Guid? EmpresaId { get; set; }

    public string Metodo { get; set; } = null!;
    public string Provedor { get; set; } = null!;

    /// <summary>Menor = preferido. Regras com mesma prioridade sao desempatadas pelo Id.</summary>
    public int Prioridade { get; set; }

    public bool Ativo { get; set; } = true;

    /// <summary>"BRL" default. Provisionado para multi-currency.</summary>
    public string Moeda { get; set; } = "BRL";

    /// <summary>"BR" default. Provisionado para multi-region.</summary>
    public string Pais { get; set; } = "BR";

    /// <summary>Faixa de valor opcional — em centavos, evita problemas de precisao decimal.</summary>
    public long? ValorMinimoCentavos { get; set; }
    public long? ValorMaximoCentavos { get; set; }

    /// <summary>Soft-rules em jsonb — ex: <c>{"min_success_rate":0.85,"max_p95_ms":3000}</c>. Lidas pelo router em P1.</summary>
    public string? RegrasJson { get; set; }

    public DateTime CriadoEm { get; set; }
    public DateTime AtualizadoEm { get; set; }

    /// <summary>RowVersion (xmin) — concorrencia otimista no Postgres.</summary>
    public uint Versao { get; set; }

    private GatewayRoutingRule() { }

    public static GatewayRoutingRule Criar(
        string metodo,
        string provedor,
        int prioridade,
        Guid? empresaId = null,
        string moeda = "BRL",
        string pais = "BR",
        long? valorMinimoCentavos = null,
        long? valorMaximoCentavos = null,
        string? regrasJson = null,
        bool ativo = true)
    {
        if (string.IsNullOrWhiteSpace(metodo))
            throw new RegraDeDominioVioladaException("Metodo e obrigatorio em GatewayRoutingRule.");
        if (string.IsNullOrWhiteSpace(provedor))
            throw new RegraDeDominioVioladaException("Provedor e obrigatorio em GatewayRoutingRule.");
        if (prioridade < 0)
            throw new RegraDeDominioVioladaException("Prioridade deve ser >= 0.");
        if (valorMinimoCentavos.HasValue && valorMaximoCentavos.HasValue
            && valorMinimoCentavos.Value > valorMaximoCentavos.Value)
            throw new RegraDeDominioVioladaException("ValorMinimo nao pode ser maior que ValorMaximo.");

        var agora = DateTime.UtcNow;
        return new GatewayRoutingRule
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Metodo = metodo.Trim().ToLowerInvariant(),
            Provedor = provedor.Trim(),
            Prioridade = prioridade,
            Ativo = ativo,
            Moeda = string.IsNullOrWhiteSpace(moeda) ? "BRL" : moeda.Trim().ToUpperInvariant(),
            Pais = string.IsNullOrWhiteSpace(pais) ? "BR" : pais.Trim().ToUpperInvariant(),
            ValorMinimoCentavos = valorMinimoCentavos,
            ValorMaximoCentavos = valorMaximoCentavos,
            RegrasJson = string.IsNullOrWhiteSpace(regrasJson) ? null : regrasJson,
            CriadoEm = agora,
            AtualizadoEm = agora
        };
    }

    public void Atualizar(
        int? prioridade = null,
        bool? ativo = null,
        long? valorMinimoCentavos = null,
        long? valorMaximoCentavos = null,
        string? regrasJson = null)
    {
        if (prioridade.HasValue)
        {
            if (prioridade.Value < 0)
                throw new RegraDeDominioVioladaException("Prioridade deve ser >= 0.");
            Prioridade = prioridade.Value;
        }
        if (ativo.HasValue) Ativo = ativo.Value;
        if (valorMinimoCentavos.HasValue) ValorMinimoCentavos = valorMinimoCentavos;
        if (valorMaximoCentavos.HasValue) ValorMaximoCentavos = valorMaximoCentavos;
        if (regrasJson != null) RegrasJson = string.IsNullOrWhiteSpace(regrasJson) ? null : regrasJson;
        AtualizadoEm = DateTime.UtcNow;
    }

    public bool AtendeFaixaValor(long valorCentavos)
    {
        if (ValorMinimoCentavos.HasValue && valorCentavos < ValorMinimoCentavos.Value) return false;
        if (ValorMaximoCentavos.HasValue && valorCentavos > ValorMaximoCentavos.Value) return false;
        return true;
    }
}
