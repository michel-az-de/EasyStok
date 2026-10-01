using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.AdicionarClienteEndereco;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ClienteDaConversa;
using EasyStock.Application.UseCases.Atendimento.Endereco;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Storefront.Frete;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Atendimento.ClienteDaConversa;

public class CadastrarClienteDaConversaUseCaseTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IClienteStorefrontRepository _clientesStorefront = Substitute.For<IClienteStorefrontRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IStorefrontRepository _storefronts = Substitute.For<IStorefrontRepository>();
    private readonly IFreteZonaRepository _zonas = Substitute.For<IFreteZonaRepository>();
    private readonly ICepLookupClient _cep = Substitute.For<ICepLookupClient>();
    private readonly IConfiguracaoAtendimentoRepository _configuracao = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly StorefrontEntity _storefront;

    public CadastrarClienteDaConversaUseCaseTests()
    {
        _storefront = StorefrontEntity.Criar(empresaId: _empresaId, slug: "casa-da-baba", tituloPublico: "Casa da Babá", pedidoMinimoEntrega: 0m);
        _storefront.Ativar();
        _storefronts.GetByEmpresaAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(_storefront);
        _storefronts.GetBySlugAsync("casa-da-baba", Arg.Any<CancellationToken>()).Returns(_storefront);
        _zonas.BuscarZonaPorCepAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((FreteZona?)null);
        _cep.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((CepLookupResult?)null);
        _configuracao.GetOrDefaultAsync(_empresaId).Returns(ConfiguracaoAtendimento.CriarPadrao(_empresaId));
        // O cliente que o use case cria passa a ser achado pelo id depois do commit, como no banco.
        _clientes.AddAsync(Arg.Do<Cliente>(c =>
        {
            _clientes.GetByIdWithDetailsAsync(_empresaId, c.Id).Returns(c);
            _clientes.GetByIdAsync(_empresaId, c.Id).Returns(c);
        }));
    }

    private CadastrarClienteDaConversaUseCase UseCase()
    {
        var geocoding = Substitute.For<IGeocodingClient>();
        geocoding.GeocodificarAsync(Arg.Any<GeocodeQuery>(), Arg.Any<CancellationToken>()).Returns((GeocodeResultado?)null);
        var frete = new CalcularFreteUseCase(_storefronts, _zonas, _cep, geocoding, NullLogger<CalcularFreteUseCase>.Instance);
        return new CadastrarClienteDaConversaUseCase(
            _conversas, _clientes, _uow,
            new IdentificarClientePorTelefoneUseCase(_clientes, _clientesStorefront, NullLogger<IdentificarClientePorTelefoneUseCase>.Instance),
            new ValidarEnderecoUseCase(_storefronts, frete, _cep, _configuracao, NullLogger<ValidarEnderecoUseCase>.Instance),
            new ConfirmarEnderecoClienteUseCase(_clientes, _uow,
                new AdicionarClienteEnderecoUseCase(_clientes, _uow, NullLogger<AdicionarClienteEnderecoUseCase>.Instance)));
    }

    private Conversa ConversaDoSite(Guid? clienteId = null)
    {
        var conversa = Conversa.Abrir(_empresaId, "visitante-1", DateTime.UtcNow.AddMinutes(-5), "Visitante do site", clienteId, CanalConversa.ChatSite);
        _conversas.ObterPorIdAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        return conversa;
    }

    private void ZonaCobre(string cep) =>
        _zonas.BuscarZonaPorCepAsync(_storefront.Id, cep, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(FreteZona.CriarPorCep(storefrontId: _storefront.Id, label: "Oeste", cepInicio: "05000000",
                cepFim: "05999999", valor: 12.5m, tempoEstimadoMinutos: 40));

    private static EnderecoDaConversaInput Endereco(string cep = "05500-000") =>
        new(cep, "Rua Alvarenga", "120", "apto 12", "Butantã");

    private CadastrarClienteDaConversaCommand Comando(Conversa conversa, string? nome = "Maria Souza",
        string? telefone = "(11) 98765-4321", EnderecoDaConversaInput? endereco = null) =>
        new(_empresaId, conversa.Id, nome, telefone, endereco);

    [Fact]
    public async Task ConversaSemCliente_CriaClientePeloTelefoneEVincula()
    {
        var conversa = ConversaDoSite();

        var r = await UseCase().ExecuteAsync(Comando(conversa));

        await _clientes.Received(1).AddAsync(Arg.Is<Cliente>(c => c.Nome == "Maria Souza" && c.Telefone == "+5511987654321"));
        conversa.ClienteId.Should().Be(r.ClienteId);
        r.Novo.Should().BeTrue();
        r.Nome.Should().Be("Maria Souza");
        r.Telefone.Should().Be("+5511987654321");
        await _uow.Received().CommitAsync();
    }

    [Fact]
    public async Task TelefoneJaCadastrado_ReaproveitaOClienteSemTrocarONome()
    {
        var existente = new Cliente { Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = "Maria Cadastrada", Telefone = "+5511987654321" };
        _clientes.FindByTelefoneAsync(_empresaId, "+5511987654321").Returns(existente);
        _clientes.GetByIdWithDetailsAsync(_empresaId, existente.Id).Returns(existente);
        var conversa = ConversaDoSite();

        var r = await UseCase().ExecuteAsync(Comando(conversa, nome: "Maria do chat"));

        await _clientes.DidNotReceive().AddAsync(Arg.Any<Cliente>());
        conversa.ClienteId.Should().Be(existente.Id);
        r.Novo.Should().BeFalse();
        r.Nome.Should().Be("Maria Cadastrada");
    }

    [Fact]
    public async Task ConversaSemClienteESemTelefone_Recusa()
    {
        var conversa = ConversaDoSite();

        var acao = () => UseCase().ExecuteAsync(Comando(conversa, telefone: null));

        await acao.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*telefone*");
        conversa.ClienteId.Should().BeNull();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task TelefoneInvalido_RecusaComoValidacao()
    {
        var conversa = ConversaDoSite();

        var acao = () => UseCase().ExecuteAsync(Comando(conversa, telefone: "123"));

        await acao.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*elefone*");
    }

    [Fact]
    public async Task ConversaComCliente_AtualizaNomeETelefone()
    {
        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = "Cliente WhatsApp", Telefone = "+5511911112222" };
        _clientes.GetByIdWithDetailsAsync(_empresaId, cliente.Id).Returns(cliente);
        var conversa = ConversaDoSite(cliente.Id);

        var r = await UseCase().ExecuteAsync(Comando(conversa, nome: "Joana Lima", telefone: "11 98765-4321"));

        cliente.Nome.Should().Be("Joana Lima");
        cliente.Telefone.Should().Be("+5511987654321");
        cliente.TelefoneHash.Should().NotBeNullOrEmpty();
        await _clientes.Received().UpdateAsync(cliente);
        await _clientes.DidNotReceive().AddAsync(Arg.Any<Cliente>());
        r.ClienteId.Should().Be(cliente.Id);
    }

    [Fact]
    public async Task ComEnderecoNaArea_GravaEnderecoPadraoEDevolveDentroDaArea()
    {
        ZonaCobre("05500000");
        var conversa = ConversaDoSite();

        var r = await UseCase().ExecuteAsync(Comando(conversa, endereco: Endereco()));

        await _clientes.Received(1).AddEnderecoAsync(Arg.Is<ClienteEndereco>(e =>
            e.Padrao && e.Cep == "05500000" && e.Logradouro == "Rua Alvarenga" && e.Numero == "120" && e.Bairro == "Butantã"));
        r.DentroDaArea.Should().BeTrue();
        r.Cep.Should().Be("05500000");
        r.Endereco.Should().Be("Rua Alvarenga, 120");
    }

    [Fact]
    public async Task ComEnderecoForaDaArea_GravaEDevolveForaComAMensagem()
    {
        var conversa = ConversaDoSite();

        var r = await UseCase().ExecuteAsync(Comando(conversa, endereco: Endereco("01001-000")));

        await _clientes.Received(1).AddEnderecoAsync(Arg.Is<ClienteEndereco>(e => e.Padrao && e.Cep == "01001000"));
        r.DentroDaArea.Should().BeFalse();
    }

    [Fact]
    public async Task CepInvalido_RecusaComoValidacaoSemGravarEndereco()
    {
        var conversa = ConversaDoSite();

        var acao = () => UseCase().ExecuteAsync(Comando(conversa, endereco: Endereco("123")));

        await acao.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*CEP*");
        await _clientes.DidNotReceive().AddEnderecoAsync(Arg.Any<ClienteEndereco>());
    }

    [Fact]
    public async Task NomeDoContatoNaInboxPassaASerODoCliente()
    {
        var existente = new Cliente { Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = "Maria Cadastrada", Telefone = "+5511987654321" };
        _clientes.FindByTelefoneAsync(_empresaId, "+5511987654321").Returns(existente);
        _clientes.GetByIdWithDetailsAsync(_empresaId, existente.Id).Returns(existente);
        var conversa = ConversaDoSite();

        await UseCase().ExecuteAsync(Comando(conversa, nome: "Maria do chat"));

        conversa.ContatoNome.Should().Be("Maria Cadastrada");
    }

    [Fact]
    public async Task ConversaInexistente_LancaNaoEncontrada()
    {
        var acao = () => UseCase().ExecuteAsync(new CadastrarClienteDaConversaCommand(_empresaId, Guid.NewGuid(), "Maria", "11987654321", null));

        await acao.Should().ThrowAsync<ConversaNaoEncontradaException>();
    }
}
