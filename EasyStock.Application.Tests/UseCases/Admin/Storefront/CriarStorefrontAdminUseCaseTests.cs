using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Admin.Storefront.CriarStorefrontAdmin;
using EasyStock.Domain.Exceptions.Storefront;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Admin.Storefront;

public class CriarStorefrontAdminUseCaseTests
{
    private readonly IStorefrontRepository _storefrontRepo = Substitute.For<IStorefrontRepository>();
    private readonly IEmpresaRepository _empresaRepo = Substitute.For<IEmpresaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private CriarStorefrontAdminUseCase Sut() => new(_storefrontRepo, _empresaRepo, _uow);

    private static Empresa EmpresaFake(Guid id)
    {
        // Empresa não tem factory pública convencional — usamos reflection via private setter.
        var e = (Empresa)System.Activator.CreateInstance(typeof(Empresa), nonPublic: true)!;
        typeof(Empresa).GetProperty("Id")!.SetValue(e, id);
        typeof(Empresa).GetProperty("Nome")!.SetValue(e, "Empresa de teste");
        return e;
    }

    [Fact]
    public async Task DeveCriarStorefront_QuandoTudoOk()
    {
        var empresaId = Guid.NewGuid();
        _empresaRepo.GetByIdAsync(empresaId).Returns(EmpresaFake(empresaId));
        _storefrontRepo.GetByEmpresaAsync(empresaId, Arg.Any<CancellationToken>())
            .Returns((StorefrontEntity?)null);
        _storefrontRepo.GetBySlugAsync("casa-da-baba", Arg.Any<CancellationToken>())
            .Returns((StorefrontEntity?)null);

        var result = await Sut().ExecuteAsync(new CriarStorefrontAdminCommand(
            empresaId, "casa-da-baba", "Casa da Babá", 50m));

        result.Should().NotBeNull();
        result.Slug.Should().Be("casa-da-baba");
        result.StorefrontId.Should().NotBeEmpty();
        await _storefrontRepo.Received(1).AddAsync(Arg.Any<StorefrontEntity>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DeveLancarExcecao_QuandoEmpresaNaoExiste()
    {
        var empresaId = Guid.NewGuid();
        _empresaRepo.GetByIdAsync(empresaId).Returns((Empresa?)null);

        var act = async () => await Sut().ExecuteAsync(new CriarStorefrontAdminCommand(
            empresaId, "qualquer", "Título", 0m));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .Where(e => e.Code == "EMPRESA_INEXISTENTE");
    }

    [Fact]
    public async Task DeveLancarExcecao_QuandoEmpresaJaTemStorefront()
    {
        var empresaId = Guid.NewGuid();
        _empresaRepo.GetByIdAsync(empresaId).Returns(EmpresaFake(empresaId));
        var existente = StorefrontEntity.Criar(empresaId, "outro-slug", "Outro", 0m);
        _storefrontRepo.GetByEmpresaAsync(empresaId, Arg.Any<CancellationToken>()).Returns(existente);

        var act = async () => await Sut().ExecuteAsync(new CriarStorefrontAdminCommand(
            empresaId, "novo-slug", "Novo", 0m));

        await act.Should().ThrowAsync<EmpresaJaTemStorefrontException>();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DeveLancarExcecao_QuandoSlugDuplicado()
    {
        var empresaId = Guid.NewGuid();
        var outraEmpresaId = Guid.NewGuid();
        _empresaRepo.GetByIdAsync(empresaId).Returns(EmpresaFake(empresaId));
        _storefrontRepo.GetByEmpresaAsync(empresaId, Arg.Any<CancellationToken>())
            .Returns((StorefrontEntity?)null);
        var slugAlheio = StorefrontEntity.Criar(outraEmpresaId, "casa-da-baba", "Conflito", 0m);
        _storefrontRepo.GetBySlugAsync("casa-da-baba", Arg.Any<CancellationToken>())
            .Returns(slugAlheio);

        var act = async () => await Sut().ExecuteAsync(new CriarStorefrontAdminCommand(
            empresaId, "casa-da-baba", "Casa da Babá", 0m));

        await act.Should().ThrowAsync<StorefrontSlugDuplicadoException>()
            .Where(e => e.Slug == "casa-da-baba");
    }

    [Fact]
    public async Task DeveAplicarDefaultsSeguros()
    {
        var empresaId = Guid.NewGuid();
        _empresaRepo.GetByIdAsync(empresaId).Returns(EmpresaFake(empresaId));
        _storefrontRepo.GetByEmpresaAsync(empresaId, Arg.Any<CancellationToken>())
            .Returns((StorefrontEntity?)null);
        _storefrontRepo.GetBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((StorefrontEntity?)null);

        StorefrontEntity? capturado = null;
        await _storefrontRepo.AddAsync(
            Arg.Do<StorefrontEntity>(s => capturado = s), Arg.Any<CancellationToken>());

        await Sut().ExecuteAsync(new CriarStorefrontAdminCommand(
            empresaId, "casa-da-baba", "Casa da Babá", 50m));

        capturado.Should().NotBeNull();
        capturado!.Ativo.Should().BeFalse();
        capturado.NfeAutomaticaHabilitada.Should().BeFalse();
        capturado.ModeloFiscal.Should().Be("manual");
    }
}
