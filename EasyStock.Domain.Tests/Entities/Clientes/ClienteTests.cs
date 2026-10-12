using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Clientes;

/// <summary>S24: bloqueio que vale em todos os canais e preferências de aviso e marketing.</summary>
public class ClienteTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BloquearGravaMotivoEData()
    {
        var cliente = Cliente.Criar(Guid.NewGuid(), "Maria");

        cliente.Bloquear("  não pagou dois pedidos ", Agora);

        cliente.Bloqueado.Should().BeTrue();
        cliente.BloqueadoEm.Should().Be(Agora);
        cliente.MotivoBloqueio.Should().Be("não pagou dois pedidos");
        cliente.AlteradoEm.Should().Be(Agora);
    }

    [Fact]
    public void BloquearSemMotivoGravaMotivoNulo()
    {
        var cliente = Cliente.Criar(Guid.NewGuid(), "Maria");
        cliente.Bloquear("   ", Agora);
        cliente.Bloqueado.Should().BeTrue();
        cliente.MotivoBloqueio.Should().BeNull();
    }

    [Fact]
    public void MotivoAcimaDoLimiteEhRecusado()
    {
        var cliente = Cliente.Criar(Guid.NewGuid(), "Maria");
        var act = () => cliente.Bloquear(new string('x', Cliente.MotivoBloqueioTamanhoMaximo + 1), Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
        cliente.Bloqueado.Should().BeFalse();
    }

    [Fact]
    public void DesbloquearLimpaMotivoEData()
    {
        var cliente = Cliente.Criar(Guid.NewGuid(), "Maria");
        cliente.Bloquear("golpe", Agora);

        cliente.Desbloquear(Agora.AddDays(1));

        cliente.Bloqueado.Should().BeFalse();
        cliente.BloqueadoEm.Should().BeNull();
        cliente.MotivoBloqueio.Should().BeNull();
        cliente.AlteradoEm.Should().Be(Agora.AddDays(1));
    }

    [Fact]
    public void AvisosDeStatusComecamAtivos() =>
        Cliente.Criar(Guid.NewGuid(), "Maria").AvisosStatusAtivos.Should().BeTrue();

    [Fact]
    public void DefinirAvisosStatus()
    {
        var cliente = Cliente.Criar(Guid.NewGuid(), "Maria");
        cliente.DefinirAvisosStatus(false, Agora);
        cliente.AvisosStatusAtivos.Should().BeFalse();
        cliente.AlteradoEm.Should().Be(Agora);
    }

    [Fact]
    public void DefinirConsentimentoMarketingCarimbaQuando()
    {
        var cliente = Cliente.Criar(Guid.NewGuid(), "Maria");

        cliente.DefinirConsentimentoMarketing(true, Agora);
        cliente.ConsentiuMarketing.Should().BeTrue();
        cliente.ConsentimentoEm.Should().Be(Agora);

        cliente.DefinirConsentimentoMarketing(false, Agora.AddDays(2));
        cliente.ConsentiuMarketing.Should().BeFalse();
        cliente.ConsentimentoEm.Should().Be(Agora.AddDays(2));
    }
}
