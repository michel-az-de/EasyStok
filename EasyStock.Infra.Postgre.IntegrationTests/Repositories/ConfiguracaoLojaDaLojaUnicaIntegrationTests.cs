using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2003 (estoque-07): "a loja única da empresa" não conta a loja desativada. Com uma loja ativa e uma desativada,
/// ambas com configuração, o <c>FifoAtivo</c> da ativa era ignorado em silêncio.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ConfiguracaoLojaDaLojaUnicaIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private sealed record Semente(Guid EmpresaId, Guid LojaAtivaId, Guid LojaDesativadaId);

    [SkippableFact]
    public async Task SemLoja_UmaAtivaEUmaDesativada_ValeAConfiguracaoDaAtiva()
    {
        var s = await SemearAsync(fifoDaAtiva: false, fifoDaDesativada: true);

        await using var db = fixture.CreateDbContext();
        var config = await new ConfiguracaoLojaRepository(db).GetDaLojaAsync(s.EmpresaId, null);

        config.Should().NotBeNull();
        config!.LojaId.Should().Be(s.LojaAtivaId);
        config.FifoAtivo.Should().BeFalse();
    }

    [SkippableFact]
    public async Task ComLoja_ValeAConfiguracaoDaquelaLoja_MesmoDesativada()
    {
        var s = await SemearAsync(fifoDaAtiva: false, fifoDaDesativada: true);

        await using var db = fixture.CreateDbContext();
        var config = await new ConfiguracaoLojaRepository(db).GetDaLojaAsync(s.EmpresaId, s.LojaDesativadaId);

        config!.FifoAtivo.Should().BeTrue();
    }

    [SkippableFact]
    public async Task SemLoja_DuasAtivas_NaoHaLojaUnica()
    {
        var s = await SemearAsync(fifoDaAtiva: false, fifoDaDesativada: true, reativar: true);

        await using var db = fixture.CreateDbContext();
        (await new ConfiguracaoLojaRepository(db).GetDaLojaAsync(s.EmpresaId, null)).Should().BeNull();
    }

    private async Task<Semente> SemearAsync(bool fifoDaAtiva, bool fifoDaDesativada, bool reativar = false)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var agora = DateTime.UtcNow;
        var empresaId = Guid.NewGuid();
        var ativa = Loja.Criar(empresaId, "Casa da Baba");
        var desativada = Loja.Criar(empresaId, "Loja antiga");
        if (!reativar) desativada.Desativar();
        await using var db = fixture.CreateDbContext();
        db.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Lojas", Documento = empresaId.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora });
        db.Lojas.AddRange(ativa, desativada);
        var configAtiva = ConfiguracaoLoja.CriarPadrao(ativa.Id);
        configAtiva.FifoAtivo = fifoDaAtiva;
        var configDesativada = ConfiguracaoLoja.CriarPadrao(desativada.Id);
        configDesativada.FifoAtivo = fifoDaDesativada;
        db.ConfiguracoesLoja.AddRange(configAtiva, configDesativada);
        await db.SaveChangesAsync();
        return new Semente(empresaId, ativa.Id, desativada.Id);
    }
}
