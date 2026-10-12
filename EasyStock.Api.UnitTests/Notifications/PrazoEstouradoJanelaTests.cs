using FluentAssertions;
using EasyStock.Api.Data;
using EasyStock.Application.Common;
using EasyStock.Application.Services.Notifications;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>N11: a rotina <c>prazo_estourado_global</c> do seed adia o aviso das 23:30 para as 07:00, nunca o suprime.</summary>
public class PrazoEstouradoJanelaTests
{
    [Fact]
    public void EventoAs2330FicaParaAs0700DeBrasilia()
    {
        var rotina = NotificacoesGlobaisSeed.BuildDefaultRotinas().Single(r => r.Codigo == "prazo_estourado_global");
        var meiaNoiteMenos30 = HorarioBrasil.InstanteUtc(new DateOnly(2026, 10, 2), new TimeOnly(23, 30));

        var abertura = JanelaDeEnvio.ProximaAbertura(rotina.JanelaInicio, rotina.JanelaFim, meiaNoiteMenos30);

        abertura.Should().Be(HorarioBrasil.InstanteUtc(new DateOnly(2026, 10, 3), new TimeOnly(7, 0)));
        JanelaDeEnvio.ProximaAbertura(rotina.JanelaInicio, rotina.JanelaFim,
                HorarioBrasil.InstanteUtc(new DateOnly(2026, 10, 2), new TimeOnly(14, 0)))
            .Should().BeNull("dentro da janela sai na hora");
    }
}
