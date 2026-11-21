using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.ValueObjects.Pix;

namespace EasyStock.Application.UseCases.Pedidos.Cobranca;

/// <summary>
/// Conta e texto do Pix direto (#2233) num lugar só, para a troca de forma e a cobrança da alteração da comanda
/// (#2684) gerarem o mesmo copia e cola. A cobrança guarda o saldo manual; o Pix pede o sinal da encomenda ou o saldo.
/// </summary>
public static class PixDiretoDoPedido
{
    public static decimal ValorDoPix(DateTime? agendadoParaEm, DateTime criadoEm, decimal total, decimal valor)
    {
        var sinal = PlanoPagamentoEncomenda.TemSinal(agendadoParaEm, criadoEm)
            ? PlanoPagamentoEncomenda.SinalRestante(total, valor)
            : 0m;
        return sinal > 0m ? sinal : valor;
    }

    public static string CopiaECola(RecebedorPix recebedor, Guid pedidoId, decimal valorDoPix) =>
        BrCodePix.Estatico(recebedor, valorDoPix, BrCodePix.TxidDoPedido(pedidoId));

    /// <summary>Recebedor da loja do pedido; sem loja no pedido, o da única loja da empresa com chave.</summary>
    public static async Task<RecebedorPix?> RecebedorAsync(ILojaRepository? lojas, Guid empresaId, Guid? lojaId)
    {
        if (lojas is null) return null;
        if (lojaId is { } id)
            return (await lojas.GetByIdAsync(empresaId, id))?.ObterRecebedorPix();
        var comChave = (await lojas.GetByEmpresaAsync(empresaId))
            .Select(l => l.ObterRecebedorPix()).OfType<RecebedorPix>().ToList();
        return comChave.Count == 1 ? comChave[0] : null;
    }

    /// <summary>Mensagem ao cliente com o valor, o recebedor e o copia e cola da cobrança.</summary>
    public static string Texto(CobrancaPedido cobranca, RecebedorPix recebedor, decimal valorDoPix) =>
        AvisoCobrancaConversa.TextoPixDireto(valorDoPix, cobranca.Valor - valorDoPix, cobranca.PixCopiaCola!, recebedor.Nome);
}
