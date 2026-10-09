using EasyStock.Api.Middleware;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Middleware;

public class IdempotencyOptionsTests
{
    [Theory]
    [InlineData("/api/atendimento/mensagens-programadas")]
    [InlineData("/api/atendimento/lembretes")]
    public void Programacao_usa_a_protecao_de_retentativa(string rota)
    {
        EasyStock.Api.Hosting.PipelineExtensions.ConfigurarRotasIdempotentes(new IdempotencyOptions())
            .PathMatchesAny(rota).Should().BeTrue();
    }

    [Fact]
    public void PathMatchesAny_sem_prefixos_sempre_falso()
    {
        var opts = new IdempotencyOptions();
        opts.PathMatchesAny("/api/itensestoque").Should().BeFalse();
    }

    [Fact]
    public void PathMatchesAny_match_por_prefixo_case_insensitive()
    {
        var opts = new IdempotencyOptions().Add("/api/itensestoque").Add("/api/vendas");

        opts.PathMatchesAny("/api/itensestoque").Should().BeTrue();
        opts.PathMatchesAny("/API/ItensEstoque/123").Should().BeTrue();
        opts.PathMatchesAny("/api/vendas/checkout").Should().BeTrue();
        opts.PathMatchesAny("/api/produtos").Should().BeFalse();
        opts.PathMatchesAny("").Should().BeFalse();
    }

    [Fact]
    public void Add_ignora_string_vazia_ou_null()
    {
        var opts = new IdempotencyOptions().Add("").Add(null!).Add("   ").Add("/api/x");
        opts.PathMatchesAny("/api/x").Should().BeTrue();
        opts.PathMatchesAny("/api/y").Should().BeFalse();
    }
}
