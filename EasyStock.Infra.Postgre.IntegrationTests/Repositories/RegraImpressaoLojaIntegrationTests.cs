using System.Text.Json.Nodes;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>#2199 (D2): regra de impressão da loja gravada por versão, isolada por empresa e a salvo do PUT de configurações.</summary>
[Collection("PostgreSqlTestCollection")]
public sealed class RegraImpressaoLojaIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private const string RegraA4 =
        """{"v":1,"documentos":{"pedido":{"papel":"a4","template":null,"orientacao":null,"codigos":null,"copias":1}}}""";
    private const string RegraA5 =
        """{"v":1,"documentos":{"pedido":{"papel":"a5","template":null,"orientacao":null,"codigos":null,"copias":1}}}""";

    private static readonly DateTime Agora = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task Loja_sem_configuracao_grava_na_versao_zero_e_le_a_regra_de_volta()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var (empresa, loja) = await SemearAsync();
        await using var db = Contexto(empresa.Id);
        var repo = new RegraImpressaoLojaRepository(db);

        (await repo.ObterAsync(empresa.Id, loja.Id)).Should().Be(new RegraImpressaoSalva(null, 0, null));
        var gravacao = await repo.GravarAsync(empresa.Id, loja.Id, 0, RegraA4, Agora);
        var lida = await repo.ObterAsync(empresa.Id, loja.Id);

        gravacao.Should().Be(new GravacaoRegraImpressao(true, 1));
        lida!.Versao.Should().Be(1);
        lida.AlteradoEm.Should().Be(Agora);
        JsonNode.DeepEquals(JsonNode.Parse(lida.Json!), JsonNode.Parse(RegraA4)).Should().BeTrue("o jsonb pode reordenar chaves, nunca o conteúdo");
    }

    [SkippableFact]
    public async Task Versao_velha_nao_grava_e_devolve_a_versao_atual()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var (empresa, loja) = await SemearAsync();
        await using var db = Contexto(empresa.Id);
        var repo = new RegraImpressaoLojaRepository(db);
        await repo.GravarAsync(empresa.Id, loja.Id, 0, RegraA4, Agora);

        var tardia = await repo.GravarAsync(empresa.Id, loja.Id, 0, RegraA5, Agora.AddMinutes(1));

        tardia.Gravada.Should().BeFalse();
        tardia.Versao.Should().Be(1);
        (await repo.ObterAsync(empresa.Id, loja.Id))!.Json.Should().Contain("\"a4\"");
    }

    [SkippableFact]
    public async Task Gravacoes_simultaneas_da_mesma_versao_so_uma_vence()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var (empresa, loja) = await SemearAsync();

        // Loja sem linha de configuração: as duas também disputam a criação da linha.
        var resultados = await Task.WhenAll(new[] { RegraA4, RegraA5 }.Select(async regra =>
        {
            await using var db = Contexto(empresa.Id);
            return await new RegraImpressaoLojaRepository(db).GravarAsync(empresa.Id, loja.Id, 0, regra, Agora);
        }));

        resultados.Count(r => r.Gravada).Should().Be(1);
        resultados.Should().OnlyContain(r => r.Versao == 1);
        await using var conferencia = Contexto(empresa.Id);
        (await conferencia.ConfiguracoesLoja.CountAsync(c => c.LojaId == loja.Id)).Should().Be(1);
    }

    [SkippableFact]
    public async Task Loja_de_outra_empresa_nao_e_lida_nem_gravada()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var (empresa, loja) = await SemearAsync();
        var (outra, _) = await SemearAsync();
        await using (var dono = Contexto(empresa.Id))
            await new RegraImpressaoLojaRepository(dono).GravarAsync(empresa.Id, loja.Id, 0, RegraA4, Agora);

        await using var db = Contexto(outra.Id);
        var repo = new RegraImpressaoLojaRepository(db);
        (await repo.ObterAsync(outra.Id, loja.Id)).Should().BeNull();
        var invasao = await repo.GravarAsync(outra.Id, loja.Id, 1, RegraA5, Agora);

        invasao.Gravada.Should().BeFalse();
        await using var conferencia = Contexto(empresa.Id);
        (await new RegraImpressaoLojaRepository(conferencia).ObterAsync(empresa.Id, loja.Id))!.Json.Should().Contain("\"a4\"");
    }

    [SkippableFact]
    public async Task Sob_RLS_a_loja_de_outra_empresa_nao_aparece_nem_com_o_id_dela()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var (empresa, loja) = await SemearAsync();
        var (outra, _) = await SemearAsync();
        await using (var dono = Contexto(empresa.Id))
            await new RegraImpressaoLojaRepository(dono).GravarAsync(empresa.Id, loja.Id, 0, RegraA4, Agora);

        await using var rls = fixture.CreateRlsClientDbContext();
        await rls.Database.OpenConnectionAsync();
        await rls.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {outra.Id.ToString()}, false)");
        // O filtro do EF diria a empresa do token; aqui ele aponta a empresa dona para provar que o banco segura sozinho.
        rls.SetMobileTenantContext(empresa.Id);

        (await new RegraImpressaoLojaRepository(rls).ObterAsync(empresa.Id, loja.Id)).Should().BeNull();
        (await new RegraImpressaoLojaRepository(rls).GravarAsync(empresa.Id, loja.Id, 1, RegraA5, Agora)).Gravada.Should().BeFalse();
    }

    [SkippableFact]
    public async Task Salvar_configuracoes_lidas_antes_nao_apaga_a_regra_gravada_no_meio()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var (empresa, loja) = await SemearAsync();
        await using (var semente = Contexto(empresa.Id))
        {
            semente.ConfiguracoesLoja.Add(ConfiguracaoLoja.CriarPadrao(loja.Id));
            await semente.SaveChangesAsync();
        }

        await using var telaConfig = Contexto(empresa.Id);
        var configRepo = new ConfiguracaoLojaRepository(telaConfig);
        var lidaAntes = (await configRepo.GetByLojaIdAsync(loja.Id))!;

        await using (var telaImpressao = Contexto(empresa.Id))
            await new RegraImpressaoLojaRepository(telaImpressao).GravarAsync(empresa.Id, loja.Id, 0, RegraA4, Agora);

        lidaAntes.DiasAlertaValidade += 1;
        await configRepo.UpdateAsync(lidaAntes);
        await telaConfig.SaveChangesAsync();

        await using var conferencia = Contexto(empresa.Id);
        var final = await conferencia.ConfiguracoesLoja.AsNoTracking().SingleAsync(c => c.LojaId == loja.Id);
        final.DiasAlertaValidade.Should().Be(lidaAntes.DiasAlertaValidade);
        final.ImpressaoVersao.Should().Be(1);
        final.ImpressaoJson.Should().Contain("\"a4\"");
    }

    private async Task<(Empresa Empresa, Loja Loja)> SemearAsync()
    {
        var empresa = Empresa.Criar($"Empresa {Guid.NewGuid():N}", null);
        var loja = Loja.Criar(empresa.Id, "Loja");
        await using var db = fixture.CreateDbContext();
        db.Empresas.Add(empresa);
        db.Lojas.Add(loja);
        await db.SaveChangesAsync();
        return (empresa, loja);
    }

    private EasyStockDbContext Contexto(Guid empresaId)
    {
        var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        return db;
    }
}
