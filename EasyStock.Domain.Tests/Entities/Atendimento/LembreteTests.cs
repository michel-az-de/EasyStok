using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>Lembrete da dona (S43): interno, manual ou automático, concluído por alguém ou sozinho.</summary>
public class LembreteTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ManualSemHorarioVenceAgoraEAvisaUmaVez()
    {
        var lembrete = Lembrete.Manual(Empresa, "  Ligar para o fornecedor  ", null, Usuario, Agora);

        lembrete.Texto.Should().Be("Ligar para o fornecedor");
        lembrete.VenceEm.Should().Be(Agora);
        lembrete.Referencia.Should().BeNull();
        lembrete.DeveAvisar(Agora).Should().BeTrue();

        lembrete.MarcarAvisado(Agora);
        lembrete.DeveAvisar(Agora.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void ManualNoFuturoSoAvisaQuandoVence()
    {
        var lembrete = Lembrete.Manual(Empresa, "Conferir entrega", Agora.AddHours(1), Usuario, Agora);

        lembrete.DeveAvisar(Agora).Should().BeFalse();
        lembrete.DeveAvisar(Agora.AddHours(1)).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TextoVazioRecusa(string texto)
    {
        var act = () => Lembrete.Manual(Empresa, texto, null, Usuario, Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void AutomaticoExigeReferenciaETipoAutomatico()
    {
        var semReferencia = () => Lembrete.Automatico(Empresa, TipoLembrete.PagamentoSemBaixa, " ", "texto", Agora);
        var tipoManual = () => Lembrete.Automatico(Empresa, TipoLembrete.Manual, "ref", "texto", Agora);

        semReferencia.Should().Throw<RegraDeDominioVioladaException>();
        tipoManual.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void ConcluirEIdempotenteEGuardaQuemConcluiu()
    {
        var lembrete = Lembrete.Manual(Empresa, "Separar embalagens", null, Usuario, Agora);

        lembrete.Concluir(Agora.AddMinutes(5), Usuario);
        lembrete.Concluir(Agora.AddMinutes(9));

        lembrete.EstaAberto.Should().BeFalse();
        lembrete.ConcluidoEm.Should().Be(Agora.AddMinutes(5));
        lembrete.ConcluidoPorUsuarioId.Should().Be(Usuario);
        lembrete.DeveAvisar(Agora.AddMinutes(10)).Should().BeFalse("concluído não avisa");
    }

    [Fact]
    public void MarcarVistoPreservaOPrimeiroCarimbo()
    {
        var lembrete = Lembrete.Manual(Empresa, "Ver estoque", null, Usuario, Agora);

        lembrete.MarcarVisto(Agora.AddMinutes(1));
        lembrete.MarcarVisto(Agora.AddMinutes(2));

        lembrete.VistoEm.Should().Be(Agora.AddMinutes(1));
    }
}
