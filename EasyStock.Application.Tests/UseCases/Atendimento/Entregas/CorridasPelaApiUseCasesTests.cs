using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Integration.Logistica;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Logging.Abstractions;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Entregas;

/// <summary>
/// #2160 (ADR-0064): a corrida chamada pela API. Gravada antes do POST, nunca repetida num timeout, e o status
/// que o provedor devolve move a corrida, a viagem e o pedido.
/// </summary>
public class CorridasPelaApiUseCasesTests
{
    private static readonly DateTime Agora = new(2026, 10, 31, 18, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _operadora = Guid.NewGuid();
    private readonly IViagemRepository _viagens = Substitute.For<IViagemRepository>();
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly IStorefrontRepository _storefronts = Substitute.For<IStorefrontRepository>();
    private readonly ICorridaExternaRepository _corridas = Substitute.For<ICorridaExternaRepository>();
    private readonly ILogisticaGateway _gateway = Substitute.For<ILogisticaGateway>();
    private readonly IPublicadorEventoIntegracao _publicador = Substitute.For<IPublicadorEventoIntegracao>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly List<CorridaExterna> _gravadas = [];
    private readonly List<string> _ordem = [];
    private DateTime _agora = Agora;

    private readonly Viagem _viagem;
    private readonly Pedido _pedido;

    public CorridasPelaApiUseCasesTests()
    {
        _pedido = Pedido.Criar(_empresaId);
        _pedido.Status = StatusPedidoMapper.Pronto;
        _pedido.ClienteNome = "Ana";
        _pedido.ClienteTelefone = "11999990000";
        _pedido.EnderecoEntrega = "Rua A, 10";
        _pedido.EntregaLat = -23.55;
        _pedido.EntregaLng = -46.63;
        _pedidos.GetByIdAsync(_empresaId, _pedido.Id).Returns(_pedido);

        _viagem = Viagem.Criar(_empresaId, Agora);
        _viagem.IncluirParada(_pedido.Id, clienteBloqueado: false);
        _viagens.ObterAsync(_empresaId, _viagem.Id, Arg.Any<CancellationToken>()).Returns(_viagem);

        var loja = StorefrontEntity.Criar(_empresaId, "casa-da-baba", "Casa da Baba", 0m);
        loja.ConfigurarFreteRaio(-23.56, -46.64, 1.3, 0, 8000, null);
        loja.AtualizarBranding(whatsappPedidos: "11988887777");
        _storefronts.GetByEmpresaAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(loja);

        _corridas.When(r => r.AddAsync(Arg.Any<CorridaExterna>(), Arg.Any<CancellationToken>()))
            .Do(c => { _gravadas.Add(c.Arg<CorridaExterna>()); _ordem.Add("add"); });
        _corridas.ObterAsync(_empresaId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => _gravadas.FirstOrDefault(x => x.Id == c.ArgAt<Guid>(1)));
        _uow.CommitAsync().Returns(1).AndDoes(_ => _ordem.Add("commit"));

        _gateway.Provedor.Returns(EmpresaEntregador.Lalamove);
        _gateway.ConectadoAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(true);
        _gateway.ObterCotacaoAsync(_empresaId, "cot-1", Arg.Any<CancellationToken>())
            .Returns(new CotacaoCorrida("cot-1", 1890, new DateTimeOffset(Agora.AddMinutes(5)), 3200, "s1", "s2"));
    }

    private sealed class Relogio(Func<DateTime> agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(agora(), TimeSpan.Zero);
    }

    private ChamarCorridaUseCase Chamar() => new(_viagens, _pedidos, _storefronts, _corridas, _gateway, _uow,
        new Relogio(() => _agora), NullLogger<ChamarCorridaUseCase>.Instance);

    private SincronizarCorridaUseCase Sincronizar() => new(_corridas, _viagens, _pedidos, _publicador, _gateway, _uow,
        new Relogio(() => _agora), NullLogger<SincronizarCorridaUseCase>.Instance);

    private ChamarCorridaInput Input() => new(_empresaId, _operadora, _viagem.Id, "cot-1", VeiculoCorrida.Moto, null);

    private void ProvedorAceita(string id = "ord-1") =>
        _gateway.ChamarAsync(_empresaId, Arg.Any<PedidoChamadaCorrida>(), Arg.Any<CancellationToken>())
            .Returns(c => { _ordem.Add("post"); return new SituacaoCorridaProvedor(id, StatusCorrida.ProcurandoMotorista, "https://share/x", 1890, null, null); });

    private void ProvedorDiz(StatusCorrida status, string? motorista = null, int? valor = null) =>
        _gateway.ConsultarAsync(_empresaId, "ord-1", Arg.Any<CancellationToken>())
            .Returns(new SituacaoCorridaProvedor("ord-1", status, "https://share/x", valor, motorista, null));

    private async Task<CorridaExterna> ChamadaAsync()
    {
        ProvedorAceita();
        await Chamar().ExecuteAsync(Input());
        return _gravadas.Single();
    }

    [Fact]
    public async Task Chamar_grava_a_corrida_antes_do_post_e_manda_o_id_no_metadata()
    {
        ProvedorAceita();

        var resultado = await Chamar().ExecuteAsync(Input());

        _ordem.Should().StartWith(["add", "commit", "post"], "R4: a corrida existe antes do POST");
        var corrida = _gravadas.Single();
        corrida.Status.Should().Be(StatusCorrida.ProcurandoMotorista);
        corrida.IdExterno.Should().Be("ord-1");
        corrida.CustoCotadoCentavos.Should().Be(1890, "o preço vem da cotação relida no provedor");
        resultado.LinkRastreio.Should().Be("https://share/x");
        await _gateway.Received(1).ChamarAsync(_empresaId,
            Arg.Is<PedidoChamadaCorrida>(p => p.CorridaId == corrida.Id && p.Destinatario.Telefone == "11999990000"
                                               && p.Remetente.Telefone == "11988887777"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Chamar_recusa_pedido_que_ainda_nao_esta_pronto()
    {
        _pedido.Status = StatusPedidoMapper.Preparando;

        var act = () => Chamar().ExecuteAsync(Input());

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*pronto*");
        _gravadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Chamar_recusa_viagem_com_mais_de_uma_parada()
    {
        var outro = Pedido.Criar(_empresaId);
        _viagem.IncluirParada(outro.Id, false);

        var act = () => Chamar().ExecuteAsync(Input());

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*um pedido por vez*");
    }

    [Fact]
    public async Task Chamar_recusa_pedido_sem_endereco_no_mapa()
    {
        _pedido.EntregaLat = null;

        var act = () => Chamar().ExecuteAsync(Input());

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*mapa*");
    }

    [Fact]
    public async Task Chamar_recusa_cotacao_vencida()
    {
        _agora = Agora.AddMinutes(6);

        var act = () => Chamar().ExecuteAsync(Input());

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*venceu*");
        _gravadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Chamar_sem_conexao_avisa_para_conectar()
    {
        _gateway.ConectadoAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(false);

        var act = () => Chamar().ExecuteAsync(Input());

        await act.Should().ThrowAsync<LogisticaNaoConectadaException>();
    }

    [Fact]
    public async Task Recusa_definitiva_do_provedor_encerra_a_corrida_sem_custo()
    {
        _gateway.ChamarAsync(_empresaId, Arg.Any<PedidoChamadaCorrida>(), Arg.Any<CancellationToken>())
            .Returns<SituacaoCorridaProvedor>(_ => throw new LogisticaRecusadaException("Endereço fora da área da Lalamove."));

        var act = () => Chamar().ExecuteAsync(Input());

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*fora da área*");
        _gravadas.Single().Status.Should().Be(StatusCorrida.Falha);
        _gravadas.Single().CustoCentavos.Should().Be(0);
    }

    [Fact]
    public async Task Timeout_deixa_a_corrida_solicitando_e_nao_repete_o_post()
    {
        _gateway.ChamarAsync(_empresaId, Arg.Any<PedidoChamadaCorrida>(), Arg.Any<CancellationToken>())
            .Returns<SituacaoCorridaProvedor>(_ => throw new LogisticaIndisponivelException("A Lalamove não respondeu."));

        var act = () => Chamar().ExecuteAsync(Input());

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*Não chame de novo*");
        _gravadas.Single().Status.Should().Be(StatusCorrida.Solicitando);
        await _gateway.Received(1).ChamarAsync(_empresaId, Arg.Any<PedidoChamadaCorrida>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Segundo_clique_barrado_pelo_indice_nao_chama_o_provedor()
    {
        var violacao = new InvalidOperationException("23505");
        _uow.CommitAsync().Returns<int>(_ => throw violacao);
        _uow.EhViolacaoDeUnicidade(violacao).Returns(true);

        var act = () => Chamar().ExecuteAsync(Input());

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*corrida em curso*");
        await _gateway.DidNotReceive().ChamarAsync(Arg.Any<Guid>(), Arg.Any<PedidoChamadaCorrida>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Motorista_achado_grava_nome_telefone_e_placa()
    {
        var corrida = await ChamadaAsync();
        ProvedorDiz(StatusCorrida.MotoristaACaminho, motorista: "drv-9");
        _gateway.ObterMotoristaAsync(_empresaId, "ord-1", "drv-9", Arg.Any<CancellationToken>())
            .Returns(new MotoristaCorrida("drv-9", "Carlos", "11977776666", "XYZ9A88"));

        var r = await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);

        r.Status.Should().Be(StatusCorrida.MotoristaACaminho);
        r.Motorista.Should().Be("Carlos");
        r.MotoristaPlaca.Should().Be("XYZ9A88");
        _viagem.Situacao.Should().Be(SituacaoViagem.Montando);
    }

    [Fact]
    public async Task Coleta_poe_a_viagem_em_rota_e_o_pedido_saiu_para_entrega()
    {
        var corrida = await ChamadaAsync();
        ProvedorDiz(StatusCorrida.Coletada);

        await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);

        _viagem.Situacao.Should().Be(SituacaoViagem.EmRota);
        _pedido.StatusEnum.Should().Be(StatusPedido.SaiuParaEntrega);
        await _publicador.Received(1).PublicarAsync(_empresaId, "pedido.mudou_status", "pedido", _pedido.Id,
            Arg.Any<EasyStock.Application.Events.Pedidos.PedidoMudouStatusEvent>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Entregue_conclui_a_viagem_o_pedido_e_grava_o_preco_final()
    {
        var corrida = await ChamadaAsync();
        ProvedorDiz(StatusCorrida.Entregue, valor: 2140);

        await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);

        _viagem.Situacao.Should().Be(SituacaoViagem.Concluida);
        _pedido.StatusEnum.Should().Be(StatusPedido.Entregue);
        corrida.CustoCentavos.Should().Be(2140);
        corrida.Ativa.Should().BeFalse();
    }

    [Fact]
    public async Task Cancelada_antes_da_coleta_deixa_a_viagem_montando_e_zera_o_custo()
    {
        var corrida = await ChamadaAsync();
        ProvedorDiz(StatusCorrida.Cancelada);

        await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);

        _viagem.Situacao.Should().Be(SituacaoViagem.Montando);
        _pedido.StatusEnum.Should().Be(StatusPedido.Pronto);
        corrida.CustoCentavos.Should().Be(0);
    }

    [Fact]
    public async Task Cancelada_depois_da_coleta_devolve_o_pedido_a_fila()
    {
        var corrida = await ChamadaAsync();
        ProvedorDiz(StatusCorrida.Coletada);
        await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);
        ProvedorDiz(StatusCorrida.Cancelada);

        await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);

        _viagem.Situacao.Should().Be(SituacaoViagem.Desfeita);
        _pedido.StatusEnum.Should().Be(StatusPedido.Pronto);
        corrida.CustoCentavos.Should().Be(1890, "depois da coleta a corrida foi feita e é paga");
    }

    // #2168: o pedido pode ter sido criado na Lalamove sem o EasyStok saber o id. Dar como falha sozinho liberava
    // a viagem para uma segunda corrida, paga, por cima da primeira. Só a atendente, olhando o painel, libera.
    [Fact]
    public async Task Solicitando_sem_resposta_nunca_vira_falha_sozinha_e_pede_conferencia()
    {
        _gateway.ChamarAsync(_empresaId, Arg.Any<PedidoChamadaCorrida>(), Arg.Any<CancellationToken>())
            .Returns<SituacaoCorridaProvedor>(_ => throw new LogisticaIndisponivelException("x"));
        await FluentActions.Invoking(() => Chamar().ExecuteAsync(Input())).Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*painel da Lalamove*");
        var corrida = _gravadas.Single();

        _agora = Agora.AddHours(3);
        var r = await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);

        corrida.Status.Should().Be(StatusCorrida.Solicitando);
        r.ConferirNoProvedor.Should().BeTrue();
        await _gateway.DidNotReceive().ConsultarAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Atendente_libera_a_viagem_depois_de_conferir_que_nao_ha_pedido()
    {
        _gateway.ChamarAsync(_empresaId, Arg.Any<PedidoChamadaCorrida>(), Arg.Any<CancellationToken>())
            .Returns<SituacaoCorridaProvedor>(_ => throw new LogisticaIndisponivelException("x"));
        await FluentActions.Invoking(() => Chamar().ExecuteAsync(Input())).Should().ThrowAsync<UseCaseValidationException>();
        var corrida = _gravadas.Single();
        var liberar = new DescartarSolicitacaoCorridaUseCase(_corridas, _uow, new Relogio(() => _agora));

        var cedo = () => liberar.ExecuteAsync(_empresaId, _viagem.Id, corrida.Id);
        await cedo.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*aguarde*");

        _agora = Agora.AddMinutes(3);
        var r = await liberar.ExecuteAsync(_empresaId, _viagem.Id, corrida.Id);

        r.Status.Should().Be(StatusCorrida.Falha);
        corrida.CustoCentavos.Should().Be(0);
        _ordem.Should().EndWith("commit");
    }

    [Fact]
    public async Task Chamar_persiste_o_id_do_provedor_num_segundo_commit()
    {
        ProvedorAceita();

        await Chamar().ExecuteAsync(Input());

        _ordem.Should().Equal("add", "commit", "post", "commit");
    }

    [Fact]
    public async Task Recusa_definitiva_grava_a_falha_no_banco()
    {
        _gateway.ChamarAsync(_empresaId, Arg.Any<PedidoChamadaCorrida>(), Arg.Any<CancellationToken>())
            .Returns<SituacaoCorridaProvedor>(c => { _ordem.Add("post"); throw new LogisticaRecusadaException("fora da área"); });

        await FluentActions.Invoking(() => Chamar().ExecuteAsync(Input())).Should().ThrowAsync<UseCaseValidationException>();

        _ordem.Should().Equal("add", "commit", "post", "commit");
    }

    [Fact]
    public async Task Erro_inesperado_depois_do_post_deixa_a_corrida_solicitando_e_pede_conferencia()
    {
        _gateway.ChamarAsync(_empresaId, Arg.Any<PedidoChamadaCorrida>(), Arg.Any<CancellationToken>())
            .Returns<SituacaoCorridaProvedor>(_ => throw new NullReferenceException("resposta sem campo"));

        var act = () => Chamar().ExecuteAsync(Input());

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*painel da Lalamove*");
        _gravadas.Single().Status.Should().Be(StatusCorrida.Solicitando, "o pedido pode existir lá: a viagem segue travada");
    }

    [Fact]
    public async Task Motorista_que_nao_carregou_e_buscado_de_novo_na_proxima_consulta()
    {
        var corrida = await ChamadaAsync();
        ProvedorDiz(StatusCorrida.MotoristaACaminho, motorista: "drv-9");
        _gateway.ObterMotoristaAsync(_empresaId, "ord-1", "drv-9", Arg.Any<CancellationToken>())
            .Returns((MotoristaCorrida?)null, new MotoristaCorrida("drv-9", "Carlos", "11977776666", "XYZ9A88"));

        await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);
        var r = await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);

        r.Motorista.Should().Be("Carlos");
        r.MotoristaTelefone.Should().Be("11977776666");
        await _gateway.Received(2).ObterMotoristaAsync(_empresaId, "ord-1", "drv-9", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(StatusCorrida.Cancelada)]
    [InlineData(StatusCorrida.Rejeitada)]
    [InlineData(StatusCorrida.Expirada)]
    public async Task Sem_motorista_antes_da_coleta_a_viagem_fica_montando_sem_custo(StatusCorrida final)
    {
        var corrida = await ChamadaAsync();
        ProvedorDiz(final);

        await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);

        _viagem.Situacao.Should().Be(SituacaoViagem.Montando);
        _pedido.StatusEnum.Should().Be(StatusPedido.Pronto);
        corrida.CustoCentavos.Should().Be(0);
    }

    [Fact]
    public async Task Corrida_encerrada_nao_reconsulta_o_provedor()
    {
        var corrida = await ChamadaAsync();
        ProvedorDiz(StatusCorrida.Cancelada);
        await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);
        _gateway.ClearReceivedCalls();

        await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);

        await _gateway.DidNotReceive().ConsultarAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pedido_desconhecido_na_lalamove_nao_muda_nada()
    {
        var corrida = await ChamadaAsync();
        _gateway.ConsultarAsync(_empresaId, "ord-1", Arg.Any<CancellationToken>()).Returns((SituacaoCorridaProvedor?)null);

        var r = await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);

        r.Status.Should().Be(StatusCorrida.ProcurandoMotorista);
    }

    [Fact]
    public async Task Coleta_com_pedido_que_nao_aceita_a_transicao_nao_derruba_a_viagem()
    {
        var corrida = await ChamadaAsync();
        _pedido.Status = StatusPedidoMapper.Cancelado;
        ProvedorDiz(StatusCorrida.Coletada);

        await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);

        _viagem.Situacao.Should().Be(SituacaoViagem.EmRota);
        corrida.Status.Should().Be(StatusCorrida.Coletada);
    }

    [Fact]
    public async Task Preco_do_provedor_com_gorjeta_entra_no_custo_enquanto_a_corrida_anda()
    {
        var corrida = await ChamadaAsync();
        ProvedorDiz(StatusCorrida.ProcurandoMotorista, valor: 2390);

        var r = await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);

        r.CustoCentavos.Should().Be(2390);
        r.GorjetaCentavos.Should().Be(500);
    }

