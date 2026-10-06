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
}
