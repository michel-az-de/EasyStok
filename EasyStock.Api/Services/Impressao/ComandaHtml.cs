using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.UseCases.Operacao.Impressao;
using static EasyStock.Api.Services.Impressao.ImpressoHtml;

namespace EasyStock.Api.Services.Impressao;

/// <summary>
/// Canhoto de cozinha em HTML (S52, #1632), em qualquer papel de <see cref="ImpressoHtml.Papeis"/>, sobre as
/// peças de <see cref="ImpressoHtml"/>. Papel de produção: sem marca, sem preço, sem endereço. Layout aprovado em
/// <c>docs/plan/atendimento-whatsapp/impressos/impressos-v4-proposta.html</c>.
/// </summary>
public sealed class ComandaHtml(IRendererTemplate renderer)
{
    public const string Documento = "cozinha";

    public static readonly IReadOnlyList<string> Modelos = Papeis;

    public async Task<string> RenderizarAsync(ComandaDto comanda, string modelo, string baseUrl, CancellationToken ct = default, string codigo = "qr")
    {
        ArgumentNullException.ThrowIfNull(comanda);
        if (!Modelos.Contains(modelo))
            throw new ArgumentException($"Modelo desconhecido: {modelo}.", nameof(modelo));

        var corpo = await renderer.RenderizarAsync(
            Recurso("comanda.sbn"),
            new Dictionary<string, object?> { ["m"] = Modelo(comanda, modelo, codigo) },
            htmlEscape: false,
            ct);
        return CodigoImpresso.AjustarPagina(Pagina("Comanda #" + comanda.Numero, modelo, baseUrl, corpo), codigo);
    }

    private static Dictionary<string, object?> Modelo(ComandaDto c, string modelo, string codigo)
    {
        var hoje = DateOnly.FromDateTime(c.ImpressoEm);
        return new Dictionary<string, object?>
        {
            ["papel"] = Papel(modelo, Documento),
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
                    ["quantidade"] = T(QuantidadeComUnidade(i.Quantidade, i.Unidade)),
                    ["nome"] = T(i.Nome),
                    ["porcao"] = T(i.Porcao),
                    ["molho"] = T(i.Molho?.ToUpperInvariant()),
                    ["observacao"] = T(i.Observacao),
                }).ToList(),
            }).ToList(),
            ["total_linhas"] = Linhas(c.TotalLinhas),
            ["observacao"] = T(c.Observacao),
            ["qr"] = CodigoImpresso.Gerar(codigo, c.Numero, c.Numero),
            ["solicitado"] = T(DataCurta(c.SolicitadoEm, hoje) + " " + Hora(c.SolicitadoEm)),
            ["impresso"] = Curta(c.ImpressoEm),
        };
    }

    private static string Linhas(int n) => n + (n == 1 ? " item" : " itens");
}
