using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>#1411: no callback <c>failed</c> só insiste em código sabidamente temporário; o resto não reenvia.</summary>
public class ClassificadorFalhaEnvioTests
{
    [Theory]
    [InlineData(131000)]
    [InlineData(130429)]
    [InlineData(131016)]
    [InlineData(131056)]
    [InlineData(133004)]
    public void CodigoTemporarioConhecidoReenvia(int codigo) =>
        ClassificadorFalhaEnvio.ClassificarCodigoMeta(codigo).Should().Be(TipoFalhaEnvio.Temporaria);

    [Theory]
    [InlineData(999999)]
    [InlineData(131026)]
    [InlineData(190)]
    [InlineData(null)]
    public void CodigoDesconhecidoOuPermanenteNaoInsiste(int? codigo) =>
        ClassificadorFalhaEnvio.ClassificarCodigoMeta(codigo).Should().Be(TipoFalhaEnvio.Permanente);

    [Fact]
    public void NotificacoesContinuamTratandoCodigoForaDaTabelaComoTransitorio() =>
        CodigosErroMeta.Classificar(999999).Should().Be(ClasseErroMeta.Transitorio);

    /// <summary>#1765-1: sem resposta do canal a mensagem pode ter saído; não reenvia sozinho nem cai na reserva por SMS.</summary>
    [Fact]
    public void TimeoutDoCanalEIncerto()
    {
        ClassificadorFalhaEnvio.Classificar(new TimeoutException()).Should().Be(TipoFalhaEnvio.Incerta);
        ClassificadorFalhaEnvio.Classificar(new TaskCanceledException()).Should().Be(TipoFalhaEnvio.Incerta);
    }

    /// <summary>#1915: só reenvia sozinho quando nada saiu; a conexão que cai depois do POST pode ter entregue.</summary>
    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, TipoFalhaEnvio.Temporaria)]
    [InlineData(HttpRequestError.ConnectionError, TipoFalhaEnvio.Temporaria)]
    [InlineData(HttpRequestError.SecureConnectionError, TipoFalhaEnvio.Temporaria)]
    [InlineData(HttpRequestError.ResponseEnded, TipoFalhaEnvio.Incerta)]
    [InlineData(HttpRequestError.InvalidResponse, TipoFalhaEnvio.Incerta)]
    [InlineData(HttpRequestError.Unknown, TipoFalhaEnvio.Incerta)]
    public void RedeSoReenviaQuandoNadaSaiu(HttpRequestError erro, TipoFalhaEnvio esperado) =>
        ClassificadorFalhaEnvio.Classificar(new HttpRequestException(erro, "rede")).Should().Be(esperado);

    /// <summary>#1915: 5xx e 2xx sem message id podem ter saído (como no outbox e no envio de modelo); 4xx não saiu.</summary>
    [Theory]
    [InlineData(500, false, TipoFalhaEnvio.Incerta)]
    [InlineData(503, false, TipoFalhaEnvio.Incerta)]
    [InlineData(200, true, TipoFalhaEnvio.Incerta)]
    [InlineData(429, false, TipoFalhaEnvio.Temporaria)]
    [InlineData(400, true, TipoFalhaEnvio.Permanente)]
    [InlineData(null, true, TipoFalhaEnvio.Permanente)]
    [InlineData(null, false, TipoFalhaEnvio.Temporaria)]
    public void MetaPeloStatusHttp(int? status, bool permanente, TipoFalhaEnvio esperado) =>
        ClassificadorFalhaEnvio.Classificar(new WhatsAppCloudException(131000, "Meta", permanente, status)).Should().Be(esperado);
}
