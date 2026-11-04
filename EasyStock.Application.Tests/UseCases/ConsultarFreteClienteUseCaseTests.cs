using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Atendimento.Frete;
using EasyStock.Application.UseCases.Storefront.Frete;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.Extensions.Logging;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases;

public sealed class ConsultarFreteClienteUseCaseTests
{
    private readonly Guid empresa = Guid.NewGuid();
    private readonly IClienteRepository clientes = Substitute.For<IClienteRepository>();
    private readonly IStorefrontRepository vitrines = Substitute.For<IStorefrontRepository>();
    private readonly IFreteZonaRepository zonas = Substitute.For<IFreteZonaRepository>();
    private readonly ICepLookupClient cep = Substitute.For<ICepLookupClient>();

    private ConsultarFreteClienteUseCase Criar() => new(clientes, vitrines, new CalcularFreteUseCase(
        vitrines, zonas, cep, Substitute.For<IGeocodingClient>(), Substitute.For<IRotaClient>(), Substitute.For<ILogger<CalcularFreteUseCase>>()));

    private (Cliente Cliente, StorefrontEntity Vitrine) Preparar()
    {
        var cliente = Cliente.Criar(empresa, "Cliente de frete");
        cliente.Enderecos.Add(new ClienteEndereco { Id = Guid.NewGuid(), ClienteId = cliente.Id, Cep = "05500-000", Numero = "10", Padrao = true });
        clientes.GetByIdWithDetailsAsync(empresa, cliente.Id).Returns(cliente);
        var vitrine = StorefrontEntity.Criar(empresa, "loja-da-sessao", "Loja", pedidoMinimoEntrega: 0m);
        vitrine.Ativar();
        vitrines.GetByEmpresaAsync(empresa, Arg.Any<CancellationToken>()).Returns(vitrine);
        vitrines.GetBySlugAsync(vitrine.Slug, Arg.Any<CancellationToken>()).Returns(vitrine);
        return (cliente, vitrine);
    }

    [Fact]
    public async Task Consulta_UsaEnderecoSalvoEVitrineDoTenantNaMesmaContaDoCheckout()
    {
        var (cliente, vitrine) = Preparar();
        var zona = FreteZona.CriarPorCep(vitrine.Id, "Centro", "05000000", "05999999", 15m, 30);
        zonas.BuscarZonaPorCepAsync(vitrine.Id, "05500000", string.Empty, Arg.Any<CancellationToken>()).Returns(zona);
        var resultado = await Criar().ExecuteAsync(empresa, cliente.Id);
        resultado!.Valor.Should().Be(1500);
        resultado.ZonaId.Should().Be(zona.Id);
        await clientes.Received(1).GetByIdWithDetailsAsync(empresa, cliente.Id);
        await vitrines.Received(1).GetByEmpresaAsync(empresa, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutroTenant_NaoConsultaVitrineNemProvedor()
    {
        var (cliente, _) = Preparar();
        (await Criar().ExecuteAsync(Guid.NewGuid(), cliente.Id)).Should().BeNull();
        await vitrines.DidNotReceive().GetByEmpresaAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await cep.DidNotReceive().LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepositorioComTenantDivergente_ERecusadoAntesDeCotar()
    {
        var (cliente, _) = Preparar();
        var outra = Guid.NewGuid();
        clientes.GetByIdWithDetailsAsync(outra, cliente.Id).Returns(cliente);
        (await Criar().ExecuteAsync(outra, cliente.Id)).Should().BeNull();
        await vitrines.DidNotReceive().GetByEmpresaAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VitrineDeOutraEmpresa_NaoEUsadaParaCotar()
    {
        var (cliente, _) = Preparar();
        var outra = StorefrontEntity.Criar(Guid.NewGuid(), "outra-loja", "Outra loja", pedidoMinimoEntrega: 0m);
        outra.Ativar();
        vitrines.GetByEmpresaAsync(empresa, Arg.Any<CancellationToken>()).Returns(outra);
        Func<Task> acao = () => Criar().ExecuteAsync(empresa, cliente.Id);
        await acao.Should().ThrowAsync<StorefrontNaoEncontradoException>();
        await vitrines.DidNotReceive().GetBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SemCobertura_PropagaCombinarSemInventarFreteZero()
    {
        var (cliente, _) = Preparar();
        Func<Task> acao = () => Criar().ExecuteAsync(empresa, cliente.Id);
        await acao.Should().ThrowAsync<CepSemCoberturaException>();
    }

    [Fact]
    public async Task EnderecosAmbiguosSemPadrao_NaoEscolhemUmPorAcaso()
    {
        var (cliente, _) = Preparar();
        cliente.Enderecos.First().Padrao = false;
        cliente.Enderecos.Add(new ClienteEndereco { Id = Guid.NewGuid(), ClienteId = cliente.Id, Cep = "05500001" });
        Func<Task> acao = () => Criar().ExecuteAsync(empresa, cliente.Id);
        await acao.Should().ThrowAsync<UseCaseValidationException>();
        await vitrines.DidNotReceive().GetByEmpresaAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
