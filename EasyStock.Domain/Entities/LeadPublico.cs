using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities;

/// <summary>
/// Lead capturado na landing publica antes (ou independente) de virar Empresa.
/// NAO e multi-tenant: existe fora do contexto autenticado. Guarda IP/UserAgent
/// para auditoria anti-spam e correlacao posterior com Empresa.
/// </summary>
public sealed class LeadPublico
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = null!;
    public EmailAddress Email { get; set; } = null!;
    public Telefone? Telefone { get; set; }
    public string? Empresa { get; set; }
    public string? Mensagem { get; set; }
    public OrigemLead Origem { get; set; }
    public string? TipoNegocio { get; set; }
    public bool ReceberNewsletter { get; set; }
    public bool ConsentimentoLgpd { get; set; }
    public string? IpOrigem { get; set; }
    public string? UserAgent { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? ProcessadoEm { get; set; }

    /// <summary>FK opcional para AdminTicket gerado a partir do lead (preenchido em P1+).</summary>
    public Guid? TicketGeradoId { get; set; }

    /// <summary>FK opcional para Empresa criada se o lead virou cadastro completo.</summary>
    public Guid? EmpresaCriadaId { get; set; }

    public static LeadPublico Criar(
        string nome,
        EmailAddress email,
        OrigemLead origem,
        bool consentimentoLgpd,
        Telefone? telefone = null,
        string? empresa = null,
        string? mensagem = null,
        string? tipoNegocio = null,
        bool receberNewsletter = false,
        string? ipOrigem = null,
        string? userAgent = null,
        string? utmSource = null,
        string? utmMedium = null,
        string? utmCampaign = null)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("Nome do lead e obrigatorio.", nameof(nome));
        if (!consentimentoLgpd)
            throw new ArgumentException("Consentimento LGPD e obrigatorio.", nameof(consentimentoLgpd));

        return new LeadPublico
        {
            Id = Guid.NewGuid(),
            Nome = nome.Trim(),
            Email = email,
            Telefone = telefone,
            Empresa = empresa?.Trim(),
            Mensagem = mensagem?.Trim(),
            Origem = origem,
            TipoNegocio = tipoNegocio?.Trim(),
            ReceberNewsletter = receberNewsletter,
            ConsentimentoLgpd = consentimentoLgpd,
            IpOrigem = ipOrigem,
            UserAgent = userAgent,
            UtmSource = utmSource?.Trim(),
            UtmMedium = utmMedium?.Trim(),
            UtmCampaign = utmCampaign?.Trim(),
            CriadoEm = DateTime.UtcNow
        };
    }

    public void MarcarProcessado(Guid? empresaCriadaId = null, Guid? ticketGeradoId = null)
    {
        ProcessadoEm = DateTime.UtcNow;
        if (empresaCriadaId.HasValue) EmpresaCriadaId = empresaCriadaId;
        if (ticketGeradoId.HasValue) TicketGeradoId = ticketGeradoId;
    }
}
