using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Domain.Enums.Storefront;

namespace EasyStock.Application.UseCases.Operacao.Impressao;

public sealed record MontarComandaInput(Guid EmpresaId, Guid PedidoId);

/// <summary>
/// Monta a <see cref="ComandaDto"/> (S52) da mesma leitura do impresso do pedido. Grupos na ordem preparar em
/// casa, para servir, outros; itens na ordem do pedido; frete e taxa ficam fora (não são produção). Alergias vêm
/// das tags do cadastro com prefixo <see cref="PrefixoAlergia"/>. Devolve <c>null</c> para pedido inexistente ou
/// de outra empresa.
/// </summary>
public sealed class MontarComandaUseCase(IPedidoImpressoQueries queries, TimeProvider relogio)
{
    public const string PrefixoAlergia = "alergia_";
    public const string LinhaOutros = "outros";

    private static readonly string[] OrdemLinhas =
    [
        LinhaProduto.PrepararEmCasa.ParaContrato(),
        LinhaProduto.ParaServir.ParaContrato(),
        LinhaOutros,
    ];

    public async Task<ComandaDto?> ExecuteAsync(MontarComandaInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(input.PedidoId, "PedidoId");

        var p = await queries.ObterAsync(input.EmpresaId, input.PedidoId, ct);
        if (p is null) return null;

        var grupos = p.Itens
            .Where(i => i.EhProduto)
            .GroupBy(i => Limpo(i.Linha) ?? LinhaOutros)
            .OrderBy(g => OrdemDaLinha(g.Key))
            .Select(g => new ComandaGrupoDto(g.Key, Titulo(g.Key), g
                .Select(i => new ComandaItemDto(
                    i.Quantidade,
                    Limpo(i.Unidade)?.ToLowerInvariant() ?? MontarPedidoImpressoUseCase.UnidadePadrao,
                    i.Nome.Trim(),
                    Limpo(i.Variacao),
                    Limpo(i.Molho),
                    Limpo(i.Observacao)))
                .ToList()))
            .ToList();

        return new ComandaDto(
            p.Id.ToString("N")[..8].ToUpperInvariant(),
            NumeroDoDia: null,
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

    private static string Titulo(string linha) => linha switch
    {
        "prepararEmCasa" => "Preparar em casa",
        "paraServir" => "Para servir",
        _ => "Outros",
    };

    private static string? Limpo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
