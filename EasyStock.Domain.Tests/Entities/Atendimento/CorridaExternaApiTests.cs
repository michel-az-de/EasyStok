using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>
/// Corrida chamada pela API do provedor (#2158, ADR-0064): nasce Solicitando antes do POST, ganha o id
/// externo na confirmação e segue o status que o provedor devolve. Estado final não reabre, salvo a
/// troca de pedido feita pelo suporte da Lalamove (ORDER_REPLACED).
/// </summary>
public class CorridaExternaApiTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Operadora = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 10, 31, 15, 0, 0, DateTimeKind.Utc);

    private static Viagem ViagemComParada()
    {
        var viagem = Viagem.Criar(Empresa, Agora);
        viagem.IncluirParada(Guid.NewGuid(), clienteBloqueado: false);
        return viagem;
    }

    private static CorridaExterna Solicitada(Viagem? viagem = null) => CorridaExterna.SolicitarPorApi(
        viagem ?? ViagemComParada(), EmpresaEntregador.Lalamove, VeiculoCorrida.Moto, "cot-1", 1890, Operadora, Agora);

    private static CorridaExterna Confirmada()
    {
        var corrida = Solicitada();
        corrida.ConfirmarNoProvedor("ord-1", "https://share.lalamove.com/x");
        return corrida;
    }

    [Fact]
    public void SolicitarNasceSolicitandoComCustoCotado()
    {
        var corrida = Solicitada();

        corrida.Origem.Should().Be(OrigemCorridaExterna.Api);
        corrida.Status.Should().Be(StatusCorrida.Solicitando);
        corrida.CustoCotadoCentavos.Should().Be(1890);
        corrida.CustoCentavos.Should().Be(1890);
        corrida.Veiculo.Should().Be(VeiculoCorrida.Moto);
        corrida.CotacaoId.Should().Be("cot-1");
        corrida.IdExterno.Should().BeNull();
        corrida.Ativa.Should().BeTrue();
    }

    [Fact]
    public void SolicitarSoComViagemMontandoComParada()
    {
        var vazia = Viagem.Criar(Empresa, Agora);
        var semParada = () => Solicitada(vazia);
        semParada.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*parada*");

        var desfeita = ViagemComParada();
        desfeita.Desfazer();
        var act = () => Solicitada(desfeita);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData(EmpresaEntregador.Propria)]
    [InlineData(EmpresaEntregador.NoveNove)]
    public void SolicitarPorApiSoComProvedorIntegrado(EmpresaEntregador provedor)
    {
        var act = () => CorridaExterna.SolicitarPorApi(ViagemComParada(), provedor, VeiculoCorrida.Moto, "c", 100, Operadora, Agora);

        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*API*");
    }

    [Fact]
    public void ConfirmarGravaIdELinkEPassaAProcurarMotorista()
    {
        var corrida = Confirmada();

        corrida.IdExterno.Should().Be("ord-1");
        corrida.LinkRastreio.Should().Be("https://share.lalamove.com/x");
        corrida.Status.Should().Be(StatusCorrida.ProcurandoMotorista);
    }

    [Fact]
    public void FalhaNaSolicitacaoEncerraSemCusto()
    {
        var corrida = Solicitada();

        corrida.FalharSolicitacao(Agora);

        corrida.Status.Should().Be(StatusCorrida.Falha);
        corrida.CustoCentavos.Should().Be(0);
        corrida.Ativa.Should().BeFalse();
        corrida.EncerradaEm.Should().Be(Agora);
    }

    [Fact]
    public void StatusDoProvedorSegueAteEntregue()
    {
        var corrida = Confirmada();

        corrida.AplicarStatus(StatusCorrida.MotoristaACaminho, Agora.AddMinutes(1));
        corrida.AplicarStatus(StatusCorrida.Coletada, Agora.AddMinutes(10));
        corrida.AplicarStatus(StatusCorrida.Entregue, Agora.AddMinutes(30));

        corrida.Status.Should().Be(StatusCorrida.Entregue);
        corrida.Ativa.Should().BeFalse();
        corrida.EncerradaEm.Should().Be(Agora.AddMinutes(30));
    }

    [Fact]
    public void StatusRepetidoNaoMudaNada()
    {
        var corrida = Confirmada();
        corrida.AplicarStatus(StatusCorrida.MotoristaACaminho, Agora.AddMinutes(1));

        var mudou = corrida.AplicarStatus(StatusCorrida.MotoristaACaminho, Agora.AddMinutes(2));

        mudou.Should().BeFalse();
        corrida.MotoristaAtribuidoEm.Should().Be(Agora.AddMinutes(1));
    }

    [Fact]
    public void MotoristaQueDesisteVoltaAProcurarEZeraOMatch()
    {
        var corrida = Confirmada();
        corrida.AplicarStatus(StatusCorrida.MotoristaACaminho, Agora.AddMinutes(1));
        corrida.AtribuirMotorista("drv-9", "Carlos", "11988887777", "XYZ9A88");

        corrida.AplicarStatus(StatusCorrida.ProcurandoMotorista, Agora.AddMinutes(3));

        corrida.MotoristaAtribuidoEm.Should().BeNull();
        corrida.MotoristaIdExterno.Should().BeNull();
        corrida.Motorista.Should().BeNull();
    }

    [Fact]
    public void EstadoFinalNaoReabre()
    {
        var corrida = Confirmada();
        corrida.AplicarStatus(StatusCorrida.Cancelada, Agora.AddMinutes(1));

        var act = () => corrida.AplicarStatus(StatusCorrida.MotoristaACaminho, Agora.AddMinutes(2));

        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*encerrada*");
    }

    [Fact]
    public void SolicitandoNaoEhStatusQueOProvedorDevolve()
    {
        var corrida = Confirmada();

        var act = () => corrida.AplicarStatus(StatusCorrida.Solicitando, Agora);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void StatusAntesDaConfirmacaoRecusa()
    {
        var corrida = Solicitada();

        var act = () => corrida.AplicarStatus(StatusCorrida.MotoristaACaminho, Agora);

        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*confirm*");
    }

    [Fact]
    public void CancelarSoProcurandoOuAteCincoMinutosDoMatch()
    {
        var corrida = Confirmada();
        corrida.PodeCancelar(Agora).Should().BeTrue();

        corrida.AplicarStatus(StatusCorrida.MotoristaACaminho, Agora.AddMinutes(1));
        corrida.PodeCancelar(Agora.AddMinutes(6)).Should().BeTrue();
        corrida.PodeCancelar(Agora.AddMinutes(6).AddSeconds(1)).Should().BeFalse();

        corrida.AplicarStatus(StatusCorrida.Coletada, Agora.AddMinutes(2));
        corrida.PodeCancelar(Agora.AddMinutes(2)).Should().BeFalse();
    }

    [Fact]
    public void TrocaDePedidoPeloSuporteReabreComONovoId()
    {
        var corrida = Confirmada();
        corrida.AplicarStatus(StatusCorrida.Cancelada, Agora.AddMinutes(1));

        corrida.SubstituirPedidoNoProvedor("ord-1", "ord-2", Agora.AddMinutes(2));

        corrida.IdExterno.Should().Be("ord-2");
        corrida.IdExternoAnterior.Should().Be("ord-1");
        corrida.Status.Should().Be(StatusCorrida.ProcurandoMotorista);
        corrida.EncerradaEm.Should().BeNull();
        corrida.Ativa.Should().BeTrue();
    }

    [Fact]
    public void TrocaDePedidoDeOutroIdRecusa()
    {
        var corrida = Confirmada();

        var act = () => corrida.SubstituirPedidoNoProvedor("ord-x", "ord-2", Agora);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void CustoFinalDoProvedorSubstituiOCotado()
    {
        var corrida = Confirmada();

        corrida.AtualizarCustoDoProvedor(2140);

        corrida.CustoCentavos.Should().Be(2140);
        corrida.CustoCotadoCentavos.Should().Be(1890);
    }

    [Fact]
    public void CorrecaoManualNaoValeParaCorridaDaApi()
    {
        var viagem = ViagemComParada();
        var corrida = Solicitada(viagem);

        var act = () => corrida.Corrigir(viagem, 100, "x", Operadora, Agora);

        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*provedor*");
    }

    [Fact]
    public void ViagemSaiComCorridaColetadaEGravaRetratoDoMotorista()
    {
        var viagem = ViagemComParada();
        var corrida = Solicitada(viagem);
        corrida.ConfirmarNoProvedor("ord-1", null);
        corrida.AplicarStatus(StatusCorrida.MotoristaACaminho, Agora);
        corrida.AtribuirMotorista("drv-9", "Carlos", "11988887777", "XYZ9A88");
        corrida.AplicarStatus(StatusCorrida.Coletada, Agora.AddMinutes(5));

        viagem.SairComCorrida(corrida, Agora.AddMinutes(5));

        viagem.Situacao.Should().Be(SituacaoViagem.EmRota);
        viagem.EntregadorId.Should().BeNull();
        var parada = viagem.Paradas.Single();
        parada.EntregadorNome.Should().Be("Carlos");
        parada.Placa.Should().Be("XYZ9A88");
        parada.Veiculo.Should().Be("Moto");
        parada.EmpresaEntregador.Should().Be(EmpresaEntregador.Lalamove);
    }

    [Fact]
    public void ViagemNaoSaiComCorridaQueNaoColetouOuDeOutraViagem()
    {
        var viagem = ViagemComParada();
        var corrida = Solicitada(viagem);
        corrida.ConfirmarNoProvedor("ord-1", null);

        var naoColetada = () => viagem.SairComCorrida(corrida, Agora);
        naoColetada.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*colet*");

        var outra = ViagemComParada();
        var deOutra = () => outra.SairComCorrida(corrida, Agora);
        deOutra.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*outra viagem*");
    }
}
