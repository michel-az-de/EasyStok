using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;

namespace EasyStock.Application.Services.Pedidos;

/// <summary>
/// #3190: o canhoto da Casa que a dona manda pelo WhatsApp do próprio celular, a partir do app. A conexão do sistema
/// (<see cref="CanhotoPedidoCliente"/>) fica em outro número; aqui não há envio, só a imagem e o registro de que ela
/// foi aberta para compartilhar. O pedido é achado pelo id do app, e só existe depois do sync.
/// </summary>
public sealed class CanhotoClienteDoApp(
    IPedidoRepository pedidos,
    IStorefrontRepository storefronts,
    IConfiguracaoAtendimentoRepository configuracoes,
    IRenderizadorResumoPedido renderizador,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    /// <summary>Mede intenção: o compartilhamento foi aberto no aparelho. Não prova que a cliente recebeu.</summary>
    public const string Marco = "canhoto_aberto_para_compartilhar";

    public sealed record Imagem(byte[] Conteudo, string ContentType);

    /// <summary>A imagem do canhoto, ou nulo se o pedido ainda não chegou ao ERP nessa empresa.</summary>
    public async Task<Imagem?> GerarAsync(Guid empresaId, string mobileOrderId, CancellationToken ct)
    {
        var pedido = await AcharAsync(empresaId, mobileOrderId);
        if (pedido is null) return null;

        var pago = await pedidos.ExisteEventoAsync(empresaId, pedido.Id, QuitacaoPedido.Marco, ct);
        var storefront = await storefronts.GetByEmpresaAsync(empresaId, ct);
        var fecho = (await configuracoes.GetOrDefaultAsync(empresaId)).FraseFechoCanhoto;
        var resumo = ResumoPedidoAtendimento.Montar(pedido, storefront?.TituloPublico, pago, fecho);
        var anexo = await renderizador.RenderizarAsync(resumo, ct);
        return new Imagem(anexo.Conteudo, anexo.ContentType);
    }

    /// <summary>Grava o marco uma vez por pedido. Falso se o pedido ainda não chegou ao ERP nessa empresa.</summary>
    public async Task<bool> RegistrarAbertoAsync(Guid empresaId, string mobileOrderId, CancellationToken ct)
    {
        var pedido = await AcharAsync(empresaId, mobileOrderId);
        if (pedido is null) return false;
        if (await pedidos.ExisteEventoAsync(empresaId, pedido.Id, Marco, ct)) return true;

        await pedidos.AddEventoAsync(new PedidoEvento
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Tipo = Marco, OcorridoEm = relogio.GetUtcNow().UtcDateTime,
            Origem = "mobile", Detalhes = "Canhoto aberto no app para compartilhar com a cliente.",
        });
        await unitOfWork.CommitAsync();
        return true;
    }

    private async Task<Pedido?> AcharAsync(Guid empresaId, string mobileOrderId)
    {
        if (string.IsNullOrWhiteSpace(mobileOrderId)) return null;
        var achado = await pedidos.FindByMobileOrderIdAsync(empresaId, mobileOrderId);
        return achado is null || achado.EmpresaId != empresaId ? null : await pedidos.GetByIdWithDetailsAsync(empresaId, achado.Id);
    }
}
