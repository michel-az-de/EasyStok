using EasyStock.Application.UseCases.Banners;

namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Leitura do console de recebimento (#875): resumo (elegíveis/viram/confirmaram), série
/// diária e log paginado de interações (join a <c>Usuario</c>). Cross-tenant — só o
/// AdminBannersController (SuperAdmin) consome. <c>banner_confirmacoes</c> e <c>Usuario</c>
/// são globais (sem EmpresaId/RLS), então a leitura enxerga todos os lojistas.
/// </summary>
public interface IBannerRecebimentoQuery
{
    /// <summary>Monta o read model do aviso; <c>null</c> se o aviso não existe.</summary>
    Task<BannerRecebimentoReadModel?> ObterAsync(
        Guid bannerId, int page, int pageSize, string? tipo, string? busca, CancellationToken ct = default);

    /// <summary>E-mail completo de um usuário (revelação sob demanda; auditoria fica no use case).</summary>
    Task<string?> ObterEmailUsuarioAsync(Guid usuarioId, CancellationToken ct = default);
}
