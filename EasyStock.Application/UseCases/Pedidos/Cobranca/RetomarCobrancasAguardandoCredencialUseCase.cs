using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;

namespace EasyStock.Application.UseCases.Pedidos.Cobranca;

/// <summary>
/// I3.2 (#1688): quando a credencial do Mercado Pago volta, as cobranças que aguardavam por ela e são da
/// conta dela voltam à varredura do <c>CobrancaPedidoJob</c>. Roda no começo de cada rodada. Sem credencial,
/// ou com o provedor fora do ar, ninguém volta: a próxima rodada tenta de novo.
/// </summary>
public sealed class RetomarCobrancasAguardandoCredencialUseCase(
    IMercadoPagoClient mercadoPagoClient,
    ICobrancaPedidoRepository cobrancaRepository,
    ILogger<RetomarCobrancasAguardandoCredencialUseCase> logger)
{
    public async Task<int> ExecuteAsync(CancellationToken ct = default)
    {
        if (!await cobrancaRepository.ExisteAguardandoCredencialAsync(ct)) return 0;

        string contaAtual;
        try
        {
            contaAtual = await mercadoPagoClient.ObterContaRecebedoraAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return 0;
        }

        var retomadas = await cobrancaRepository.RetomarAguardandoCredencialAsync(contaAtual, ct);
        if (retomadas > 0)
            logger.LogInformation("Cobrancas de volta a varredura apos a credencial voltar: {Retomadas}", retomadas);
        return retomadas;
    }
}