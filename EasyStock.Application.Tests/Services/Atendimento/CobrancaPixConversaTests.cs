using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Notifications.Anexos;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute.ExceptionExtensions;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>#2962 (E3): a cobrança Pix em dois balões, a falha parcial, o fallback sem PDF e o código fora de log e de falha.</summary>
public class CobrancaPixConversaTests
{
    private const string Codigo = "00020126580014BR.GOV.BCB.PIX0136chave-secreta-da-loja5204000053039865802BR6304ABCD";
    private static readonly DateTime Agora = new(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);

    private sealed class LoggerCapturado : ILogger<CobrancaPixConversa>
    {
        public List<string> Linhas { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Linhas.Add(formatter(state, exception) + exception);
    }

    private sealed class Cenario
    {
        public Guid EmpresaId { get; } = Guid.NewGuid();
        public Pedido Pedido { get; }
        public Conversa Conversa { get; }
        public ICanalMensageria Canal { get; } = Substitute.For<ICanalMensageria>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IRenderizadorPdfPedido Renderizador { get; } = Substitute.For<IRenderizadorPdfPedido>();
        public IFileStorage Armazenamento { get; } = Substitute.For<IFileStorage>();
        public LoggerCapturado Log { get; } = new();
        public List<Mensagem> Gravadas { get; } = [];
        public List<string> Ordem { get; } = [];
        public CobrancaPixConversa Servico { get; }

        public Cenario(DateTime? ultimaEntrada = null, string? enviarPdf = null, bool comPublicador = true)
        {
            Pedido = Pedido.Criar(EmpresaId, origem: "whatsapp");
            var item = new PedidoItem { Id = Guid.NewGuid(), PedidoId = Pedido.Id, Nome = "Lasanha Bolonhesa", Quantidade = 2, PrecoUnitario = 30m };
            item.RecalcularSubtotal();
            Pedido.Itens.Add(item);
            Pedido.RecalcularTotal();
            Conversa = Conversa.Abrir(EmpresaId, "5511999998888", Agora.AddDays(-3), "Maria");
            Conversa.RegistrarEntrada(ultimaEntrada ?? Agora.AddMinutes(-5));

            Canal.Canal.Returns(CanalConversa.WhatsApp);
            Canal.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci => { Ordem.Add("texto:" + ci.ArgAt<string>(1)); return "wamid.texto"; });
            Canal.EnviarDocumentoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(ci => { Ordem.Add("documento"); return "wamid.pdf"; });

            var conversas = Substitute.For<IConversaRepository>();
            conversas.ObterPorIdAsync(EmpresaId, Conversa.Id, Arg.Any<CancellationToken>()).Returns(Conversa);
            conversas.AddMensagemAsync(Arg.Do<Mensagem>(Gravadas.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            var pedidos = Substitute.For<IPedidoRepository>();
            pedidos.GetByIdWithDetailsAsync(EmpresaId, Pedido.Id).Returns(Pedido);
            var storefronts = Substitute.For<IStorefrontRepository>();
            storefronts.GetByEmpresaAsync(EmpresaId, Arg.Any<CancellationToken>())
                .Returns(StorefrontEntity.Criar(EmpresaId, "loja", "Casa da Baba", 0m));

            Renderizador.RenderizarPedidoAsync(Arg.Any<ResumoPedidoDto>(), Arg.Any<PixPdfDto>(), Arg.Any<CancellationToken>())
                .Returns(new AnexoGerado([37, 80, 68, 70], "application/pdf", "pdf"));
            Armazenamento.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
                .Returns(new StoredFileResult("k", "https://loja.exemplo/privado/k", "application/pdf", 4));
            Armazenamento.CreatePreSignedDownloadUrlAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new Uri("https://loja.exemplo/files/assinado.pdf?sig=1"));

            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:BaseUrl"] = "https://loja.exemplo",
                [PublicadorDocumentoPedido.ChaveEnviarPdf] = enviarPdf,
            }).Build();
            var publicador = comPublicador ? new PublicadorDocumentoPedido(Renderizador, Armazenamento, config) : null;
            Servico = new CobrancaPixConversa(conversas, new ResolvedorCanal([Canal]), UnitOfWork, pedidos, storefronts, config, Log, publicador);
        }

        public CobrancaPixAviso Aviso() => new(EmpresaId, Conversa.Id, Pedido.Id,
            new PixPdfDto(Codigo, 60m, 0m, "CASA DA BABA"), "TEXTO UNICO ANTIGO\n\n" + Codigo);

