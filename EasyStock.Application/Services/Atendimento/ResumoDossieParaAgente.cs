using System.Globalization;
using System.Text;
using EasyStock.Application.UseCases.Cliente.Dossie;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Versão curta do <see cref="DossieClienteDto"/> para o contexto do LLM (S25, S06). Toda nota interna
/// sai numa linha só, prefixada por <see cref="PromptAtendimento.MarcadorInterno"/> (RN-08): quebra de
/// linha dentro da nota não pode gerar linha sem marcador. O sinal de mesmo domicílio fica de fora
/// (D10): o agente não recebe nome nem dado de outro cadastro.
/// </summary>
public static class ResumoDossieParaAgente
{
    public const int PedidosNoResumo = 3;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Montar(DossieClienteDto dossie)
    {
        var interno = PromptAtendimento.MarcadorInterno;
        var sb = new StringBuilder();
        sb.AppendLine("Histórico do cliente (CRM):");

        if (dossie.Tags.Count > 0)
            sb.AppendLine($"- Tags: {string.Join(", ", dossie.Tags.Select(t => t.Tag))}.");

        sb.AppendLine(dossie.TotalPedidos == 0
            ? "- Pedidos: nenhum."
            : $"- Pedidos: {dossie.TotalPedidos}; última compra em {Data(dossie.UltimaCompraEm!.Value)}.");

        if (dossie.ItemFavorito is { } favorito)
            sb.AppendLine($"- Item mais pedido: {favorito.Nome} (em {favorito.Pedidos} pedido(s)).");

        foreach (var pedido in dossie.UltimosPedidos.Take(PedidosNoResumo))
        {
            var itens = string.Join(", ", pedido.Itens.Select(i => $"{i.Quantidade.ToString("0.##", PtBr)}x {UmaLinha(i.Nome)}"));
            sb.AppendLine($"- Pedido de {Data(pedido.CriadoEm)} ({pedido.Status}): {itens}.");
        }

        if (dossie.Notas.Count > 0)
        {
            sb.AppendLine("Notas internas do cadastro:");
            foreach (var nota in dossie.Notas)
                sb.AppendLine($"- {interno} {Data(nota.CriadoEm)}: {UmaLinha(nota.Texto)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string Data(DateTime utc) => HorarioBrasil.ConverterParaBrasilia(utc).ToString("dd/MM/yyyy", PtBr);

    /// <summary>Colapsa qualquer espaço em branco (inclusive quebra de linha) num espaço.</summary>
    private static string UmaLinha(string texto) =>
        string.Join(' ', texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
