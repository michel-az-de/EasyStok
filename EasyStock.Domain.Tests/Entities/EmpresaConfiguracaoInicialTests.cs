using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

/// <summary>#2070: a empresa nasce com a configuração inicial pendente; concluir vale uma vez, adiar só enquanto pendente.</summary>
public class EmpresaConfiguracaoInicialTests
{
    private static readonly DateTime Agora = new(2026, 10, 26, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EmpresaNovaNasceComAConfiguracaoPendenteESemAdiamento()
    {
        var empresa = Empresa.Criar("Casa da Baba", "11111111000191");

        empresa.ConfiguracaoInicialConcluidaEm.Should().BeNull();
        empresa.ConfiguracaoInicialConcluidaPorId.Should().BeNull();
        empresa.ConfiguracaoInicialAdiadaEm.Should().BeNull();
    }

    [Fact]
    public void ConcluirGuardaInstanteEQuemConcluiu()
    {
        var empresa = Empresa.Criar("Casa da Baba", "11111111000191");
        var dona = Guid.NewGuid();

        empresa.ConcluirConfiguracaoInicial(dona, Agora);

        empresa.ConfiguracaoInicialConcluidaEm.Should().Be(Agora);
        empresa.ConfiguracaoInicialConcluidaPorId.Should().Be(dona);
        empresa.AlteradoEm.Should().Be(Agora);
    }

    [Fact]
    public void ConcluirDeNovoMantemAPrimeiraConclusao()
    {
        var empresa = Empresa.Criar("Casa da Baba", "11111111000191");
        var dona = Guid.NewGuid();
        empresa.ConcluirConfiguracaoInicial(dona, Agora);

        empresa.ConcluirConfiguracaoInicial(Guid.NewGuid(), Agora.AddHours(2));

        empresa.ConfiguracaoInicialConcluidaEm.Should().Be(Agora);
        empresa.ConfiguracaoInicialConcluidaPorId.Should().Be(dona);
    }

    [Fact]
    public void ConcluirSemAutorEhRecusado() =>
        FluentActions.Invoking(() => Empresa.Criar("Casa da Baba", "11111111000191").ConcluirConfiguracaoInicial(Guid.Empty, Agora))
            .Should().Throw<RegraDeDominioVioladaException>().WithMessage("Informe quem concluiu a configuração inicial.");

    [Fact]
    public void AdiarGuardaOUltimoAdiamento()
    {
        var empresa = Empresa.Criar("Casa da Baba", "11111111000191");

        empresa.AdiarConfiguracaoInicial(Agora);
        empresa.AdiarConfiguracaoInicial(Agora.AddDays(1));

        empresa.ConfiguracaoInicialAdiadaEm.Should().Be(Agora.AddDays(1));
        empresa.ConfiguracaoInicialConcluidaEm.Should().BeNull();
    }

    [Fact]
    public void AdiarDepoisDeConcluidaNaoMudaNada()
    {
        var empresa = Empresa.Criar("Casa da Baba", "11111111000191");
        empresa.ConcluirConfiguracaoInicial(Guid.NewGuid(), Agora);

        empresa.AdiarConfiguracaoInicial(Agora.AddDays(1));

        empresa.ConfiguracaoInicialAdiadaEm.Should().BeNull();
    }
}
