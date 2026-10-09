using EasyStock.Application.UseCases.Producao;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;

namespace EasyStock.Application.UseCases.Atendimento.Producao;

/// <param name="ItemEstoqueId">Lote escolhido. Null = sai por FEFO.</param>
/// <param name="Texto">Obrigatório quando o motivo é Outro.</param>
public sealed record LancarPerdaInput(Guid ProdutoId, Guid? ItemEstoqueId, decimal Quantidade, MotivoPerda Motivo, string? Texto = null);

public sealed record PerdaLancada(IReadOnlyList<Guid> Movimentacoes, decimal Quantidade, decimal Valor);

public sealed record LancamentoDePerda(
    Guid MovimentacaoId, DateTime Data, Guid ProdutoId, string Produto, string? Lote,
    decimal Quantidade, decimal Valor, MotivoPerda Motivo, string? Descricao, bool Desfeita);

public sealed record PerdaPorMotivo(MotivoPerda Motivo, string Rotulo, decimal Quantidade, decimal Valor);

public sealed record PerdaPorProduto(Guid ProdutoId, string Produto, decimal Quantidade, decimal Valor);

/// <param name="Valor">Total do período sem as perdas desfeitas.</param>
public sealed record ResumoDePerdas(
    DateOnly De, DateOnly Ate, decimal Valor, IReadOnlyList<PerdaPorMotivo> PorMotivo, IReadOnlyList<PerdaPorProduto> PorProduto,
    IReadOnlyList<LancamentoDePerda> Lancamentos);

public sealed record LoteVencido(
    Guid ItemEstoqueId, Guid ProdutoId, string Produto, string? Lote, decimal Quantidade, DateTime ValidadeEm, int DiasVencido, decimal Valor);

