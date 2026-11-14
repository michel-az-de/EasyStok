using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.Services.Notifications.Anexos;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Configuration;
using NSubstitute.ExceptionExtensions;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.Services.Atendimento.Ferramentas;

/// <summary>
/// #2544 (S73): antes do link, o cliente recebe o resumo do pedido como imagem (D2) e confirma. A legenda leva o código
/// que <c>enviar_link_pagamento</c> procura.
/// </summary>
public class EnviarResumoPedidoFerramentaTests
{
    private static readonly DateTime Agora = new(2026, 11, 14, 15, 0, 0, DateTimeKind.Utc);

    private sealed class Cenario
    {
        private readonly EnviarResumoPedidoFerramenta _ferramenta;
        public Guid EmpresaId { get; } = Guid.NewGuid();
        public Pedido Pedido { get; }
        public Conversa Conversa { get; }
        public IWhatsAppCloudClient Cloud { get; } = Substitute.For<IWhatsAppCloudClient>();
        public IRenderizadorResumoPedido Renderizador { get; } = Substitute.For<IRenderizadorResumoPedido>();
        public IFileStorage Armazenamento { get; } = Substitute.For<IFileStorage>();
        public List<Mensagem> Mensagens { get; } = [];
        public ResumoPedidoDto? Desenhado { get; private set; }
        public ContextoTurnoAgente Contexto { get; private set; } = null!;

        public Cenario()
        {
            var clienteId = Guid.NewGuid();
            Pedido = Pedido.Criar(EmpresaId, origem: "whatsapp");
            Pedido.ClienteId = clienteId;
            Pedido.Status = StatusPedidoMapper.AguardandoPagamento;
            Pedido.EnderecoEntrega = "Rua das Flores, 10";
            Pedido.AgendadoParaEm = new DateTime(2026, 11, 15, 15, 0, 0, DateTimeKind.Utc);
            Pedido.Itens.Add(Item("Lasanha Bolonhesa", 2, 30m, "sem cebola"));
            Pedido.Itens.Add(Item(PedidoItem.PrefixoDoFrete + "Centro", 1, 8m, null));
            Pedido.RecalcularTotal();

            var pedidos = Substitute.For<IPedidoRepository>();
            pedidos.GetByIdWithDetailsAsync(EmpresaId, Pedido.Id).Returns(Pedido);
            var storefronts = Substitute.For<IStorefrontRepository>();
            storefronts.GetByEmpresaAsync(EmpresaId, Arg.Any<CancellationToken>())
                .Returns(StorefrontEntity.Criar(EmpresaId, "loja", "Casa da Massa", 0m));
            Renderizador.RenderizarAsync(Arg.Do<ResumoPedidoDto>(r => Desenhado = r), Arg.Any<CancellationToken>())
                .Returns(new AnexoGerado([1, 2, 3], "image/png", "png"));
            Armazenamento.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
                .Returns(new StoredFileResult("k", "https://loja.exemplo/files/resumo.png", "image/png", 3));
            Cloud.EnviarImagemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(new EnvioWhatsAppResult("wamid.resumo"));
            var conversas = Substitute.For<IConversaRepository>();
            conversas.AddMensagemAsync(Arg.Do<Mensagem>(Mensagens.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            var configuracao = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["App:BaseUrl"] = "https://loja.exemplo" }).Build();

            Conversa = Conversa.Abrir(EmpresaId, "5511999998888", Agora, "Maria", clienteId);
            _ferramenta = new EnviarResumoPedidoFerramenta(pedidos, storefronts, conversas, Cloud, configuracao,
                Renderizador, Armazenamento);
        }

        private PedidoItem Item(string nome, decimal qtd, decimal preco, string? obs)
        {
            var item = new PedidoItem { Id = Guid.NewGuid(), PedidoId = Pedido.Id, Nome = nome, Quantidade = qtd, PrecoUnitario = preco, Observacao = obs };
            item.RecalcularSubtotal();
            return item;
        }

        public async Task<JsonElement> ExecutarAsync()
        {
            Contexto = new ContextoTurnoAgente(EmpresaId, Conversa, Agora);
            var resultado = await _ferramenta.ExecutarAsync(Contexto, JsonSerializer.SerializeToElement(new { pedidoId = Pedido.Id }));
            return JsonDocument.Parse(resultado).RootElement.Clone();
        }
    }

    [Fact]
    public async Task ResumoTrazItensFreteETotal_EVaiComALegendaDoPedido()
    {
        var c = new Cenario();

        var resultado = await c.ExecutarAsync();

        resultado.GetProperty("enviado").GetBoolean().Should().BeTrue();
        c.Desenhado!.Itens.Should().ContainSingle().Which.Should().Be(new ResumoPedidoItemDto("Lasanha Bolonhesa", 2, 60m, "sem cebola"));
        c.Desenhado.Frete.Should().Be(8m);
        c.Desenhado.Total.Should().Be(68m);
        c.Desenhado.Endereco.Should().Be("Rua das Flores, 10");
        c.Desenhado.Entrega.Should().Contain("15/11/2026");
        var legenda = ResumoPedidoAtendimento.Legenda(c.Pedido.Id);
        await c.Cloud.Received(1).EnviarImagemAsync("5511999998888", "https://loja.exemplo/files/resumo.png", legenda, Arg.Any<CancellationToken>());
        c.Mensagens.Should().ContainSingle(m => m.Direcao == DirecaoMensagem.Saida && m.TipoConteudo == TipoConteudoMensagem.Imagem
            && m.Texto == legenda && m.ExternoId == "wamid.resumo");
        c.Contexto.HouveEnvio.Should().BeTrue();
    }

    [Fact]
    public async Task PedidoDeOutroCliente_Recusa()
    {
        var c = new Cenario();
        c.Pedido.ClienteId = Guid.NewGuid();

        var resultado = await c.ExecutarAsync();

        resultado.GetProperty("motivo").GetString().Should().Be("pedido_nao_encontrado");
        await c.Cloud.DidNotReceiveWithAnyArgs().EnviarImagemAsync(default!, default!, default, default);
        c.Mensagens.Should().BeEmpty();
    }

    [Fact]
    public async Task FalhaNoEnvio_NaoGravaComoEnviada()
    {
        var c = new Cenario();
        c.Cloud.EnviarImagemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("sessão caída"));

        var resultado = await c.ExecutarAsync();

        resultado.GetProperty("enviado").GetBoolean().Should().BeFalse();
        resultado.GetProperty("motivo").GetString().Should().Be("falha_no_envio");
        c.Mensagens.Should().BeEmpty();
        c.Contexto.HouveEnvio.Should().BeFalse();
    }

    [Fact]
    public async Task PedidoCancelado_NaoManda()
    {
        var c = new Cenario();
        c.Pedido.Status = StatusPedidoMapper.Cancelado;

        var resultado = await c.ExecutarAsync();

        resultado.GetProperty("motivo").GetString().Should().Be("pedido_finalizado");
        await c.Cloud.DidNotReceiveWithAnyArgs().EnviarImagemAsync(default!, default!, default, default);
    }
}
