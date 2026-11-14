namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #2544: legenda do resumo do pedido mandado ao cliente para confirmar (S73). O link de pagamento (S74) só sai depois
/// de o cliente responder a esta mensagem; o código curto do pedido liga uma à outra.
/// </summary>
public static class ResumoPedidoAtendimento
{
    private const string Prefixo = "Resumo do pedido ";

    public static string Codigo(Guid pedidoId) => pedidoId.ToString("N")[..6].ToUpperInvariant();

    public static string Legenda(Guid pedidoId) =>
        $"{Prefixo}{Codigo(pedidoId)}. Está tudo certo? Com o seu ok, mando o link do pagamento.";

    public static bool EhResumo(string? texto, Guid pedidoId) =>
        texto is not null && texto.StartsWith($"{Prefixo}{Codigo(pedidoId)}.", StringComparison.Ordinal);
}