/// <summary>
/// Controle de perdas no console (M2.6, #1511).
/// <para>
/// <b>D-M2-02 = a:</b> o motivo é a natureza da saída (<see cref="MotivoPerdaExtensions"/>), sem campo
/// novo; "Outro" exige texto. A perda sai do lote escolhido ou por FEFO, grava o custo de cada lote
/// e não cria descoberto (perda de algo que não existe é ajuste de contagem).
/// </para>
/// <para>
/// <b>D-M2-06:</b> acima de <see cref="LimiteSemGerente"/>, pelo custo dos lotes, só o Gerente lança.
/// Desfazer é o estorno de saída que já existe (Gerente).
/// </para>
/// <para>
/// O resumo não conta ajuste de contagem (o card antigo misturava) nem a baixa de insumo da produção
/// (M2.4b, mesma natureza da degustação). Lote vencido com saldo vira sugestão, sem lançar sozinho.
/// </para>
/// </summary>
public sealed class PerdasDaProducaoUseCase(
    IItemEstoqueRepository itemEstoqueRepository,
    IMovimentacaoEstoqueRepository movimentacaoRepository,
    RegistrarSaidaEstoqueUseCase registrarSaida,
    TimeProvider relogio)
{
    public const decimal LimiteSemGerente = 50m;
    public const string CodigoExigeGerente = "PERDA_EXIGE_GERENTE";
    public const int DiasDoResumo = 7;

    private static readonly NaturezaMovimentacaoEstoque[] NaturezasDePerda =
        Enum.GetValues<MotivoPerda>().Select(m => m.Natureza()).ToArray();

    public async Task<PerdaLancada> LancarAsync(Guid empresaId, bool podeAcimaDoLimite, LancarPerdaInput input, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        Validar(input);

        var natureza = input.Motivo.Natureza();
        var permitirVencido = natureza.PermiteBaixaDeLoteVencido();
        var hoje = HorarioBrasil.DataOperacional(relogio.GetUtcNow().UtcDateTime);
        var plano = await PlanoDeSaidaAsync(empresaId, input, permitirVencido, hoje);

        var valor = Math.Round(plano.Sum(p => p.Quantidade * p.Lote.CustoUnitario.Valor), 2, MidpointRounding.AwayFromZero);
        if (valor > LimiteSemGerente && !podeAcimaDoLimite)
            throw new UseCaseValidationException(CodigoExigeGerente,
                $"Perda de {Reais(valor)} passa de {Reais(LimiteSemGerente)}: só o Gerente lança.");

        var texto = input.Texto?.Trim();
        var descricao = $"Perda · {input.Motivo.Rotulo()}" + (string.IsNullOrEmpty(texto) ? "" : $": {texto}");
        var agora = relogio.GetUtcNow().UtcDateTime;
        var saida = await registrarSaida.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            empresaId,
            plano.Select(p => new RegistrarSaidaEstoqueItemCommand(p.Lote.Id, p.Quantidade, p.Lote.CustoUnitario.Valor, descricao)).ToList(),
            agora, agora, null, null, natureza, CanalVenda.Outro, descricao));

        return new PerdaLancada(saida.Itens.Select(i => i.MovimentacaoId).ToList(), input.Quantidade, valor);
    }

    public async Task<ResumoDePerdas> ResumoAsync(Guid empresaId, DateOnly? de = null, DateOnly? ate = null, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var hoje = HorarioBrasil.DataOperacional(relogio.GetUtcNow().UtcDateTime);
        var fim = ate ?? hoje;
        var inicio = de ?? fim.AddDays(-(DiasDoResumo - 1));
        if (inicio > fim) throw new UseCaseValidationException("O início do período vem depois do fim.");

        var movimentos = await movimentacaoRepository.GetSaidasPorNaturezaAsync(empresaId, NaturezasDePerda,
            HorarioBrasil.JanelaDiaUtc(inicio).IniUtc, HorarioBrasil.JanelaDiaUtc(fim).FimUtc, ct);

        var lancamentos = movimentos
            // M2.4b: a baixa de insumo da produção também é UsoInterno, mas não é perda.
            .Where(m => !(m.Descricao ?? "").StartsWith(BaixaDeInsumosDaProducao.PrefixoDaDescricao, StringComparison.Ordinal))
            .Select(m => new LancamentoDePerda(
                m.Id, m.DataMovimentacao, m.ProdutoId, m.Produto?.Nome ?? "(produto)", m.ItemEstoque?.CodigoLote?.Value,
                m.Quantidade.Value, ValorDe(m), m.Natureza.MotivoDaPerda()!.Value, m.Descricao, m.EstornadaEm.HasValue))
            .ToList();
        var valendo = lancamentos.Where(l => !l.Desfeita).ToList();

        return new ResumoDePerdas(inicio, fim, valendo.Sum(l => l.Valor),
            valendo.GroupBy(l => l.Motivo)
                .Select(g => new PerdaPorMotivo(g.Key, g.Key.Rotulo(), g.Sum(l => l.Quantidade), g.Sum(l => l.Valor)))
                .OrderByDescending(p => p.Valor).ToList(),
            valendo.GroupBy(l => (l.ProdutoId, l.Produto))
                .Select(g => new PerdaPorProduto(g.Key.ProdutoId, g.Key.Produto, g.Sum(l => l.Quantidade), g.Sum(l => l.Valor)))
                .OrderByDescending(p => p.Valor).ToList(),
            lancamentos);
    }

    public async Task<IReadOnlyList<LoteVencido>> VencidosAsync(Guid empresaId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var agora = relogio.GetUtcNow().UtcDateTime;
        var hoje = HorarioBrasil.DataOperacional(agora);
        return (await itemEstoqueRepository.GetComSaldoEValidadeAteAsync(empresaId, agora, ct))
            .Where(i => i.ValidadeEm!.EstaVencido(hoje))
            .Select(i => new LoteVencido(
                i.Id, i.ProdutoId, i.Produto?.Nome ?? "(produto)", i.CodigoLote?.Value, i.QuantidadeAtual.Value,
                i.ValidadeEm!.DataValidade, -i.ValidadeEm.DiasAteVencimento(hoje),
                Math.Round(i.QuantidadeAtual.Value * i.CustoUnitario.Valor, 2, MidpointRounding.AwayFromZero)))
            .ToList();
    }

    private static void Validar(LancarPerdaInput input)
    {
        if (input.ProdutoId == Guid.Empty) throw new UseCaseValidationException("Escolha o prato ou o insumo perdido.");
        if (input.Quantidade <= 0) throw new UseCaseValidationException("Quantidade perdida maior que zero.");
        if (!Enum.IsDefined(input.Motivo)) throw new UseCaseValidationException("Escolha o motivo da perda.");
        if (input.Motivo == MotivoPerda.Outro && (input.Texto?.Trim().Length ?? 0) < 3)
            throw new UseCaseValidationException("Em \"Outro\", escreva o motivo da perda.");
    }

    private async Task<List<(ItemEstoque Lote, decimal Quantidade)>> PlanoDeSaidaAsync(
        Guid empresaId, LancarPerdaInput input, bool permitirVencido, DateOnly hoje)
    {
        IReadOnlyCollection<ItemEstoque> lotes;
        if (input.ItemEstoqueId is { } loteId)
        {
            var lote = await itemEstoqueRepository.GetByIdAsync(empresaId, loteId);
            if (lote is null || lote.ProdutoId != input.ProdutoId) throw new UseCaseValidationException("Lote não encontrado para esse produto.");
            if (!permitirVencido && lote.ValidadeEm?.EstaVencido(hoje) == true)
                throw new UseCaseValidationException("Lote vencido não se doa nem vai para degustação: lance como Vencido.");
            lotes = [lote];
        }
        else
        {
            lotes = await itemEstoqueRepository.GetLotesDisponiveisParaSaidaAsync(empresaId, input.ProdutoId, null, true, permitirVencido);
        }

        var plano = new List<(ItemEstoque, decimal)>();
        var restante = input.Quantidade;
        foreach (var lote in lotes.Where(l => l.QuantidadeAtual.Value > 0))
        {
            if (restante <= 0) break;
            var tirar = Math.Min(lote.QuantidadeAtual.Value, restante);
            plano.Add((lote, tirar));
            restante -= tirar;
        }
        if (restante > 0)
            throw new UseCaseValidationException(
                $"Só há {input.Quantidade - restante:0.###} em estoque{(input.ItemEstoqueId is null ? "" : " nesse lote")}: confira a quantidade.");
        return plano;
    }

    private static string Reais(decimal valor) => valor.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

    private static decimal ValorDe(MovimentacaoEstoque m) =>
        m.ValorTotal?.Valor ?? (m.ValorUnitario?.Valor ?? 0m) * m.Quantidade.Value;
}
