using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Campanhas.Interesse;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Entities.Storefront;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>registrar_interesse</c> (S31): grava o item do cardápio quando o agente o identifica (pelo id ou
/// pelo nome exato) e só a descrição quando não. O commit é do turno, não da ferramenta.
/// </summary>
public class RegistrarInteresseFerramentaTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    private readonly StorefrontEntity _storefront;
    private readonly CardapioItem _boloEsgotado;
    private readonly Cliente _cliente;
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IStorefrontRepository _storefronts = Substitute.For<IStorefrontRepository>();
    private readonly ICardapioItemRepository _cardapio = Substitute.For<ICardapioItemRepository>();
    private readonly IInteresseItemRepository _interesses = Substitute.For<IInteresseItemRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly List<InteresseItem> _gravados = new();

    public RegistrarInteresseFerramentaTests()
    {
        var empresaId = Guid.NewGuid();
        _storefront = StorefrontEntity.Criar(empresaId, "casa-da-baba", "Casa da Baba", 0m);
        _boloEsgotado = CardapioItem.CriarAvulso(_storefront.Id, "Bolo de Cenoura", 35m);
        _boloEsgotado.MarcarEsgotado();
        _cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Maria" };

        _clientes.GetByIdAsync(empresaId, _cliente.Id).Returns(_cliente);
        _storefronts.GetByEmpresaAsync(empresaId, Arg.Any<CancellationToken>()).Returns(_storefront);
        _cardapio.GetByIdAndScopeAsync(_storefront.Id, _boloEsgotado.Id, empresaId, Arg.Any<CancellationToken>()).Returns(_boloEsgotado);
        _cardapio.GetTodosDoStorefrontAsync(_storefront.Id, Arg.Any<CancellationToken>()).Returns([_boloEsgotado]);
        _interesses.When(r => r.AddAsync(Arg.Any<InteresseItem>(), Arg.Any<CancellationToken>()))
            .Do(ci => _gravados.Add(ci.Arg<InteresseItem>()));
    }

    private Task<string> ExecutarAsync(object entrada, Guid? clienteId = null, bool semCliente = false)
    {
        var empresaId = _storefront.EmpresaId;
        var conversa = Conversa.Abrir(empresaId, "5511999998888", Agora, "Maria", semCliente ? null : clienteId ?? _cliente.Id);
        var useCase = new RegistrarInteresseItemUseCase(_clientes, _storefronts, _cardapio, _interesses, _uow, TimeProvider.System);
        return new RegistrarInteresseFerramenta(useCase).ExecutarAsync(
            new ContextoTurnoAgente(empresaId, conversa, Agora), JsonSerializer.SerializeToElement(entrada));
    }

    [Fact]
    public async Task ComItemIdentificado_GravaCardapioItemId()
    {
        var resultado = await ExecutarAsync(new { cardapio_item_id = _boloEsgotado.Id.ToString() });

        var interesse = _gravados.Should().ContainSingle().Subject;
        interesse.CardapioItemId.Should().Be(_boloEsgotado.Id);
        interesse.ClienteId.Should().Be(_cliente.Id);
        interesse.Origem.Should().Be(OrigemInteresse.Agente);
        interesse.RegistradoEm.Should().Be(Agora);
        resultado.Should().Contain("\"registrado\":true");
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task PeloNomeExatoDoItem_GravaCardapioItemId()
    {
        await ExecutarAsync(new { descricao = "bolo de cenoura" });

        _gravados.Should().ContainSingle().Which.CardapioItemId.Should().Be(_boloEsgotado.Id);
    }

    [Fact]
    public async Task SemItemIdentificado_GravaSoDescricao()
    {
        await ExecutarAsync(new { descricao = "Torta de limão" });

        var interesse = _gravados.Should().ContainSingle().Subject;
        interesse.CardapioItemId.Should().BeNull();
        interesse.Descricao.Should().Be("Torta de limão");
    }

    [Fact]
    public async Task IdDeOutroItem_ComDescricao_GravaSoDescricao()
    {
        await ExecutarAsync(new { cardapio_item_id = Guid.NewGuid().ToString(), descricao = "Pudim" });

        var interesse = _gravados.Should().ContainSingle().Subject;
        interesse.CardapioItemId.Should().BeNull();
        interesse.Descricao.Should().Be("Pudim");
    }

    [Fact]
    public async Task ConversaSemCliente_NaoGrava()
    {
        var resultado = await ExecutarAsync(new { descricao = "Pudim" }, semCliente: true);

        resultado.Should().Contain("cliente_nao_identificado");
        _gravados.Should().BeEmpty();
    }

    [Fact]
    public async Task SemItemESemDescricao_DevolveErro()
    {
        var resultado = await ExecutarAsync(new { });

        resultado.Should().Contain("erro");
        _gravados.Should().BeEmpty();
    }
}
