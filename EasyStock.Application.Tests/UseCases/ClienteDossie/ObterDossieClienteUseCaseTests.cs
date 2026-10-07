using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.UseCases.ClienteDossie;

/// <summary>S25: dossiê do cliente ao lado da conversa (US-007, US-017, US-018, RN-12, RN-13).</summary>
public class ObterDossieClienteUseCaseTests
{
    private static readonly DateTime Base = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IClienteCrmRepository _crm = Substitute.For<IClienteCrmRepository>();
    private readonly IHistoricoPedidosClienteQueries _pedidos = Substitute.For<IHistoricoPedidosClienteQueries>();
    private readonly IDomicilioQueries _domicilio = Substitute.For<IDomicilioQueries>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly Cliente _cliente;

    public ObterDossieClienteUseCaseTests()
    {
        _cliente = Cliente.Criar(_empresaId, "Maria");
        _cliente.Telefone = "11999998888";
        _cliente.Enderecos.Add(new ClienteEndereco
        {
            Id = Guid.NewGuid(), ClienteId = _cliente.Id, Cep = "05500000", Numero = "12", Padrao = true, CriadoEm = Base,
        });
        _cliente.AdicionarTag("vegano", OrigemClienteTag.Dona, Base);
        _clientes.GetByIdWithDetailsAsync(_empresaId, _cliente.Id).Returns(_cliente);
        _crm.ObterComTagsAsync(_empresaId, _cliente.Id, Arg.Any<CancellationToken>()).Returns(_cliente);
        _crm.ListarNotasAsync(_empresaId, _cliente.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([ClienteNota.Criar(_empresaId, _cliente.Id, "não gosta de coco", "Baba", Base)]);
        _pedidos.ListarAsync(_empresaId, _cliente.Id, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        _domicilio.ListarMesmoDomicilioAsync(_empresaId, _cliente.Id, Arg.Any<CancellationToken>()).Returns([]);
        _conversas.ListarPorClienteAsync(_empresaId, _cliente.Id, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private ObterDossieClienteUseCase CriarUseCase() => new(_clientes, _crm, _pedidos, _domicilio, _conversas);

    private static PedidoResumoCliente Pedido(int dia, string status, params (string Nome, decimal Qtd)[] itens) =>
        new(Guid.NewGuid(), status, Base.AddDays(dia), 10m,
            itens.Select(i => new ItemPedidoResumo(i.Nome, i.Qtd)).ToList());

    [Fact]
    public async Task FavoritoEUltimos10()
    {
        // 12 pedidos, mais novo primeiro (contrato da consulta). Brigadeiro aparece em 7 pedidos;
        // Bolo de cenoura soma mais quantidade (em 5 pedidos) mas aparece em menos pedidos.
        var pedidos = Enumerable.Range(0, 12)
            .Select(i => i < 7
                ? Pedido(12 - i, StatusPedidoMapper.Entregue, ("Brigadeiro", 1m))
                : Pedido(12 - i, StatusPedidoMapper.Entregue, ("Bolo de cenoura", 10m)))
            .ToList();
        // O cancelado mais novo não conta como compra nem para o favorito.
        pedidos.Insert(0, Pedido(20, StatusPedidoMapper.Cancelado, ("Bolo de cenoura", 50m)));
        _pedidos.ListarAsync(_empresaId, _cliente.Id, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(pedidos);

        var dossie = await CriarUseCase().ExecuteAsync(new ObterDossieClienteQuery(_empresaId, _cliente.Id));

        dossie.Should().NotBeNull();
        dossie!.UltimosPedidos.Should().HaveCount(10);
        dossie.UltimosPedidos.Select(p => p.Id).Should().Equal(pedidos.Take(10).Select(p => p.Id));
        dossie.ItemFavorito.Should().Be(new ItemFavoritoDossie("Brigadeiro", 7, 7m));
        dossie.UltimaCompraEm.Should().Be(Base.AddDays(12));
        dossie.TotalPedidos.Should().Be(12);
        dossie.Cliente.Should().Be(new DossieClienteDados(_cliente.Id, "Maria", "11999998888", null, null, null));
        dossie.Tags.Should().ContainSingle(t => t.Tag == "vegano");
        dossie.Notas.Should().ContainSingle(n => n.Texto == "não gosta de coco" && n.Autor == "Baba");
        dossie.Enderecos.Should().ContainSingle(e => e.Cep == "05500000");
        dossie.Preferencias!.AvisosStatusAtivos.Should().BeTrue();
    }

    [Fact]
    public async Task ClienteDeOutraEmpresaDevolveNulo()
    {
        var dossie = await CriarUseCase().ExecuteAsync(new ObterDossieClienteQuery(_empresaId, Guid.NewGuid()));
        dossie.Should().BeNull();
    }

    [Fact]
    public async Task DomicilioEConversasRecentesVemDasConsultas()
    {
        var vizinho = new ClienteMesmoDomicilio(Guid.NewGuid(), "João");
        _domicilio.ListarMesmoDomicilioAsync(_empresaId, _cliente.Id, Arg.Any<CancellationToken>()).Returns([vizinho]);
        var pedidoAberto = Pedido(1, StatusPedidoMapper.Preparando, ("Brigadeiro", 2m));
        _pedidos.ListarAsync(_empresaId, _cliente.Id, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([pedidoAberto]);
        var conversa = Conversa.Abrir(_empresaId, "5511999998888", Base, "Maria", _cliente.Id);
        conversa.DefinirPedidoEmAndamento(pedidoAberto.Id);
        _conversas.ListarPorClienteAsync(_empresaId, _cliente.Id, 5, Arg.Any<CancellationToken>()).Returns([conversa]);

        var dossie = await CriarUseCase().ExecuteAsync(new ObterDossieClienteQuery(_empresaId, _cliente.Id));

        dossie!.Domicilio.Should().Equal(vizinho);
        dossie.ConversasRecentes.Should().ContainSingle()
            .Which.Should().Be(new ConversaRecenteDossie(conversa.Id, CanalConversa.WhatsApp, SituacaoConversa.Automatica, Base, Base));
        dossie.PedidoEmAndamento.Should().Be(pedidoAberto);
    }

    [Fact]
    public async Task ConversaSemClienteDevolveDossieMinimo()
    {
        var conversa = Conversa.Abrir(_empresaId, "+55 11 97777-6666", Base, "Ana do perfil");
        _conversas.ObterPorIdAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);

        var dossie = await CriarUseCase().ObterPorConversaAsync(_empresaId, conversa.Id);

        dossie.Should().NotBeNull();
        dossie!.Cliente.Should().Be(new DossieClienteDados(null, "Ana do perfil", "5511977776666", null, null, null));
        dossie.UltimosPedidos.Should().BeEmpty();
        dossie.Notas.Should().BeEmpty();
        dossie.Domicilio.Should().BeEmpty();
        dossie.Preferencias.Should().BeNull();
        await _pedidos.DidNotReceiveWithAnyArgs().ListarAsync(default, default, default, default);
    }

    [Fact]
    public async Task ConversaComClienteUsaOPedidoDaConversa()
    {
        var pedidoDaConversa = Pedido(2, StatusPedidoMapper.AguardandoPagamento, ("Brigadeiro", 1m));
        _pedidos.ListarAsync(_empresaId, _cliente.Id, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([pedidoDaConversa]);
        var conversa = Conversa.Abrir(_empresaId, "5511999998888", Base, "Maria", _cliente.Id);
        conversa.DefinirPedidoEmAndamento(pedidoDaConversa.Id);
        _conversas.ObterPorIdAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);

        var dossie = await CriarUseCase().ObterPorConversaAsync(_empresaId, conversa.Id);

        dossie!.Cliente.Id.Should().Be(_cliente.Id);
        dossie.PedidoEmAndamento.Should().Be(pedidoDaConversa);
    }

    [Fact]
    public async Task LeadDoChatDoSite_TrazOContatoInformadoForaDoCadastro()
    {
        // #1430: o que o visitante escreveu no formulário vem à parte, não como dado do cliente.
        var conversa = Conversa.Abrir(_empresaId, "sessao-1", Base, "Visitante do site", canal: CanalConversa.ChatSite);
        conversa.RegistrarContatoInformado(ContatoInformadoVisitante.Criar("Ana Lima", "(11) 97777-6666", "ana@exemplo.com", true, Base));
        _conversas.ObterPorIdAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);

        var dossie = await CriarUseCase().ObterPorConversaAsync(_empresaId, conversa.Id);

        dossie!.Cliente.Should().Be(new DossieClienteDados(null, "Ana Lima", null, null, null, null));
        dossie.ContatoInformado.Should().Be(new ContatoInformadoDossie("Ana Lima", "+5511977776666", "ana@exemplo.com", Base));
    }

    [Fact]
    public async Task ConversaComCliente_NaoTrazContatoInformado()
    {
        var conversa = Conversa.Abrir(_empresaId, "sessao-1", Base, "Maria", _cliente.Id, CanalConversa.ChatSite);
        conversa.RegistrarContatoInformado(ContatoInformadoVisitante.Criar("Maria", "(11) 97777-6666", null, true, Base));
        _conversas.ObterPorIdAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);

        var dossie = await CriarUseCase().ObterPorConversaAsync(_empresaId, conversa.Id);

        dossie!.Cliente.Id.Should().Be(_cliente.Id);
        dossie.ContatoInformado.Should().BeNull();
    }

    [Fact]
    public async Task ConversaInexistenteDevolveNulo() =>
        (await CriarUseCase().ObterPorConversaAsync(_empresaId, Guid.NewGuid())).Should().BeNull();
}
