namespace EasyStock.Domain.Entities;

/// <summary>
/// Audit trail de eventos de uma <see cref="Fatura"/>. Padrao analogo a
/// <see cref="TicketHistorico"/>: cada operacao registra um evento com
/// valor antes/depois, autor e payload JSON livre.
/// </summary>
public class FaturaEvento
{
    public Guid Id { get; set; }
    public Guid FaturaId { get; set; }
    public TipoEventoFatura Tipo { get; set; }
    public string? ValorAntes { get; set; }
    public string? ValorDepois { get; set; }
    public string? MetadadosJson { get; set; }
    public Guid? UsuarioId { get; set; }
    public string? UsuarioNome { get; set; }
    public string? Origem { get; set; } // "web" | "api" | "job" | "webhook"
    public DateTime OcorridoEm { get; set; }

    public Fatura? Fatura { get; set; }

    public static FaturaEvento Criar(
        Guid faturaId,
        TipoEventoFatura tipo,
        Guid? usuarioId = null,
        string? usuarioNome = null,
        string? valorAntes = null,
        string? valorDepois = null,
        string? metadadosJson = null,
        string? origem = "api")
    {
        return new FaturaEvento
        {
            Id = Guid.NewGuid(),
            FaturaId = faturaId,
            Tipo = tipo,
            UsuarioId = usuarioId,
            UsuarioNome = usuarioNome,
            ValorAntes = valorAntes,
            ValorDepois = valorDepois,
            MetadadosJson = metadadosJson,
            Origem = origem,
            OcorridoEm = DateTime.UtcNow
        };
    }
}
