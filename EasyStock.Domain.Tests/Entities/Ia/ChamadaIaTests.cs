using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Enums.Ia;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Ia;

public class ChamadaIaTests
{
    private static readonly DateTime Agora = new(2026, 11, 20, 15, 0, 0, DateTimeKind.Unspecified);

    private static ChamadaIa Registrar(Guid? empresaId = null, int entrada = 100, string? modelo = "deepseek-flash") =>
        ChamadaIa.Registrar(empresaId ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), modelo, FinalidadeIa.Atendimento,
            null, entrada, 20, 5, 0, 0.0001m, 900, DesfechoChamadaIa.Ok, Agora);

    [Fact]
    public void GuardaNumerosEDesfechoEmUtc()
    {
        var c = Registrar();
        c.TokensEntrada.Should().Be(100);
        c.Finalidade.Should().Be(FinalidadeIa.Atendimento);
        c.Desfecho.Should().Be(DesfechoChamadaIa.Ok);
        c.CriadoEm.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ProvedorEModeloSaoOpcionaisAteORoteador()
    {
        // S2a grava com o cliente e o .env atuais, antes de existir provedor cadastrado.
        var c = ChamadaIa.Registrar(Guid.NewGuid(), null, null, "deepseek-v4p1-flash", FinalidadeIa.Descricao,
            null, 1, 1, 0, 0, null, 10, DesfechoChamadaIa.Erro, Agora);
        c.ProvedorId.Should().BeNull();
        c.CustoUsd.Should().BeNull();
    }

    [Fact]
    public void RecusaEmpresaVazia()
    {
        var act = () => Registrar(empresaId: Guid.Empty);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void RecusaTokensNegativos()
    {
        var act = () => Registrar(entrada: -1);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void ModeloVazioViraDesconhecidoECortaNoLimite()
    {
        Registrar(modelo: " ").ModeloRespondido.Should().Be("desconhecido");
        Registrar(modelo: new string('m', 300)).ModeloRespondido.Should().HaveLength(ChamadaIa.ModeloTamanhoMaximo);
    }
}
