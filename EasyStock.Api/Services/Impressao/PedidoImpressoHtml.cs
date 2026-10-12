using System.Globalization;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.UseCases.Operacao.Impressao;
using static EasyStock.Api.Services.Impressao.ImpressoHtml;

namespace EasyStock.Api.Services.Impressao;

/// <summary>O que varia por aparelho ou por pedido na hora de imprimir.</summary>
/// <param name="LogoNaTermica">Sai o logo também na térmica (10×15, 80 mm, 58 mm). Padrão: nome da casa em texto.</param>
/// <param name="Alergias">Alergias do cadastro da cliente, para a confirmação de agendamento.</param>
public sealed record ImpressoOpcoes(bool LogoNaTermica = false, IReadOnlyList<string>? Alergias = null, string Codigo = "qr");

/// <summary>
/// Impressos do pedido voltados à cliente e ao entregador (#1632): pedido, agendamento e entrega, um modelo
/// Scriban por documento sobre as peças de <see cref="ImpressoHtml"/>, em qualquer papel de
/// <see cref="ImpressoHtml.Papeis"/>. Layout aprovado em
/// <c>docs/plan/atendimento-whatsapp/impressos/impressos-v4-proposta.html</c>.
/// </summary>
public sealed class PedidoImpressoHtml(IRendererTemplate renderer, IConfiguration? configuracao = null)
{
    public const string Pedido = "pedido";
    public const string Agendamento = "agendamento";
    public const string Entrega = "entrega";

    /// <summary>Chave de configuração com quem assina o fecho (ex.: o nome da dona). Sem ela, assina a casa.</summary>
    public const string ChaveAssinatura = "Impressao:Assinatura";

    public static readonly IReadOnlyList<string> Documentos = [Pedido, Agendamento, Entrega];
    public static readonly IReadOnlyList<string> Modelos = Papeis;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <param name="baseUrl">Origem da Api (ex.: <c>https://api.casadababa.com.br</c>), para as fontes.</param>
    public async Task<string> RenderizarAsync(
        PedidoImpressoDto pedido, string documento, string modelo, string baseUrl, ImpressoOpcoes? opcoes = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        if (!Documentos.Contains(documento))
            throw new ArgumentException($"Documento desconhecido: {documento}.", nameof(documento));
        if (!Modelos.Contains(modelo))
            throw new ArgumentException($"Modelo desconhecido: {modelo}.", nameof(modelo));

        var corpo = await renderer.RenderizarAsync(
            Recurso($"{documento}.sbn"),
            new Dictionary<string, object?> { ["m"] = Modelo(pedido, documento, modelo, baseUrl, opcoes ?? new ImpressoOpcoes()) },
            htmlEscape: false,
            ct);
        return CodigoImpresso.AjustarPagina(Pagina(Titulo(documento) + " #" + pedido.Numero, modelo, baseUrl, corpo), opcoes?.Codigo ?? "qr");
    }

    private Dictionary<string, object?> Modelo(PedidoImpressoDto p, string documento, string modelo, string baseUrl, ImpressoOpcoes opcoes)
    {
        var hoje = DateOnly.FromDateTime(p.ImpressoEm);
        var link = LinkWhatsApp(p.Casa.WhatsApp, p.Numero);
        var forma = p.Cobranca.Forma is { } f ? Forma(f) : null;
        return new Dictionary<string, object?>
        {
            ["papel"] = Papel(modelo, documento),
            ["numero"] = T(p.Numero),
            ["logo"] = Logo(p.Casa.LogoUrl, modelo, opcoes.LogoNaTermica, baseUrl),
            ["orn"] = Ornamento,
            ["assinatura"] = T(Assinatura(p.Casa.Nome)),
            ["casa"] = new Dictionary<string, object?>
            {
                ["nome"] = T(p.Casa.Nome),
                ["contatos"] = new[]
                    {
                        p.Casa.Site is { } s ? Site(s) : null,
                        p.Casa.WhatsApp is { } w ? "WhatsApp " + Telefone(w) : null,
                    }
                    .Where(c => c is not null)
                    .Select(T)
                    .ToList(),
            },
            ["prazo"] = Prazo(p.Prazo, hoje),
            ["solicitado"] = T(Curta(p.SolicitadoEm)),
            ["impresso"] = Curta(p.ImpressoEm),
            ["entrega"] = T(ImpressoHtml.Entrega(p.Entrega)),
            ["cliente"] = new Dictionary<string, object?>
            {
                ["nome"] = T(p.Cliente.Nome ?? "Cliente sem nome"),
                ["primeiro"] = T(PrimeiroNome(p.Cliente.Nome)),
                ["telefone"] = p.Cliente.Telefone is { } tel ? T(Telefone(tel)) : null,
                ["endereco"] = T(p.Cliente.Endereco),
            },
            ["itens"] = p.Itens.Select(i => new Dictionary<string, object?>
            {
                ["quantidade"] = T(QuantidadeComUnidade(i.Quantidade, i.Unidade)),
                ["nome"] = T(i.Nome),
                ["observacao"] = T(i.Observacao),
                ["valor"] = Valor(i.Subtotal),
            }).ToList(),
            ["observacao"] = T(p.Observacao),
            ["nota"] = T(p.Nota),
            ["alergias"] = opcoes.Alergias is { Count: > 0 } a ? T(string.Join(", ", a).ToLower(PtBr)) : null,
            ["total"] = Moeda(p.Cobranca.Total),
            ["pago"] = p.Cobranca.Pago,
            ["saldo"] = Moeda(p.Cobranca.Saldo),
            ["forma"] = T(forma),
            ["cobranca_frase"] = T(p.Cobranca.Pago
                ? "Tudo pago" + (forma is null ? string.Empty : " no " + forma) + ". Obrigada!"
                : (p.Cobranca.Recebido > 0 ? "Falta " + Moeda(p.Cobranca.Saldo) + ", a acertar na entrega" : "A acertar na entrega")
                  + (forma is null ? string.Empty : " · " + forma) + "."),
            ["qr"] = CodigoImpresso.Gerar(opcoes.Codigo, p.Numero, link),
            ["codigo_barras"] = opcoes.Codigo == "barras",
        };
    }

    private string Assinatura(string casa) =>
        configuracao?[ChaveAssinatura] is { Length: > 0 } quem ? $"{quem.Trim()} · {casa}" : casa;

    private static string Titulo(string documento) => documento switch
    {
        Agendamento => "Agendamento",
        Entrega => "Entrega",
        _ => "Pedido",
    };

    /// <summary>Mantido para quem já chamava pelo Pedido; a regra mora em <see cref="ImpressoHtml.Telefone"/>.</summary>
    public static string Telefone(string telefone) => ImpressoHtml.Telefone(telefone);

    private static string Forma(string forma) => forma.Trim().ToLowerInvariant() switch
    {
        "pix" => "PIX",
        "cartao" or "cartão" => "cartão",
        "credito" or "crédito" or "cartao_credito" => "crédito",
        "debito" or "débito" or "cartao_debito" => "débito",
        "dinheiro" => "dinheiro",
        var outra => outra,
    };

    private static string Site(string site) =>
        site.Trim().Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase).TrimEnd('/');
}
