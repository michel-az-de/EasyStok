using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.FeatureFlags;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Feature flags por tenant (ADR-0048). O que estes testes protegem: o nome da feature ser
/// validado antes de virar linha no banco, e a escrita levar auditoria de quem alterou.
/// O isolamento entre tenants — que aqui é responsabilidade do repository, porque a entidade
/// é isenta do query filter e do RLS — tem teste próprio em Infra.
/// </summary>
public class FeatureFlagsUseCaseTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    [Fact]
    public async Task Obter_ativas_devolve_o_que_o_repositorio_traz()
    {
        var repo = Substitute.For<ITenantFeatureFlagRepository>();
        repo.ListarAtivasAsync(Empresa, Arg.Any<CancellationToken>())
            .Returns(new[] { "modulo.comercial" });

        var uc = new ObterFeaturesAtivasUseCase(repo);
        var r = await uc.ExecuteAsync(new ObterFeaturesAtivasQuery(Empresa));

        r.Should().Equal("modulo.comercial");
    }

    [Fact]
    public async Task Empresa_sem_flag_nenhuma_nao_e_erro()
    {
        var repo = Substitute.For<ITenantFeatureFlagRepository>();
        repo.ListarAtivasAsync(Empresa, Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());

        var r = await new ObterFeaturesAtivasUseCase(repo).ExecuteAsync(new ObterFeaturesAtivasQuery(Empresa));

        r.Should().BeEmpty();
    }

    [Fact]
    public async Task Definir_persiste_com_quem_alterou()
    {
        var repo = Substitute.For<ITenantFeatureFlagRepository>();
        repo.DefinirAsync(Empresa, "modulo.crm", true, "admin@easystok.com", Arg.Any<CancellationToken>())
            .Returns(new TenantFeatureFlagItem("modulo.crm", true, DateTime.UtcNow, "admin@easystok.com"));

        var r = await new DefinirFeatureDoTenantUseCase(repo).ExecuteAsync(
            new DefinirFeatureDoTenantCommand(Empresa, "modulo.crm", true, "admin@easystok.com"));

        r.Ativo.Should().BeTrue();
        r.AlteradoPor.Should().Be("admin@easystok.com");
        await repo.Received(1).DefinirAsync(Empresa, "modulo.crm", true, "admin@easystok.com", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Feature_vazia_e_recusada(string feature)
    {
        var repo = Substitute.For<ITenantFeatureFlagRepository>();

        await Assert.ThrowsAsync<UseCaseValidationException>(() =>
            new DefinirFeatureDoTenantUseCase(repo).ExecuteAsync(
                new DefinirFeatureDoTenantCommand(Empresa, feature, true, "admin@easystok.com")));

        await repo.DidNotReceive().DefinirAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("modulo comercial")]      // espaço
    [InlineData("modulo/comercial")]      // barra
    [InlineData("modulo;drop table")]     // tentativa grosseira
    public async Task Feature_com_caractere_invalido_e_recusada(string feature)
    {
        var repo = Substitute.For<ITenantFeatureFlagRepository>();

        await Assert.ThrowsAsync<UseCaseValidationException>(() =>
            new DefinirFeatureDoTenantUseCase(repo).ExecuteAsync(
                new DefinirFeatureDoTenantCommand(Empresa, feature, true, "admin@easystok.com")));
    }

    [Fact]
    public async Task Feature_maior_que_a_coluna_e_recusada_antes_do_banco()
    {
        // A coluna tem 50 chars; recusar aqui dá mensagem melhor que erro de constraint.
        var repo = Substitute.For<ITenantFeatureFlagRepository>();

        await Assert.ThrowsAsync<UseCaseValidationException>(() =>
            new DefinirFeatureDoTenantUseCase(repo).ExecuteAsync(
                new DefinirFeatureDoTenantCommand(Empresa, new string('a', 51), true, "admin@easystok.com")));
    }

    [Fact]
    public async Task Empresa_vazia_e_recusada()
    {
        var repo = Substitute.For<ITenantFeatureFlagRepository>();

        await Assert.ThrowsAsync<UseCaseValidationException>(() =>
            new DefinirFeatureDoTenantUseCase(repo).ExecuteAsync(
                new DefinirFeatureDoTenantCommand(Guid.Empty, "modulo.crm", true, "admin@easystok.com")));
    }

    [Fact]
    public void Catalogo_expoe_os_modulos_b2b_e_recusa_nome_torto()
    {
        var nomes = FeatureCatalogo.Conhecidas.Select(c => c.Nome).ToList();
        nomes.Should().Contain(FeatureCatalogo.ModuloComercial);
        nomes.Should().Contain(FeatureCatalogo.ModuloCrm);
        nomes.Should().OnlyHaveUniqueItems();
        FeatureCatalogo.Conhecidas.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Descricao));

        FeatureCatalogo.NomeValido("modulo.comercial").Should().BeTrue();
        FeatureCatalogo.NomeValido("modulo comercial").Should().BeFalse();
    }

    // ── listagem do back-office (catálogo + estado) ──────────────────

    [Fact]
    public async Task Tenant_sem_flag_salva_ainda_ve_o_catalogo_inteiro_desligado()
    {
        // Sem isto a aba "Features" ficaria vazia num tenant novo — e a tela só oferece
        // toggle para o que vem na lista, então não haveria como ligar o primeiro módulo.
        var repo = Substitute.For<ITenantFeatureFlagRepository>();
        repo.ListarPorEmpresaAsync(Empresa, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TenantFeatureFlagItem>());

        var r = await new ListarFeaturesDoTenantUseCase(repo).ExecuteAsync(new ListarFeaturesDoTenantQuery(Empresa));

        r.Select(f => f.Feature).Should().Equal(FeatureCatalogo.Conhecidas.Select(c => c.Nome));
        r.Should().OnlyContain(f => !f.Ativo);
        r.Should().OnlyContain(f => f.AlteradoPor == null);
    }

    [Fact]
    public async Task Estado_salvo_vence_o_default_do_catalogo()
    {
        var repo = Substitute.For<ITenantFeatureFlagRepository>();
        repo.ListarPorEmpresaAsync(Empresa, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new TenantFeatureFlagItem(FeatureCatalogo.ModuloCrm, true, new DateTime(2026, 8, 8), "admin@easystok.com"),
        });

        var r = await new ListarFeaturesDoTenantUseCase(repo).ExecuteAsync(new ListarFeaturesDoTenantQuery(Empresa));

        var crm = r.Single(f => f.Feature == FeatureCatalogo.ModuloCrm);
        crm.Ativo.Should().BeTrue();
        crm.AlteradoPor.Should().Be("admin@easystok.com");
        r.Single(f => f.Feature == FeatureCatalogo.ModuloComercial).Ativo.Should().BeFalse();
    }

    [Fact]
    public async Task Feature_salva_fora_do_catalogo_nao_some_da_tela()
    {
        // Feature de uma versão anterior: some do catálogo mas continua valendo no banco.
        // Escondê-la deixaria um módulo ligado sem ninguém conseguir desligar.
        var repo = Substitute.For<ITenantFeatureFlagRepository>();
        repo.ListarPorEmpresaAsync(Empresa, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new TenantFeatureFlagItem("modulo.legado", true, DateTime.UtcNow, "admin@easystok.com"),
        });

        var r = await new ListarFeaturesDoTenantUseCase(repo).ExecuteAsync(new ListarFeaturesDoTenantQuery(Empresa));

        r.Select(f => f.Feature).Should().Contain("modulo.legado");
        r.Should().HaveCount(FeatureCatalogo.Conhecidas.Count + 1);
    }
}
