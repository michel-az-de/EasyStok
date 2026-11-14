using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Application.Tests.Services.Atendimento.Ferramentas;

/// <summary>
/// #2544 (S74): o link de pagamento sai por ferramenta própria e só depois de o cliente responder ao resumo do pedido.
/// Serve também para o link vencido.
/// </summary>
public class EnviarLinkPagamentoFerramentaTests
{
    private static readonly DateTime Agora = CobrancaPedidoFixture.Agora;

    private sealed class Cenario
    {
        public CobrancaPedidoFixture Cobranca { get; } = new();
        public Conversa Conversa { get; }
        public List<Mensagem> Mensagens { get; } = [];
        private readonly EnviarLinkPagamentoFerramenta _ferramenta;

        public Cenario()
        {
            var clienteId = Guid.NewGuid();
            Cobranca.Pedido.ClienteId = clienteId;
            Cobranca.Pedido.CriadoEm = Agora.AddMinutes(-10);
            Cobranca.PedidoRepo.GetByIdAsync(Cobranca.EmpresaId, Cobranca.Pedido.Id).Returns(Cobranca.Pedido);
            Conversa = Conversa.Abrir(Cobranca.EmpresaId, "5511999998888", Agora.AddMinutes(-20), "Maria", clienteId);
            Cobranca.ConversaRepo.ListarMensagensDepoisAsync(Cobranca.EmpresaId, Conversa.Id, Arg.Any<DateTime?>(),
                    Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(_ => (IReadOnlyList<Mensagem>)Mensagens.ToList());
            _ferramenta = new EnviarLinkPagamentoFerramenta(Cobranca.PedidoRepo, Cobranca.Gerar(), Cobranca.ConversaRepo);
        }

        public void ResumoEnviado(bool clienteRespondeu)
        {
            Mensagens.Add(Mensagem.Saida(Cobranca.EmpresaId, Conversa.Id, AutorMensagem.Agente, Agora.AddMinutes(-5),
                TipoConteudoMensagem.Imagem, ResumoPedidoAtendimento.Legenda(Cobranca.Pedido.Id), "wamid.resumo"));
            if (clienteRespondeu)
                Mensagens.Add(Mensagem.Entrada(Cobranca.EmpresaId, Conversa.Id, Agora.AddMinutes(-1),
                    TipoConteudoMensagem.Texto, "ok, pode mandar", "wamid.ok"));
        }

        public ContextoTurnoAgente Contexto { get; private set; } = null!;

        public async Task<JsonElement> ExecutarAsync(Guid? pedidoId = null)
        {
            Contexto = new ContextoTurnoAgente(Cobranca.EmpresaId, Conversa, Agora);
            var resultado = await _ferramenta.ExecutarAsync(Contexto,
                JsonSerializer.SerializeToElement(new { pedidoId = pedidoId ?? Cobranca.Pedido.Id }));
            return JsonDocument.Parse(resultado).RootElement.Clone();
        }
    }

    [Fact]
    public async Task SemResumo_PedeOResumoAntesDoLink()
    {
        var c = new Cenario();

        var resultado = await c.ExecutarAsync();

        resultado.GetProperty("motivo").GetString().Should().Be("resumo_nao_enviado");
        resultado.GetProperty("orientacao").GetString().Should().Contain("enviar_resumo_pedido");
        c.Cobranca.Preferencias.Should().BeEmpty();
        c.Contexto.RespostaVerificada.Should().BeNull();
    }

    [Fact]
    public async Task ResumoSemOkDoCliente_PedeConfirmacao()
    {
        var c = new Cenario();
        c.ResumoEnviado(clienteRespondeu: false);

        var resultado = await c.ExecutarAsync();

        resultado.GetProperty("motivo").GetString().Should().Be("aguardando_ok");
        c.Cobranca.Preferencias.Should().BeEmpty();
    }

    [Fact]
    public async Task ResumoComOk_GeraOLinkEMandaComoResposta()
    {
        var c = new Cenario();
        c.ResumoEnviado(clienteRespondeu: true);

        var resultado = await c.ExecutarAsync();

        resultado.GetProperty("linkGerado").GetBoolean().Should().BeTrue();
        c.Cobranca.Cobrancas.Should().ContainSingle().Which.ConversaId.Should().Be(c.Conversa.Id);
        c.Contexto.RespostaVerificada.Should().Contain("https://mp.test/pref-1").And.Contain("R$ 25,00").And.Contain("Pix");
    }

    [Fact]
    public async Task LinkVencido_GeraOutro()
    {
        var c = new Cenario();
        c.ResumoEnviado(clienteRespondeu: true);
        c.Cobranca.AdicionarOnline(conversaId: c.Conversa.Id);
        c.Cobranca.Relogio.Agora = Agora.AddMinutes(31);

        await c.ExecutarAsync();

        c.Contexto.RespostaVerificada.Should().Contain("https://mp.test/pref-1").And.NotContain("pref-antiga");
    }

    [Fact]
    public async Task PedidoPago_NaoGeraLink()
    {
        var c = new Cenario();
        c.ResumoEnviado(clienteRespondeu: true);
        c.Cobranca.AdicionarPagamento("pref-antiga", 25m);

        var resultado = await c.ExecutarAsync();

        resultado.GetProperty("motivo").GetString().Should().Be("pedido_ja_pago");
        c.Cobranca.Preferencias.Should().BeEmpty();
        c.Contexto.RespostaVerificada.Should().BeNull();
    }

    [Fact]
    public async Task MercadoPagoFora_NaoInventaLinkEOrientaTentarDeNovo()
    {
        // #1301, agora no link (#2544): o provedor caído não vira link nem resposta pronta.
        var c = new Cenario();
        c.ResumoEnviado(clienteRespondeu: true);
        c.Cobranca.MpClient.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("500"));

        var resultado = await c.ExecutarAsync();

        resultado.GetProperty("motivo").GetString().Should().Be("pagamento_indisponivel");
        c.Cobranca.Cobrancas.Should().BeEmpty();
        c.Contexto.RespostaVerificada.Should().BeNull();
    }

    [Fact]
    public async Task PedidoCancelado_OrientaMontarDeNovo()
    {
        var c = new Cenario();
        c.ResumoEnviado(clienteRespondeu: true);
        c.Cobranca.Pedido.Status = StatusPedidoMapper.Cancelado;

        var resultado = await c.ExecutarAsync();

        resultado.GetProperty("motivo").GetString().Should().Be("pedido_finalizado");
        c.Cobranca.Preferencias.Should().BeEmpty();
    }

    [Fact]
    public async Task PedidoDeOutroCliente_NaoEncontrado()
    {
        var c = new Cenario();
        c.Cobranca.Pedido.ClienteId = Guid.NewGuid();
        c.ResumoEnviado(clienteRespondeu: true);

        var resultado = await c.ExecutarAsync();

        resultado.GetProperty("motivo").GetString().Should().Be("pedido_nao_encontrado");
        c.Cobranca.Preferencias.Should().BeEmpty();
    }
}
