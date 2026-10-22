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
    public static TheoryData<Exception, string> Motivos => new()
    {
        { new WhatsAppCloudException(131026, "(#131026) Message undeliverable", true, 400), "Falha no canal (código Meta 131026)." },
        { new WhatsAppCloudException(0, "Meta retornou 2xx sem message id.", true, 200), "O canal aceitou sem devolver o id da mensagem (CANAL_SEM_ID)." },
        { new WhatsAppCloudException(0, "Internal Server Error", false, 503), "O canal respondeu com erro (HTTP 503)." },
        { new WhatsAppCloudException(0, "Empresa sem número do WhatsApp.", true), "Empresa sem número do WhatsApp." },
        { new TimeoutException("The operation has timed out."), "O canal não respondeu a tempo (CANAL_SEM_RESPOSTA)." },
        { new TaskCanceledException("A task was canceled."), "O canal não respondeu a tempo (CANAL_SEM_RESPOSTA)." },
        { new HttpRequestException("No such host is known."), "Sem conexão com o canal (CANAL_SEM_CONEXAO)." },
        { new InvalidOperationException("Sequence contains no elements"), "Falha no canal (CANAL_ERRO)." },
    };

    /// <summary>#1765-11: o erro gravado é frase estável em português; o texto do provedor fica só no log.</summary>
    [Theory]
    [MemberData(nameof(Motivos))]
    public void MotivoGravadoSemOTextoDoProvedor(Exception ex, string esperado) =>
        ClassificadorFalhaEnvio.Motivo(ex).Should().Be(esperado);

    [Theory]
    [InlineData(131047, "Falha no canal (código Meta 131047).")]
    [InlineData(null, "Falha no canal (sem código da Meta).")]
    public void MotivoDoCallbackFailedPeloCodigo(int? codigo, string esperado) =>
        ClassificadorFalhaEnvio.MotivoCodigoMeta(codigo).Should().Be(esperado);
}
