using System.Reflection;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.EstornarMovimentoCaixa;
using EasyStock.Application.UseCases.ObterCaixaDia;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>Dependencias reais do <c>SyncMutationDispatcher</c> montadas sobre o contexto do teste.</summary>
internal static class DispatcherDeTeste
{
    /// <summary>#1520: a exclusao de lancamento do PWA estorna o movimento pelo caso de uso do ERP.</summary>
    public static EstornarMovimentoCaixaUseCase EstornoDeCaixa(EasyStockDbContext db)
    {
        var repo = SemTravaDeTransacao(new CaixaRepository(db));
        return new(repo, db, new ObterCaixaDiaUseCase(new CaixaSaldoCalculator(repo)),
            NullLogger<EstornarMovimentoCaixaUseCase>.Instance);
    }

    // #2736: o estorno toma a trava do Caixa, que exige transação real (advisory lock do Postgres). O contexto
    // InMemory ignora a transação, então a trava é a única chamada que o teste dispensa; o resto vai ao repositório.
    // A trava no caminho do aparelho é provada no Postgres real em SyncEstornoTravaE2ETests (#2765).
    private static ICaixaRepository SemTravaDeTransacao(ICaixaRepository repo)
    {
        var proxy = DispatchProxy.Create<ICaixaRepository, DispensaTrava>();
        ((DispensaTrava)(object)proxy).Alvo = repo;
        return proxy;
    }

    private class DispensaTrava : DispatchProxy
    {
        public ICaixaRepository Alvo { get; set; } = null!;

        protected override object? Invoke(MethodInfo? metodo, object?[]? args)
        {
            if (metodo!.Name == nameof(ICaixaRepository.TravarSaidasAsync)) return Task.CompletedTask;
            try { return metodo.Invoke(Alvo, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }
    }
}
