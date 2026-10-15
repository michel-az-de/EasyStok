using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Operacao;
using static EasyStock.Api.Services.Impressao.ImpressoHtml;

namespace EasyStock.Api.Services.Impressao;

/// <summary>Venda do atendimento como a folha do Encerrar mostra (texto já pronto no console).</summary>
public sealed record ResumoVendaImpresso(string Numero, string Itens, string? Meio, decimal Valor, string Situacao);

/// <summary>Marco do atendimento: hora local "HH:mm" e o rótulo.</summary>
public sealed record ResumoMarcoImpresso(string Hora, string Rotulo);

/// <summary>
/// O resumo que o console já calculou no Encerrar (#1839, <c>dominio/resumoAtendimento.js</c>): a conta fica num
/// lugar só e o papel sai pelo mesmo layout dos outros impressos.
/// </summary>
public sealed record ResumoAtendimentoImpresso(
    string Cliente,
    string? Contato,
    string? Numero,
    DateTime Data,
    string Duracao,
    string Faixa,
    int MensagensCliente,
    int MensagensVoce,
    int MensagensAutomatico,
    decimal Receita,
    decimal Estorno,
    decimal FaltaReceber,
    string Converteu,
    bool VirouCliente,
    IReadOnlyList<ResumoVendaImpresso> Vendas,
    decimal VendasTotal,
    IReadOnlyList<ResumoMarcoImpresso> Marcos,
    string? AvaliacaoCliente,
    string? Autoavaliacao,
    string? Anotacao);

/// <summary>
/// Resumo do atendimento em HTML (#1839) no layout "bilhete da casa", em qualquer papel de
/// <see cref="ImpressoHtml.Papeis"/>, sobre as peças de <see cref="ImpressoHtml"/>. Antes saía pelo
/// <c>window.print()</c> da folha React, fora do esquema de papel, fontes e logo dos impressos.
/// </summary>
public sealed class ResumoAtendimentoHtml(IRendererTemplate renderer)
{
    public const string Documento = "resumo";

    public async Task<string> RenderizarAsync(
        ResumoAtendimentoImpresso resumo, PedidoImpressoCasaLeitura casa, string modelo, string baseUrl, DateTime agora, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resumo);
        ArgumentNullException.ThrowIfNull(casa);
        if (!Papeis.Contains(modelo))
            throw new ArgumentException($"Modelo desconhecido: {modelo}.", nameof(modelo));

        var corpo = await renderer.RenderizarAsync(
            Recurso("resumo.sbn"),
            new Dictionary<string, object?> { ["m"] = Modelo(resumo, casa, modelo, baseUrl, agora) },
            htmlEscape: false,
            ct);
        return Pagina("Resumo de atendimento" + (resumo.Numero is { } n ? " " + n : string.Empty), modelo, baseUrl, corpo);
    }

    private static Dictionary<string, object?> Modelo(
        ResumoAtendimentoImpresso r, PedidoImpressoCasaLeitura casa, string modelo, string baseUrl, DateTime agora) => new()
    {
        ["papel"] = Papel(modelo, Documento),
        // Logo só no jato de tinta, como nos outros impressos; na térmica sai o nome em texto.
        ["logo"] = Logo(casa.LogoUrl, modelo, logoNaTermica: false, baseUrl),
        ["casa"] = new Dictionary<string, object?>
        {
            ["nome"] = T(casa.Nome),
            ["contatos"] = new[] { casa.Site, casa.WhatsApp is { } w ? "WhatsApp " + Telefone(w) : null }
                .Where(c => !string.IsNullOrWhiteSpace(c)).Select(T).ToList(),
        },
        ["numero"] = T(Vazio(r.Numero)),
        ["data"] = DataLonga(r.Data, null),
        ["cliente"] = T(r.Cliente),
        ["contato"] = T(Vazio(r.Contato)),
        ["virou_cliente"] = r.VirouCliente,
        ["duracao"] = T(r.Duracao),
        ["faixa"] = T(r.Faixa),
        ["converteu"] = T(r.Converteu),
        ["receita"] = Moeda(r.Receita),
        ["estorno"] = r.Estorno > 0 ? Moeda(-r.Estorno) : null,
        ["falta_receber"] = r.FaltaReceber > 0 ? Moeda(r.FaltaReceber) : null,
        ["mensagens"] = new Dictionary<string, object?>
        {
            ["cliente"] = r.MensagensCliente,
            ["voce"] = r.MensagensVoce,
            ["automatico"] = r.MensagensAutomatico,
            ["total"] = r.MensagensCliente + r.MensagensVoce + r.MensagensAutomatico,
        },
        ["vendas"] = r.Vendas.Select(v => new Dictionary<string, object?>
        {
            ["numero"] = T(v.Numero),
            ["itens"] = T(v.Itens),
            ["detalhe"] = T(string.Join(" · ", new[] { v.Meio, v.Situacao }.Where(x => !string.IsNullOrWhiteSpace(x)))),
            ["valor"] = Valor(v.Valor),
        }).ToList(),
        ["vendas_total"] = Moeda(r.VendasTotal),
        ["marcos"] = r.Marcos.Select(x => new Dictionary<string, object?> { ["hora"] = T(x.Hora), ["rotulo"] = T(x.Rotulo) }).ToList(),
        ["avaliacao_cliente"] = T(Vazio(r.AvaliacaoCliente)),
        ["autoavaliacao"] = T(Vazio(r.Autoavaliacao)),
        ["anotacao"] = T(Vazio(r.Anotacao)),
        ["impresso"] = Curta(agora),
    };

    // O Scriban trata string vazia como verdadeira: vazio vira nulo para o bloco não sair em branco.
    private static string? Vazio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
