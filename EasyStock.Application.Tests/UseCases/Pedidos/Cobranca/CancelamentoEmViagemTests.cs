using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;
using PedidoEntity = EasyStock.Domain.Entities.Pedido;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// estoque-03 (banca 23/10): pedido cancelado dentro de viagem. Em rota, a parada nunca virava entregue
/// (Cancelado nao vai a Entregue) e a viagem ficava "na rua" para sempre; montando, o pedido cancelado derrubava o "Sair".
/// O cancelamento tira a parada da viagem e a conclui quando as restantes ja foram entregues.
/// </summary>
public class CancelamentoEmViagemTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 10, 23, 18, 0, 0, DateTimeKind.Utc);

    private readonly IViagemRepository _viagens = Substitute.For<IViagemRepository>();
    private readonly EfeitosCancelamentoPedido _efeitos;

    public CancelamentoEmViagemTests()
    {
        _efeitos = new EfeitosCancelamentoPedido(Substitute.For<IContaReceberRepository>(), Substitute.For<IVagaOcupadaRepository>(),
            NullLogger<EfeitosCancelamentoPedido>.Instance, viagens: _viagens);
    }

    private static Entregador NovoEntregador() => Entregador.Criar(
        EmpresaId, "José", TipoEntregador.Plataforma, EmpresaEntregador.Lalamove, "11999990000", "Moto", "ABC1D23", Agora);

    private static PedidoEntity NovoPedido() => PedidoEntity.Criar(EmpresaId, origem: "whatsapp");

    private Viagem EmRota(params Guid[] pedidos)
    {
        var entregador = NovoEntregador();
        var viagem = Viagem.Criar(EmpresaId, Agora);
        viagem.DefinirEntregador(entregador);
        foreach (var p in pedidos) viagem.IncluirParada(p, clienteBloqueado: false);
        viagem.Sair(entregador, Agora);
        return viagem;
    }

    private void ViagemAtivaDoPedido(PedidoEntity pedido, Viagem viagem) =>
        _viagens.ObterAtivaDoPedidoAsync(EmpresaId, pedido.Id, Arg.Any<CancellationToken>()).Returns(viagem);

    [Fact]
    public async Task EmRota_ConcluiAViagemQuandoAsRestantesJaForamEntregues()
    {
        var entregue = NovoPedido();
        var cancelado = NovoPedido();
        var viagem = EmRota(entregue.Id, cancelado.Id);
        viagem.MarcarParadaEntregue(entregue.Id, Agora.AddMinutes(10));
        ViagemAtivaDoPedido(cancelado, viagem);

        await _efeitos.AplicarAsync(cancelado, "Cliente desistiu", null);

        viagem.Paradas.Should().ContainSingle(p => p.PedidoId == entregue.Id);
        viagem.Situacao.Should().Be(SituacaoViagem.Concluida);
        viagem.ConcluidaEm.Should().NotBeNull();
    }

    [Fact]
    public async Task EmRota_AViagemSegueQuandoAindaHaParadaPorEntregar()
    {
        var cancelado = NovoPedido();
        var seguinte = Guid.NewGuid();
        var viagem = EmRota(cancelado.Id, seguinte);
        ViagemAtivaDoPedido(cancelado, viagem);

        await _efeitos.AplicarAsync(cancelado, null, null);

        viagem.Situacao.Should().Be(SituacaoViagem.EmRota);
        viagem.Paradas.Should().ContainSingle(p => p.PedidoId == seguinte && p.Ordem == 1);
    }

    [Fact]
    public async Task EmRota_SeFoiASoParadaAViagemNaoFicaPresa()
    {
        var cancelado = NovoPedido();
        var viagem = EmRota(cancelado.Id);
        ViagemAtivaDoPedido(cancelado, viagem);

        await _efeitos.AplicarAsync(cancelado, null, null);

        viagem.Situacao.Should().NotBe(SituacaoViagem.EmRota);
        viagem.Paradas.Should().BeEmpty();
    }

    [Fact]
    public async Task Montando_RetiraAParadaParaOSairNaoQuebrar()
    {
        var cancelado = NovoPedido();
        var outro = Guid.NewGuid();
        var viagem = Viagem.Criar(EmpresaId, Agora);
        viagem.IncluirParada(cancelado.Id, false);
        viagem.IncluirParada(outro, false);
        ViagemAtivaDoPedido(cancelado, viagem);

        await _efeitos.AplicarAsync(cancelado, null, null);

        viagem.Situacao.Should().Be(SituacaoViagem.Montando);
        viagem.Paradas.Should().ContainSingle(p => p.PedidoId == outro && p.Ordem == 1);
    }

    [Fact]
    public async Task ViagemDaLogisticaContratadaNaoEMexida()
    {
        var cancelado = NovoPedido();
        var viagem = EmRota(cancelado.Id, Guid.NewGuid());
        ViagemAtivaDoPedido(cancelado, viagem);
        _viagens.VinculadaLogisticaAsync(EmpresaId, viagem.Id, Arg.Any<CancellationToken>()).Returns(true);

        await _efeitos.AplicarAsync(cancelado, null, null);

        viagem.Paradas.Should().HaveCount(2, "o retorno dessa viagem e do fluxo da logistica contratada");
    }

    [Fact]
    public async Task PedidoForaDeViagemNaoMudaNada()
    {
        var pedido = NovoPedido();

        var act = () => _efeitos.AplicarAsync(pedido, null, null);

        await act.Should().NotThrowAsync();
        await _viagens.Received(1).ObterAtivaDoPedidoAsync(EmpresaId, pedido.Id, Arg.Any<CancellationToken>());
    }
}
