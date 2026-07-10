using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.GerenciarCategoria;

namespace EasyStock.Application.Tests.UseCases.GerenciarCategoria;

/// <summary>BUG-08: unicidade de nome de categoria (case-insensitive) por empresa.</summary>
public class GerenciarCategoriaUseCaseTests
{
    private readonly ICategoriaRepository _repo = Substitute.For<ICategoriaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private static readonly Guid EmpId = Guid.NewGuid();

    private GerenciarCategoriaUseCase Sut() => new(_repo, _uow);

    [Fact]
    public async Task CriarAsync_rejeita_nome_duplicado()
    {
        _repo.ExisteNomeAsync(EmpId, Arg.Any<string>(), Arg.Any<Guid?>()).Returns(true);

        var act = () => Sut().CriarAsync(new CriarCategoriaCommand(EmpId, "Teste", null, null));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*existe uma categoria*");
        await _repo.DidNotReceive().AddAsync(Arg.Any<Categoria>());
    }

    [Fact]
    public async Task CriarAsync_cria_e_faz_trim_quando_nome_unico()
    {
        _repo.ExisteNomeAsync(EmpId, Arg.Any<string>(), Arg.Any<Guid?>()).Returns(false);

        await Sut().CriarAsync(new CriarCategoriaCommand(EmpId, "  Bebidas  ", null, null));

        await _repo.Received(1).AddAsync(Arg.Is<Categoria>(c => c.Nome == "Bebidas"));
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task AtualizarAsync_ignora_a_propria_categoria_na_unicidade()
    {
        var id = Guid.NewGuid();
        _repo.GetByIdAsync(EmpId, id).Returns(new Categoria { Id = id, EmpresaId = EmpId, Nome = "Antigo" });
        _repo.ExisteNomeAsync(EmpId, Arg.Any<string>(), id).Returns(false);

        await Sut().AtualizarAsync(new AtualizarCategoriaCommand(id, EmpId, "Novo", null, null));

        await _repo.Received(1).ExisteNomeAsync(EmpId, "Novo", id);
        await _uow.Received(1).CommitAsync();
    }

    // Issue 884: os limiares eram descartados na criacao — o command nem os carregava,
    // entao a API os perdia na desserializacao e a entidade nascia com null.
    [Fact]
    public async Task CriarAsync_persiste_limiares_de_estoque()
    {
        _repo.ExisteNomeAsync(EmpId, Arg.Any<string>(), Arg.Any<Guid?>()).Returns(false);

        await Sut().CriarAsync(new CriarCategoriaCommand(EmpId, "Bebidas", null, null,
            QuantidadeMinima: 10, QuantidadeCritica: 3));

        await _repo.Received(1).AddAsync(Arg.Is<Categoria>(c =>
            c.QuantidadeMinima == 10 && c.QuantidadeCritica == 3));
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task CriarAsync_sem_limiares_mantem_null_padrao_do_sistema()
    {
        _repo.ExisteNomeAsync(EmpId, Arg.Any<string>(), Arg.Any<Guid?>()).Returns(false);

        await Sut().CriarAsync(new CriarCategoriaCommand(EmpId, "Bebidas", null, null));

        await _repo.Received(1).AddAsync(Arg.Is<Categoria>(c =>
            c.QuantidadeMinima == null && c.QuantidadeCritica == null));
    }

    [Fact]
    public async Task CriarAsync_rejeita_critica_maior_ou_igual_a_minima()
    {
        _repo.ExisteNomeAsync(EmpId, Arg.Any<string>(), Arg.Any<Guid?>()).Returns(false);

        // exatamente os valores do QA (min=3, crit=10): invertidos em relacao a regra.
        var act = () => Sut().CriarAsync(new CriarCategoriaCommand(EmpId, "Bebidas", null, null,
            QuantidadeMinima: 3, QuantidadeCritica: 10));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*critica precisa ser menor*");
        await _repo.DidNotReceive().AddAsync(Arg.Any<Categoria>());
    }

    [Fact]
    public async Task CriarAsync_rejeita_limiar_negativo()
    {
        _repo.ExisteNomeAsync(EmpId, Arg.Any<string>(), Arg.Any<Guid?>()).Returns(false);

        var act = () => Sut().CriarAsync(new CriarCategoriaCommand(EmpId, "Bebidas", null, null,
            QuantidadeMinima: -1));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*nao pode ser negativa*");
        await _repo.DidNotReceive().AddAsync(Arg.Any<Categoria>());
    }
}
