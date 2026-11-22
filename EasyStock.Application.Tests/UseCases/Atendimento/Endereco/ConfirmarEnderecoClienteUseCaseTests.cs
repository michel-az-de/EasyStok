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
        await _clientes.Received(2).UpdateAsync(cliente);
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

    // #1991-3: o endereço salvo sem cidade e UF (ViaCEP fora do ar, console que não mandava) fica com o que chegou agora.
    [Fact]
    public async Task EnderecoJaSalvoGanhaCidadeEUfQueFaltavam()
    {
        var salvo = new ClienteEndereco { Id = Guid.NewGuid(), Cep = "05500000", Logradouro = "Rua Alvarenga", Numero = "120" };
        var cliente = ClienteCom(salvo);

        await UseCase().ExecuteAsync(new ConfirmarEnderecoClienteCommand(_empresaId, cliente.Id, Endereco()));

        salvo.Cidade.Should().Be("São Paulo");
        salvo.Estado.Should().Be("SP");
        salvo.Bairro.Should().Be("Butantã");
    }

    [Fact]
    public async Task ClienteInexistenteDevolveNulo()
    {
        var id = await UseCase().ExecuteAsync(new ConfirmarEnderecoClienteCommand(_empresaId, Guid.NewGuid(), Endereco()));
        id.Should().BeNull();
    }

    [Fact]
    public async Task ComplementoConfirmadoApareceNoCadastro()
    {
        // #2776: Cadastros mostra Apt como "Complemento"; a conversa gravava só Cliente.Complemento.
        var cliente = ClienteCom();
        cliente.Apt = "casa 2";

        await UseCase().ExecuteAsync(new ConfirmarEnderecoClienteCommand(_empresaId, cliente.Id, Endereco()));

        cliente.Apt.Should().Be("apto 12");
    }

    [Fact]
    public async Task ComplementoQueNaoCabeNoAptMantemODaLoja()
    {
        var cliente = ClienteCom();
        cliente.Apt = "casa 2";
        var longo = new string('x', 33);
        var endereco = new EnderecoNormalizado("05500000", "Rua Alvarenga", "120", longo, "Butantã", "São Paulo", "SP", null);

        await UseCase().ExecuteAsync(new ConfirmarEnderecoClienteCommand(_empresaId, cliente.Id, endereco));

        cliente.Apt.Should().Be("casa 2");
        cliente.Complemento.Should().Be(longo);
    }

    [Fact]
    public async Task ConfirmarSemComplementoMantemOAptDaLoja()
    {
        var cliente = ClienteCom();
        cliente.Apt = "casa 2";
        var endereco = new EnderecoNormalizado("05500000", "Rua Alvarenga", "120", null, "Butantã", "São Paulo", "SP", null);

        await UseCase().ExecuteAsync(new ConfirmarEnderecoClienteCommand(_empresaId, cliente.Id, endereco));

        cliente.Apt.Should().Be("casa 2");
    }

    [Fact]
    public async Task AdicionarPadraoDesmarcaOAnterior()
    {
        // #2776: o Padrão novo vindo de Cadastros deixava dois Padrões, e a Ficha seguia no antigo.
        var antigo = new ClienteEndereco { Id = Guid.NewGuid(), Cep = "01000000", Logradouro = "Rua X", Numero = "1", Padrao = true };
        var cliente = ClienteCom(antigo);
        var adicionar = new AdicionarClienteEnderecoUseCase(_clientes, _uow, NullLogger<AdicionarClienteEnderecoUseCase>.Instance);

        await adicionar.ExecuteAsync(new AdicionarClienteEnderecoCommand(_empresaId, cliente.Id, Logradouro: "Rua Y", Numero: "2", Padrao: true));

        antigo.Padrao.Should().BeFalse();
        await _clientes.Received(1).AddEnderecoAsync(Arg.Is<ClienteEndereco>(e => e.Padrao && e.Logradouro == "Rua Y"));
        await _clientes.Received().GetByIdWithDetailsAsync(_empresaId, cliente.Id);
    }

    [Fact]
    public async Task AdicionarEnderecoIgualReaproveitaSemRepetir()
    {
        // #3186: mudar só o complemento em Cadastros criava outro endereço com o mesmo CEP, rua e número.
        var antigo = new ClienteEndereco { Id = Guid.NewGuid(), Cep = "01001000", Logradouro = "Rua X", Numero = "1", Complemento = "apto 1", Padrao = true };
        var cliente = ClienteCom(antigo);
        var adicionar = new AdicionarClienteEnderecoUseCase(_clientes, _uow, NullLogger<AdicionarClienteEnderecoUseCase>.Instance);

        var id = await adicionar.ExecuteAsync(new AdicionarClienteEnderecoCommand(_empresaId, cliente.Id,
            Logradouro: " rua x ", Numero: "1", Complemento: "apto 3", Cep: "01001-000", Padrao: true));

        id.Should().Be(antigo.Id);
        antigo.Complemento.Should().Be("apto 3");
        antigo.Padrao.Should().BeTrue();
        await _clientes.DidNotReceive().AddEnderecoAsync(Arg.Any<ClienteEndereco>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task ConfirmarComCepSemMascaraAchaOSalvoEPreservaOComplemento()
    {
        // #3186: o salvo com máscara e a conversa sem ela são o mesmo endereço; complemento nulo não apaga o salvo.
        var salvo = new ClienteEndereco { Id = Guid.NewGuid(), Cep = "05500-000", Logradouro = "Rua Alvarenga", Numero = "120", Complemento = "apto 12" };
        var cliente = ClienteCom(salvo);
        var endereco = new EnderecoNormalizado("05500000", "Rua Alvarenga", "120", null, "Butantã", "São Paulo", "SP", null);

        var id = await UseCase().ExecuteAsync(new ConfirmarEnderecoClienteCommand(_empresaId, cliente.Id, endereco));

        id.Should().Be(salvo.Id);
        salvo.Complemento.Should().Be("apto 12");
        await _clientes.DidNotReceive().AddEnderecoAsync(Arg.Any<ClienteEndereco>());
    }

    [Fact]
    public async Task AdicionarSemLogradouroNaoFundeComOutroVazio()
    {
        var vazio = new ClienteEndereco { Id = Guid.NewGuid(), Referencia = "portaria" };
        var cliente = ClienteCom(vazio);
        var adicionar = new AdicionarClienteEnderecoUseCase(_clientes, _uow, NullLogger<AdicionarClienteEnderecoUseCase>.Instance);

        var id = await adicionar.ExecuteAsync(new AdicionarClienteEnderecoCommand(_empresaId, cliente.Id, Referencia: "fundos"));

        id.Should().NotBe(vazio.Id);
        await _clientes.Received(1).AddEnderecoAsync(Arg.Any<ClienteEndereco>());
    }

    [Fact]
    public async Task AdicionarSemPadraoMantemOAtual()
    {
        var antigo = new ClienteEndereco { Id = Guid.NewGuid(), Cep = "01000000", Logradouro = "Rua X", Numero = "1", Padrao = true };
        var cliente = ClienteCom(antigo);
        var adicionar = new AdicionarClienteEnderecoUseCase(_clientes, _uow, NullLogger<AdicionarClienteEnderecoUseCase>.Instance);

        await adicionar.ExecuteAsync(new AdicionarClienteEnderecoCommand(_empresaId, cliente.Id, Logradouro: "Rua Y", Numero: "2"));

        antigo.Padrao.Should().BeTrue();
    }
}
