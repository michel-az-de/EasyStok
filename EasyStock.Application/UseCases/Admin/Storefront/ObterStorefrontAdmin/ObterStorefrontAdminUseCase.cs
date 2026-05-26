using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Admin.Storefront.ObterStorefrontAdmin;

public sealed record ObterStorefrontAdminCommand(Guid Id) : ICommand;

/// <summary>
/// Detalhe completo de Storefront para o admin — inclui campos negócio, branding,
/// fiscal e nome da empresa associada (resolvido via IEmpresaRepository).
/// </summary>
public sealed record StorefrontAdminDetalhe(
    Guid Id,
    Guid EmpresaId,
    string EmpresaNome,
    string Slug,
    string TituloPublico,
    string? SubtituloPublico,
    string? DominioCustom,
    string? LogoUrl,
    string? CorPrimaria,
    string? WhatsappPedidos,
    decimal PedidoMinimoEntrega,
    decimal? FreteGratisAcima,
    string? MensagemForaArea,
    string ModeloFiscal,
    bool NfeAutomaticaHabilitada,
    Guid? LojaPadraoId,
    bool Ativo,
    int CardapioCount,
    DateTime CriadoEm,
    DateTime AlteradoEm);

public class ObterStorefrontAdminUseCase(
    IStorefrontRepository storefrontRepository,
    IEmpresaRepository empresaRepository,
    ICardapioItemRepository cardapioRepository)
    : IUseCase<ObterStorefrontAdminCommand, StorefrontAdminDetalhe>
{
    public async Task<StorefrontAdminDetalhe> ExecuteAsync(ObterStorefrontAdminCommand command)
    {
        var s = await storefrontRepository.GetByIdAsync(command.Id)
            ?? throw new StorefrontNaoEncontradoException();

        var empresa = await empresaRepository.GetByIdAsync(s.EmpresaId);
        var nomeEmpresa = empresa?.Nome ?? "(empresa removida)";
        var count = await cardapioRepository.ContarPorStorefrontAsync(s.Id);

        return new StorefrontAdminDetalhe(
            s.Id,
            s.EmpresaId,
            nomeEmpresa,
            s.Slug,
            s.TituloPublico,
            s.SubtituloPublico,
            s.DominioCustom,
            s.LogoUrl,
            s.CorPrimaria,
            s.WhatsappPedidos,
            s.PedidoMinimoEntrega,
            s.FreteGratisAcima,
            s.MensagemForaArea,
            s.ModeloFiscal,
            s.NfeAutomaticaHabilitada,
            s.LojaPadraoId,
            s.Ativo,
            count,
            s.CriadoEm,
            s.AlteradoEm);
    }
}
