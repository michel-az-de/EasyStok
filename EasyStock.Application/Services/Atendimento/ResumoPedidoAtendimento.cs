using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Services.Atendimento.Ferramentas;

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

    /// <summary>Resumo em texto (chat do site, sem imagem): abre com o código do pedido, como a legenda, e leva o que a imagem mostra.</summary>
    public static string TextoCompleto(ResumoPedidoDto resumo, Guid pedidoId)
    {
        var linhas = new List<string> { $"{Prefixo}{Codigo(pedidoId)}." };
        linhas.AddRange(resumo.Itens.Select(i =>
            $"- {i.Quantidade.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}x {i.Nome}: {FerramentaJson.FormatarReais(i.Subtotal)}"
            + (string.IsNullOrWhiteSpace(i.Observacao) ? "" : $" ({i.Observacao})")));
        if (resumo.Frete > 0) linhas.Add($"Frete: {FerramentaJson.FormatarReais(resumo.Frete)}");
        linhas.Add($"Total: {FerramentaJson.FormatarReais(resumo.Total)}");
        linhas.Add($"Entrega: {resumo.Entrega}" + (string.IsNullOrWhiteSpace(resumo.Endereco) ? "" : $", {resumo.Endereco}"));
        linhas.Add("Está tudo certo? Com o seu ok, mando o link do pagamento.");
        return string.Join(Convert.ToChar(10), linhas);
    }

    public static bool EhResumo(string? texto, Guid pedidoId) =>
        texto is not null && texto.StartsWith($"{Prefixo}{Codigo(pedidoId)}.", StringComparison.Ordinal);
}
