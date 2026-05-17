using EasyStock.Api.Services.Helpdesk;
using EasyStock.Domain.Enums;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Services;

/// <summary>
/// Cobertura do fallback hardcoded do SlaResolver (defesa contra DB vazio).
/// Os 3 niveis de hierarquia anteriores (empresa, plano, global) usam queries
/// EF e ficam cobertos por integration tests com Postgres real.
/// </summary>
public class SlaResolverFallbackTests
{
    [Theory]
    [InlineData(TicketPrioridade.Critica, 30, 240)]
    [InlineData(TicketPrioridade.Alta, 120, 480)]
    [InlineData(TicketPrioridade.Normal, 480, 1440)]
    [InlineData(TicketPrioridade.Baixa, 1440, 4320)]
    public void Fallback_retorna_minutos_alinhados_com_produto(
        TicketPrioridade prioridade, int minResp, int minResol)
    {
        var (resp, resol) = SlaResolver.FallbackHardcoded(prioridade);
        resp.Should().Be(minResp);
        resol.Should().Be(minResol);
    }

    [Fact]
    public void Fallback_resposta_sempre_menor_ou_igual_que_resolucao()
    {
        foreach (TicketPrioridade p in Enum.GetValues<TicketPrioridade>())
        {
            var (resp, resol) = SlaResolver.FallbackHardcoded(p);
            resp.Should().BeLessThanOrEqualTo(resol,
                $"resposta nao pode ser maior que resolucao para {p}");
            resp.Should().BeGreaterThan(0);
            resol.Should().BeGreaterThan(0);
        }
    }
}
