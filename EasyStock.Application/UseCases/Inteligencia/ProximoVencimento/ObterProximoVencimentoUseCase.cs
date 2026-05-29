using System.Diagnostics;
using EasyStock.Application.Configuration;

namespace EasyStock.Application.UseCases.Inteligencia.ProximoVencimento;

public class ObterProximoVencimentoUseCase(
    IItemEstoqueRepository itemEstoqueRepository,
    IConfiguracaoLojaRepository configuracaoRepository,
    IEasyStockConfiguracoes config,
    ILogger<ObterProximoVencimentoUseCase> logger)
{
    public async Task<(IEnumerable<ProximoVencimentoResult> Items, int Total)> ExecuteAsync(ObterProximoVencimentoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var stopwatch = Stopwatch.StartNew();

        var configuracao = cmd.LojaId.HasValue
            ? await configuracaoRepository.GetByLojaIdAsync(cmd.LojaId.Value).ConfigureAwait(false)
            : null;

        var diasEfetivos = cmd.Dias
            ?? configuracao?.DiasAlertaValidade
            ?? config.DiasAlertaVencimento;

        var (items, totalCount) = await itemEstoqueRepository.GetProximoVencimentoAsync(
            cmd.EmpresaId, diasEfetivos, cmd.Page, cmd.PageSize, cmd.LojaId).ConfigureAwait(false);

        stopwatch.Stop();

        var resultados = items.Select(item =>
        {
            dynamic dinamico = item;
            var diasAteVencimento = (int)Math.Ceiling(((DateTime)dinamico.DataVencimento - DateTime.UtcNow).TotalDays);
            return new ProximoVencimentoResult(
                (Guid)dinamico.Id,
                (Guid)dinamico.ProdutoId,
                (string?)dinamico.Produto?.Nome ?? (string?)dinamico.CodigoInterno,
                (string?)dinamico.CodigoInterno,
                (decimal)dinamico.QuantidadeAtual,
                (DateTime)dinamico.DataVencimento,
                diasAteVencimento);
        });

        logger.LogInformation(
            "Próximo vencimento obtido em {Ms}ms para empresa {EmpresaId} | "
            + "Dias: {Dias}, Loja: {LojaId}, Total: {Total}",
            stopwatch.ElapsedMilliseconds,
            cmd.EmpresaId,
            diasEfetivos,
            cmd.LojaId ?? Guid.Empty,
            totalCount);

        return (resultados, totalCount);
    }
}
