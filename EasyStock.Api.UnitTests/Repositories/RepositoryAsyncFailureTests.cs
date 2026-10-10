using System.Data.Common;
using EasyStock.Application.Reporting;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Reporting;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EasyStock.Api.UnitTests.Repositories;

public sealed class RepositoryAsyncFailureTests
{
    [Theory]
    [InlineData("receitas", false)]
    [InlineData("relatorios", false)]
    [InlineData("avaliacoes", false)]
    [InlineData("receitas", true)]
    [InlineData("relatorios", true)]
    [InlineData("avaliacoes", true)]
    public async Task Consulta_propaga_falha_original_sem_AggregateException(string consulta, bool cancelamento)
    {
        Exception falha = cancelamento
            ? new OperationCanceledException("Consulta cancelada pelo provider.")
            : new InvalidOperationException("Conexão indisponível.");
        await using var db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql("Host=localhost;Database=teste;Username=teste;Password=teste")
            .AddInterceptors(new FalhaAoAbrirConexao(falha))
            .Options);

        Func<Task> executar = consulta switch
        {
            "receitas" => async () => await new ProdutoComposicaoRepository(db)
                .GetOndeInsumoAsync(Guid.NewGuid(), Guid.NewGuid()),
            "relatorios" => async () => await new ReportRunRepository(db)
                .ListMineAsync(Guid.NewGuid(), Guid.NewGuid(), new ReportListFilter(), 0, 10, CancellationToken.None),
            "avaliacoes" => async () => await new PedidoStorefrontRepository(db)
                .GetEntreguesElegiveisPraAvaliacaoAsync(DateTime.UtcNow),
            _ => throw new ArgumentOutOfRangeException(nameof(consulta))
        };

        var excecao = await Record.ExceptionAsync(executar);
        excecao.Should().BeSameAs(falha);
    }

    private sealed class FalhaAoAbrirConexao(Exception falha) : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default) => ValueTask.FromException<InterceptionResult>(falha);
    }
}
