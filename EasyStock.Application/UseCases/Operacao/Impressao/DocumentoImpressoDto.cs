using EasyStock.Domain.Entities.Operacao;

namespace EasyStock.Application.UseCases.Operacao.Impressao;

public sealed record DocumentoImpressoDto(Guid Id, Guid PedidoId, Guid? OriginalId, string? Documento,
    string? Modelo, string? Codigo, string? Perfil, string? Versao, string? Hash, Guid? UsuarioId,
    string Status, DateTime CriadaEm, DateTime? ImpressaEm)
{
    public static DocumentoImpressoDto De(ImpressaoPendente i) => new(i.Id, i.PedidoId, i.OriginalId,
        i.Documento, i.Modelo, i.Codigo, i.Perfil, i.Versao, i.Hash, i.UsuarioId,
        i.Status.ToString().ToLowerInvariant(), i.CriadaEm, i.ImpressaEm);
}
