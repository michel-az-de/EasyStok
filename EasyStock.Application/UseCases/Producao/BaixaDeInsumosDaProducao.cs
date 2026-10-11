using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Producao;

/// <summary>Um prato que acabou de ser produzido, com o que a baixa precisa saber dele.</summary>
/// <param name="Porcao">Rótulo da porção produzida, só quando o prato tem mais de um tamanho de porção.</param>
public sealed record PratoParaBaixa(Produto Prato, int Porcoes, int? PesoProduzidoG, string? Porcao = null)
{
    public static PratoParaBaixa Da(Produto prato, RegistrarProducaoItemInput item) => new(
        prato, item.Porcoes, item.PesoRealG ?? item.Porcoes * item.PesoPorPorcaoG,
        item.VariosTamanhos ? item.Porcao : null);
}

/// <summary>
/// D-M2-01 (#1499): baixa dos insumos pela receita, dentro da transação da produção (S23).
/// Entra o prato com <see cref="Produto.BaixaInsumoAutomatica"/> (a receita toda) e, em qualquer
/// prato, a linha de embalagem (<see cref="Produto.EhEmbalagem"/>, D-M2-03). Consumo de cada insumo =
/// quantidade da linha × produzido ÷ rendimento, convertido para a unidade-base do insumo.
/// Falta de insumo AVISA e não trava: com algum saldo, a falta vira descoberto no lote (#540);
/// sem saldo nenhum não há lote onde registrar, então só avisa. A saída é <c>UsoInterno</c>
/// e cita o código do lote produzido.
/// <para>
/// A receita é do prato, não da porção (#1542). Produzida uma porção, a conta não muda, mas avisa onde
/// ela não acompanha o tamanho: receita que rende em porções baixa o mesmo insumo para 300 g e 800 g,
/// e receita que rende em peso baixa a embalagem pelo peso em vez de uma por porção.
/// </para>
/// </summary>
public sealed class BaixaDeInsumosDaProducao(
    IProdutoComposicaoRepository composicaoRepository,
    IItemEstoqueRepository itemEstoqueRepository,
    RegistrarSaidaEstoqueUseCase registrarSaida)
{
    /// <summary>Início da descrição da saída de insumo. O relatório de perdas (M2.6) a separa da degustação.</summary>
    public const string PrefixoDaDescricao = "Insumo da produção";

    public async Task<IReadOnlyList<string>> BaixarAsync(
        Guid empresaId, IReadOnlyList<PratoParaBaixa> pratos, string codigoLote, DateTime data, CancellationToken ct = default)
    {
        var avisos = new List<string>();
        var consumo = new Dictionary<Guid, (Produto Insumo, decimal Quantidade)>();

        foreach (var p in pratos)
        {
            // D-M2-03 (#1523): embalagem desce em toda produção; o resto da receita só no prato marcado (D-M2-01).
            var marcado = p.Prato.BaixaInsumoAutomatica;
            var receita = await composicaoRepository.GetByProdutoFinalAsync(empresaId, p.Prato.Id, null, ct);
            var linhas = marcado ? receita : receita.Where(l => l.Insumo?.EhEmbalagem == true).ToList();
            if (!marcado && linhas.Count == 0) continue;
            if (linhas.Count == 0)
            {
                avisos.Add($"{p.Prato.Nome}: marcado para baixar insumos, mas está sem receita.");
                continue;
            }

            var (fator, erroFator) = FatorDaReceita(p);
            if (fator is null)
            {
                avisos.Add($"{p.Prato.Nome}: insumos não baixados ({erroFator}).");
                continue;
            }

            if (AvisoDoTamanhoDaPorcao(p, linhas) is { } avisoDaPorcao) avisos.Add(avisoDaPorcao);

            foreach (var linha in linhas)
            {
                var insumo = linha.Insumo!;
                var (qtd, erro) = UnidadeMedidaConverter.Converter(linha.Quantidade * fator.Value, linha.Unidade, insumo.UnidadeMedidaBase);
                if (qtd is null)
                {
                    avisos.Add($"{p.Prato.Nome}: {insumo.Nome} não baixado ({erro}).");
                    continue;
                }
                consumo[insumo.Id] = consumo.TryGetValue(insumo.Id, out var atual)
                    ? (insumo, atual.Quantidade + qtd.Value)
                    : (insumo, qtd.Value);
            }
        }

        var itens = new List<RegistrarSaidaEstoqueItemCommand>();
        foreach (var (insumoId, (insumo, bruto)) in consumo)
        {
            var quantidade = Math.Round(bruto, 3, MidpointRounding.AwayFromZero);
            if (quantidade <= 0) continue;

            var lotes = await itemEstoqueRepository.GetLotesDisponiveisParaSaidaAsync(empresaId, insumoId, null, true, false);
            var disponivel = lotes.Sum(l => l.QuantidadeAtual.Value);
            var unidade = insumo.UnidadeMedidaBase;
            if (!lotes.Any())
            {
                avisos.Add($"Sem estoque de {insumo.Nome}: a produção pedia {quantidade:0.###} {unidade} e nada foi baixado.");
                continue;
            }
            if (disponivel < quantidade)
                avisos.Add($"Faltou {quantidade - disponivel:0.###} {unidade} de {insumo.Nome}: ficou descoberto no estoque.");

            itens.Add(new RegistrarSaidaEstoqueItemCommand(insumoId, null, quantidade, 0m, $"{PrefixoDaDescricao} {codigoLote}"));
        }

        if (itens.Count > 0)
            await registrarSaida.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
                empresaId, itens, data, data, null, null,
                NaturezaMovimentacaoEstoque.UsoInterno, CanalVenda.Outro,
                $"Insumos da produção {codigoLote}", PermitirDescoberto: true));

        return avisos;
    }

    private static string? AvisoDoTamanhoDaPorcao(PratoParaBaixa p, IReadOnlyCollection<ProdutoComposicao> linhas)
    {
        if (string.IsNullOrWhiteSpace(p.Porcao)) return null;
        var quem = $"{p.Prato.Nome} {p.Porcao.Trim()}";

        if (p.Prato.RendimentoUnidade == UnidadeMedida.Un)
            return linhas.Any(l => l.Insumo?.EhEmbalagem != true)
                ? $"{quem}: a receita rende em porções e não distingue o tamanho, então os insumos desceram como {p.Porcoes} porções iguais. Confira o estoque dos insumos."
                : null;

        var embalagens = linhas.Where(l => l.Insumo?.EhEmbalagem == true).Select(l => l.Insumo!.Nome).ToList();
        return embalagens.Count > 0
            ? $"{quem}: a receita rende em {p.Prato.RendimentoUnidade}, então a embalagem desceu pelo peso e não uma por porção. Confira o saldo de {string.Join(", ", embalagens)}."
            : null;
    }

    // Receita em Un rende porções; em massa/volume, a produção é medida pelo peso produzido.
    private static (decimal? Fator, string? Erro) FatorDaReceita(PratoParaBaixa p)
    {
        var receita = p.Prato;
        if (receita.RendimentoBase <= 0) return (null, "rendimento da receita é zero");
        if (receita.RendimentoUnidade == UnidadeMedida.Un) return (p.Porcoes / receita.RendimentoBase, null);
        if (p.PesoProduzidoG is not { } pesoG) return (null, $"a receita rende em {receita.RendimentoUnidade} e a produção veio sem peso");
        var (produzido, erro) = UnidadeMedidaConverter.Converter(pesoG, UnidadeMedida.G, receita.RendimentoUnidade);
        return produzido is null ? (null, erro) : (produzido.Value / receita.RendimentoBase, null);
    }
}
