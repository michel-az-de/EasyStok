using System.Text.Json;
using EasyStock.Application.Events.Atendimento;
using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Notifications.Anexos;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Integration;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>#2937: o canhoto em imagem sai uma vez por pedido, no pagamento ou no preparo de quem paga na entrega.</summary>
public class CanhotoPedidoClienteTests
{
    private static readonly DateTime Agora = new(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Pagamento_ComConversaNaJanela_EnviaComPagoEFechoEMarcaOPedido()
    {
        var c = new Cenario();

        var enviou = await c.EnviarAsync(GatilhoCanhoto.Pagamento);

        enviou.Should().BeTrue();
        c.Desenhado!.Pago.Should().BeTrue();
        c.Desenhado.Fecho.Should().Be("Boa massa!");
        c.Desenhado.Itens.Should().ContainSingle(i => i.Nome == "Lasanha Bolonhesa");
        await c.Cloud.Received(1).EnviarImagemAsync("5511999998888", "https://loja.exemplo/files/canhoto.png",
            CanhotoPedidoCliente.Legenda(c.Pedido.Id), Arg.Any<CancellationToken>());
        c.Eventos.Should().ContainSingle(e => e.Tipo == CanhotoPedidoCliente.Marco && e.PedidoId == c.Pedido.Id);
        c.Mensagens.Should().ContainSingle(m => m.Autor == AutorMensagem.Sistema && m.TipoConteudo == TipoConteudoMensagem.Imagem);
        await c.UnitOfWork.Received(1).CommitAsync();
        c.Tenant.Received().SetCurrentTenant(c.EmpresaId);
    }

    [Fact]
    public void Legenda_NaoSePassaPeloResumoQuePedeOk()
    {
        var pedidoId = Guid.NewGuid();
        ResumoPedidoAtendimento.EhResumo(CanhotoPedidoCliente.Legenda(pedidoId), pedidoId).Should().BeFalse();
    }

    [Fact]
    public async Task EmPreparo_ComCobrancaNaEntrega_EnviaSemPago()
    {
        var c = new Cenario();
        c.ComCobrancaNaEntrega();

        var enviou = await c.EnviarAsync(GatilhoCanhoto.EmPreparo);

        enviou.Should().BeTrue();
        c.Desenhado!.Pago.Should().BeFalse();
        c.Eventos.Should().ContainSingle(e => e.Tipo == CanhotoPedidoCliente.Marco);
    }

    [Fact]
    public async Task EmPreparo_SemCobrancaNaEntregaESemQuitacao_NaoEnvia()
    {
        var c = new Cenario();

        (await c.EnviarAsync(GatilhoCanhoto.EmPreparo)).Should().BeFalse();

        c.NadaSaiu();
    }

    [Fact]
    public async Task EmPreparo_PedidoJaQuitadoSemCanhoto_EnviaComPago()
    {
        var c = new Cenario();
        c.Quitado();

        (await c.EnviarAsync(GatilhoCanhoto.EmPreparo)).Should().BeTrue();

        c.Desenhado!.Pago.Should().BeTrue();
    }

    [Fact]
    public async Task CanhotoJaEnviado_NaoEnviaDeNovo()
    {
        var c = new Cenario();
        c.ComCobrancaNaEntrega();
        (await c.EnviarAsync(GatilhoCanhoto.EmPreparo)).Should().BeTrue();

        (await c.EnviarAsync(GatilhoCanhoto.Pagamento)).Should().BeFalse();

        await c.Cloud.Received(1).EnviarImagemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        c.Eventos.Should().ContainSingle();
    }

    [Fact]
    public async Task ForaDaJanela_NaoEnviaNemMarca()
    {
        var c = new Cenario(ultimaEntrada: Agora.AddHours(-25));

        (await c.EnviarAsync(GatilhoCanhoto.Pagamento)).Should().BeFalse();

        c.NadaSaiu();
    }

    [Fact]
    public async Task SemConversa_NaoEnviaNemMarca()
    {
        var c = new Cenario(comConversa: false);

        (await c.EnviarAsync(GatilhoCanhoto.Pagamento)).Should().BeFalse();

        c.NadaSaiu();
    }

    [Fact]
    public async Task ConversaDoChatDoSite_NaoEnviaNemMarca()
    {
        var c = new Cenario(canal: CanalConversa.ChatSite);

        (await c.EnviarAsync(GatilhoCanhoto.Pagamento)).Should().BeFalse();

        c.NadaSaiu();
    }

    [Fact]
    public async Task ClienteBloqueada_NaoEnviaNemMarca()
    {
        var c = new Cenario();
        c.Cliente.Bloquear("teste", Agora);

        (await c.EnviarAsync(GatilhoCanhoto.Pagamento)).Should().BeFalse();

        c.NadaSaiu();
    }

    [Fact]
    public async Task ContatoRevogado_NaoEnviaNemMarca()
    {
        var c = new Cenario();
        c.Consentimentos.Add(ConsentimentoContato.Registrar(c.EmpresaId, c.Cliente.Id, CanalConversa.WhatsApp,
            FinalidadeContato.Transacional, SituacaoConsentimento.Revogado, "teste", Agora));

        (await c.EnviarAsync(GatilhoCanhoto.Pagamento)).Should().BeFalse();

        c.NadaSaiu();
    }

    [Fact]
    public async Task EmPreparo_ClienteDesligouOsAvisos_NaoEnvia()
    {
        var c = new Cenario();
        c.ComCobrancaNaEntrega();
        c.Cliente.DefinirAvisosStatus(false, Agora);

        (await c.EnviarAsync(GatilhoCanhoto.EmPreparo)).Should().BeFalse();

        c.NadaSaiu();
    }

    [Fact]
    public async Task PedidoCancelado_NaoEnvia()
    {
        var c = new Cenario();
        c.Pedido.Status = StatusPedidoMapper.Cancelado;

        (await c.EnviarAsync(GatilhoCanhoto.Pagamento)).Should().BeFalse();

        c.NadaSaiu();
    }

    [Fact]
    public async Task FalhaNoEnvio_NaoLancaNemMarca()
    {
        var c = new Cenario();
        c.Cloud.EnviarImagemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("canal fora"));

        (await c.EnviarAsync(GatilhoCanhoto.Pagamento)).Should().BeFalse();

        c.Eventos.Should().BeEmpty();
        c.Mensagens.Should().BeEmpty();
    }

