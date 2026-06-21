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
}
