using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using Microsoft.Extensions.Logging.Abstractions;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

public class IdentificarClientePorTelefoneUseCaseTests
{
    private const string WaId = "5511997573992";
    private const string TelefoneE164 = "+5511997573992";

    private readonly IClienteRepository _clienteRepository = Substitute.For<IClienteRepository>();
    private readonly IClienteStorefrontRepository _clienteStorefrontRepository = Substitute.For<IClienteStorefrontRepository>();
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IdentificarClientePorTelefoneUseCase _useCase;

    public IdentificarClientePorTelefoneUseCaseTests()
    {
        _useCase = new IdentificarClientePorTelefoneUseCase(
            _clienteRepository, _clienteStorefrontRepository,
            NullLogger<IdentificarClientePorTelefoneUseCase>.Instance);
    }

    [Fact]
    public async Task ConhecidoRetornaExistente()
    {
        var existente = ClienteEntity.Criar(_empresaId, "Maria Silva");
        existente.RegistrarPedido(DateTime.UtcNow);
        _clienteStorefrontRepository
            .GetByTelefoneHashAsync(_empresaId, ClienteOtp.CalcularTelefoneHash(TelefoneE164), Arg.Any<CancellationToken>())
            .Returns(existente);

        var resultado = await _useCase.ExecuteAsync(
            new IdentificarClientePorTelefoneInput(_empresaId, WaId, "Maria do Zap"));

        resultado.Cliente.Id.Should().Be(existente.Id);
        resultado.EhNovo.Should().BeFalse();
        resultado.EhLead.Should().BeFalse();
        await _clienteRepository.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task ConhecidoPeloTelefoneDoCadastroRetornaExistente()
    {
        // Cadastro feito pelo ERP (sem TelefoneHash), com o número gravado sem o +55.
        var existente = ClienteEntity.Criar(_empresaId, "João");
        _clienteRepository.FindByTelefoneAsync(_empresaId, "11997573992").Returns(existente);

        var resultado = await _useCase.ExecuteAsync(
            new IdentificarClientePorTelefoneInput(_empresaId, WaId, null));

        resultado.Cliente.Id.Should().Be(existente.Id);
        resultado.EhNovo.Should().BeFalse();
        resultado.EhLead.Should().BeTrue("OrderCount == 0 define lead");
    }

    [Fact]
    public async Task WaIdSemNonoDigitoEncontraCadastroComNonoDigito()
    {
        // A Meta entrega wa_id de celular BR antigo sem o 9: 55 11 97573992.
        var existente = ClienteEntity.Criar(_empresaId, "Ana");
        _clienteRepository.FindByTelefoneAsync(_empresaId, TelefoneE164).Returns(existente);

        var resultado = await _useCase.ExecuteAsync(
            new IdentificarClientePorTelefoneInput(_empresaId, "551197573992", null));

        resultado.Cliente.Id.Should().Be(existente.Id);
    }

    [Fact]
    public async Task DesconhecidoCriaLead()
    {
        ClienteEntity? adicionado = null;
        await _clienteRepository.AddAsync(Arg.Do<ClienteEntity>(c => adicionado = c));

        var resultado = await _useCase.ExecuteAsync(
            new IdentificarClientePorTelefoneInput(_empresaId, WaId, "  Fulano de Tal "));

        resultado.EhNovo.Should().BeTrue();
        resultado.EhLead.Should().BeTrue();
        adicionado.Should().NotBeNull();
        adicionado!.Should().BeSameAs(resultado.Cliente);
        adicionado.EmpresaId.Should().Be(_empresaId);
        adicionado.Nome.Should().Be("Fulano de Tal");
        adicionado.OrderCount.Should().Be(0);
        adicionado.Telefone.Should().Be(TelefoneE164);
        adicionado.TelefoneHash.Should().Be(ClienteOtp.CalcularTelefoneHash(TelefoneE164));
        adicionado.Telefones.Should().ContainSingle(t =>
            t.Numero == TelefoneE164 && t.Whatsapp && t.Principal && t.ClienteId == adicionado.Id);
    }

    [Fact]
    public async Task DesconhecidoSemNomeDePerfilUsaNomePadrao()
    {
        var resultado = await _useCase.ExecuteAsync(
            new IdentificarClientePorTelefoneInput(_empresaId, WaId, "   "));

        resultado.Cliente.Nome.Should().Be(IdentificarClientePorTelefoneUseCase.NomePadraoLead);
    }
}
