using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>N12: o resumo diário vem do resumo do dia de Brasília e sai formatado em pt-BR para o template.</summary>
public class ConstrutorPayloadResumoDiarioTests
{
    private readonly IAnalyticsRepository _analytics = Substitute.For<IAnalyticsRepository>();
    private readonly Guid _empresaId = Guid.NewGuid();
    private static readonly DateOnly Dia = new(2026, 10, 1);

    private static ResumoDia Resumo(
        int entregues = 18, decimal faturamento = 1234.56m, decimal ticket = 68.59m, int pendentes = 3,
        decimal valorPendentes = 210m, bool aberta = false, bool fechada = true, decimal saldo = 0m,
        int pix = 4, decimal valorPix = 320.5m) =>
        new(entregues, faturamento, ticket, pendentes, valorPendentes, aberta, fechada, saldo, pix, valorPix,
            true, 0, 0);

    private async Task<JsonElement> ConstruirAsync(ResumoDia resumo)
    {
        _analytics.GetResumoDiaAsync(_empresaId, null).Returns(resumo);
        var json = await new ConstrutorPayloadResumoDiario(_analytics).ConstruirAsync(_empresaId, Dia);
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public void ConstroiOTipoResumoDiario() =>
        new ConstrutorPayloadResumoDiario(_analytics).Tipo.Should().Be(TipoEventoNotificacao.ResumoDiario);

    [Fact]
    public async Task FormataValoresEmPtBr()
    {
        var p = await ConstruirAsync(Resumo());

        p.GetProperty("data").GetString().Should().Be("01/10/2026");
        p.GetProperty("entregues").GetInt32().Should().Be(18);
        p.GetProperty("faturamento").GetString().Should().Be("R$ 1.234,56");
        p.GetProperty("ticket_medio").GetString().Should().Be("R$ 68,59");
        p.GetProperty("pendentes").GetInt32().Should().Be(3);
        p.GetProperty("valor_pendentes").GetString().Should().Be("R$ 210,00");
        p.GetProperty("pix_texto").GetString().Should().Be("4 Pix recebidos, somando R$ 320,50");
    }

    [Fact]
    public async Task CaixaAbertoFechadoESemCaixaViramTextoCerto()
    {
        (await ConstruirAsync(Resumo(aberta: true, fechada: false, saldo: 1500.5m)))
            .GetProperty("caixa_texto").GetString().Should().Be("aberto com saldo de R$ 1.500,50");
        (await ConstruirAsync(Resumo(aberta: false, fechada: true)))
            .GetProperty("caixa_texto").GetString().Should().Be("fechado");
        (await ConstruirAsync(Resumo(aberta: false, fechada: false)))
            .GetProperty("caixa_texto").GetString().Should().Be("sem movimento hoje");
    }

    [Fact]
    public async Task PixNoSingularENenhum()
    {
        (await ConstruirAsync(Resumo(pix: 1, valorPix: 10m))).GetProperty("pix_texto").GetString()
            .Should().Be("1 Pix recebido, somando R$ 10,00");
        (await ConstruirAsync(Resumo(pix: 0, valorPix: 0m))).GetProperty("pix_texto").GetString()
            .Should().Be("nenhum Pix recebido");
    }

    [Fact]
    public async Task UsaOResumoDoDiaDeBrasilia()
    {
        await ConstruirAsync(Resumo());

        // Resumo da empresa inteira (sem loja); o repositório recorta o dia pela janela de Brasília.
        await _analytics.Received(1).GetResumoDiaAsync(_empresaId, null);
    }

    [Fact]
    public async Task TrazAsVariaveisDoExemploDoCatalogo()
    {
        var p = await ConstruirAsync(Resumo());
        ExemplosDeEvento.TryObter(TipoEventoNotificacao.ResumoDiario, out var exemplo).Should().BeTrue();

        p.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo(exemplo.Keys);
    }
}
