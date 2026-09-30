using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>Viagem de entrega (S44): paradas ordenadas, RN-32 (sem entregador não sai) e retrato do entregador.</summary>
public class ViagemTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 9, 30, 18, 0, 0, DateTimeKind.Utc);

    private static Entregador NovoEntregador() => Entregador.Criar(
        Empresa, "José", TipoEntregador.Plataforma, EmpresaEntregador.Lalamove, "11999990000", "Moto", "ABC1D23", Agora);

    [Fact]
    public void SairSemEntregadorRecusa()
    {
        var viagem = Viagem.Criar(Empresa, Agora);
        viagem.IncluirParada(Guid.NewGuid(), clienteBloqueado: false);

        var act = () => viagem.Sair(null, Agora);

        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*entregador*");
        viagem.Situacao.Should().Be(SituacaoViagem.Montando);
    }

    [Fact]
    public void SairSemParadasRecusa()
    {
        var entregador = NovoEntregador();
        var viagem = Viagem.Criar(Empresa, Agora);
        viagem.DefinirEntregador(entregador);

        var act = () => viagem.Sair(entregador, Agora);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void ClienteBloqueadoNaoEntra()
    {
        var viagem = Viagem.Criar(Empresa, Agora);

        var act = () => viagem.IncluirParada(Guid.NewGuid(), clienteBloqueado: true);

        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*bloqueado*");
        viagem.Paradas.Should().BeEmpty();
    }

    [Fact]
    public void PedidoRepetidoNaoEntraDuasVezes()
    {
        var viagem = Viagem.Criar(Empresa, Agora);
        var pedido = Guid.NewGuid();
        viagem.IncluirParada(pedido, false);

        var act = () => viagem.IncluirParada(pedido, false);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void RetratoDoEntregadorNaSaida()
    {
        var entregador = NovoEntregador();
        var viagem = Viagem.Criar(Empresa, Agora);
        viagem.DefinirEntregador(entregador);
        viagem.IncluirParada(Guid.NewGuid(), false);
        viagem.IncluirParada(Guid.NewGuid(), false);

        viagem.Sair(entregador, Agora);
        entregador.Atualizar("José Silva", TipoEntregador.Plataforma, EmpresaEntregador.Lalamove, "11999990000", "Carro", "XYZ9Z99", Agora.AddHours(1));

        viagem.Situacao.Should().Be(SituacaoViagem.EmRota);
        viagem.SaiuEm.Should().Be(Agora);
        viagem.Paradas.Should().AllSatisfy(p =>
        {
            p.EntregadorNome.Should().Be("José");
            p.Veiculo.Should().Be("Moto");
            p.Placa.Should().Be("ABC1D23");
            p.EmpresaEntregador.Should().Be(EmpresaEntregador.Lalamove);
        });
    }

    [Fact]
    public void ReordenarMoveAParadaERenumera()
    {
        var viagem = Viagem.Criar(Empresa, Agora);
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        viagem.IncluirParada(a, false);
        viagem.IncluirParada(b, false);
        viagem.IncluirParada(c, false);

        viagem.ReordenarParada(c, 1);

        viagem.Paradas.OrderBy(p => p.Ordem).Select(p => p.PedidoId).Should().Equal(c, a, b);
        viagem.Paradas.Select(p => p.Ordem).Order().Should().Equal(1, 2, 3);
    }

    [Fact]
    public void RetirarParadaRenumera()
    {
        var viagem = Viagem.Criar(Empresa, Agora);
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        viagem.IncluirParada(a, false);
        viagem.IncluirParada(b, false);

        viagem.RetirarParada(a);

        viagem.Paradas.Should().ContainSingle(p => p.PedidoId == b && p.Ordem == 1);
    }

    [Fact]
    public void MarcarTodasEntreguesConcluiAViagem()
    {
        var entregador = NovoEntregador();
        var viagem = Viagem.Criar(Empresa, Agora);
        viagem.DefinirEntregador(entregador);
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        viagem.IncluirParada(a, false);
        viagem.IncluirParada(b, false);
        viagem.Sair(entregador, Agora);

        viagem.MarcarParadaEntregue(a, Agora.AddMinutes(10));
        viagem.Situacao.Should().Be(SituacaoViagem.EmRota);
        viagem.MarcarParadaEntregue(b, Agora.AddMinutes(20));

        viagem.Situacao.Should().Be(SituacaoViagem.Concluida);
        viagem.Paradas.Single(p => p.PedidoId == a).EntregueEm.Should().Be(Agora.AddMinutes(10));
    }

    [Fact]
    public void MarcarEntregueAntesDeSairRecusa()
    {
        var viagem = Viagem.Criar(Empresa, Agora);
        var a = Guid.NewGuid();
        viagem.IncluirParada(a, false);

        var act = () => viagem.MarcarParadaEntregue(a, Agora);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void DesfazerSoAntesDeSair()
    {
        var entregador = NovoEntregador();
        var montando = Viagem.Criar(Empresa, Agora);
        montando.IncluirParada(Guid.NewGuid(), false);
        montando.Desfazer();
        montando.Situacao.Should().Be(SituacaoViagem.Desfeita);
        montando.Paradas.Should().BeEmpty();

        var emRota = Viagem.Criar(Empresa, Agora);
        emRota.DefinirEntregador(entregador);
        emRota.IncluirParada(Guid.NewGuid(), false);
        emRota.Sair(entregador, Agora);
        var act = () => emRota.Desfazer();
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void EntregadorInativoNaoSai()
    {
        var entregador = NovoEntregador();
        var viagem = Viagem.Criar(Empresa, Agora);
        viagem.DefinirEntregador(entregador);
        viagem.IncluirParada(Guid.NewGuid(), false);
        entregador.Desativar(Agora);

        var act = () => viagem.Sair(entregador, Agora);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void EntregadorExigeNome()
    {
        var act = () => Entregador.Criar(Empresa, " ", TipoEntregador.Motoboy, EmpresaEntregador.Propria, null, null, null, Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void ChamadoAbreEAtende()
    {
        var chamado = ChamadoEntregador.Abrir(Empresa, "  Preciso de motoboy às 19h  ", null, Agora);
        chamado.Texto.Should().Be("Preciso de motoboy às 19h");
        chamado.Situacao.Should().Be(SituacaoChamadoEntregador.Aberto);

        chamado.Atender(Agora.AddMinutes(5));

        chamado.Situacao.Should().Be(SituacaoChamadoEntregador.Atendido);
        chamado.AtendidoEm.Should().Be(Agora.AddMinutes(5));
    }
}
