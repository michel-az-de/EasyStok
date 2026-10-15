using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento.Ferramentas;

public sealed class ConsultarEntregaFerramentaTests
{
    [Fact]
    public async Task Conversa_sem_pedido_nao_consulta_repositorios()
    {
        var fixture = new Cenario();
        var conversa = Conversa.Abrir(fixture.Empresa, "5511999998888", DateTime.UtcNow, "Cliente", fixture.Cliente.Id);
        var resposta = await fixture.Ferramenta.ExecutarAsync(new(fixture.Empresa, conversa, DateTime.UtcNow), JsonSerializer.SerializeToElement(new { }));
        resposta.Should().Contain("conversa_sem_pedido");
        fixture.Pedidos.ReceivedCalls().Should().BeEmpty();
        fixture.Logistica.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Cliente_divergente_nao_expoe_entrega()
    {
        var fixture = new Cenario();
        fixture.Conversa.VincularCliente(Guid.NewGuid());
        var contexto = fixture.Contexto();
        var resposta = await fixture.Ferramenta.ExecutarAsync(contexto, JsonSerializer.SerializeToElement(new { }));
        resposta.Should().Contain("pedido_nao_encontrado");
        contexto.RespostaVerificada.Should().BeNull();
        fixture.Logistica.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Pedido_de_outra_empresa_retornado_por_erro_nao_vaza()
    {
        var fixture = new Cenario();
        fixture.Pedidos.GetByIdWithDetailsAsync(fixture.Empresa, fixture.Pedido.Id).Returns(Pedido.Criar(Guid.NewGuid()));
        var resposta = await fixture.Ferramenta.ExecutarAsync(fixture.Contexto(), JsonSerializer.SerializeToElement(new { }));
        resposta.Should().Contain("pedido_nao_encontrado");
        fixture.Logistica.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Ignora_id_injetado_e_retorna_somente_status_verificado_da_conversa()
    {
        var fixture = new Cenario();
        var contexto = fixture.Contexto();
        var idInjetado = Guid.NewGuid();
        using var json = JsonDocument.Parse(await fixture.Ferramenta.ExecutarAsync(contexto,
            JsonSerializer.SerializeToElement(new { pedido_id = idInjetado, empresa_id = Guid.NewGuid() })));
        json.RootElement.GetProperty("status").GetString().Should().Be("agendado");
        json.RootElement.GetProperty("simulado").GetBoolean().Should().BeTrue();
        json.RootElement.TryGetProperty("rastreioUrl", out _).Should().BeFalse();
        contexto.RespostaVerificada.Should().Contain("Demonstração").And.Contain("eventos simulados");
        contexto.RespostaVerificada.Should().Contain("aguardando coleta").And.NotContain("a caminho");
        await fixture.Pedidos.Received(1).GetByIdWithDetailsAsync(fixture.Empresa, fixture.Pedido.Id);
        await fixture.Pedidos.DidNotReceive().GetByIdWithDetailsAsync(Arg.Any<Guid>(), idInjetado);
        fixture.Logistica.ReceivedCalls().Should().OnlyContain(c => new[] { "PedidosAsync", "DespachosAsync", "PoliticaAsync" }.Contains(c.GetMethodInfo().Name));
    }

    [Fact]
    public async Task Sem_despacho_proprio_nao_usa_entrega_de_outro_cliente_no_mesmo_dia()
    {
        var fixture = new Cenario();
        fixture.Despachos.RemoveAll(d => d.PedidoIdsJson.Contains(fixture.Pedido.Id.ToString()));
        var contexto = fixture.Contexto();
        var resposta = await fixture.Ferramenta.ExecutarAsync(contexto, JsonSerializer.SerializeToElement(new { }));
        resposta.Should().Contain("sem_transporte_registrado").And.NotContain("outro-cliente").And.NotContain("a_caminho");
        contexto.RespostaVerificada.Should().BeNull();
    }

    [Fact]
    public async Task Empresa_fora_da_demo_recebe_indisponivel_sem_promessa()
    {
        var fixture = new Cenario(permitirDemo: false);
        var contexto = fixture.Contexto();
        var resposta = await fixture.Ferramenta.ExecutarAsync(contexto, JsonSerializer.SerializeToElement(new { }));
        resposta.Should().Contain("acompanhamento_indisponivel");
        contexto.RespostaVerificada.Should().BeNull();
        fixture.Logistica.ReceivedCalls().Should().BeEmpty();
    }

    private sealed class Cenario
    {
        public Guid Empresa { get; } = Guid.NewGuid();
        public Cliente Cliente { get; }
        public Pedido Pedido { get; }
        public Conversa Conversa { get; }
        public IPedidoRepository Pedidos { get; } = Substitute.For<IPedidoRepository>();
        public ILogisticaRepository Logistica { get; } = Substitute.For<ILogisticaRepository>();
        public ConsultarEntregaFerramenta Ferramenta { get; }
        public List<DespachoLogistica> Despachos { get; } = [];

        public Cenario(bool permitirDemo = true)
        {
            Cliente = Cliente.Criar(Empresa, "Maria");
            Pedido = Pedido.Criar(Empresa, Cliente);
            Conversa = Conversa.Abrir(Empresa, "5511999998888", DateTime.UtcNow, "Maria", Cliente.Id);
            Conversa.DefinirPedidoEmAndamento(Pedido.Id);
            Pedidos.GetByIdWithDetailsAsync(Empresa, Pedido.Id).Returns(Pedido);
            var inicio = new DateTime(2026, 10, 13, 13, 0, 0, DateTimeKind.Utc);
            var pedidoAtual = new LogisticaPedido(Pedido.Id, "demo", "Maria", "Endereço privado", "Bairro", inicio, inicio.AddHours(1), "pronto", 800, "5511999998888", -23.5, -46.6, false);
            var outroPedido = pedidoAtual with { Id = Guid.NewGuid(), Cliente = "Outro cliente" };
            Logistica.PedidosAsync(Empresa, null, Pedido.Id, Arg.Any<CancellationToken>()).Returns([pedidoAtual]);
            Logistica.PedidosAsync(Empresa, new DateOnly(2026, 10, 13), null, Arg.Any<CancellationToken>()).Returns([pedidoAtual, outroPedido]);
            Despachos.Add(new DespachoLogistica { EmpresaId = Empresa, ViagemId = Guid.NewGuid(), Simulado = true, Provedor = "lalamove",
                CriadoEm = inicio, PedidoIdsJson = JsonSerializer.Serialize(new[] { Pedido.Id }) });
            var outroDespacho = new DespachoLogistica { EmpresaId = Empresa, ViagemId = Guid.NewGuid(), Simulado = true, Provedor = "lalamove",
                CriadoEm = inicio.AddMinutes(30), PedidoIdsJson = JsonSerializer.Serialize(new[] { outroPedido.Id }), RastreioUrl = "https://rastreio.invalid/outro-cliente" };
            outroDespacho.RegistrarEvento("a_caminho", null);
            Despachos.Add(outroDespacho);
            Logistica.DespachosAsync(Empresa, Arg.Any<CancellationToken>()).Returns(_ => Despachos);
            var useCase = new LogisticaUseCases(Logistica, Substitute.For<IUnitOfWork>(), Substitute.For<ICaixaRepository>(), [],
                new(permitirDemo ? Empresa : Guid.Empty, new("Demo", "Origem", "5500000000000", -23.5, -46.6)), TimeProvider.System);
            Ferramenta = new ConsultarEntregaFerramenta(Pedidos, useCase);
            Pedidos.ClearReceivedCalls();
            Logistica.ClearReceivedCalls();
        }

        public ContextoTurnoAgente Contexto() => new(Empresa, Conversa, DateTime.UtcNow);
    }
}
