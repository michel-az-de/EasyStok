using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Domain.Enums.Storefront;

namespace EasyStock.Application.UseCases.Operacao.Impressao;

public sealed record MontarComandaInput(Guid EmpresaId, Guid PedidoId);

/// <summary>
/// Monta a <see cref="ComandaDto"/> (S52) da mesma leitura do impresso do pedido. Grupos na ordem preparar em
/// casa (congelado, refrigerado, o resto: S53), para servir, outros; itens na ordem do pedido; frete e taxa ficam
/// fora (não são produção). Alergias vêm das tags do cadastro com prefixo <see cref="PrefixoAlergia"/>. Devolve
/// <c>null</c> para pedido inexistente ou de outra empresa.
/// </summary>
public sealed class MontarComandaUseCase(IPedidoImpressoQueries queries, TimeProvider relogio)
{
    public const string PrefixoAlergia = "alergia_";
    public const string LinhaOutros = "outros";

    private static readonly string PrepararEmCasa = LinhaProduto.PrepararEmCasa.ParaContrato();

    private static readonly string[] OrdemLinhas =
    [
        PrepararEmCasa,
        LinhaProduto.ParaServir.ParaContrato(),
        LinhaOutros,
    ];

    /// <summary>Dentro de "preparar em casa", congelado vem antes de refrigerado; o resto (ambiente) por último.</summary>
    private static readonly string?[] OrdemConservacao =
        [ConservacaoProdutoExtensions.Congelado, ConservacaoProdutoExtensions.Refrigerado, null];

    public async Task<ComandaDto?> ExecuteAsync(MontarComandaInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(input.PedidoId, "PedidoId");

        var p = await queries.ObterAsync(input.EmpresaId, input.PedidoId, ct);
        if (p is null) return null;

        var grupos = p.Itens
            .Where(i => i.EhProduto)
            .GroupBy(i => (Linha: Limpo(i.Linha) ?? LinhaOutros, Conservacao: ConservacaoDoGrupo(i)))
            .OrderBy(g => OrdemDaLinha(g.Key.Linha))
            .ThenBy(g => Array.IndexOf(OrdemConservacao, g.Key.Conservacao))
            .Select(g => new ComandaGrupoDto(g.Key.Linha, Titulo(g.Key.Linha, g.Key.Conservacao), g
                .Select(i => new ComandaItemDto(
                    i.Quantidade,
                    Limpo(i.Unidade)?.ToLowerInvariant() ?? MontarPedidoImpressoUseCase.UnidadePadrao,
                    i.Nome.Trim(),
                    Limpo(i.Variacao),
                    Limpo(i.Molho),
                    Limpo(i.Observacao)))
                .ToList(), g.Key.Conservacao))
            .ToList();

        return new ComandaDto(
            p.Id.ToString("N")[..8].ToUpperInvariant(),
            p.NumeroDoDia,
            PrazoImpresso.Calcular(p),
            Limpo(p.Cliente.Nome),
            p.Entrega is { } e ? new PedidoImpressoEntregaDto(e.Tipo, Limpo(e.Nome)) : null,
            Alergias(p.Cliente.Alergias),
            grupos,
            Limpo(p.Observacoes),
            HorarioBrasil.ConverterParaBrasilia(p.CriadoEm),
            HorarioBrasil.ConverterParaBrasilia(relogio.GetUtcNow().UtcDateTime));
    }

    /// <summary><c>alergia_frutos_do_mar</c> vira "FRUTOS DO MAR". Outras tags não entram.</summary>
    private static IReadOnlyList<string> Alergias(IReadOnlyList<string>? tags) =>
        (tags ?? [])
            .Where(t => t.StartsWith(PrefixoAlergia, StringComparison.OrdinalIgnoreCase) && t.Length > PrefixoAlergia.Length)
            .Select(t => t[PrefixoAlergia.Length..].Replace('_', ' ').Trim().ToUpperInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

    private static int OrdemDaLinha(string linha)
    {
        var i = Array.IndexOf(OrdemLinhas, linha);
        return i < 0 ? OrdemLinhas.Length : i;
    }

    /// <summary>Só "preparar em casa" se divide; congelado e refrigerado viram grupo próprio, ambiente fica no geral.</summary>
    private static string? ConservacaoDoGrupo(PedidoImpressoItemLeitura i) =>
        Limpo(i.Linha) == PrepararEmCasa
        && Limpo(i.Conservacao)?.ToLowerInvariant() is ConservacaoProdutoExtensions.Congelado or ConservacaoProdutoExtensions.Refrigerado
            ? i.Conservacao!.Trim().ToLowerInvariant()
            : null;

    private static string Titulo(string linha, string? conservacao) => linha switch
    {
        "prepararEmCasa" => conservacao is null ? "Preparar em casa" : $"Preparar em casa · {conservacao}",
        "paraServir" => "Para servir",
        _ => "Outros",
    };

    private static string? Limpo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
