using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ClienteCrm;

namespace EasyStock.Application.Tests.UseCases.ClienteCrm;

public sealed class NotasClientePaginaTests
{
    private readonly Guid empresa = Guid.NewGuid();
    private readonly IClienteRepository clientes = Substitute.For<IClienteRepository>();
    private readonly IClienteCrmRepository crm = Substitute.For<IClienteCrmRepository>();
    private readonly Cliente cliente;
    private ListarNotasClienteUseCase Caso() => new(clientes, crm);

    public NotasClientePaginaTests()
    {
        cliente = Cliente.Criar(empresa, "Nome do cadastro");
        clientes.GetByIdAsync(empresa, cliente.Id).Returns(cliente);
    }

    [Fact]
    public async Task PaginaBuscaUmaNotaExtraEPodeUltrapassarAsCemNotasAntigas()
    {
        var notas = Enumerable.Range(0, 51).Select(i => ClienteNota.Criar(empresa, cliente.Id, $"Nota {i}", "Operador", DateTime.UtcNow)).ToArray();
        crm.ListarPaginaNotasAsync(empresa, cliente.Id, 100, 51, Arg.Any<CancellationToken>()).Returns(notas);
        var pagina = await Caso().PaginaAsync(empresa, cliente.Id, 3, 50);
        pagina.Itens.Should().HaveCount(50);
        pagina.TemMais.Should().BeTrue();
        pagina.ProximaPagina.Should().Be(4);
    }

    [Fact]
    public async Task UltimaPaginaNaoInventaPaginaSeguinte()
    {
        crm.ListarPaginaNotasAsync(empresa, cliente.Id, 150, 51, Arg.Any<CancellationToken>())
            .Returns(new[] { ClienteNota.Criar(empresa, cliente.Id, "Última", "Operador", DateTime.UtcNow) });
        var pagina = await Caso().PaginaAsync(empresa, cliente.Id, 4, 50);
        pagina.Itens.Should().ContainSingle();
        pagina.TemMais.Should().BeFalse();
        pagina.ProximaPagina.Should().BeNull();
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(int.MaxValue - 1, 50)]
    public async Task ParametrosInvalidosNaoConsultamNotas(int pagina, int tamanho)
    {
        Func<Task> agir = () => Caso().PaginaAsync(empresa, cliente.Id, pagina, tamanho);
        await agir.Should().ThrowAsync<UseCaseValidationException>();
        await crm.DidNotReceive().ListarPaginaNotasAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestaurarNotaUsaNomeAtualDoCadastroETenantDaConsulta()
    {
        var nota = ClienteNota.Criar(empresa, cliente.Id, "Nota confirmada", "Operador", DateTime.UtcNow);
        crm.ObterNotaAsync(empresa, cliente.Id, nota.Id, Arg.Any<CancellationToken>()).Returns(nota);
        var resultado = await Caso().ObterAsync(empresa, cliente.Id, nota.Id);
        resultado.ClienteId.Should().Be(cliente.Id);
        resultado.ClienteNome.Should().Be(cliente.Nome);
        resultado.Nota.Id.Should().Be(nota.Id);
    }

    [Fact]
    public async Task ClienteDeOutroTenantNaoConsultaNotaNemPagina()
    {
        var outro = Guid.NewGuid();
        Func<Task> nota = () => Caso().ObterAsync(outro, cliente.Id, Guid.NewGuid());
        Func<Task> pagina = () => Caso().PaginaAsync(outro, cliente.Id, 1, 50);
        await nota.Should().ThrowAsync<ClienteCrmNaoEncontradoException>();
        await pagina.Should().ThrowAsync<ClienteCrmNaoEncontradoException>();
        await crm.DidNotReceive().ObterNotaAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await crm.DidNotReceive().ListarPaginaNotasAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepositorioInconsistenteNaoVazaNotaDeOutroCliente()
    {
        var estrangeira = ClienteNota.Criar(empresa, Guid.NewGuid(), "Não pode sair", "Operador", DateTime.UtcNow);
        crm.ObterNotaAsync(empresa, cliente.Id, estrangeira.Id, Arg.Any<CancellationToken>()).Returns(estrangeira);
        crm.ListarPaginaNotasAsync(empresa, cliente.Id, 0, 51, Arg.Any<CancellationToken>()).Returns(new[] { estrangeira });
        Func<Task> nota = () => Caso().ObterAsync(empresa, cliente.Id, estrangeira.Id);
        Func<Task> pagina = () => Caso().PaginaAsync(empresa, cliente.Id, 1, 50);
        await nota.Should().ThrowAsync<ClienteCrmNaoEncontradoException>();
        await pagina.Should().ThrowAsync<ClienteCrmNaoEncontradoException>();
    }
}
