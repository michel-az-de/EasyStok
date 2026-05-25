using EasyStock.Domain.Fiscal;

namespace EasyStock.Api.Models.Fiscal;

/// <summary>
/// Resposta de operacoes que retornam estado atual da NFC-e.
/// </summary>
public sealed class NfeResponse
{
    public Guid Id { get; init; }
    public string? ChaveAcesso { get; init; }
    public StatusNfe Status { get; init; }
    public string Modelo { get; init; } = null!;
    public short Serie { get; init; }
    public long Numero { get; init; }
    public string? ProtocoloAutorizacao { get; init; }
    public DateTime? DataAutorizacao { get; init; }
    public string? MotivoRejeicao { get; init; }
    public string? DanfeUrl { get; init; }
    public decimal TotalNota { get; init; }
    public DateTime CriadoEm { get; init; }
    public DateTime AlteradoEm { get; init; }
}

public sealed class CancelarNfeResponse
{
    public Guid Id { get; init; }
    public StatusNfe Status { get; init; }
    public string? ProtocoloEvento { get; init; }
}

public sealed class InutilizarNumeracaoResponse
{
    public string ProtocoloEvento { get; init; } = null!;
    public DateTime DataInutilizacao { get; init; }
}

public sealed class NfeListItemResponse
{
    public Guid Id { get; init; }
    public string? ChaveAcesso { get; init; }
    public StatusNfe Status { get; init; }
    public short Serie { get; init; }
    public long Numero { get; init; }
    public decimal TotalNota { get; init; }
    public DateTime CriadoEm { get; init; }
    public DateTime? DataAutorizacao { get; init; }
}

public sealed class NfeDetalheResponse
{
    public NfeResponse Nfe { get; init; } = null!;
    public List<NfeItemResponse> Itens { get; init; } = new();
    public List<NfeEventoResponse> Eventos { get; init; } = new();
}

public sealed class NfeItemResponse
{
    public Guid Id { get; init; }
    public int Ordem { get; init; }
    public string NomeSnapshot { get; init; } = null!;
    public decimal Quantidade { get; init; }
    public decimal PrecoUnitario { get; init; }
    public string Unidade { get; init; } = null!;
    public string? Ncm { get; init; }
    public string? Cfop { get; init; }
    public string? CstOuCsosn { get; init; }
}

public sealed class NfeEventoResponse
{
    public Guid Id { get; init; }
    public string Tipo { get; init; } = null!;
    public DateTime OcorridoEm { get; init; }
    public string? UsuarioNome { get; init; }
    public string? Origem { get; init; }
}
