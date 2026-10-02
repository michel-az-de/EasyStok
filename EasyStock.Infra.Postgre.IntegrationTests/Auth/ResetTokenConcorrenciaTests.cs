using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Auth;

/// <summary>
/// N8: o uso único e o contador de tentativas dos segredos de acesso em Postgres real. Cada chamada usa o próprio
/// <c>EasyStockDbContext</c> (uma conexão por requisição concorrente), como a API.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ResetTokenConcorrenciaTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task DoisUsosSimultaneosUmSoVence()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var token = await SemearAsync(FinalidadeResetToken.Reset, expiraEm: DateTime.UtcNow.AddMinutes(30));
        var agora = DateTime.UtcNow;

        var resultados = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var db = fixture.CreateDbContext();
            return await new ResetTokenRepository(db).ConsumirAsync(token.Id, agora);
        }));

        resultados.Count(r => r).Should().Be(1, "dois POST simultaneos com o mesmo token: 1 sucesso e 1 falha");
        (await Ler(token.Id)).Usado.Should().BeTrue();
    }

    [SkippableFact]
    public async Task ConsumirNaoValeDepoisDaValidadeNemDeUmTokenJaUsado()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var vencido = await SemearAsync(FinalidadeResetToken.Reset, expiraEm: DateTime.UtcNow.AddMinutes(-1));
        var vigente = await SemearAsync(FinalidadeResetToken.Reset, expiraEm: DateTime.UtcNow.AddMinutes(30));
        await using var db = fixture.CreateDbContext();
        var repo = new ResetTokenRepository(db);

        (await repo.ConsumirAsync(vencido.Id, DateTime.UtcNow)).Should().BeFalse();
        (await repo.ConsumirAsync(vigente.Id, DateTime.UtcNow)).Should().BeTrue();
        (await repo.ConsumirAsync(vigente.Id, DateTime.UtcNow)).Should().BeFalse("o segundo uso afeta 0 linhas");
    }

    [SkippableFact]
    public async Task DezCodigosErradosEmParaleloContamNoMaximoCinco()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var token = await SemearAsync(FinalidadeResetToken.ResetCodigo, expiraEm: DateTime.UtcNow.AddMinutes(10));
        var agora = DateTime.UtcNow;

        var resultados = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var db = fixture.CreateDbContext();
            return await new ResetTokenRepository(db).RegistrarTentativaAsync(token.Id, agora);
        }));

        resultados.Count(r => r > 0).Should().Be(5, "só 5 tentativas são gastas, as outras 5 nem chegam a comparar");
        resultados.Where(r => r > 0).Should().BeEquivalentTo([1, 2, 3, 4, 5], "cada tentativa recebe o valor novo do contador");
        (await Ler(token.Id)).Tentativas.Should().Be(5);
    }

    [SkippableFact]
    public async Task InvalidarAbertosMataLinkECodigoDoUsuarioEMaisNada()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var usuario = await SemearUsuarioAsync();
        var link = await SemearAsync(FinalidadeResetToken.Reset, DateTime.UtcNow.AddMinutes(30), usuario);
        var codigo = await SemearAsync(FinalidadeResetToken.ResetCodigo, DateTime.UtcNow.AddMinutes(10), usuario);
        var convite = await SemearAsync("Convite", DateTime.UtcNow.AddHours(72), usuario);
        var deOutro = await SemearAsync(FinalidadeResetToken.Reset, DateTime.UtcNow.AddMinutes(30));
        await using var db = fixture.CreateDbContext();

        var alteradas = await new ResetTokenRepository(db).InvalidarAbertosAsync(usuario.Id, DateTime.UtcNow);

        alteradas.Should().Be(2);
        (await Ler(link.Id)).Usado.Should().BeTrue();
        (await Ler(codigo.Id)).Usado.Should().BeTrue();
        (await Ler(convite.Id)).Usado.Should().BeFalse("o convite e da N9, nao deste fluxo");
        (await Ler(deOutro.Id)).Usado.Should().BeFalse();
    }

    [SkippableFact]
    public async Task ContarPedidosSoContaLinhasResetDasUltimas24Horas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var usuario = await SemearUsuarioAsync();
        var agora = DateTime.UtcNow;
        await SemearAsync(FinalidadeResetToken.Reset, agora.AddMinutes(30), usuario, criadoEm: agora.AddMinutes(-10));
        await SemearAsync(FinalidadeResetToken.Reset, agora.AddMinutes(30), usuario, criadoEm: agora.AddHours(-5));
        await SemearAsync(FinalidadeResetToken.Reset, agora.AddMinutes(30), usuario, criadoEm: agora.AddHours(-30));
        await SemearAsync(FinalidadeResetToken.ResetCodigo, agora.AddMinutes(10), usuario, criadoEm: agora.AddMinutes(-10));
        await using var db = fixture.CreateDbContext();

        var contagem = await new ResetTokenRepository(db).ContarPedidosAsync(usuario.Id, agora);

        contagem.NaUltimaHora.Should().Be(1);
        contagem.NasUltimas24Horas.Should().Be(2, "o de 30 h e o codigo nao entram");
        contagem.UltimoPedidoEm.Should().BeCloseTo(agora.AddMinutes(-10), TimeSpan.FromSeconds(1));
    }

    [SkippableFact]
    public async Task LimpezaApagaExpiradosEUsadosAntigos()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var agora = DateTime.UtcNow;
        var expiradoHa25h = await SemearAsync(FinalidadeResetToken.Reset, agora.AddHours(-25));
        var usadoExpiradoHa30h = await SemearAsync(FinalidadeResetToken.ResetCodigo, agora.AddHours(-30), usado: true);
        var expiradoHa23h = await SemearAsync(FinalidadeResetToken.Reset, agora.AddHours(-23));
        var vigente = await SemearAsync(FinalidadeResetToken.Reset, agora.AddMinutes(20));
        await using var db = fixture.CreateDbContext();

        var apagados = await new ResetTokenRepository(db).ApagarExpiradosAsync(agora.AddHours(-24));

        apagados.Should().BeGreaterThanOrEqualTo(2);
        await using var leitura = fixture.CreateDbContext();
        var restantes = await leitura.ResetTokens.AsNoTracking().Select(t => t.Id).ToListAsync();
        restantes.Should().NotContain([expiradoHa25h.Id, usadoExpiradoHa30h.Id]);
        restantes.Should().Contain([expiradoHa23h.Id, vigente.Id]);
    }

    [SkippableFact]
    public async Task MigracaoPreservaTokensExistentesComoReset()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var usuario = await SemearUsuarioAsync();
        await using var db = fixture.CreateDbContext();

        // Insert como o codigo antigo, sem as colunas novas: o DEFAULT da migracao vale.
        var id = Guid.NewGuid();
        var hash = $"hash-antigo-{id:N}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO reset_tokens ("Id", "UsuarioId", "TokenHash", "CriadoEm", "ExpiraEm", "Usado")
            VALUES ({id}, {usuario.Id}, {hash}, now(), now() + interval '30 minutes', false)
            """);

        var linha = await Ler(id);
        linha.Finalidade.Should().Be(FinalidadeResetToken.Reset);
        linha.Tentativas.Should().Be(0);
        linha.Canal.Should().BeNull();
        (await new ResetTokenRepository(db).ObterAbertoAsync(usuario.Id, FinalidadeResetToken.Reset, DateTime.UtcNow))!.Id
            .Should().Be(id, "a linha antiga continua valendo");

        var colunas = await db.Database.SqlQueryRaw<string>("""
            SELECT column_name || '|' || is_nullable || '|' || COALESCE(column_default, 'sem-default') AS "Value"
            FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = 'reset_tokens'
              AND column_name IN ('Finalidade', 'Tentativas', 'Canal')
            ORDER BY column_name
            """).ToListAsync();
        colunas.Should().Equal(
            "Canal|YES|sem-default",
            "Finalidade|NO|'Reset'::character varying",
            "Tentativas|NO|0");
    }

    private async Task<Usuario> SemearUsuarioAsync()
    {
        var usuario = Usuario.Criar("Ana", $"ana-{Guid.NewGuid():N}@casadababa.com", "hash");
        await using var db = fixture.CreateDbContext();
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }

    private async Task<ResetToken> SemearAsync(
        string finalidade, DateTime expiraEm, Usuario? usuario = null, bool usado = false, DateTime? criadoEm = null)
    {
        usuario ??= await SemearUsuarioAsync();
        var token = ResetToken.Criar(
            usuario.Id, $"hash-{Guid.NewGuid():N}", expiraEm, null, null, finalidade, criadoEm: criadoEm);
        token.Usado = usado;
        await using var db = fixture.CreateDbContext();
        db.ResetTokens.Add(token);
        await db.SaveChangesAsync();
        return token;
    }

    private async Task<ResetToken> Ler(Guid id)
    {
        await using var db = fixture.CreateDbContext();
        return await db.ResetTokens.AsNoTracking().SingleAsync(t => t.Id == id);
    }
}
