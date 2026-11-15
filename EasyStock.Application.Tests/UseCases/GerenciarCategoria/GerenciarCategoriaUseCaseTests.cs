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

    [Fact]
    public async Task AtualizarAsync_rejeita_pai_que_e_descendente_da_propria_categoria()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        _repo.GetByIdAsync(EmpId, a).Returns(new Categoria { Id = a, EmpresaId = EmpId, Nome = "Bebidas" });
        _repo.GetByIdAsync(EmpId, b).Returns(new Categoria { Id = b, EmpresaId = EmpId, Nome = "Sucos", CategoriaPaiId = a });
        _repo.ExisteNomeAsync(EmpId, Arg.Any<string>(), a).Returns(false);

        var act = () => Sut().AtualizarAsync(new AtualizarCategoriaCommand(a, EmpId, "Bebidas", null, b));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*ciclo*");
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<Categoria>());
    }

    [Fact]
    public async Task AtualizarAsync_aceita_pai_que_nao_forma_ciclo()
    {
        var id = Guid.NewGuid();
        var pai = Guid.NewGuid();
        var avo = Guid.NewGuid();
        _repo.GetByIdAsync(EmpId, id).Returns(new Categoria { Id = id, EmpresaId = EmpId, Nome = "Antigo" });
        _repo.GetByIdAsync(EmpId, pai).Returns(new Categoria { Id = pai, EmpresaId = EmpId, Nome = "Bebidas", CategoriaPaiId = avo });
        _repo.GetByIdAsync(EmpId, avo).Returns(new Categoria { Id = avo, EmpresaId = EmpId, Nome = "Raiz" });
        _repo.ExisteNomeAsync(EmpId, Arg.Any<string>(), id).Returns(false);

        await Sut().AtualizarAsync(new AtualizarCategoriaCommand(id, EmpId, "Novo", null, pai));

        await _repo.Received(1).UpdateAsync(Arg.Is<Categoria>(c => c.CategoriaPaiId == pai));
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task AtualizarAsync_rejeita_cadeia_de_pais_acima_do_teto_de_profundidade()
    {
        var id = Guid.NewGuid();
        var cadeia = Enumerable.Range(0, 60).Select(_ => Guid.NewGuid()).ToArray();
        _repo.GetByIdAsync(EmpId, id).Returns(new Categoria { Id = id, EmpresaId = EmpId, Nome = "Antigo" });
        for (var i = 0; i < cadeia.Length; i++)
        {
            Guid? paiDoNivel = i + 1 < cadeia.Length ? cadeia[i + 1] : null;
            _repo.GetByIdAsync(EmpId, cadeia[i]).Returns(new Categoria { Id = cadeia[i], EmpresaId = EmpId, Nome = $"Nivel {i}", CategoriaPaiId = paiDoNivel });
        }
        _repo.ExisteNomeAsync(EmpId, Arg.Any<string>(), id).Returns(false);

        var act = () => Sut().AtualizarAsync(new AtualizarCategoriaCommand(id, EmpId, "Novo", null, cadeia[0]));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*limite permitido*");
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<Categoria>());
    }
}
