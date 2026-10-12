using EasyStock.Application.Ports.Output.Notifications;

namespace EasyStock.Application.Tests.Ports.Notifications;

/// <summary>
/// N2: o desfecho do envio é tipado e aditivo. O construtor antigo continua valendo (14 arquivos o usam) e o
/// <see cref="ResultadoEnvio.Desfecho"/> sai de <c>Sucesso</c> e <c>FalhaPermanente</c>; só as fábricas
/// <see cref="ResultadoEnvio.Simulado"/> e <see cref="ResultadoEnvio.Indeterminado"/> o fixam.
/// </summary>
public class ResultadoEnvioTests
{
    [Fact]
    public void Construtor_antigo_deriva_o_desfecho_de_Sucesso_e_FalhaPermanente()
    {
        new ResultadoEnvio(true, "smtp").Desfecho.Should().Be(DesfechoEnvio.Enviado);
        new ResultadoEnvio(false, "smtp", "421").Desfecho.Should().Be(DesfechoEnvio.FalhaTransitoria);
        new ResultadoEnvio(false, "smtp", "550", FalhaPermanente: true).Desfecho.Should().Be(DesfechoEnvio.FalhaPermanente);
    }

    [Fact]
    public void Simulado_conta_como_Sucesso_para_quem_esta_fora_do_motor()
    {
        // CanalSms (atendimento) só olha Sucesso: no desenvolvimento
        // o stub não pode virar erro para ele. Só o dispatcher grava o status Simulado.
        var resultado = ResultadoEnvio.Simulado("stub", duracaoMs: 7);

        resultado.Desfecho.Should().Be(DesfechoEnvio.Simulado);
        resultado.Sucesso.Should().BeTrue();
        resultado.FalhaPermanente.Should().BeFalse();
        resultado.ProviderUsado.Should().Be("stub");
        resultado.DuracaoMs.Should().Be(7);
        resultado.ErroDetalhado.Should().BeNull();
    }

    [Fact]
    public void Indeterminado_nao_e_sucesso_nem_falha_permanente()
    {
        var resultado = ResultadoEnvio.Indeterminado("twilio", "HTTP 503", statusHttp: 503, duracaoMs: 42);

        resultado.Desfecho.Should().Be(DesfechoEnvio.Indeterminado);
        resultado.Sucesso.Should().BeFalse("não há como confirmar a entrega");
        resultado.FalhaPermanente.Should().BeFalse("o provider pode ter entregado: reenviar duplicaria");
        resultado.ProviderUsado.Should().Be("twilio");
        resultado.ErroDetalhado.Should().Be("HTTP 503");
        resultado.StatusHttp.Should().Be(503);
        resultado.DuracaoMs.Should().Be(42);
    }

    [Fact]
    public void IdExterno_e_aditivo_e_opcional()
    {
        new ResultadoEnvio(true, "meta").IdExterno.Should().BeNull();

        var comId = new ResultadoEnvio(true, "meta") { IdExterno = "wamid.ABC" };

        comId.IdExterno.Should().Be("wamid.ABC");
        comId.Desfecho.Should().Be(DesfechoEnvio.Enviado);
    }

    [Fact]
    public void Desfecho_sobrevive_ao_with()
    {
        var simulado = ResultadoEnvio.Simulado("console");

        (simulado with { DuracaoMs = 5 }).Desfecho.Should().Be(DesfechoEnvio.Simulado);
    }
}
