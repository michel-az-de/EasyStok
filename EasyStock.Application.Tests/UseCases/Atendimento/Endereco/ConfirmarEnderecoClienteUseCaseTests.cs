using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AdicionarClienteEndereco;
using EasyStock.Application.UseCases.Atendimento.Endereco;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Endereco;

public class ConfirmarEnderecoClienteUseCaseTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private ConfirmarEnderecoClienteUseCase UseCase() => new(_clientes, _uow,
        new AdicionarClienteEnderecoUseCase(_clientes, _uow, NullLogger<AdicionarClienteEnderecoUseCase>.Instance));

    private static EnderecoNormalizado Endereco() =>
        new("05500000", "Rua Alvarenga", "120", "apto 12", "Butantã", "São Paulo", "SP", "portão azul");

    private Cliente ClienteCom(params ClienteEndereco[] enderecos)
    {
        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = "Maria" };
        foreach (var e in enderecos) cliente.Enderecos.Add(e);
        _clientes.GetByIdWithDetailsAsync(_empresaId, cliente.Id).Returns(cliente);
        _clientes.GetByIdAsync(_empresaId, cliente.Id).Returns(cliente);
        return cliente;
    }

    [Fact]
    public async Task GravaPadraoEPrimarios()
    {
        var antigo = new ClienteEndereco { Id = Guid.NewGuid(), Cep = "01000000", Logradouro = "Rua X", Numero = "1", Padrao = true };
        var cliente = ClienteCom(antigo);

        var id = await UseCase().ExecuteAsync(new ConfirmarEnderecoClienteCommand(_empresaId, cliente.Id, Endereco()));

        id.Should().NotBeNull();
        await _clientes.Received(1).AddEnderecoAsync(Arg.Is<ClienteEndereco>(e =>
            e.Padrao && e.Cep == "05500000" && e.Logradouro == "Rua Alvarenga" && e.Numero == "120" && e.Referencia == "portão azul"));
        antigo.Padrao.Should().BeFalse();
        cliente.Endereco.Should().Be("Rua Alvarenga, 120");
        cliente.Cep.Should().Be("05500000");
        cliente.Bairro.Should().Be("Butantã");
        cliente.Cidade.Should().Be("São Paulo");
        cliente.Complemento.Should().Be("apto 12");
        await _clientes.Received(1).UpdateAsync(cliente);
        await _uow.Received().CommitAsync();
    }

    [Fact]
    public async Task EnderecoJaSalvoViraPadraoSemDuplicar()
    {
        var salvo = new ClienteEndereco { Id = Guid.NewGuid(), Cep = "05500000", Logradouro = "Rua Alvarenga", Numero = "120" };
        var cliente = ClienteCom(salvo);

        var id = await UseCase().ExecuteAsync(new ConfirmarEnderecoClienteCommand(_empresaId, cliente.Id, Endereco()));

        id.Should().Be(salvo.Id);
        salvo.Padrao.Should().BeTrue();
        await _clientes.DidNotReceive().AddEnderecoAsync(Arg.Any<ClienteEndereco>());
    }

    [Fact]
    public async Task ClienteInexistenteDevolveNulo()
    {
        var id = await UseCase().ExecuteAsync(new ConfirmarEnderecoClienteCommand(_empresaId, Guid.NewGuid(), Endereco()));
        id.Should().BeNull();
    }
}