    [Fact]
    public async Task Entregue_com_a_viagem_ja_concluida_a_mao_grava_o_preco_final()
    {
        var corrida = await ChamadaAsync();
        ProvedorDiz(StatusCorrida.Coletada);
        await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);
        _viagem.MarcarParadaEntregue(_pedido.Id, Agora);
        ProvedorDiz(StatusCorrida.Entregue, valor: 2140);

        await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);

        corrida.CustoCentavos.Should().Be(2140);
    }

    [Fact]
    public async Task Troca_de_pedido_de_corrida_cancelada_com_a_viagem_desfeita_nao_reabre()
    {
        var corrida = await ChamadaAsync();
        ProvedorDiz(StatusCorrida.Coletada);
        await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);
        ProvedorDiz(StatusCorrida.Cancelada);
        await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);
        _viagem.Situacao.Should().Be(SituacaoViagem.Desfeita);

        var r = await Sincronizar().SubstituirPedidoAsync(_empresaId, corrida.Id, "ord-1", "ord-2");

        r.Status.Should().Be(StatusCorrida.Cancelada);
        corrida.IdExterno.Should().Be("ord-1");
    }

    [Fact]
    public async Task Troca_de_pedido_com_outra_corrida_ja_em_curso_nao_derruba_o_webhook()
    {
        var corrida = await ChamadaAsync();
        ProvedorDiz(StatusCorrida.Cancelada);
        await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);
        var outra = CorridaExterna.SolicitarPorApi(_viagem, EmpresaEntregador.Lalamove, VeiculoCorrida.Moto, "cot-2", 2000, _operadora, Agora);
        _corridas.ListarDaViagemAsync(_empresaId, _viagem.Id, Arg.Any<CancellationToken>()).Returns([corrida, outra]);

        var r = await Sincronizar().SubstituirPedidoAsync(_empresaId, corrida.Id, "ord-1", "ord-2");

        r.Status.Should().Be(StatusCorrida.Cancelada);
        corrida.IdExterno.Should().Be("ord-1");
    }

    [Fact]
    public async Task Troca_de_pedido_pelo_suporte_segue_com_o_id_novo()
    {
        var corrida = await ChamadaAsync();
        _gateway.ConsultarAsync(_empresaId, "ord-2", Arg.Any<CancellationToken>())
            .Returns(new SituacaoCorridaProvedor("ord-2", StatusCorrida.ProcurandoMotorista, null, null, null, null));

        var r = await Sincronizar().SubstituirPedidoAsync(_empresaId, corrida.Id, "ord-1", "ord-2");

        r.IdExterno.Should().Be("ord-2");
        r.Status.Should().Be(StatusCorrida.ProcurandoMotorista);
    }

    [Fact]
    public async Task Cancelar_fora_da_janela_nao_chama_o_provedor()
    {
        var corrida = await ChamadaAsync();
        ProvedorDiz(StatusCorrida.MotoristaACaminho);
        await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);
        _agora = Agora.AddMinutes(6);

        var act = () => new CancelarCorridaUseCase(_corridas, _gateway, Sincronizar(), new Relogio(() => _agora))
            .ExecuteAsync(_empresaId, _viagem.Id, corrida.Id);

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*5 minutos*");
        await _gateway.DidNotReceive().CancelarAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancelar_procurando_motorista_cancela_no_provedor_e_reconsulta()
    {
        var corrida = await ChamadaAsync();
        ProvedorDiz(StatusCorrida.Cancelada);

        var r = await new CancelarCorridaUseCase(_corridas, _gateway, Sincronizar(), new Relogio(() => _agora))
            .ExecuteAsync(_empresaId, _viagem.Id, corrida.Id);

        await _gateway.Received(1).CancelarAsync(_empresaId, "ord-1", Arg.Any<CancellationToken>());
        r.Status.Should().Be(StatusCorrida.Cancelada);
    }

    [Fact]
    public async Task Gorjeta_so_procurando_motorista()
    {
        var corrida = await ChamadaAsync();
        ProvedorDiz(StatusCorrida.MotoristaACaminho);
        await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);

        var act = () => new AdicionarGorjetaCorridaUseCase(_corridas, _gateway, Sincronizar())
            .ExecuteAsync(_empresaId, _viagem.Id, corrida.Id, 500);

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*procura motorista*");
    }

    private AdicionarGorjetaCorridaUseCase Gorjeta() => new(_corridas, _gateway, Sincronizar());

    // #2168: na Lalamove a gorjeta nova SUBSTITUI a anterior e precisa ser maior. O valor da tela é o total.
    [Fact]
    public async Task Gorjeta_manda_o_total_ao_provedor()
    {
        var corrida = await ChamadaAsync();
        ProvedorDiz(StatusCorrida.ProcurandoMotorista);

        await Gorjeta().ExecuteAsync(_empresaId, _viagem.Id, corrida.Id, 500);

        await _gateway.Received(1).AdicionarGorjetaAsync(_empresaId, "ord-1", 500, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gorjeta_igual_ou_menor_que_a_atual_nao_vai_ao_provedor()
    {
        var corrida = await ChamadaAsync();
        ProvedorDiz(StatusCorrida.ProcurandoMotorista, valor: 2390);
        await Sincronizar().ExecuteAsync(_empresaId, corrida.Id);

        var act = () => Gorjeta().ExecuteAsync(_empresaId, _viagem.Id, corrida.Id, 500);

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*maior que a atual*R$ 5,00*");
        await _gateway.DidNotReceive().AdicionarGorjetaAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_001)]
    public async Task Gorjeta_fora_do_limite_nao_vai_ao_provedor(int centavos)
    {
        var corrida = await ChamadaAsync();

        var act = () => Gorjeta().ExecuteAsync(_empresaId, _viagem.Id, corrida.Id, centavos);

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _gateway.DidNotReceive().AdicionarGorjetaAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancelar_com_a_viagem_errada_nao_acha_a_corrida()
    {
        var corrida = await ChamadaAsync();

        var act = () => new CancelarCorridaUseCase(_corridas, _gateway, Sincronizar(), new Relogio(() => _agora))
            .ExecuteAsync(_empresaId, Guid.NewGuid(), corrida.Id);

        await act.Should().ThrowAsync<CorridaExternaNaoEncontradaException>();
        await _gateway.DidNotReceive().CancelarAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Atualizar_pela_rota_de_outra_viagem_nao_fala_com_o_provedor()
    {
        var corrida = await ChamadaAsync();
        ProvedorDiz(StatusCorrida.Coletada);

        var act = () => Sincronizar().ExecuteAsync(_empresaId, Guid.NewGuid(), corrida.Id);

        await act.Should().ThrowAsync<CorridaExternaNaoEncontradaException>();
        await _gateway.DidNotReceive().ConsultarAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _viagem.Situacao.Should().Be(SituacaoViagem.Montando);
    }

    [Fact]
    public async Task Cancelar_de_outra_loja_nao_acha_a_corrida()
    {
        var corrida = await ChamadaAsync();

        var act = () => new CancelarCorridaUseCase(_corridas, _gateway, Sincronizar(), new Relogio(() => _agora))
            .ExecuteAsync(Guid.NewGuid(), _viagem.Id, corrida.Id);

        await act.Should().ThrowAsync<CorridaExternaNaoEncontradaException>();
        await _gateway.DidNotReceive().CancelarAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private readonly IWebhookRecebidoRepository _webhooks = Substitute.For<IWebhookRecebidoRepository>();
    private readonly EasyStock.Application.Ports.Output.ITenantContextAccessor _tenant = Substitute.For<EasyStock.Application.Ports.Output.ITenantContextAccessor>();

    private ProcessarWebhookLalamoveUseCase Webhook() => new(_corridas, _webhooks, _tenant, _gateway, Sincronizar(),
        NullLogger<ProcessarWebhookLalamoveUseCase>.Instance);

    private static string CorpoWebhook(string tipo, string orderId, string? prev = null)
    {
        var anterior = prev is null ? "" : $",\"prevOrderId\":\"{prev}\"";
        return "{\"apiKey\":\"pk\",\"timestamp\":1,\"signature\":\"s\",\"eventId\":\"ev-" + orderId + "\",\"eventType\":\"" + tipo
            + "\",\"data\":{\"order\":{\"orderId\":\"" + orderId + "\"}" + anterior + "}}";
    }

    private async Task<CorridaExterna> ChamadaComWebhookAsync()
    {
        var corrida = await ChamadaAsync();
        _corridas.LocalizarPorIdExternoAsync(EmpresaEntregador.Lalamove, "ord-1", Arg.Any<CancellationToken>())
            .Returns(new CorridaLocalizada(_empresaId, corrida.Id));
        _gateway.WebhookAutenticoAsync(_empresaId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _webhooks.TryRegistrarAsync("Lalamove", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(c => WebhookRecebido.Criar("Lalamove", c.ArgAt<string>(1), c.ArgAt<string>(2)));
        return corrida;
    }

    [Fact]
    public async Task Webhook_de_pedido_que_nao_e_nosso_e_ignorado()
    {
        var desfecho = await Webhook().ExecuteAsync(CorpoWebhook("ORDER_STATUS_CHANGED", "alheio"));

        desfecho.Should().Be(DesfechoWebhookLalamove.Ignorado);
        _tenant.DidNotReceive().SetCurrentTenant(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Webhook_sem_corpo_de_pedido_e_sondagem_e_responde_ignorado()
    {
        (await Webhook().ExecuteAsync("""{"eventType":"WALLET_BALANCE_CHANGED","data":{}}""")).Should().Be(DesfechoWebhookLalamove.Ignorado);
        (await Webhook().ExecuteAsync("")).Should().Be(DesfechoWebhookLalamove.Ignorado);
    }

    [Fact]
    public async Task Webhook_com_assinatura_que_nao_confere_nao_muda_nada()
    {
        var corrida = await ChamadaComWebhookAsync();
        _gateway.WebhookAutenticoAsync(_empresaId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        ProvedorDiz(StatusCorrida.Coletada);

        var desfecho = await Webhook().ExecuteAsync(CorpoWebhook("ORDER_STATUS_CHANGED", "ord-1"));

        desfecho.Should().Be(DesfechoWebhookLalamove.AssinaturaInvalida);
        corrida.Status.Should().Be(StatusCorrida.ProcurandoMotorista);
        await _gateway.DidNotReceive().ConsultarAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _webhooks.DidNotReceive().TryRegistrarAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Webhook_repetido_que_falhou_antes_e_reprocessado()
    {
        await ChamadaComWebhookAsync();
        _webhooks.TryRegistrarAsync("Lalamove", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((WebhookRecebido?)null);
        var anterior = WebhookRecebido.Criar("Lalamove", "ev-ord-1", "h");
        _webhooks.ObterAsync("Lalamove", "ev-ord-1", Arg.Any<CancellationToken>()).Returns(anterior);
        ProvedorDiz(StatusCorrida.MotoristaACaminho);

        var desfecho = await Webhook().ExecuteAsync(CorpoWebhook("ORDER_STATUS_CHANGED", "ord-1"));

        desfecho.Should().Be(DesfechoWebhookLalamove.Processado);
        await _webhooks.Received(1).MarcarProcessadoAsync(anterior.Id, true, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Webhook_cuja_reconsulta_falha_fica_marcado_para_a_lalamove_reenviar()
    {
        await ChamadaComWebhookAsync();
        _gateway.ConsultarAsync(_empresaId, "ord-1", Arg.Any<CancellationToken>())
            .Returns<SituacaoCorridaProvedor?>(_ => throw new LogisticaIndisponivelException("fora"));

        var act = () => Webhook().ExecuteAsync(CorpoWebhook("ORDER_STATUS_CHANGED", "ord-1"));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _webhooks.Received(1).MarcarProcessadoAsync(Arg.Any<Guid>(), false, Arg.Is<string>(s => s.StartsWith("UseCaseValidationException")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Webhook_autentico_fixa_a_loja_e_aplica_o_status_da_reconsulta()
    {
        var corrida = await ChamadaComWebhookAsync();
        ProvedorDiz(StatusCorrida.Coletada);

        var desfecho = await Webhook().ExecuteAsync(CorpoWebhook("ORDER_STATUS_CHANGED", "ord-1"));

        desfecho.Should().Be(DesfechoWebhookLalamove.Processado);
        _tenant.Received(1).SetCurrentTenant(_empresaId);
        corrida.Status.Should().Be(StatusCorrida.Coletada);
        _pedido.StatusEnum.Should().Be(StatusPedido.SaiuParaEntrega);
        await _webhooks.Received(1).MarcarProcessadoAsync(Arg.Any<Guid>(), true, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Webhook_repetido_ja_processado_nao_reconsulta()
    {
        await ChamadaComWebhookAsync();
        _webhooks.TryRegistrarAsync("Lalamove", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((WebhookRecebido?)null);
        var anterior = WebhookRecebido.Criar("Lalamove", "ev-ord-1", "h");
        anterior.MarcarProcessado(true);
        _webhooks.ObterAsync("Lalamove", "ev-ord-1", Arg.Any<CancellationToken>()).Returns(anterior);

        var desfecho = await Webhook().ExecuteAsync(CorpoWebhook("ORDER_STATUS_CHANGED", "ord-1"));

        desfecho.Should().Be(DesfechoWebhookLalamove.Duplicado);
        await _gateway.DidNotReceive().ConsultarAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Webhook_de_troca_de_pedido_acha_a_corrida_pelo_id_anterior()
    {
        var corrida = await ChamadaComWebhookAsync();
        _corridas.LocalizarPorIdExternoAsync(EmpresaEntregador.Lalamove, "ord-2", Arg.Any<CancellationToken>()).Returns((CorridaLocalizada?)null);
        _gateway.ConsultarAsync(_empresaId, "ord-2", Arg.Any<CancellationToken>())
            .Returns(new SituacaoCorridaProvedor("ord-2", StatusCorrida.ProcurandoMotorista, null, null, null, null));

        await Webhook().ExecuteAsync(CorpoWebhook("ORDER_REPLACED", "ord-2", prev: "ord-1"));

        corrida.IdExterno.Should().Be("ord-2");
        corrida.IdExternoAnterior.Should().Be("ord-1");
    }
}
