namespace EasyStock.Domain.Fiscal;

/// <summary>
/// Audit trail append-only do <see cref="NfeDocumento"/>. Cada transicao de
/// status gera uma linha aqui com tipo, usuario e dados livres em jsonb.
/// Espelha o padrao usado em <c>FaturaEvento</c> e <c>PedidoEvento</c>.
/// </summary>
public class NfeEvento
{
    public Guid Id { get; set; }
    public Guid NfeDocumentoId { get; set; }
    public NfeDocumento? NfeDocumento { get; set; }

    /// <summary>Tipo do evento: criado | enviado | autorizado | rejeitado | cancelado | inutilizado | erro_transiente.</summary>
    public string Tipo { get; set; } = null!;

    /// <summary>Detalhes livres em jsonb (payload SEFAZ, motivo, stacktrace transiente).</summary>
    public string? DadosJson { get; set; }

    public Guid? UsuarioId { get; set; }
    public string? UsuarioNome { get; set; }

    /// <summary>"web" | "mobile" | "worker" | "webhook" — origem da acao.</summary>
    public string? Origem { get; set; }

    /// <summary>
    /// Número do protocolo SEFAZ do evento (cancelamento, inutilização).
    /// Exemplo: protocolo do evento de cancelamento — distinto do ProtocoloAutorizacao da nota.
    /// NULL para eventos que não geram protocolo próprio (criado, enviado, erro_transiente).
    /// </summary>
    public string? ProtocoloEvento { get; set; }

    public DateTime OcorridoEm { get; set; }
}
