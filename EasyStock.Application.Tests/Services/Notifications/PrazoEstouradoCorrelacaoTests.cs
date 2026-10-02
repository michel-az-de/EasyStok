using EasyStock.Application.Services.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>N11: a chave determinística do <c>PrazoEstourado</c> cabe na coluna <c>CorrelationId varchar(64)</c>.</summary>
public class PrazoEstouradoCorrelacaoTests
{
    [Theory]
    [InlineData(TipoPrazo.ClienteSemResposta)]
    [InlineData(TipoPrazo.PedidoAtrasado)]
    [InlineData(TipoPrazo.ImpressaoTravada)]
    [InlineData(TipoPrazo.CaixaEsquecido)]
    public void CorrelationIdCabeEmSessentaEQuatroCaracteres(TipoPrazo tipo)
    {
        var id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

        var correlationId = PrazoEstouradoEvento.CorrelationId(tipo, id);

        correlationId.Length.Should().BeLessThanOrEqualTo(64);
        correlationId.Should().Be($"prazo:{PrazoEstouradoEvento.Nome(tipo)}:{id:N}");
        PrazoEstouradoEvento.CorrelationId(tipo, id).Should().Be(correlationId, "é determinística");
    }

    [Theory]
    [InlineData(1, "1 minuto")]
    [InlineData(35, "35 minutos")]
    [InlineData(65, "1 hora e 5 minutos")]
    [InlineData(120, "2 horas")]
    [InlineData(2880, "2 dias")]
    public void DuracaoSaiEmPortugues(int minutos, string esperado) =>
        PrazoEstouradoEvento.Duracao(TimeSpan.FromMinutes(minutos)).Should().Be(esperado);
}
