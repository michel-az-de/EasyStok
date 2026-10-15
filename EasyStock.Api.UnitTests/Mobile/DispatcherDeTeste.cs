using EasyStock.Application.UseCases.EstornarMovimentoCaixa;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>Dependencias reais do <c>SyncMutationDispatcher</c> montadas sobre o contexto do teste.</summary>
internal static class DispatcherDeTeste
{
    /// <summary>#1520: a exclusao de lancamento do PWA estorna o movimento pelo caso de uso do ERP.</summary>
    public static EstornarMovimentoCaixaUseCase EstornoDeCaixa(EasyStockDbContext db) =>
        new(new CaixaRepository(db), db, NullLogger<EstornarMovimentoCaixaUseCase>.Instance);
}
