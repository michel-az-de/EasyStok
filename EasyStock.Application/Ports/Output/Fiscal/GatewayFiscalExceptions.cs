namespace EasyStock.Application.Ports.Output.Fiscal;

/// <summary>
/// Lançada quando o gateway está indisponível (timeout, 5xx persistente,
/// circuit breaker aberto). Use cases tratam esta exception entrando em
/// contingência offline (tpEmis=9). Retry de 4xx é desencorajado.
/// </summary>
public class FocusUnreachableException : Exception
{
    public FocusUnreachableException(string message) : base(message) { }
    public FocusUnreachableException(string message, Exception inner) : base(message, inner) { }
}

public sealed class GatewayFiscalSemCredencialException : Exception
{
    public Guid EmpresaId { get; }

    public GatewayFiscalSemCredencialException(Guid empresaId)
        : base($"Empresa {empresaId} sem credencial fiscal cadastrada (token Focus + CSC).")
    {
        EmpresaId = empresaId;
    }
}

public sealed class GatewayFiscalRespostaInesperadaException : Exception
{
    public GatewayFiscalRespostaInesperadaException(string message) : base(message) { }
    public GatewayFiscalRespostaInesperadaException(string message, Exception inner) : base(message, inner) { }
}

public sealed class CertificadoA1IndisponivelException : Exception
{
    public Guid EmpresaId { get; }

    public CertificadoA1IndisponivelException(Guid empresaId, string motivo)
        : base($"Certificado A1 indisponivel para empresa {empresaId}: {motivo}")
    {
        EmpresaId = empresaId;
    }
}

public sealed class CredencialFiscalAusenteException : Exception
{
    public Guid EmpresaId { get; }

    public CredencialFiscalAusenteException(Guid empresaId)
        : base($"Credencial Focus NFe ausente ou expirada para empresa {empresaId}.")
    {
        EmpresaId = empresaId;
    }
}
