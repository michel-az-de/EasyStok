using EasyStock.Domain.Enums.Pagamentos;

namespace EasyStock.Domain.Entities.Pagamentos;

/// <summary>
/// Snapshot persistido de saude por gateway. Uma linha por <see cref="Provedor"/>
/// (PK logico). Atualizada por flush periodico (<c>GatewayHealthFlushBackgroundService</c>
/// em P1) — fonte da verdade da decisao de routing fica em-memoria
/// (<c>InMemoryGatewayHealthStore</c>); a tabela serve para audit e dashboards.
///
/// <para>
/// <b>Sem multi-tenant</b>: saude de gateway e GLOBAL (Stripe atende todos os
/// tenants igualmente, nao faz sentido ter snapshot por empresa). Tipo isento
/// do Global Query Filter (sem coluna <c>EmpresaId</c>).
/// </para>
///
/// <para>
/// <b>Em P0</b>: a tabela e criada vazia. Decorator de gateways apenas loga
/// metricas OTel; o store em memoria e o flush sao introduzidos em P1.
/// </para>
/// </summary>
public class GatewayHealthSnapshot
{
    /// <summary>"EfiPix" | "EfiBoleto" | "Stripe" | "MercadoPago" | "Manual". PK logico.</summary>
    public string Provedor { get; set; } = null!;

    public EstadoSaudeGateway Estado { get; set; } = EstadoSaudeGateway.Saudavel;

    public DateTime? JanelaInicioEm { get; set; }
    public DateTime? JanelaFimEm { get; set; }

    public int TotalAttempts { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int TimeoutCount { get; set; }
    public int RateLimitCount { get; set; }

    public int LatenciaP50Ms { get; set; }
    public int LatenciaP95Ms { get; set; }

    /// <summary>Quando o circuit suspende, ate quando ficar fora do roteamento.</summary>
    public DateTime? SuspensoAte { get; set; }

    /// <summary>Quando rodou o ultimo canary apos voltar de Suspenso.</summary>
    public DateTime? UltimoCanaryEm { get; set; }

    public string? UltimoErro { get; set; }
    public DateTime? UltimoErroEm { get; set; }
    public DateTime? UltimoSucessoEm { get; set; }

    public DateTime AtualizadoEm { get; set; }

    private GatewayHealthSnapshot() { }

    public static GatewayHealthSnapshot Inicial(string provedor)
    {
        if (string.IsNullOrWhiteSpace(provedor))
            throw new ArgumentException("Provedor e obrigatorio.", nameof(provedor));

        return new GatewayHealthSnapshot
        {
            Provedor = provedor.Trim(),
            Estado = EstadoSaudeGateway.Saudavel,
            AtualizadoEm = DateTime.UtcNow
        };
    }
}
