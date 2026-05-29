using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Pedidos;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Storefront.Pedidos;

/// <summary>
/// Testes unitários do <see cref="ListarPedidosClienteUseCase"/> (TASK-EZ-PEDIDOS-001).
///
/// <para>
/// Cobertura:
/// </para>
/// <list type="bullet">
///   <item>Validação de input (ClienteId vazio).</item>
///   <item>Storefront inexistente/inativo → <see cref="StorefrontNaoEncontradoException"/>.</item>
///   <item>Cliente sem pedidos → lista vazia.</item>
///   <item>Mix de status (Aguardando→EmPreparo, AguardandoAprovacaoBaba, Entregue, Cancelado) → mapeamento PascalCase do contrato.</item>
///   <item>Limit clamp: 100 → 50, 0/negativo → 20, valor válido preservado.</item>
///   <item>Item de frete separado: subtotal (itens-produto) + frete + total.</item>
///   <item>Avaliação oculta (<c>OcultadoEm != null</c>) → <c>DTO.Avaliacao == null</c>.</item>
///   <item>Vaga ausente → <c>JanelaEntrega == null</c>.</item>
///   <item>Motivo cancelamento: prioriza <c>MensagemRecusaCliente</c>, fallback pra <c>MotivoRecusa</c>.</item>
///   <item>Endereço sempre null (limitação MVP documentada).</item>
/// </list>
/// </summary>
public class ListarPedidosClienteUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private const string Slug = "casa-da-baba";

    // ── Fixture ────────────────────────────────────────────────────────────

    private sealed record Sut(
        ListarPedidosClienteUseCase UseCase,
        IStorefrontRepository StorefrontRepo,
        IPedidoStorefrontRepository PedidoRepo,
        IPedidoAvaliacaoRepository AvaliacaoRepo,
        IVagaOcupadaRepository VagaRepo);

    private static Sut BuildSut(
        Domain.Entities.Storefront.Storefront? storefront = null,
        IReadOnlyList<Pedido>? pedidos = null,
        IReadOnlyDictionary<Guid, PedidoAvaliacao>? avaliacoes = null,
        IReadOnlyDictionary<Guid, (VagaOcupada, JanelaEntrega)>? vagas = null)
    {
        var storefrontRepo = Substitute.For<IStorefrontRepository>();
        storefrontRepo.GetBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => storefront);

        var pedidoRepo = Substitute.For<IPedidoStorefrontRepository>();
        pedidoRepo.ListarPorClienteAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Pedido>>(pedidos ?? Array.Empty<Pedido>()));

        var avaliacaoRepo = Substitute.For<IPedidoAvaliacaoRepository>();
        avaliacaoRepo.GetByPedidoIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, PedidoAvaliacao>>(
                avaliacoes ?? new Dictionary<Guid, PedidoAvaliacao>()));

        var vagaRepo = Substitute.For<IVagaOcupadaRepository>();
        vagaRepo.GetByPedidoIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, (VagaOcupada, JanelaEntrega)>>(
                vagas ?? new Dictionary<Guid, (VagaOcupada, JanelaEntrega)>()));

        var useCase = new ListarPedidosClienteUseCase(
            storefrontRepo, pedidoRepo, avaliacaoRepo, vagaRepo,
            NullLogger<ListarPedidosClienteUseCase>.Instance);

        return new Sut(useCase, storefrontRepo, pedidoRepo, avaliacaoRepo, vagaRepo);
    }

    private static Domain.Entities.Storefront.Storefront StorefrontAtivo()
    {
        var s = Domain.Entities.Storefront.Storefront.Criar(
            empresaId: EmpresaId,
            slug: Slug,
            tituloPublico: "Casa da Babá",
            pedidoMinimoEntrega: 0m);
        s.Ativar();
        return s;
    }

    private static Pedido PedidoStub(
        string status = StatusPedidoMapper.Entregue,
        decimal total = 100m,
        Guid? id = null,
        DateTime? criadoEm = null,
        string? mensagemRecusaCliente = null,
        string? motivoRecusa = null,
        IEnumerable<PedidoItem>? itens = null)
    {
        var agora = DateTime.UtcNow;
        var pedido = new Pedido
        {
            Id = id ?? Guid.NewGuid(),
            EmpresaId = EmpresaId,
            ClienteId = ClienteId,
            Status = status,
            Total = Dinheiro.FromDecimal(total),
            Origem = "storefront",
            CriadoEm = criadoEm ?? agora,
            AlteradoEm = agora,
            MensagemRecusaCliente = mensagemRecusaCliente,
            MotivoRecusa = motivoRecusa,
        };
        if (itens is not null)
            pedido.Itens = itens.ToList();
        return pedido;
    }

    // ── Validação de input ──────────────────────────────────────────────────

    [Fact]
    public async Task ClienteId_vazio_lanca_ArgumentException()
    {
        var sut = BuildSut(storefront: StorefrontAtivo());
        var input = new ListarPedidosClienteInput(Slug, Guid.Empty);

        Func<Task> act = async () => await sut.UseCase.ExecuteAsync(input);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*ClienteId*");
    }

    [Fact]
    public async Task Storefront_inexistente_lanca_StorefrontNaoEncontradoException()
    {
        var sut = BuildSut(storefront: null);
        var input = new ListarPedidosClienteInput(Slug, ClienteId);

        Func<Task> act = async () => await sut.UseCase.ExecuteAsync(input);

        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>();
    }

    [Fact]
    public async Task Storefront_inativo_lanca_StorefrontNaoEncontradoException()
    {
        var storefront = StorefrontAtivo();
        storefront.Desativar();
        var sut = BuildSut(storefront: storefront);
        var input = new ListarPedidosClienteInput(Slug, ClienteId);

        Func<Task> act = async () => await sut.UseCase.ExecuteAsync(input);

        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>();
    }

    // ── Happy paths ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Cliente_sem_pedidos_retorna_lista_vazia()
    {
        var sut = BuildSut(
            storefront: StorefrontAtivo(),
            pedidos: Array.Empty<Pedido>());
        var input = new ListarPedidosClienteInput(Slug, ClienteId);

        var result = await sut.UseCase.ExecuteAsync(input);

        result.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Lista_pedidos_preserva_ordem_do_repo()
    {
        var antigo = PedidoStub(id: Guid.NewGuid(), criadoEm: DateTime.UtcNow.AddDays(-5));
        var recente = PedidoStub(id: Guid.NewGuid(), criadoEm: DateTime.UtcNow);

        // Repo já entrega em DESC por contrato (verificado no PedidoStorefrontRepository).
        var sut = BuildSut(
            storefront: StorefrontAtivo(),
            pedidos: new[] { recente, antigo });
        var input = new ListarPedidosClienteInput(Slug, ClienteId);

        var result = await sut.UseCase.ExecuteAsync(input);

        result.Pedidos.Should().HaveCount(2);
        result.Pedidos[0].PedidoId.Should().Be(recente.Id);
        result.Pedidos[1].PedidoId.Should().Be(antigo.Id);
    }

    [Fact]
    public async Task Status_mapeado_para_PascalCase_do_contrato()
    {
        var pedidos = new[]
        {
            PedidoStub(status: StatusPedidoMapper.AguardandoPagamento, id: Guid.NewGuid()),
            PedidoStub(status: StatusPedidoMapper.AguardandoAprovacaoBaba, id: Guid.NewGuid()),
            PedidoStub(status: StatusPedidoMapper.AprovadoBaba, id: Guid.NewGuid()),
            PedidoStub(status: StatusPedidoMapper.Preparando, id: Guid.NewGuid()),
            PedidoStub(status: StatusPedidoMapper.Entregue, id: Guid.NewGuid()),
            PedidoStub(status: StatusPedidoMapper.Cancelado, id: Guid.NewGuid()),
        };
        var sut = BuildSut(storefront: StorefrontAtivo(), pedidos: pedidos);

        var result = await sut.UseCase.ExecuteAsync(new ListarPedidosClienteInput(Slug, ClienteId));

        result.Pedidos.Select(p => p.Status).Should().BeEquivalentTo(new[]
        {
            "AguardandoPagamento",
            "AguardandoAprovacaoBaba",
            "AprovadoBaba",
            "EmPreparo",
            "Entregue",
            "Cancelado",
        });
    }

    // ── Limit clamp ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, 20)]
    [InlineData(0, 20)]
    [InlineData(-5, 20)]
    [InlineData(10, 10)]
    [InlineData(50, 50)]
    [InlineData(100, 50)]
    public async Task Limit_clamp_passa_valor_correto_ao_repo(int? input, int esperado)
    {
        var sut = BuildSut(storefront: StorefrontAtivo());

        await sut.UseCase.ExecuteAsync(new ListarPedidosClienteInput(Slug, ClienteId, Limit: input));

        await sut.PedidoRepo.Received(1).ListarPorClienteAsync(
            EmpresaId, ClienteId, esperado, Arg.Any<CancellationToken>());
    }

    // ── Mapeamento de itens / frete / totais ─────────────────────────────────

    [Fact]
    public async Task Item_de_frete_extraido_e_separado_dos_itens_produto()
    {
        var pedido = PedidoStub(total: 99.50m, itens: new[]
        {
            new PedidoItem
            {
                Id = Guid.NewGuid(), ProdutoId = Guid.NewGuid(),
                Nome = "Lasanha", Quantidade = 1, PrecoUnitario = 84m, Subtotal = 84m,
            },
            new PedidoItem
            {
                Id = Guid.NewGuid(), ProdutoId = null,
                Nome = "Entrega — Butantã", Quantidade = 1, PrecoUnitario = 15.50m, Subtotal = 15.50m,
            },
        });
        var sut = BuildSut(storefront: StorefrontAtivo(), pedidos: new[] { pedido });

        var result = await sut.UseCase.ExecuteAsync(new ListarPedidosClienteInput(Slug, ClienteId));

        var dto = result.Pedidos.Single();
        dto.Itens.Should().HaveCount(1);
        dto.Itens[0].Nome.Should().Be("Lasanha");
        dto.Itens[0].PrecoCentavos.Should().Be(8400);
        dto.SubtotalCentavos.Should().Be(8400);
        dto.FreteCentavos.Should().Be(1550);
        dto.TotalCentavos.Should().Be(9950);
    }

    [Fact]
    public async Task Pedido_sem_item_frete_tem_frete_zero()
    {
        var pedido = PedidoStub(total: 50m, itens: new[]
        {
            new PedidoItem
            {
                Id = Guid.NewGuid(), ProdutoId = Guid.NewGuid(),
                Nome = "Bolo", Quantidade = 1, PrecoUnitario = 50m, Subtotal = 50m,
            },
        });
        var sut = BuildSut(storefront: StorefrontAtivo(), pedidos: new[] { pedido });

        var result = await sut.UseCase.ExecuteAsync(new ListarPedidosClienteInput(Slug, ClienteId));

        var dto = result.Pedidos.Single();
        dto.FreteCentavos.Should().Be(0);
        dto.SubtotalCentavos.Should().Be(5000);
        dto.TotalCentavos.Should().Be(5000);
    }

    // ── Avaliação ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Avaliacao_visivel_eh_mapeada()
    {
        var pedido = PedidoStub(status: StatusPedidoMapper.Entregue);
        var avaliacao = PedidoAvaliacao.Criar(
            pedidoId: pedido.Id,
            clienteId: ClienteId,
            empresaId: EmpresaId,
            estrelas: 5,
            comentario: "Maravilhoso!",
            recomendariaParaAmigos: true,
            fotoUrl: null,
            solicitadoEm: DateTime.UtcNow.AddHours(-1));

        var sut = BuildSut(
            storefront: StorefrontAtivo(),
            pedidos: new[] { pedido },
            avaliacoes: new Dictionary<Guid, PedidoAvaliacao> { [pedido.Id] = avaliacao });

        var result = await sut.UseCase.ExecuteAsync(new ListarPedidosClienteInput(Slug, ClienteId));

        result.Pedidos.Single().Avaliacao.Should().NotBeNull();
        result.Pedidos.Single().Avaliacao!.Estrelas.Should().Be(5);
        result.Pedidos.Single().Avaliacao!.Comentario.Should().Be("Maravilhoso!");
    }

    [Fact]
    public async Task Avaliacao_oculta_eh_omitida_do_DTO()
    {
        var pedido = PedidoStub(status: StatusPedidoMapper.Entregue);
        var avaliacao = PedidoAvaliacao.Criar(
            pedidoId: pedido.Id,
            clienteId: ClienteId,
            empresaId: EmpresaId,
            estrelas: 1,
            comentario: "spam",
            recomendariaParaAmigos: false,
            fotoUrl: null,
            solicitadoEm: DateTime.UtcNow.AddHours(-1));
        avaliacao.Ocultar();

        var sut = BuildSut(
            storefront: StorefrontAtivo(),
            pedidos: new[] { pedido },
            avaliacoes: new Dictionary<Guid, PedidoAvaliacao> { [pedido.Id] = avaliacao });

        var result = await sut.UseCase.ExecuteAsync(new ListarPedidosClienteInput(Slug, ClienteId));

        result.Pedidos.Single().Avaliacao.Should().BeNull();
    }

    // ── Vaga / Janela ───────────────────────────────────────────────────────

    [Fact]
    public async Task Pedido_sem_vaga_tem_JanelaEntrega_null()
    {
        var pedido = PedidoStub();
        var sut = BuildSut(
            storefront: StorefrontAtivo(),
            pedidos: new[] { pedido },
            vagas: new Dictionary<Guid, (VagaOcupada, JanelaEntrega)>());

        var result = await sut.UseCase.ExecuteAsync(new ListarPedidosClienteInput(Slug, ClienteId));

        result.Pedidos.Single().JanelaEntrega.Should().BeNull();
    }

    [Fact]
    public async Task Pedido_com_vaga_mapeia_janela_com_horarios_formatados()
    {
        var pedido = PedidoStub();
        var storefront = StorefrontAtivo();
        var janela = JanelaEntrega.Criar(
            storefrontId: storefront.Id,
            diaDaSemana: 6, // Sábado
            horaInicio: new TimeOnly(12, 0),
            horaFim: new TimeOnly(14, 0),
            capacidadeMaxima: 5,
            label: "Sábado 12h–14h");
        var dataEntrega = new DateOnly(2026, 5, 30);
        var vaga = VagaOcupada.Ocupar(janela.Id, dataEntrega, pedido.Id);

        var sut = BuildSut(
            storefront: storefront,
            pedidos: new[] { pedido },
            vagas: new Dictionary<Guid, (VagaOcupada, JanelaEntrega)>
            {
                [pedido.Id] = (vaga, janela),
            });

        var result = await sut.UseCase.ExecuteAsync(new ListarPedidosClienteInput(Slug, ClienteId));

        var dto = result.Pedidos.Single().JanelaEntrega;
        dto.Should().NotBeNull();
        dto!.Data.Should().Be(dataEntrega);
        dto.HoraInicio.Should().Be("12:00");
        dto.HoraFim.Should().Be("14:00");
        dto.Label.Should().Be("Sábado 12h–14h");
    }

    // ── Motivo cancelamento ─────────────────────────────────────────────────

    [Fact]
    public async Task Cancelado_com_MensagemRecusaCliente_usa_mensagem()
    {
        var pedido = PedidoStub(
            status: StatusPedidoMapper.Cancelado,
            mensagemRecusaCliente: "Babá sem disponibilidade no dia",
            motivoRecusa: "operacional");

        var sut = BuildSut(storefront: StorefrontAtivo(), pedidos: new[] { pedido });

        var result = await sut.UseCase.ExecuteAsync(new ListarPedidosClienteInput(Slug, ClienteId));

        result.Pedidos.Single().MotivoCancelamento.Should().Be("Babá sem disponibilidade no dia");
    }

    [Fact]
    public async Task Cancelado_sem_mensagem_usa_MotivoRecusa_canonico()
    {
        var pedido = PedidoStub(
            status: StatusPedidoMapper.Cancelado,
            mensagemRecusaCliente: null,
            motivoRecusa: "estoque_insuficiente");

        var sut = BuildSut(storefront: StorefrontAtivo(), pedidos: new[] { pedido });

        var result = await sut.UseCase.ExecuteAsync(new ListarPedidosClienteInput(Slug, ClienteId));

        result.Pedidos.Single().MotivoCancelamento.Should().Be("estoque_insuficiente");
    }

    [Fact]
    public async Task Nao_cancelado_tem_MotivoCancelamento_null()
    {
        var pedido = PedidoStub(
            status: StatusPedidoMapper.Entregue,
            mensagemRecusaCliente: "ignorar",  // não deve aparecer pra Entregue
            motivoRecusa: "operacional");

        var sut = BuildSut(storefront: StorefrontAtivo(), pedidos: new[] { pedido });

        var result = await sut.UseCase.ExecuteAsync(new ListarPedidosClienteInput(Slug, ClienteId));

        result.Pedidos.Single().MotivoCancelamento.Should().BeNull();
    }

    // ── Limitações MVP ──────────────────────────────────────────────────────

    [Fact]
    public async Task Endereco_sempre_null_no_MVP()
    {
        var pedido = PedidoStub();
        var sut = BuildSut(storefront: StorefrontAtivo(), pedidos: new[] { pedido });

        var result = await sut.UseCase.ExecuteAsync(new ListarPedidosClienteInput(Slug, ClienteId));

        result.Pedidos.Single().Endereco.Should().BeNull();
    }

    [Fact]
    public async Task InitPointUrl_sempre_null_no_MVP()
    {
        var pedido = PedidoStub(status: StatusPedidoMapper.AguardandoPagamento);
        var sut = BuildSut(storefront: StorefrontAtivo(), pedidos: new[] { pedido });

        var result = await sut.UseCase.ExecuteAsync(new ListarPedidosClienteInput(Slug, ClienteId));

        result.Pedidos.Single().InitPointUrl.Should().BeNull();
    }
}
