using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Producao;

/// <summary>Um prato que acabou de ser produzido, com o que a baixa precisa saber dele.</summary>
/// <param name="Porcao">Rótulo da porção produzida, só quando o prato tem mais de um tamanho de porção.</param>
/// <param name="EmbalagemDaPorcaoId">M1.4e (#1552): a embalagem escolhida para a porção produzida.</param>
public sealed record PratoParaBaixa(
    Produto Prato, int Porcoes, int? PesoProduzidoG, string? Porcao = null, Guid? EmbalagemDaPorcaoId = null)
{
    public static PratoParaBaixa Da(Produto prato, RegistrarProducaoItemInput item) => new(
        prato, item.Porcoes, item.PesoRealG ?? item.Porcoes * item.PesoPorPorcaoG,
        item.VariosTamanhos ? item.Porcao : null, item.EmbalagemDaPorcaoId);
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
/// A receita é do prato, não da porção (#1542). Para a conta acompanhar o tamanho (M1.4e, #1552): a
/// comida segue o peso produzido quando a receita rende em peso, e a embalagem é da porção
/// (<see cref="PratoParaBaixa.EmbalagemDaPorcaoId"/>), uma por porção produzida, no lugar da embalagem
/// da receita. Onde isso ainda não está montado, avisa: receita que rende em porções baixa o mesmo
/// insumo para 300 g e 800 g, e receita em peso baixa a embalagem dela pelo peso.
/// </para>
/// </summary>
public sealed class BaixaDeInsumosDaProducao(
    IProdutoComposicaoRepository composicaoRepository,
    IItemEstoqueRepository itemEstoqueRepository,
    RegistrarSaidaEstoqueUseCase registrarSaida,
    IProdutoRepository produtoRepository)
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
            // M1.4e (#1552): a embalagem da porção desce uma por porção e tira de cena a embalagem da receita.
            var embalagemDaPorcao = await EmbalagemDaPorcaoAsync(empresaId, p, avisos);
            if (embalagemDaPorcao is not null) Somar(consumo, embalagemDaPorcao, p.Porcoes);

            // D-M2-03 (#1523): embalagem desce em toda produção; o resto da receita só no prato marcado (D-M2-01).
            var marcado = p.Prato.BaixaInsumoAutomatica;
            var receita = await composicaoRepository.GetByProdutoFinalAsync(empresaId, p.Prato.Id, null, ct);
            var linhas = receita
                .Where(l => l.Insumo?.EhEmbalagem == true ? embalagemDaPorcao is null : marcado)
                .ToList();
            if (linhas.Count == 0)
            {
                if (marcado && receita.Count == 0)
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
                Somar(consumo, insumo, qtd.Value);
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

    private static void Somar(Dictionary<Guid, (Produto Insumo, decimal Quantidade)> consumo, Produto insumo, decimal quantidade) =>
        consumo[insumo.Id] = (insumo, consumo.GetValueOrDefault(insumo.Id).Quantidade + quantidade);

    private static string Quem(PratoParaBaixa p) =>
        string.IsNullOrWhiteSpace(p.Porcao) ? p.Prato.Nome : $"{p.Prato.Nome} {p.Porcao.Trim()}";

    // A escolha é validada ao gravar a porção, mas a embalagem pode mudar depois em Insumos (desmarcada, outra
    // unidade, apagada). Aí avisa e devolve null: vale a embalagem da receita, para a baixa não sumir.
    private async Task<Produto?> EmbalagemDaPorcaoAsync(Guid empresaId, PratoParaBaixa p, List<string> avisos)
    {
        if (p.EmbalagemDaPorcaoId is not { } id) return null;

        var embalagem = await produtoRepository.GetByIdAsync(empresaId, id);
        var motivo = embalagem switch
        {
            null => "a embalagem escolhida para a porção não foi encontrada",
            { EhEmbalagem: false } => $"{embalagem.Nome} não está mais marcada como embalagem",
            { UnidadeMedidaBase: not UnidadeMedida.Un } => $"{embalagem.Nome} não é contada por unidade",
            _ => null,
        };
        if (motivo is null) return embalagem;

        avisos.Add($"{Quem(p)}: {motivo}, então desceu a embalagem da receita. Escolha a embalagem da porção no cardápio.");
        return null;
    }

    // O que ainda não acompanha o tamanho da porção. Com a receita em peso e a embalagem na porção, nada a avisar.
    private static string? AvisoDoTamanhoDaPorcao(PratoParaBaixa p, IReadOnlyCollection<ProdutoComposicao> linhas)
    {
        if (string.IsNullOrWhiteSpace(p.Porcao)) return null;
        var quem = Quem(p);

        if (p.Prato.RendimentoUnidade == UnidadeMedida.Un)
            return linhas.Any(l => l.Insumo?.EhEmbalagem != true)
                ? $"{quem}: a receita rende em porções e não distingue o tamanho, então os insumos desceram como {p.Porcoes} porções iguais. Informe o rendimento da receita em peso para a baixa acompanhar o tamanho."
                : null;

        var embalagens = linhas.Where(l => l.Insumo?.EhEmbalagem == true).Select(l => l.Insumo!.Nome).ToList();
        return embalagens.Count > 0
            ? $"{quem}: a receita rende em {p.Prato.RendimentoUnidade}, então a embalagem desceu pelo peso e não uma por porção. Escolha a embalagem da porção no cardápio e confira o saldo de {string.Join(", ", embalagens)}."
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
