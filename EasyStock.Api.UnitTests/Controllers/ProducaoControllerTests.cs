using EasyStock.Api.Controllers;
using EasyStock.Api.Hosting;
using EasyStock.Api.Middleware;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>S23 (#1137): rotas de producao em porcoes.</summary>
public class ProducaoControllerTests
{
    [Theory]
    [InlineData("/api/producao")]
    [InlineData("/API/Producao/")]
    public void IdempotentePorChave(string path)
    {
        // POST api/producao entra na whitelist do IdempotencyMiddleware: a mesma
        // Idempotency-Key devolve a resposta gravada em vez de registrar outra producao.
        var opts = PipelineExtensions.ConfigurarRotasIdempotentes(new IdempotencyOptions());

        opts.PathMatchesAny(path).Should().BeTrue();
    }

    [Fact]
    public async Task LotesVencendo_DevolveSoItensComSaldo()
    {
        var empresaId = Guid.NewGuid();
        var repo = Substitute.For<IItemEstoqueRepository>();
        var user = Substitute.For<ICurrentUserAccessor>();
        user.EmpresaId.Returns(empresaId);

        var comSaldo = NovoItem(empresaId, 2);
        var zerado = NovoItem(empresaId, 0);
        repo.GetProximoVencimentoAsync(empresaId, 3, 1, 200, null)
            .Returns((new[] { comSaldo, zerado }.AsEnumerable(), 2));

        var controller = new ProducaoController(null!, repo, user);

        var result = await controller.LotesVencendo(empresaId, vencendoEmDias: 3);

        var ok = result.Should().BeAssignableTo<ObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain(comSaldo.Id.ToString()).And.NotContain(zerado.Id.ToString());
    }

    private static ItemEstoque NovoItem(Guid empresaId, decimal qtd) => new()
    {
        Id = Guid.NewGuid(),
        EmpresaId = empresaId,
        ProdutoId = Guid.NewGuid(),
        QuantidadeAtual = Quantidade.From(qtd),
        ValidadeEm = Validade.From(DateTime.UtcNow.AddDays(2)),
        CodigoLote = CodigoLote.From("LOT-260929-001")
    };
}