    [Fact]
    public async Task EnvioIncerto_MarcaParaOOutroGatilhoNaoRepetir()
    {
        var c = new Cenario();
        c.Quitado();
        c.Cloud.EnviarImagemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("a mensagem pode ter saído"));

        (await c.EnviarAsync(GatilhoCanhoto.Pagamento)).Should().BeFalse();
        (await c.EnviarAsync(GatilhoCanhoto.EmPreparo)).Should().BeFalse();

        c.Eventos.Should().ContainSingle(e => e.Tipo == CanhotoPedidoCliente.Marco);
        c.Mensagens.Should().BeEmpty();
        await c.Cloud.Received(1).EnviarImagemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await c.UnitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task ConversaDeOutraCliente_NaoEnviaNemMarca()
    {
        var c = new Cenario(donoDaConversa: Guid.NewGuid());

        (await c.EnviarAsync(GatilhoCanhoto.Pagamento)).Should().BeFalse();

        c.NadaSaiu();
    }

    [Fact]
    public async Task FalhaAntesDoEnvio_NaoLancaNemMarca()
    {
        var c = new Cenario();
        c.Tenant.When(t => t.SetCurrentTenant(Arg.Any<Guid>())).Do(_ => throw new InvalidOperationException("banco fora"));

        (await c.EnviarAsync(GatilhoCanhoto.Pagamento)).Should().BeFalse();

        c.NadaSaiu();
    }

