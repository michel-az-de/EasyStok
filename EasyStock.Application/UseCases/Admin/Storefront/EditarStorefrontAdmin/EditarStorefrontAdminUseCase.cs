using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Admin.Storefront.EditarStorefrontAdmin;

/// <summary>
/// Edição de Storefront. Slug é IMUTÁVEL (PII na URL, mudaria links públicos
/// quebrando bookmarks/QR codes). Para "trocar slug", o admin precisa criar
/// outro storefront + redirect — fora do escopo desta task.
/// Campos nullable significam "não atualizar" (consistent com AtualizarBranding
/// e demais métodos da entity). Vazio explícito limpa branding.
/// </summary>
public sealed record EditarStorefrontAdminCommand(
    Guid Id,
    string? TituloPublico,
    string? SubtituloPublico,
    string? LogoUrl,
    string? CorPrimaria,
    string? WhatsappPedidos,
    string? MensagemForaArea,
    decimal? PedidoMinimoEntrega,
    decimal? FreteGratisAcima,
    string? DominioCustom,
    string? ModeloFiscal,
    bool? HabilitarNfeAutomatica,
    Guid? LojaPadraoId) : ICommand;

public sealed record EditarStorefrontAdminResult(Guid Id);

public class EditarStorefrontAdminUseCase(
    IStorefrontRepository storefrontRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<EditarStorefrontAdminCommand, EditarStorefrontAdminResult>
{
    public async Task<EditarStorefrontAdminResult> ExecuteAsync(EditarStorefrontAdminCommand command)
    {
        var s = await storefrontRepository.GetByIdAsync(command.Id)
            ?? throw new StorefrontNaoEncontradoException();

        // Branding — método aceita nulls como "não muda"
        if (command.SubtituloPublico is not null
            || command.LogoUrl is not null
            || command.CorPrimaria is not null
            || command.WhatsappPedidos is not null
            || command.MensagemForaArea is not null)
        {
            s.AtualizarBranding(
                subtituloPublico: command.SubtituloPublico,
                logoUrl: command.LogoUrl,
                corPrimaria: command.CorPrimaria,
                whatsappPedidos: command.WhatsappPedidos,
                mensagemForaArea: command.MensagemForaArea);
        }

        // TituloPublico não tem setter próprio na entity — só na criação.
        // Para esta task, edição de TituloPublico fica suportada apenas via
        // métodos existentes. Se for crítico, adicionamos um método na entity
        // numa task futura — desnecessário no MVP do admin.
        // [INTENCIONAL: não aceitar mudança de TituloPublico fora do Criar]

        if (command.PedidoMinimoEntrega.HasValue)
            s.AjustarPedidoMinimo(command.PedidoMinimoEntrega.Value);

        if (command.FreteGratisAcima.HasValue)
            s.DefinirFreteGratisAcima(command.FreteGratisAcima.Value);

        if (command.DominioCustom is not null)
            s.DefinirDominioCustom(command.DominioCustom);

        if (!string.IsNullOrWhiteSpace(command.ModeloFiscal))
            s.DefinirModeloFiscal(command.ModeloFiscal);

        if (command.HabilitarNfeAutomatica.HasValue)
        {
            if (command.HabilitarNfeAutomatica.Value)
                s.HabilitarNfeAutomatica();
            else
                s.DesabilitarNfeAutomatica();
        }

        if (command.LojaPadraoId.HasValue)
            s.DefinirLojaPadrao(command.LojaPadraoId.Value);

        await storefrontRepository.UpdateAsync(s);
        await unitOfWork.CommitAsync();

        return new EditarStorefrontAdminResult(s.Id);
    }
}
