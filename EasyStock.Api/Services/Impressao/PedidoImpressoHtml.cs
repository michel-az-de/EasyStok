using System.Globalization;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.UseCases.Operacao.Impressao;
using static EasyStock.Api.Services.Impressao.ImpressoHtml;

namespace EasyStock.Api.Services.Impressao;

/// <summary>
/// Impresso do pedido em HTML (S49): um modelo Scriban por papel sobre as peças de <see cref="ImpressoHtml"/>.
/// Layout aprovado em <c>docs/plan/atendimento-whatsapp/impressos/pedido-aprovado.html</c>.
/// </summary>
public sealed class PedidoImpressoHtml(IRendererTemplate renderer)
{
    public const string Etiqueta10x15 = ImpressoHtml.Etiqueta10x15;
    public const string Cupom58 = ImpressoHtml.Cupom58;
    public const string A4 = ImpressoHtml.A4;
    public const string CaminhoFontes = ImpressoHtml.CaminhoFontes;

    public static readonly IReadOnlyList<string> Modelos = [Etiqueta10x15, Cupom58, A4];

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <param name="baseUrl">Origem da Api (ex.: <c>https://api.casadababa.com.br</c>), para as fontes.</param>
    public async Task<string> RenderizarAsync(PedidoImpressoDto pedido, string modelo, string baseUrl, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        if (!Modelos.Contains(modelo))
            throw new ArgumentException($"Modelo desconhecido: {modelo}.", nameof(modelo));

        var corpo = await renderer.RenderizarAsync(
            Recurso($"pedido.{modelo}.sbn"),
            new Dictionary<string, object?> { ["m"] = Modelo(pedido) },
            htmlEscape: false,
            ct);
        return Pagina("Pedido #" + pedido.Numero, modelo, baseUrl, corpo);
    }

    private static Dictionary<string, object?> Modelo(PedidoImpressoDto p)
    {
        var hoje = DateOnly.FromDateTime(p.ImpressoEm);
        return new Dictionary<string, object?>
        {
            ["numero"] = T(p.Numero),
            ["casa"] = new Dictionary<string, object?>
            {
                ["nome"] = T(p.Casa.Nome),
                ["logo_url"] = T(p.Casa.LogoUrl),
                ["contatos"] = new[]
                    {
                        p.Casa.Documento is { } d ? Documento(d) : null,
                        p.Casa.Site is { } s ? Site(s) : null,
                        p.Casa.WhatsApp is { } w ? "WhatsApp " + Telefone(w) : null,
                    }
                    .Where(c => c is not null)
                    .Select(T)
                    .ToList(),
            },
            ["prazo"] = Prazo(p.Prazo, hoje),
            ["solicitado"] = T(DataCurta(p.SolicitadoEm, hoje) + " " + Hora(p.SolicitadoEm)),
            ["solicitado_longo"] = T(DataLonga(p.SolicitadoEm, hoje) + " " + Hora(p.SolicitadoEm)),
            ["entrega"] = T(Entrega(p.Entrega)),
            ["cliente"] = new Dictionary<string, object?>
            {
                ["id"] = T(p.Cliente.IdCurto),
                ["nome"] = T(p.Cliente.Nome ?? "Cliente sem nome"),
                ["telefone"] = p.Cliente.Telefone is { } tel ? T(Telefone(tel)) : null,
                ["endereco"] = T(p.Cliente.Endereco),
            },
            ["itens"] = p.Itens.Select(i => new Dictionary<string, object?>
            {
                ["quantidade"] = Quantidade(i.Quantidade),
                ["unidade"] = T(i.Unidade),
                ["nome"] = T(i.Nome),
                ["observacao"] = T(i.Observacao),
                ["unitario"] = Moeda(i.Unitario),
                ["subtotal"] = Moeda(i.Subtotal),
            }).ToList(),
            ["quantidade_total"] = p.QuantidadeItens + (p.QuantidadeItens == 1 ? " item" : " itens"),
            ["observacao"] = T(p.Observacao),
            ["nota"] = T(p.Nota),
            ["cobranca"] = new Dictionary<string, object?>
            {
                ["total"] = Moeda(p.Cobranca.Total),
                ["rotulo"] = T((p.Cobranca.Pago ? "Pago" : "Cobrar na entrega")
                               + (p.Cobranca.Forma is { } f ? " · " + Forma(f) : string.Empty)),
            },
            ["barras"] = Code128Svg.Gerar(p.Numero),
            ["alterado"] = Curta(p.AlteradoEm),
            ["impresso"] = Curta(p.ImpressoEm),
            ["alterado_longo"] = T(DataLonga(p.AlteradoEm, null) + " " + Hora(p.AlteradoEm)),
            ["impresso_longo"] = T(DataLonga(p.ImpressoEm, null) + " " + Hora(p.ImpressoEm)),
        };
    }

    /// <summary>Mantido para quem já chamava pelo Pedido; a regra mora em <see cref="ImpressoHtml.Telefone"/>.</summary>
    public static string Telefone(string telefone) => ImpressoHtml.Telefone(telefone);

    private static string Forma(string forma) => forma.Trim().ToLowerInvariant() switch
    {
        "pix" => "PIX",
        "cartao" or "cartão" => "Cartão",
        "credito" or "crédito" or "cartao_credito" => "Crédito",
        "debito" or "débito" or "cartao_debito" => "Débito",
        "dinheiro" => "Dinheiro",
        var outra => char.ToUpper(outra[0], PtBr) + outra[1..],
    };

    private static string Documento(string documento)
    {
        var d = new string(documento.Where(char.IsDigit).ToArray());
        return d.Length switch
        {
            14 => $"CNPJ {d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}",
            11 => $"CPF {d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}",
            _ => documento.Trim(),
        };
    }

    private static string Site(string site) =>
        site.Trim().Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase).TrimEnd('/');
}
