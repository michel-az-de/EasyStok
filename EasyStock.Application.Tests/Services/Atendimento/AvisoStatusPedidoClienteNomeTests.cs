using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;
using PedidoEntity = EasyStock.Domain.Entities.Pedido;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #2004 atendimento-12: o primeiro nome que o cliente lê no aviso de pedido segue a mesma regra do lead das demais
/// mensagens: nome vazio ou o "Cliente WhatsApp" do lead sem perfil não vira "Olá, Cliente".
/// </summary>
public class AvisoStatusPedidoClienteNomeTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IVagaOcupadaRepository _vagas = Substitute.For<IVagaOcupadaRepository>();

    private async Task<JsonElement> PayloadDoAvisoAsync(string? nomeDoCliente, string? nomeNoPedido = null)
    {
        var cliente = ClienteEntity.Criar(_empresaId, nomeDoCliente ?? "x");
        cliente.Nome = nomeDoCliente!;
        cliente.Telefone = "(11) 99999-0001";
        var pedido = PedidoEntity.Criar(_empresaId, cliente);
        pedido.ClienteNome = nomeNoPedido ?? nomeDoCliente;
        _pedidos.GetByIdAsync(_empresaId, pedido.Id).Returns(pedido);
        _clientes.GetByIdAsync(_empresaId, cliente.Id).Returns(cliente);
        _vagas.GetByPedidoIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, (EasyStock.Domain.Entities.Storefront.VagaOcupada, EasyStock.Domain.Entities.Storefront.JanelaEntrega)>());
        string? payload = null;
        await _notificador.EnfileirarEventoAsync(Arg.Any<TipoEventoNotificacao>(), _empresaId, Arg.Do<string>(p => payload = p),
            Arg.Any<Guid?>(), Arg.Any<CancellationToken>());

        var aviso = new AvisoStatusPedidoCliente(_pedidos, _clientes, _vagas, Substitute.For<IStorefrontRepository>(),
            new PoliticaEnvioCliente(Substitute.For<IConsentimentoContatoRepository>()), _notificador,
            Substitute.For<ITenantContextAccessor>(), NullLogger<AvisoStatusPedidoCliente>.Instance);

        (await aviso.EnfileirarAsync(TipoEventoNotificacao.PedidoSaiuParaEntrega, _empresaId, pedido.Id, "saiu", default)).Should().BeTrue();
        return JsonDocument.Parse(payload!).RootElement;
    }

    [Fact]
    public async Task LeadSemNomeDePerfilRecebeOPadraoCliente()
    {
        var payload = await PayloadDoAvisoAsync(IdentificarClientePorTelefoneUseCase.NomePadraoLead);

        payload.GetProperty("nome").GetString().Should().Be("cliente");
    }

    [Fact]
    public async Task LeadComNomeNoPedidoUsaOPrimeiroNomeDoPedido()
    {
        var payload = await PayloadDoAvisoAsync(IdentificarClientePorTelefoneUseCase.NomePadraoLead, nomeNoPedido: "Maria Souza");

        payload.GetProperty("nome").GetString().Should().Be("Maria");
    }

    [Fact]
    public async Task ClienteComNomeUsaOPrimeiroNome()
    {
        var payload = await PayloadDoAvisoAsync("Maria Souza");

        payload.GetProperty("nome").GetString().Should().Be("Maria");
    }
}
