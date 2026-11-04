using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.UseCases.Operacao.Impressao;

namespace EasyStock.Application.Services.Notifications.Anexos;

/// <summary>
/// O canhoto da comanda (#2211): o mesmo modelo que a impressora recebe (S20), desenhado em PNG ou PDF. Lê o
/// <c>pedidoId</c> do payload; pedido de outra empresa ou inexistente não gera anexo.
/// </summary>
public sealed class ModeloAnexoCanhoto(MontarCanhotoUseCase montarCanhoto, IRenderizadorCanhoto renderizador)
    : IModeloAnexoNotificacao
{
    public const string CodigoModelo = "canhoto";

    public string Codigo => CodigoModelo;

    public async Task<AnexoGerado?> GerarAsync(
        Guid empresaId, IDictionary<string, object?> variaveis, FormatoAnexo formato, CancellationToken ct = default)
    {
        if (!variaveis.TryGetValue("pedidoId", out var valor) || !Guid.TryParse(valor?.ToString(), out var pedidoId))
            return null;

        var canhoto = await montarCanhoto.ExecuteAsync(new MontarCanhotoInput(empresaId, pedidoId), ct);
        return canhoto is null ? null : await renderizador.RenderizarAsync(canhoto, formato, ct);
    }
}
