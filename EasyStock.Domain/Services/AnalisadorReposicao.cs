using System.Globalization;
using EasyStock.Domain.Reposicao;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Services;

/// <summary>
/// Fonte ÚNICA de elegibilidade e sugestão de reposição (ADR-0039). Função pura: recebe uma
/// projeção por-produto e devolve só os itens que precisam de reposição, classificados (R1/R2),
/// com quantidade sugerida (R3), motivo legível (R4) e ordenados por prioridade (R5).
/// Reusa <see cref="LimiarEstoqueResolver"/> (limiar resolvido ao vivo) e
/// <see cref="CalculadoraReposicaoEstoque"/>. Sem relógio nem I/O: tudo vem do snapshot.
/// </summary>
public sealed class AnalisadorReposicao
{
    private readonly CalculadoraReposicaoEstoque calculadora = new();

    /// <summary>Avalia a lista inteira e devolve apenas os elegíveis, ordenados por R5.</summary>
    public IReadOnlyList<ItemReposicao> Analisar(
        IEnumerable<ProdutoReposicaoSnapshot> produtos,
        OpcoesReposicao opcoes)
    {
        ArgumentNullException.ThrowIfNull(produtos);
        ArgumentNullException.ThrowIfNull(opcoes);

        var elegiveis = new List<ItemReposicao>();
        foreach (var produto in produtos)
        {
            var item = Avaliar(produto, opcoes);
            if (item is not null) elegiveis.Add(item);
        }

        // R5: ESGOTADO -> CRITICO -> ATENCAO; dentro do grupo, por dias-até-ruptura ascendente.
        return elegiveis
            .OrderBy(i => OrdemSeveridade(i.Estado))
            .ThenBy(i => i.DiasAteRuptura ?? int.MaxValue)
            .ThenBy(i => i.Nome, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Avalia um produto. Retorna <c>null</c> quando NÃO é elegível para reposição.</summary>
    public ItemReposicao? Avaliar(ProdutoReposicaoSnapshot produto, OpcoesReposicao opcoes)
    {
        ArgumentNullException.ThrowIfNull(produto);
        ArgumentNullException.ThrowIfNull(opcoes);

        // R1: limiar resolvido AO VIVO na hierarquia Produto -> Categoria -> Config -> Default.
        var limiares = LimiarEstoqueResolver.Resolver(
            produto.ProdutoMinima, produto.ProdutoCritica,
            produto.CategoriaMinima, produto.CategoriaCritica,
            produto.ConfigMinima, produto.ConfigCritica);

        var vigente = produto.QuantidadeVigente;

        // R2: estado canônico. Esgotado (vigente 0) SEMPRE entra; <= crítico = CRITICO; < mínimo = ATENCAO.
        var estado = Classificar(vigente, limiares);
        if (estado is null) return null;

        // R3: confiança pela janela de histórico de saída.
        var confianca = produto.DiasHistoricoVelocidade < opcoes.DiasHistoricoMinimoConfianca
            ? ConfiancaReposicao.Baixa
            : ConfiancaReposicao.Alta;

        var sugerida = CalcularSugestao(produto, opcoes, limiares, vigente, confianca, out var motivoValidade);

        var diasAteRuptura = produto.VelocidadeMediaDia > 0m
            ? (int?)Math.Floor(vigente / produto.VelocidadeMediaDia)
            : null;

        var motivo = motivoValidade
            ?? MontarMotivo(estado.Value, vigente, limiares, produto.VelocidadeMediaDia, diasAteRuptura);

        return new ItemReposicao(
            produto.ProdutoId, produto.VariacaoId, produto.Nome, vigente,
            limiares.QuantidadeMinima, limiares.QuantidadeCritica,
            estado.Value, sugerida, confianca, motivo, diasAteRuptura, produto.FornecedorId);
    }

    // R3: quantidade sugerida = cobre lead time + cobertura, descontando vigente; com fallback e
    // guarda de validade.
    private decimal CalcularSugestao(
        ProdutoReposicaoSnapshot produto, OpcoesReposicao opcoes,
        LimiarEstoqueResolver.Limiares limiares, decimal vigente,
        ConfiancaReposicao confianca, out string? motivoValidade)
    {
        motivoValidade = null;

        decimal sugerida;
        if (confianca == ConfiancaReposicao.Baixa || produto.VelocidadeMediaDia <= 0m)
        {
            // Fallback: histórico curto/sem giro -> repor até o mínimo.
            sugerida = Math.Max(0m, limiares.QuantidadeMinima - vigente);
        }
        else
        {
            sugerida = calculadora.CalcularQuantidadeReposicao(
                Quantidade.From(vigente),
                produto.VelocidadeMediaDia,
                produto.LeadTimeDias,
                opcoes.DiasCoberturaAlvo,
                Math.Max(1, produto.TamanhoLote)).Value;
        }

        // Guarda de validade: não sugerir mais do que o produto vende antes de vencer.
        if (produto.VelocidadeMediaDia > 0m
            && produto.ValidadeMediaDiasRestantes is int diasValidade
            && diasValidade >= 0)
        {
            var maxAntesDeVencer = produto.VelocidadeMediaDia * diasValidade;
            var tetoPorValidade = Math.Max(0m, maxAntesDeVencer - vigente);
            if (tetoPorValidade < sugerida)
            {
                sugerida = tetoPorValidade;
                motivoValidade = "giro baixo / risco de validade";
            }
        }

        return sugerida;
    }

    private static EstadoReposicao? Classificar(decimal vigente, LimiarEstoqueResolver.Limiares limiares)
    {
        if (vigente <= 0m) return EstadoReposicao.Esgotado;
        if (vigente <= limiares.QuantidadeCritica) return EstadoReposicao.Critico;   // alinhado ao domínio Warn/Critical
        if (vigente < limiares.QuantidadeMinima) return EstadoReposicao.Atencao;      // '<' deliberado (qty==min não é baixo)
        return null;
    }

    private static int OrdemSeveridade(EstadoReposicao estado) => estado switch
    {
        EstadoReposicao.Esgotado => 0,
        EstadoReposicao.Critico => 1,
        EstadoReposicao.Atencao => 2,
        _ => 3
    };

    // R4: motivo em linguagem do lojista, nunca string técnica.
    private static string MontarMotivo(
        EstadoReposicao estado, decimal vigente,
        LimiarEstoqueResolver.Limiares limiares, decimal velocidade, int? diasAteRuptura)
    {
        if (estado == EstadoReposicao.Esgotado)
            return "Acabou — sem unidades vigentes";

        if (velocidade > 0m && diasAteRuptura is int dias)
        {
            var unidade = dias == 1 ? "dia" : "dias";
            var prefixo = dias <= 1 ? "dura só" : "dura ~";
            return $"Vende ~{Formatar(velocidade)}/dia, {prefixo} {dias} {unidade}";
        }

        return $"Abaixo do mínimo ({Formatar(vigente)} de {limiares.QuantidadeMinima})";
    }

    private static string Formatar(decimal valor)
    {
        var arredondado = Math.Round(valor, 1, MidpointRounding.AwayFromZero);
        return arredondado == Math.Truncate(arredondado)
            ? ((long)arredondado).ToString(CultureInfo.InvariantCulture)
            : arredondado.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
