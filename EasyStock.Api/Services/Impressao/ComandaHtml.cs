using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.UseCases.Operacao.Impressao;
using static EasyStock.Api.Services.Impressao.ImpressoHtml;

namespace EasyStock.Api.Services.Impressao;

/// <summary>
/// Comanda de cozinha em HTML (S52), na etiqueta 10×15 ou no cupom 58 mm, sobre as peças de
/// <see cref="ImpressoHtml"/>. Layout aprovado em <c>docs/plan/atendimento-whatsapp/impressos/comanda-aprovada.html</c>.
/// </summary>
public sealed class ComandaHtml(IRendererTemplate renderer)
{
    public static readonly IReadOnlyList<string> Modelos = [Etiqueta10x15, Cupom58];

    public async Task<string> RenderizarAsync(ComandaDto comanda, string modelo, string baseUrl, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(comanda);
        if (!Modelos.Contains(modelo))
            throw new ArgumentException($"Modelo desconhecido: {modelo}.", nameof(modelo));

        var corpo = await renderer.RenderizarAsync(
            Recurso($"comanda.{modelo}.sbn"),
            new Dictionary<string, object?> { ["m"] = Modelo(comanda) },
            htmlEscape: false,
            ct);
        return Pagina("Comanda #" + comanda.Numero, modelo, baseUrl, corpo, "comanda.css");
    }

    private static Dictionary<string, object?> Modelo(ComandaDto c)
    {
        var hoje = DateOnly.FromDateTime(c.ImpressoEm);
        return new Dictionary<string, object?>
        {
            ["numero"] = T(c.Numero),
            ["numero_dia"] = c.NumeroDoDia is { } n ? n.ToString("000") : null,
            ["prazo"] = Prazo(c.Prazo, hoje),
            ["cliente"] = T(c.Cliente ?? "Cliente sem nome"),
            ["entrega"] = T(Entrega(c.Entrega)),
            ["alergias"] = c.Alergias.Count == 0 ? null : T(string.Join(", ", c.Alergias)),
            ["grupos"] = c.Grupos.Select(g => new Dictionary<string, object?>
            {
                ["titulo"] = T(g.Titulo),
                ["contagem"] = Linhas(g.Itens.Count),
                ["itens"] = g.Itens.Select(i => new Dictionary<string, object?>
                {
                    ["quantidade"] = Quantidade(i.Quantidade),
                    ["unidade"] = T(i.Unidade),
                    ["nome"] = T(i.Nome),
                    ["porcao"] = T(i.Porcao),
                    ["molho"] = T(i.Molho),
                    ["observacao"] = T(i.Observacao),
                }).ToList(),
            }).ToList(),
            ["total_linhas"] = Linhas(c.TotalLinhas),
            ["observacao"] = T(c.Observacao),
            ["barras"] = Code128Svg.Gerar(c.Numero),
            ["solicitado"] = T(DataCurta(c.SolicitadoEm, hoje) + " " + Hora(c.SolicitadoEm)),
            ["impresso"] = Curta(c.ImpressoEm),
        };
    }

    private static string Linhas(int n) => n + (n == 1 ? " item" : " itens");
}