    [Theory]
    [InlineData(StatusPedidoMapper.Preparando, 1)]
    [InlineData(StatusPedidoMapper.Entregue, 0)]
    public async Task HandlerDeStatus_SoPassaOPreparando(string statusNovo, int envios)
    {
        var c = new Cenario();
        c.ComCobrancaNaEntrega();
        var evento = OutboxEventoIntegracao.Criar(c.EmpresaId, "pedido.mudou_status", "pedido", c.Pedido.Id,
            JsonSerializer.Serialize(new { pedidoId = c.Pedido.Id, statusNovo }));

        await new CanhotoPedidoEmPreparoHandler(c.Servico).HandleAsync(evento, CancellationToken.None);

        await c.Cloud.Received(envios).EnviarImagemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandlerDePagamento_EnviaComPago()
    {
        var c = new Cenario();
        var evento = OutboxEventoIntegracao.Criar(c.EmpresaId, PedidoPagoEvent.TipoEvento, "pedido", c.Pedido.Id,
            JsonSerializer.Serialize(new { pedidoId = c.Pedido.Id, conversaId = c.Conversa.Id }));

        await new CanhotoPedidoPagoHandler(c.Servico).HandleAsync(evento, CancellationToken.None);

        c.Desenhado!.Pago.Should().BeTrue();
        c.Eventos.Should().ContainSingle(e => e.Tipo == CanhotoPedidoCliente.Marco);
    }

    [Fact]
    public async Task SemFechoNaLoja_ImagemSaiSemFecho()
    {
        var c = new Cenario(fecho: null);

        (await c.EnviarAsync(GatilhoCanhoto.Pagamento)).Should().BeTrue();

        c.Desenhado!.Fecho.Should().BeNull();
    }

    private sealed class Cenario
    {
        private readonly CanhotoPedidoCliente _servico;
        public CanhotoPedidoCliente Servico => _servico;
        private readonly bool _comConversa;
        private readonly List<CobrancaPedido> _cobrancas = [];
        public Guid EmpresaId { get; } = Guid.NewGuid();
        public Pedido Pedido { get; }
        public Cliente Cliente { get; }
        public Conversa Conversa { get; }
        public IWhatsAppCloudClient Cloud { get; } = Substitute.For<IWhatsAppCloudClient>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public ITenantContextAccessor Tenant { get; } = Substitute.For<ITenantContextAccessor>();
        public List<PedidoEvento> Eventos { get; } = [];
        public List<Mensagem> Mensagens { get; } = [];
        public List<ConsentimentoContato> Consentimentos { get; } = [];
        public ResumoPedidoDto? Desenhado { get; private set; }

        public Cenario(CanalConversa canal = CanalConversa.WhatsApp, DateTime? ultimaEntrada = null, bool comConversa = true,
            string? fecho = "Boa massa!", Guid? donoDaConversa = null)
        {
            _comConversa = comConversa;
            Cliente = Cliente.Criar(EmpresaId, "Maria");
            Pedido = Pedido.Criar(EmpresaId, origem: "whatsapp");
            Pedido.ClienteId = Cliente.Id;
            Pedido.Status = StatusPedidoMapper.Preparando;
            Pedido.Itens.Add(Item("Lasanha Bolonhesa", 2, 30m));
            Pedido.Itens.Add(Item(PedidoItem.PrefixoDoFrete + "Centro", 1, 8m));
            Pedido.RecalcularTotal();
            Conversa = Conversa.Abrir(EmpresaId, canal == CanalConversa.ChatSite ? "chatsite:abc" : "5511999998888",
                Agora.AddDays(-3), "Maria", donoDaConversa ?? Cliente.Id, canal);
            Conversa.RegistrarEntrada(ultimaEntrada ?? Agora.AddHours(-1));

            var pedidos = Substitute.For<IPedidoRepository>();
            pedidos.GetByIdWithDetailsAsync(EmpresaId, Pedido.Id).Returns(Pedido);
            pedidos.ExisteEventoAsync(EmpresaId, Pedido.Id, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(x => Eventos.Any(e => e.Tipo == x.ArgAt<string>(2)));
            pedidos.AddEventoAsync(Arg.Do<PedidoEvento>(Eventos.Add)).Returns(Task.CompletedTask);
            var clientes = Substitute.For<IClienteRepository>();
            clientes.GetByIdAsync(EmpresaId, Cliente.Id).Returns(Cliente);
            var conversas = Substitute.For<IConversaRepository>();
            if (comConversa)
            {
                conversas.ObterIdPorPedidoAsync(EmpresaId, Pedido.Id, Arg.Any<CancellationToken>()).Returns(Conversa.Id);
                conversas.ObterPorIdAsync(EmpresaId, Conversa.Id, Arg.Any<CancellationToken>()).Returns(Conversa);
            }
            conversas.AddMensagemAsync(Arg.Do<Mensagem>(Mensagens.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            var cobrancas = Substitute.For<ICobrancaPedidoRepository>();
            cobrancas.ListarDoPedidoAsync(EmpresaId, Pedido.Id, Arg.Any<CancellationToken>()).Returns(_ => _cobrancas.ToList());
            var storefronts = Substitute.For<IStorefrontRepository>();
            storefronts.GetByEmpresaAsync(EmpresaId, Arg.Any<CancellationToken>())
                .Returns(StorefrontEntity.Criar(EmpresaId, "loja", "Casa da Massa", 0m));
            var configuracao = ConfiguracaoAtendimento.CriarPadrao(EmpresaId);
            configuracao.DefinirFraseFechoCanhoto(fecho ?? string.Empty);
            var configuracoes = Substitute.For<IConfiguracaoAtendimentoRepository>();
            configuracoes.GetOrDefaultAsync(EmpresaId).Returns(configuracao);
            var consentimentos = Substitute.For<IConsentimentoContatoRepository>();
            consentimentos.ListarDoClienteAsync(EmpresaId, Cliente.Id, Arg.Any<CancellationToken>()).Returns(_ => Consentimentos.ToList());
            var renderizador = Substitute.For<IRenderizadorResumoPedido>();
            renderizador.RenderizarAsync(Arg.Do<ResumoPedidoDto>(r => Desenhado = r), Arg.Any<CancellationToken>())
                .Returns(new AnexoGerado([1, 2, 3], "image/png", "png"));
            var armazenamento = Substitute.For<IFileStorage>();
            armazenamento.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
                .Returns(new StoredFileResult("k", "https://loja.exemplo/files/canhoto.png", "image/png", 3));
            Cloud.EnviarImagemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(new EnvioWhatsAppResult("wamid.canhoto"));
            var appConfig = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["App:BaseUrl"] = "https://loja.exemplo" }).Build();
            var relogio = Substitute.For<TimeProvider>();
            relogio.GetUtcNow().Returns(new DateTimeOffset(Agora));

            _servico = new CanhotoPedidoCliente(pedidos, clientes, conversas, cobrancas, storefronts, configuracoes,
                new PoliticaEnvioCliente(consentimentos), Cloud, appConfig, UnitOfWork, relogio, Tenant,
                NullLogger<CanhotoPedidoCliente>.Instance, renderizador, armazenamento);
        }

        public void ComCobrancaNaEntrega() =>
            _cobrancas.Add(CobrancaPedido.CriarNaEntrega(EmpresaId, Pedido.Id, Pedido.Total.Valor, Agora));

        public void Quitado() => Eventos.Add(new PedidoEvento
        {
            Id = Guid.NewGuid(), PedidoId = Pedido.Id, Tipo = QuitacaoPedido.Marco, OcorridoEm = Agora,
        });

        public Task<bool> EnviarAsync(GatilhoCanhoto gatilho) =>
            _servico.EnviarAsync(EmpresaId, Pedido.Id, gatilho, _comConversa ? Conversa.Id : null, CancellationToken.None);

        public void NadaSaiu()
        {
            Cloud.DidNotReceive().EnviarImagemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
            Eventos.Should().NotContain(e => e.Tipo == CanhotoPedidoCliente.Marco);
            Mensagens.Should().BeEmpty();
        }

        private PedidoItem Item(string nome, decimal qtd, decimal preco)
        {
            var item = new PedidoItem { Id = Guid.NewGuid(), PedidoId = Pedido.Id, Nome = nome, Quantidade = qtd, PrecoUnitario = preco };
            item.RecalcularSubtotal();
            return item;
        }
    }
}
