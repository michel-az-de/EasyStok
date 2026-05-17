using System.ComponentModel.DataAnnotations;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Fiscal;

namespace EasyStock.Application.UseCases.Fiscal.ConsultarNfe;

public sealed record ConsultarNfeQuery(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid NfeId) : ICommand;

public sealed record ConsultarNfeResult(
    Guid Id,
    Guid EmpresaId,
    Guid PedidoId,
    string Modelo,
    short Serie,
    long Numero,
    string? ChaveAcesso,
    StatusNfe Status,
    string? ProtocoloAutorizacao,
    DateTime? DataAutorizacao,
    string? MotivoRejeicao,
    string? DanfeUrl,
    decimal TotalNota,
    DateTime CriadoEm,
    DateTime AlteradoEm,
    IReadOnlyList<ConsultarNfeItemDto> Itens,
    IReadOnlyList<ConsultarNfeEventoDto> Eventos);

public sealed record ConsultarNfeItemDto(
    Guid Id,
    int Ordem,
    string NomeSnapshot,
    decimal Quantidade,
    decimal PrecoUnitario,
    string Unidade,
    string? Ncm,
    string? Cfop,
    string? CstOuCsosn);

public sealed record ConsultarNfeEventoDto(
    Guid Id,
    string Tipo,
    DateTime OcorridoEm,
    string? UsuarioNome,
    string? Origem,
    string? DadosJson);
