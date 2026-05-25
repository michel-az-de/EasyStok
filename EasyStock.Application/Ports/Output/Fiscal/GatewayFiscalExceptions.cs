namespace EasyStock.Application.Ports.Output.Fiscal;

/// <summary>Exception base para falhas do gateway fiscal. Use as subclasses para distinguir tratamento no use case.</summary>
public abstract class GatewayFiscalException : Exception
{
    protected GatewayFiscalException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Falha transiente: 5xx, timeout, falha de rede, circuit breaker aberto.
/// Use case deve marcar a NfeDocumento como <c>FalhaTransiente</c> e deixar
/// o job de contingencia reprocessar.
/// </summary>
public sealed class GatewayFiscalTransienteException : GatewayFiscalException
{
    public GatewayFiscalTransienteException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// SEFAZ rejeitou explicitamente (com codigo de rejeicao). Use case deve marcar
/// a NfeDocumento como <c>Rejeitada</c> com o <see cref="Motivo"/>.
/// Codigos comuns: 539 (duplicidade), 204 (numero nao permitido), etc.
/// </summary>
public sealed class GatewayFiscalRejeitadaException : GatewayFiscalException
{
    public string? Codigo { get; }
    public string Motivo { get; }

    public GatewayFiscalRejeitadaException(string motivo, string? codigo = null, Exception? inner = null)
        : base($"SEFAZ rejeitou: {codigo ?? "?"} - {motivo}", inner)
    {
        Codigo = codigo;
        Motivo = motivo;
    }
}

/// <summary>
/// Credencial invalida (401/403 do gateway) ou token expirado. Use case nao
/// deve retry — admin do tenant precisa atualizar credencial.
/// </summary>
public sealed class GatewayFiscalCredencialException : GatewayFiscalException
{
    public GatewayFiscalCredencialException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// SEFAZ denegou (situacao do contribuinte impede emissao — irregularidade fiscal).
/// Diferente de Rejeitada: denegada e nota "perdida" (numero ja consumido pela SEFAZ).
/// </summary>
public sealed class GatewayFiscalDenegadaException : GatewayFiscalException
{
    public string Motivo { get; }

    public GatewayFiscalDenegadaException(string motivo, Exception? inner = null)
        : base($"SEFAZ denegou: {motivo}", inner)
    {
        Motivo = motivo;
    }
}
