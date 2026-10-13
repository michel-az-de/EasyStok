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
        await fixture.Pedidos.Received(1).GetByIdWithDetailsAsync(fixture.Empresa, fixture.Pedido.Id);
        await fixture.Pedidos.DidNotReceive().GetByIdWithDetailsAsync(Arg.Any<Guid>(), idInjetado);
        fixture.Logistica.ReceivedCalls().Should().OnlyContain(c => new[] { "PedidosAsync", "DespachosAsync", "PoliticaAsync" }.Contains(c.GetMethodInfo().Name));
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

        public Cenario(bool permitirDemo = true)
        {
            Cliente = Cliente.Criar(Empresa, "Maria");
            Pedido = Pedido.Criar(Empresa, Cliente);
            Conversa = Conversa.Abrir(Empresa, "5511999998888", DateTime.UtcNow, "Maria", Cliente.Id);
            Conversa.DefinirPedidoEmAndamento(Pedido.Id);
            Pedidos.GetByIdWithDetailsAsync(Empresa, Pedido.Id).Returns(Pedido);
            Logistica.PedidosAsync(Empresa, null, Pedido.Id, Arg.Any<CancellationToken>()).Returns([
                new LogisticaPedido(Pedido.Id, "demo", "Maria", "Endereço privado", "Bairro", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), "pronto", 800, "5511999998888", -23.5, -46.6, false)]);
            Logistica.DespachosAsync(Empresa, Arg.Any<CancellationToken>()).Returns([
                new DespachoLogistica { EmpresaId = Empresa, ViagemId = Guid.NewGuid(), Simulado = true, Provedor = "lalamove",
                    CriadoEm = DateTime.UtcNow, PedidoIdsJson = JsonSerializer.Serialize(new[] { Pedido.Id }) }]);
            var useCase = new LogisticaUseCases(Logistica, Substitute.For<IUnitOfWork>(), Substitute.For<ICaixaRepository>(), [],
                new(permitirDemo ? Empresa : Guid.Empty, new("Demo", "Origem", "5500000000000", -23.5, -46.6)), TimeProvider.System);
            Ferramenta = new ConsultarEntregaFerramenta(Pedidos, useCase);
            Pedidos.ClearReceivedCalls();
            Logistica.ClearReceivedCalls();
        }

        public ContextoTurnoAgente Contexto() => new(Empresa, Conversa, DateTime.UtcNow);
    }
}
