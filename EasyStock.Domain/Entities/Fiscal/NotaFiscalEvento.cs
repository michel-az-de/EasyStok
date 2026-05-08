using System;

namespace EasyStock.Domain.Entities.Fiscal;

/// <summary>
/// Evento de auditoria registrando uma transição de estado da NotaFiscal
/// (autorização, contingência, cancelamento, etc.). Permite reconstruir
/// a trajetória completa de qualquer documento fiscal.
/// </summary>
public sealed class NotaFiscalEvento
{
    public Guid Id { get; private set; }
    public Guid NotaFiscalId { get; private set; }
    public Guid EmpresaId { get; private set; }
    public string Tipo { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public string? XmlPayload { get; private set; }
    public Guid? UsuarioId { get; private set; }
    public string? Origem { get; private set; }
    public DateTime OcorridoEm { get; private set; }
    public string? CorrelationId { get; private set; }

    private NotaFiscalEvento() { }

    public static NotaFiscalEvento Criar(
        Guid notaFiscalId,
        Guid empresaId,
        string tipo,
        string payloadJson,
        Guid? usuarioId = null,
        string? origem = null,
        string? correlationId = null,
        string? xmlPayload = null)
    {
        if (notaFiscalId == Guid.Empty)
            throw new ArgumentException("NotaFiscalId é obrigatório.", nameof(notaFiscalId));
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId é obrigatório.", nameof(empresaId));
        if (string.IsNullOrWhiteSpace(tipo))
            throw new ArgumentException("Tipo é obrigatório.", nameof(tipo));
        if (string.IsNullOrWhiteSpace(payloadJson))
            payloadJson = "{}";

        return new NotaFiscalEvento
        {
            Id = Guid.NewGuid(),
            NotaFiscalId = notaFiscalId,
            EmpresaId = empresaId,
            Tipo = tipo,
            PayloadJson = payloadJson,
            XmlPayload = xmlPayload,
            UsuarioId = usuarioId,
            Origem = origem,
            OcorridoEm = DateTime.UtcNow,
            CorrelationId = correlationId,
        };
    }
}
