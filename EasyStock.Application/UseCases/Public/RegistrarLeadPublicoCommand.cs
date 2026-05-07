using System.ComponentModel.DataAnnotations;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.Public;

public sealed record RegistrarLeadPublicoCommand(
    [property: Required, MaxLength(150)] string Nome,
    [property: Required, EmailAddress, MaxLength(255)] string Email,
    OrigemLead Origem,
    bool ConsentimentoLgpd,
    [property: MaxLength(32)] string? Telefone = null,
    [property: MaxLength(150)] string? Empresa = null,
    string? Mensagem = null,
    [property: MaxLength(80)] string? TipoNegocio = null,
    bool ReceberNewsletter = false,
    string? IpOrigem = null,
    string? UserAgent = null,
    string? UtmSource = null,
    string? UtmMedium = null,
    string? UtmCampaign = null,
    string? Honeypot = null) : ICommand;

public sealed record RegistrarLeadPublicoResult(Guid LeadId, bool DescartadoPorSpam);
