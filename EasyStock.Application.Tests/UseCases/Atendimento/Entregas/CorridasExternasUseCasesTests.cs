using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Entregas;

/// <summary>
/// I9.1 (#1669, ADR-0061 D5): a corrida pedida por fora (99, Lalamove, Uber) deixa rastro e custo.
/// O custo mora em <see cref="CorridaExterna"/>, nunca na viagem nem na parada.
/// </summary>
public class CorridasExternasUseCasesTests
{
    private static readonly DateTime Agora = new(2026, 10, 12, 18, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _operadora = Guid.NewGuid();
    private readonly Guid _gerente = Guid.NewGuid();
    private readonly IViagemRepository _viagens = Substitute.For<IViagemRepository>();
    private readonly ICorridaExternaRepository _corridas = Substitute.For<ICorridaExternaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly List<CorridaExterna> _gravadas = [];
    private DateTime _agora = Agora;

    public CorridasExternasUseCasesTests()
    {
        _corridas.When(r => r.AddAsync(Arg.Any<CorridaExterna>(), Arg.Any<CancellationToken>()))
            .Do(c => _gravadas.Add(c.Arg<CorridaExterna>()));
        _corridas.ObterAsync(_empresaId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => _gravadas.FirstOrDefault(x => x.Id == c.ArgAt<Guid>(1)));
    }

    private sealed class Relogio(Func<DateTime> agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(agora(), TimeSpan.Zero);
    }

    private RegistrarCorridaExternaUseCase Registrar() => new(_viagens, _corridas, _uow, new Relogio(() => _agora));
    private CorrigirCorridaExternaUseCase Corrigir() => new(_viagens, _corridas, _uow, new Relogio(() => _agora));

    private Viagem ViagemEm(SituacaoViagem situacao)
    {
        var viagem = Viagem.Criar(_empresaId, Agora);
        if (situacao == SituacaoViagem.Desfeita) viagem.Desfazer();
        if (situacao is SituacaoViagem.EmRota or SituacaoViagem.Concluida)
        {
            var noveNove = Entregador.Criar(_empresaId, "99 Entrega", TipoEntregador.Plataforma, EmpresaEntregador.NoveNove, null, null, null, Agora);
            var pedido = Guid.NewGuid();
            viagem.DefinirEntregador(noveNove);
            viagem.IncluirParada(pedido, clienteBloqueado: false);
            viagem.Sair(noveNove, Agora);
            if (situacao == SituacaoViagem.Concluida) viagem.MarcarParadaEntregue(pedido, Agora);
        }
        viagem.Situacao.Should().Be(situacao);
        _viagens.ObterAsync(_empresaId, viagem.Id, Arg.Any<CancellationToken>()).Returns(viagem);
        return viagem;
    }

    private RegistrarCorridaExternaInput Input(Guid viagemId, int custoCentavos = 1890, EmpresaEntregador provedor = EmpresaEntregador.NoveNove) =>
        new(_empresaId, _operadora, viagemId, provedor, custoCentavos, " João, Honda CG ");

    [Theory]
    [InlineData(SituacaoViagem.Montando)]
    [InlineData(SituacaoViagem.EmRota)]
    [InlineData(SituacaoViagem.Concluida)]
    public async Task RegistrarCorrida_da_99_grava_custo_motorista_e_quem_registrou(SituacaoViagem situacao)
    {
        var viagem = ViagemEm(situacao);

        var r = await Registrar().ExecuteAsync(Input(viagem.Id));

        r.ViagemId.Should().Be(viagem.Id);
        r.Provedor.Should().Be(EmpresaEntregador.NoveNove);
        r.Origem.Should().Be(OrigemCorridaExterna.Manual);
        r.CustoCentavos.Should().Be(1890);
        r.Motorista.Should().Be("João, Honda CG");
        r.RegistradoPorUsuarioId.Should().Be(_operadora);
        r.RegistradoEm.Should().Be(Agora);
        r.CorrigidoEm.Should().BeNull();
        _gravadas.Should().ContainSingle(c => c.EmpresaId == _empresaId);
        await _uow.Received(1).CommitAsync();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-1890)]
    [InlineData(500_001)] // acima de R$ 5.000,00 é erro de digitação
    public async Task RegistrarCorrida_custo_negativo_recusa(int custoCentavos)
    {
        var viagem = ViagemEm(SituacaoViagem.EmRota);

        var act = () => Registrar().ExecuteAsync(Input(viagem.Id, custoCentavos));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        _gravadas.Should().BeEmpty();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task RegistrarCorrida_em_viagem_desfeita_recusa()
    {
        var viagem = ViagemEm(SituacaoViagem.Desfeita);

        var act = () => Registrar().ExecuteAsync(Input(viagem.Id));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        _gravadas.Should().BeEmpty();
    }

    [Fact]
    public async Task RegistrarCorrida_com_entregador_proprio_recusa()
    {
        var viagem = ViagemEm(SituacaoViagem.EmRota);

        var act = () => Registrar().ExecuteAsync(Input(viagem.Id, provedor: EmpresaEntregador.Propria));

        await act.Should().ThrowAsync<UseCaseValidationException>("corrida externa é de terceiro");
    }

    [Fact]
    public async Task RegistrarCorrida_em_viagem_de_outra_loja_nao_encontra()
    {
        var act = () => Registrar().ExecuteAsync(Input(Guid.NewGuid()));

        await act.Should().ThrowAsync<ViagemNaoEncontradaException>();
    }

    [Fact]
    public async Task Custo_zero_e_aceito()
    {
        var viagem = ViagemEm(SituacaoViagem.EmRota);

        (await Registrar().ExecuteAsync(Input(viagem.Id, custoCentavos: 0))).CustoCentavos.Should().Be(0);
    }

    [Fact]
    public async Task Corrigir_custo_guarda_quem_corrigiu()
    {
        var viagem = ViagemEm(SituacaoViagem.Concluida);
        var registrada = await Registrar().ExecuteAsync(Input(viagem.Id));
        _agora = Agora.AddHours(2);

        var r = await Corrigir().ExecuteAsync(new CorrigirCorridaExternaInput(_empresaId, _gerente, viagem.Id, registrada.Id, 2150, "João"));

        r.CustoCentavos.Should().Be(2150);
        r.Motorista.Should().Be("João");
        r.CorrigidoPorUsuarioId.Should().Be(_gerente);
        r.CorrigidoEm.Should().Be(Agora.AddHours(2));
        r.RegistradoPorUsuarioId.Should().Be(_operadora, "quem registrou não muda");
        r.RegistradoEm.Should().Be(Agora);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(500_001)]
    public async Task Corrigir_com_custo_invalido_recusa_e_mantem_o_anterior(int custoCentavos)
    {
        var viagem = ViagemEm(SituacaoViagem.EmRota);
        var registrada = await Registrar().ExecuteAsync(Input(viagem.Id));
        _uow.ClearReceivedCalls();

        var act = () => Corrigir().ExecuteAsync(new CorrigirCorridaExternaInput(_empresaId, _gerente, viagem.Id, registrada.Id, custoCentavos, null));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        _gravadas.Single().CustoCentavos.Should().Be(1890);
        _gravadas.Single().CorrigidoEm.Should().BeNull();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Corrigir_corrida_de_outra_viagem_nao_encontra()
    {
        var viagem = ViagemEm(SituacaoViagem.EmRota);
        var outra = ViagemEm(SituacaoViagem.EmRota);
        var registrada = await Registrar().ExecuteAsync(Input(viagem.Id));

        var act = () => Corrigir().ExecuteAsync(new CorrigirCorridaExternaInput(_empresaId, _gerente, outra.Id, registrada.Id, 100, null));

        await act.Should().ThrowAsync<CorridaExternaNaoEncontradaException>();
    }

    [Fact]
    public void Relatorio_compara_custo_com_frete_cobrado_por_viagem()
    {
        var viagemA = Guid.NewGuid();
        var viagemB = Guid.NewGuid();
        var linhas = new[]
        {
            new CustoDeEntregaLinha(viagemA, Agora, 2, FreteCobradoCentavos: 2400, CustoCentavos: 1890),
            new CustoDeEntregaLinha(viagemB, Agora.AddHours(1), 1, FreteCobradoCentavos: 900, CustoCentavos: 2150),
        };

        var r = CustoDeEntregaResult.De(linhas);

        r.Viagens.Select(v => (v.ViagemId, v.SaldoCentavos)).Should().Equal((viagemA, 510), (viagemB, -1250));
        r.FreteCobradoCentavos.Should().Be(3300);
        r.CustoCentavos.Should().Be(4040);
        r.SaldoCentavos.Should().Be(-740);
    }
}