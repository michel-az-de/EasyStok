using EasyStock.Application.Ports.Output.Persistence.Operacao;

namespace EasyStock.Application.UseCases.Operacao.Impressao;

/// <param name="Nota">Texto curto digitado na hora; cortado em <see cref="MontarPedidoImpressoUseCase.NotaTamanhoMaximo"/>.</param>
public sealed record MontarPedidoImpressoInput(Guid EmpresaId, Guid PedidoId, string? Nota = null);

/// <summary>
/// Monta o <see cref="PedidoImpressoDto"/> (S49), com o prazo de <see cref="PrazoImpresso"/>.
/// Devolve <c>null</c> para pedido inexistente ou de outra empresa.
/// </summary>
public sealed class MontarPedidoImpressoUseCase(IPedidoImpressoQueries queries, TimeProvider relogio)
{
    /// <summary>Folga entre o pronto e a entrega; a regra mora em <see cref="PrazoImpresso"/>.</summary>
    public const int MinutosProntoAntesDaJanela = PrazoImpresso.MinutosProntoAntesDaJanela;
    public const int NotaTamanhoMaximo = 80;
    public const string UnidadePadrao = "un";

    /// <summary>Unidades de peso e volume: o item conta como 1 volume, não pela quantidade.</summary>
    private static readonly HashSet<string> UnidadesDeMedida = new(StringComparer.OrdinalIgnoreCase)
        { "kg", "g", "mg", "l", "lt", "ml", "m", "cm" };

    public async Task<PedidoImpressoDto?> ExecuteAsync(MontarPedidoImpressoInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(input.PedidoId, "PedidoId");

        var p = await queries.ObterAsync(input.EmpresaId, input.PedidoId, ct);
        if (p is null) return null;

        var c = p.Cliente;
        var pago = p.Pagamentos.Sum(g => g.Valor) >= p.Total;
        var ultimoPagamento = p.Pagamentos.OrderBy(g => g.PagoEm).LastOrDefault();

        return new PedidoImpressoDto(
            new PedidoImpressoCasaDto(p.Casa.Nome, Limpo(p.Casa.Documento), Limpo(p.Casa.Site), Limpo(p.Casa.WhatsApp), Limpo(p.Casa.LogoUrl)),
            p.Id.ToString("N")[..8].ToUpperInvariant(),
            PrazoImpresso.Calcular(p),
            new PedidoImpressoClienteDto(
                c.Id is { } id ? id.ToString("N")[..6].ToUpperInvariant() : null,
                Limpo(c.Nome),
                Limpo(c.Telefone),
                EnderecoImpresso.Montar(c.Endereco, c.Complemento, c.Apt, c.Bairro, c.Cidade, c.Cep)),
            p.Entrega is { } e ? new PedidoImpressoEntregaDto(e.Tipo, Limpo(e.Nome)) : null,
            p.Itens.Select(i => new PedidoImpressoItemDto(
                    i.Quantidade,
                    Limpo(i.Unidade)?.ToLowerInvariant() ?? UnidadePadrao,
                    Limpo(i.Variacao) is { } v ? $"{i.Nome.Trim()} ({v})" : i.Nome.Trim(),
                    Limpo(i.Observacao),
                    i.PrecoUnitario,
                    i.Subtotal))
                .ToList(),
            Limpo(p.Observacoes),
            Nota(input.Nota),
            new PedidoImpressoCobrancaDto(p.Total, pago, Limpo(p.FormaCobranca) ?? Limpo(ultimoPagamento?.Metodo)),
            p.Itens.Where(i => i.EhProduto).Sum(Volumes),
            HorarioBrasil.ConverterParaBrasilia(p.CriadoEm),
            HorarioBrasil.ConverterParaBrasilia(p.AlteradoEm),
            HorarioBrasil.ConverterParaBrasilia(relogio.GetUtcNow().UtcDateTime));
    }

    private static int Volumes(PedidoImpressoItemLeitura i) =>
        Limpo(i.Unidade) is { } u && UnidadesDeMedida.Contains(u) || i.Quantidade != decimal.Truncate(i.Quantidade)
            ? 1
            : (int)i.Quantidade;

    private static string? Nota(string? nota) =>
        Limpo(nota) is { } n ? (n.Length > NotaTamanhoMaximo ? n[..NotaTamanhoMaximo] : n) : null;

    private static string? Limpo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