        public Task<bool> EnviarAsync(Guid? usuario = null) => Servico.EnviarAsync(Aviso(), Agora, usuario);
    }

    [Fact]
    public async Task EnviaDoisBaloesEmOrdem_PdfComLegendaEDepoisSoOCodigo()
    {
        var c = new Cenario();

        (await c.EnviarAsync()).Should().BeTrue();

        c.Ordem.Should().Equal("documento", "texto:" + Codigo);
        await c.Canal.Received(1).EnviarDocumentoAsync("5511999998888", "https://loja.exemplo/files/assinado.pdf?sig=1",
            "Pedido-" + ResumoPedidoAtendimento.Codigo(c.Pedido.Id) + ".pdf",
            Arg.Is<string?>(l => l!.StartsWith("Pedido ") && l.EndsWith("Pague em até 30 min pelo Pix abaixo") && !l.Contains(Codigo)),
            Arg.Any<CancellationToken>());
        c.Gravadas.Should().HaveCount(2);
        c.Gravadas[0].TipoConteudo.Should().Be(TipoConteudoMensagem.Documento);
        c.Gravadas[1].TipoConteudo.Should().Be(TipoConteudoMensagem.Texto);
        c.Gravadas[1].Texto.Should().Be(Codigo);
        c.Gravadas.Should().OnlyContain(m => m.Status == StatusMensagem.Enviada);
        await c.UnitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task PdfPrivadoComLinkAssinado_ComValidadeDe24Horas()
    {
        var c = new Cenario();

        await c.EnviarAsync();

        await c.Armazenamento.Received(1).UploadAsync(
            Arg.Is<FileUploadRequest>(r => !r.IsPublic && r.ContentType == "application/pdf"), Arg.Any<CancellationToken>());
        await c.Armazenamento.Received(1).CreatePreSignedDownloadUrlAsync(
            "k", TimeSpan.FromHours(24), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PdfQueNaoGera_SaiALegendaEmTextoEOCodigoSegue()
    {
        var c = new Cenario();
        c.Renderizador.RenderizarPedidoAsync(Arg.Any<ResumoPedidoDto>(), Arg.Any<PixPdfDto>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("falhou " + Codigo));

        (await c.EnviarAsync()).Should().BeTrue();

        c.Ordem.Should().HaveCount(2);
        c.Ordem[0].Should().StartWith("texto:Pedido ").And.NotContain(Codigo);
        c.Ordem[1].Should().Be("texto:" + Codigo);
        c.Gravadas[0].TipoConteudo.Should().Be(TipoConteudoMensagem.Texto);
        c.Log.Linhas.Should().NotContain(l => l.Contains(Codigo));
    }

    [Fact]
    public async Task FalhaParcial_BalaoDoCodigoNaoSai_GravaOPrimeiroEAFalhaFixaSemOCodigoENaoRepete()
    {
        var c = new Cenario();
        c.Canal.EnviarTextoAsync(Arg.Any<string>(), Codigo, Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("Connection refused " + Codigo));

        (await c.EnviarAsync()).Should().BeFalse();

        c.Gravadas.Should().HaveCount(2);
        c.Gravadas[0].Status.Should().Be(StatusMensagem.Enviada);
        var falha = c.Gravadas[1];
        falha.Status.Should().Be(StatusMensagem.Falhou);
        falha.Texto.Should().Be(CobrancaPixConversa.TextoFalhaCodigo).And.NotContain(Codigo);
        falha.Erro.Should().NotContain(Codigo);
        falha.UltimaFalhaEnvio.Should().Be(TipoFalhaEnvio.Permanente);
        falha.ProximoReenvioEm.Should().BeNull();
        await c.Canal.Received(1).EnviarTextoAsync(Arg.Any<string>(), Codigo, Arg.Any<CancellationToken>());
        c.Log.Linhas.Should().NotContain(l => l.Contains(Codigo));
    }

    [Fact]
    public async Task PrimeiroBalaoNaoSaiNemEmTexto_SoFicaAFalha_SemEnviarOCodigo()
    {
        var c = new Cenario();
        c.Canal.EnviarDocumentoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("x"));
        c.Canal.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("x " + Codigo));

        (await c.EnviarAsync()).Should().BeFalse();

        var falha = c.Gravadas.Should().ContainSingle().Subject;
        falha.Texto.Should().Be(CobrancaPixConversa.TextoFalhaResumo).And.NotContain(Codigo);
        falha.UltimaFalhaEnvio.Should().Be(TipoFalhaEnvio.Permanente);
        c.Log.Linhas.Should().NotContain(l => l.Contains(Codigo));
    }

    [Fact]
    public async Task EnviarPdfDesligado_VoltaAoTextoUnicoDeAntes()
    {
        var c = new Cenario(enviarPdf: "false");

        (await c.EnviarAsync()).Should().BeTrue();

        c.Ordem.Should().ContainSingle().Which.Should().StartWith("texto:TEXTO UNICO ANTIGO");
        await c.Canal.DidNotReceive().EnviarDocumentoAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        c.Gravadas.Should().ContainSingle();
    }

    [Fact]
    public async Task SemPublicador_VoltaAoTextoUnico()
    {
        var c = new Cenario(comPublicador: false);

        (await c.EnviarAsync()).Should().BeTrue();

        c.Ordem.Should().ContainSingle().Which.Should().StartWith("texto:TEXTO UNICO ANTIGO");
    }

    [Fact]
    public async Task ForaDaJanela_GravaFalhaFixaSemOCodigoENadaSai()
    {
        var c = new Cenario(ultimaEntrada: Agora.AddHours(-30));

        (await c.EnviarAsync()).Should().BeFalse();

        c.Ordem.Should().BeEmpty();
        var falha = c.Gravadas.Should().ContainSingle().Subject;
        falha.Texto.Should().Be(CobrancaPixConversa.TextoFalhaResumo).And.NotContain(Codigo);
        falha.Status.Should().Be(StatusMensagem.Falhou);
    }

    [Fact]
    public async Task ConversaInexistente_NaoEnviaNada()
    {
        var c = new Cenario();
        var aviso = c.Aviso() with { ConversaId = Guid.NewGuid() };

        (await c.Servico.EnviarAsync(aviso, Agora, null)).Should().BeFalse();

        c.Ordem.Should().BeEmpty();
        c.Gravadas.Should().BeEmpty();
    }

    [Fact]
    public async Task MensagemDisparadaNoConsole_FicaDaDona()
    {
        var c = new Cenario();

        await c.EnviarAsync(Guid.NewGuid());

        c.Gravadas.Should().OnlyContain(m => m.Autor == AutorMensagem.Dona);
    }
}
