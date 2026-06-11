using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.MenuFavoritos;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Favoritos do menu (ADR-0032, fatia 4): GET null vs lista + kdsHabilitado,
/// upsert, saneamento (dedup/cap/trim) e validacao loja ∈ empresa.
/// </summary>
public class MenuFavoritosUseCaseTests
{
    private static (Guid usuario, Guid empresa, Guid loja, Loja lojaEnt) Ctx()
    {
        var empresa = Guid.NewGuid();
        var loja = Guid.NewGuid();
        return (Guid.NewGuid(), empresa, loja, new Loja { Id = loja, EmpresaId = empresa, Nome = "L", Ativa = true });
    }

    [Fact]
    public async Task Obter_sem_linha_devolve_null_e_kds_da_config()
    {
        var (usuario, empresa, loja, lojaEnt) = Ctx();
        var lojaRepo = Substitute.For<ILojaRepository>();
        var configRepo = Substitute.For<IConfiguracaoLojaRepository>();
        var prefRepo = Substitute.For<IPreferenciaMenuRepository>();

        lojaRepo.GetByIdAsync(empresa, loja).Returns(lojaEnt);
        var cfg = ConfiguracaoLoja.CriarPadrao(loja);
        cfg.KdsHabilitado = true;
        configRepo.GetOrDefaultAsync(loja).Returns(cfg);
        prefRepo.GetAsync(usuario, loja).Returns((PreferenciaMenuUsuario?)null);

        var uc = new ObterFavoritosMenuUseCase(lojaRepo, configRepo, prefRepo);
        var r = await uc.ExecuteAsync(new ObterFavoritosMenuQuery(usuario, empresa, loja));

        r.Favoritos.Should().BeNull();
        r.KdsHabilitado.Should().BeTrue();
    }

    [Fact]
    public async Task Obter_com_linha_devolve_lista()
    {
        var (usuario, empresa, loja, lojaEnt) = Ctx();
        var lojaRepo = Substitute.For<ILojaRepository>();
        var configRepo = Substitute.For<IConfiguracaoLojaRepository>();
        var prefRepo = Substitute.For<IPreferenciaMenuRepository>();

        lojaRepo.GetByIdAsync(empresa, loja).Returns(lojaEnt);
        configRepo.GetOrDefaultAsync(loja).Returns(ConfiguracaoLoja.CriarPadrao(loja));
        prefRepo.GetAsync(usuario, loja).Returns(
            PreferenciaMenuUsuario.Criar(usuario, loja, empresa, new[] { "pedidos", "posicao-estoque" }));

        var uc = new ObterFavoritosMenuUseCase(lojaRepo, configRepo, prefRepo);
        var r = await uc.ExecuteAsync(new ObterFavoritosMenuQuery(usuario, empresa, loja));

        r.Favoritos.Should().Equal("pedidos", "posicao-estoque");
    }

    [Fact]
    public async Task Obter_loja_fora_da_empresa_falha()
    {
        var (usuario, empresa, loja, _) = Ctx();
        var lojaRepo = Substitute.For<ILojaRepository>();
        lojaRepo.GetByIdAsync(empresa, loja).Returns((Loja?)null); // loja nao pertence a empresa

        var uc = new ObterFavoritosMenuUseCase(lojaRepo,
            Substitute.For<IConfiguracaoLojaRepository>(), Substitute.For<IPreferenciaMenuRepository>());

        await Assert.ThrowsAsync<UseCaseValidationException>(() =>
            uc.ExecuteAsync(new ObterFavoritosMenuQuery(usuario, empresa, loja)));
    }

    [Fact]
    public async Task Salvar_novo_cria_e_saneia_dedup_e_cap()
    {
        var (usuario, empresa, loja, lojaEnt) = Ctx();
        var lojaRepo = Substitute.For<ILojaRepository>();
        var prefRepo = Substitute.For<IPreferenciaMenuRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<SalvarFavoritosMenuUseCase>>();

        lojaRepo.GetByIdAsync(empresa, loja).Returns(lojaEnt);
        prefRepo.GetAsync(usuario, loja).Returns((PreferenciaMenuUsuario?)null);

        var uc = new SalvarFavoritosMenuUseCase(lojaRepo, prefRepo, uow, logger);
        var entrada = new[] { "pedidos", "pedidos", "  ", "posicao-estoque" }; // dup + vazio
        var r = await uc.ExecuteAsync(new SalvarFavoritosMenuCommand(usuario, empresa, loja, entrada));

        r.Should().Equal("pedidos", "posicao-estoque");
        await prefRepo.Received(1).AddAsync(Arg.Any<PreferenciaMenuUsuario>());
        await uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Salvar_existente_atualiza()
    {
        var (usuario, empresa, loja, lojaEnt) = Ctx();
        var lojaRepo = Substitute.For<ILojaRepository>();
        var prefRepo = Substitute.For<IPreferenciaMenuRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<SalvarFavoritosMenuUseCase>>();

        lojaRepo.GetByIdAsync(empresa, loja).Returns(lojaEnt);
        prefRepo.GetAsync(usuario, loja).Returns(
            PreferenciaMenuUsuario.Criar(usuario, loja, empresa, new[] { "pedidos" }));

        var uc = new SalvarFavoritosMenuUseCase(lojaRepo, prefRepo, uow, logger);
        var r = await uc.ExecuteAsync(new SalvarFavoritosMenuCommand(usuario, empresa, loja, new[] { "caixa" }));

        r.Should().Equal("caixa");
        await prefRepo.Received(1).UpdateAsync(Arg.Any<PreferenciaMenuUsuario>());
        await uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Salvar_cap_20()
    {
        var (usuario, empresa, loja, lojaEnt) = Ctx();
        var lojaRepo = Substitute.For<ILojaRepository>();
        var prefRepo = Substitute.For<IPreferenciaMenuRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<SalvarFavoritosMenuUseCase>>();

        lojaRepo.GetByIdAsync(empresa, loja).Returns(lojaEnt);
        prefRepo.GetAsync(usuario, loja).Returns((PreferenciaMenuUsuario?)null);

        var entrada = Enumerable.Range(1, 30).Select(i => $"item-{i}").ToArray();
        var uc = new SalvarFavoritosMenuUseCase(lojaRepo, prefRepo, uow, logger);
        var r = await uc.ExecuteAsync(new SalvarFavoritosMenuCommand(usuario, empresa, loja, entrada));

        r.Should().HaveCount(SalvarFavoritosMenuUseCase.MaxFavoritos);
    }
}
