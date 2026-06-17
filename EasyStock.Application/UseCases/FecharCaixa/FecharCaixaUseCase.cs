using EasyStock.Application.UseCases.Caixa;

namespace EasyStock.Application.UseCases.FecharCaixa;

public sealed record FecharCaixaCommand(
    [property: Required] Guid EmpresaId,
    DateOnly? Data = null,
    Guid? LojaId = null,
    string? Observacoes = null,
    Guid? FechadoPorUserId = null,
    [property: MaxLength(120)] string? FechadoPorNome = null);

/// <summary>
/// Fecha a sessão de caixa ABERTA (server-authoritative): resolve a sessão via
/// <see cref="ICaixaRepository.GetAberturaPendenteAsync"/> — que pode ser de um dia
/// anterior (operador esqueceu de fechar) — e data o snapshot no DIA CIVIL DA ABERTURA,
/// não em "hoje". Isso libera o caixa de hoje para ser aberto (corrige o bug em que fechar
/// uma sessão de ontem gravava um fechamento de hoje e bloqueava a abertura — issue #640).
///
/// Decisão de produto (ADR-0034): a sessão é atribuída só ao dia civil da abertura;
/// transações pós-meia-noite pertencem ao caixa do dia em que ocorreram. Lançamentos em
/// dias civis intermediários de uma sessão multi-dia não são cobertos → são detectados e
/// avisados (persistidos em <see cref="FechamentoCaixa.Observacoes"/> + log), nunca
/// descartados em silêncio.
///
/// Idempotente: se o dia já fechou, retorna o snapshot existente. Sem sessão aberta, NÃO
/// fabrica fechamento (evita, em corrida/forja, gravar um fechamento de hoje que voltaria a
/// bloquear a abertura). Corrida de duplo-fechamento é resolvida pelo índice único coalescido.
/// </summary>
public class FecharCaixaUseCase(
    ICaixaRepository repo,
    IUnitOfWork uow,
    ILogger<FecharCaixaUseCase> logger)
{
    public async Task<FechamentoCaixaResult> ExecuteAsync(FecharCaixaCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var hoje = HorarioBrasil.Hoje();   // dia operacional em Brasilia (alinha com o card; BUG-09)

        // Fonte de verdade: a sessão aberta (última abertura sem fechamento posterior), que
        // pode ser de um dia anterior. É o mesmo sinal que a tela usa (ObterCaixaDia, #596).
        var pendente = await repo.GetAberturaPendenteAsync(cmd.EmpresaId, cmd.LojaId);

        if (pendente == null)
        {
            // Sem sessão aberta: NUNCA fabricar fechamento. Em corrida de duplo-clique (outro
            // device fechou antes) ou requisição forjada, gravar um fechamento de "hoje" sem
            // abertura voltaria a bloquear a abertura de hoje — o próprio bug que este UC corrige
            // (issue #640). Só honra idempotência de um snapshot já existente.
            var diaSemSessao = cmd.Data ?? hoje;
            var jaFechado = await repo.GetFechamentoDoDiaAsync(cmd.EmpresaId, diaSemSessao, cmd.LojaId);
            if (jaFechado != null) return Map(jaFechado);
            throw new UseCaseValidationException("Não há caixa aberto para fechar.");
        }

        // Dia-alvo = dia civil (Brasília) da abertura. Fechar uma sessão de ontem grava o
        // snapshot datado em ontem e deixa o caixa de hoje livre para abrir.
        var dia = HorarioBrasil.DataOperacional(pendente.DataMovimento);

        // Data explícita do cliente tem que casar com a sessão aberta — não confiar cegamente
        // (evita gravar fechamento para data arbitrária; a UI nem manda data).
        if (cmd.Data != null && cmd.Data != dia)
            throw new UseCaseValidationException(
                "A data informada não corresponde à sessão de caixa aberta.");

        var existente = await repo.GetFechamentoDoDiaAsync(cmd.EmpresaId, dia, cmd.LojaId);
        if (existente != null) return Map(existente);

        // Totais SÓ da janela civil do dia da abertura (decisão de produto ADR-0034: a sessão é
        // atribuída ao dia civil da abertura; transações pós-meia-noite vão pro caixa do dia delas).
        var movList = (await repo.GetMovimentosDoDiaAsync(cmd.EmpresaId, dia, cmd.LojaId)).ToList();
        decimal saldoInicial = movList.Where(m => m.Tipo == "abertura").Sum(m => m.Valor);
        decimal totalEntradas = movList.Where(m => m.Tipo == "entrada").Sum(m => m.Valor);
        decimal totalSaidas   = movList.Where(m => m.Tipo == "saida").Sum(m => m.Valor);
        var totalVendas = await repo.GetTotalVendasDoDiaAsync(cmd.EmpresaId, dia, cmd.LojaId);
        var totalPagamentosPedidos = await repo.GetTotalPagamentosPedidosDoDiaAsync(cmd.EmpresaId, dia, cmd.LojaId);

        // Sessão multi-dia: lançamentos em dias civis INTERMEDIÁRIOS [fim do dia da abertura,
        // início de hoje) não entram neste fechamento e não pertencem ao caixa de hoje → ficariam
        // órfãos. Não descartar em silêncio: detecta e anexa aviso persistido + log (ADR-0033).
        var observacoes = cmd.Observacoes;
        if (dia < hoje)
        {
            var iniInter = HorarioBrasil.JanelaDiaUtc(dia).FimUtc;   // = 00:00 do dia seguinte à abertura
            var fimInter = HorarioBrasil.JanelaDiaUtc(hoje).IniUtc;  // = 00:00 de hoje
            if (iniInter < fimInter)
            {
                var movsInter = await repo.GetMovimentosNoIntervaloAsync(cmd.EmpresaId, iniInter, fimInter, cmd.LojaId);
                var vendasInter = await repo.GetTotalVendasNoIntervaloAsync(cmd.EmpresaId, iniInter, fimInter, cmd.LojaId);
                var pagInter = await repo.GetTotalPagamentosPedidosNoIntervaloAsync(cmd.EmpresaId, iniInter, fimInter, cmd.LojaId);
                if (movsInter.Any() || vendasInter != 0m || pagInter != 0m)
                {
                    var aviso = $"Atencao: ha lancamentos entre {dia.AddDays(1):dd/MM/yyyy} e " +
                                $"{hoje.AddDays(-1):dd/MM/yyyy} sob esta sessao que NAO foram incluidos " +
                                "neste fechamento (revisar/reconciliar manualmente).";
                    observacoes = string.IsNullOrWhiteSpace(observacoes) ? aviso : $"{observacoes}\n{aviso}";
                    logger.LogWarning(
                        "Fechamento {Dia} (empresa {Empresa}, loja {Loja}): lancamentos em dias intermediarios nao cobertos.",
                        dia, cmd.EmpresaId, cmd.LojaId);
                }
            }
        }

        var fechamento = FechamentoCaixa.Criar(
            cmd.EmpresaId, dia, saldoInicial, totalVendas,
            totalPagamentosPedidos, totalEntradas, totalSaidas, cmd.LojaId);
        fechamento.FechadoPorUserId = cmd.FechadoPorUserId;
        fechamento.FechadoPorNome = cmd.FechadoPorNome;
        fechamento.Observacoes = observacoes;

        // Cria movimento "fechamento" como marcador (não move saldo). Usa o instante real
        // do fechamento (DateTime.UtcNow, Kind=Utc): data.ToDateTime(TimeOnly.MaxValue) produz
        // Kind=Unspecified, que o Npgsql rejeita na coluna timestamptz, abortando o CommitAsync
        // inteiro (o caixa nunca fecha). UtcNow também fica sempre após a abertura no
        // OrderByDescending(DataMovimento) de GetAberturaPendenteAsync (issue 615).
        var mov = MovimentoCaixa.Criar(cmd.EmpresaId, "fechamento", 0m,
            DateTime.UtcNow, cmd.LojaId);
        mov.Descricao = $"Fechamento {dia:yyyy-MM-dd}: saldo final {fechamento.SaldoFinal:F2}";
        mov.RegistradoPorUserId = cmd.FechadoPorUserId;
        mov.RegistradoPorNome = cmd.FechadoPorNome;
        mov.Origem = "web";

        try
        {
            await repo.AddFechamentoAsync(fechamento);
            await repo.AddMovimentoAsync(mov);
            await uow.CommitAsync();
        }
        catch (Exception ex)
        {
            // Race: 2 fechamentos concorrentes do mesmo dia/loja. O índice único coalescido
            // (migration AddUniqueFechamentoCaixaPorDia) rejeita o perdedor; re-query confirma o
            // vencedor. Se não houver vencedor, é erro real -> propaga. (Espelha EmitirNfceUseCase.)
            var vencedor = await repo.GetFechamentoDoDiaAsync(cmd.EmpresaId, dia, cmd.LojaId);
            if (vencedor == null) throw;
            logger.LogInformation(ex,
                "Fechamento {Dia} race-resolved: outra TX venceu o indice unico (empresa {Empresa}).",
                dia, cmd.EmpresaId);
            return Map(vencedor);
        }

        logger.LogInformation("Caixa {Data} fechado (saldo final={Saldo}, vendas={Vendas}).",
            dia, fechamento.SaldoFinal, totalVendas);
        return Map(fechamento);
    }

    internal static FechamentoCaixaResult Map(FechamentoCaixa f) => new(
        f.Id, f.EmpresaId, f.LojaId, f.Data,
        f.SaldoInicial, f.TotalVendas, f.TotalPagamentosPedidos,
        f.TotalEntradasExtras, f.TotalSaidasExtras, f.SaldoFinal,
        f.FechadoPorUserId, f.FechadoPorNome, f.Observacoes, f.FechadoEm);
}
