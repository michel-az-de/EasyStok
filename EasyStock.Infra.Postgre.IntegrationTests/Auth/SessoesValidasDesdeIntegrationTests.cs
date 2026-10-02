using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Auth;

/// <summary>
/// #1352 (N7): o carimbo <c>usuarios.SessoesValidasDesde</c> em Postgres real. A tabela <c>usuarios</c> não
/// tem <c>EmpresaId</c>, então a leitura que o validador faz a cada requisição roda sem tenant e sem bypass
/// de RLS: estes testes usam o papel <c>rls_test_client</c> (NOSUPERUSER) justamente para provar isso.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class SessoesValidasDesdeIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly DateTime Corte = new(2026, 10, 2, 13, 45, 10, DateTimeKind.Utc);

    [SkippableFact]
    public async Task LeituraLeveDevolveCarimboEAtivo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var ana = await SemearUsuarioAsync("ana");
        var inativo = await SemearUsuarioAsync("inativo", ativo: false);

        await using var db = CriarContextoSemTenant();
        var repo = new UsuarioRepository(db);

        (await repo.ObterSessaoAsync(ana.Id)).Should().Be(new SessaoDoUsuario(true, null));
        (await repo.ObterSessaoAsync(inativo.Id)).Should().Be(new SessaoDoUsuario(false, null));
        (await repo.ObterSessaoAsync(Guid.NewGuid())).Should().BeNull("usuário que não existe não tem sessão");

        await repo.AtualizarSessoesValidasDesdeAsync(ana.Id, Corte);

        (await repo.ObterSessaoAsync(ana.Id)).Should().Be(new SessaoDoUsuario(true, Corte));
    }

    [SkippableFact]
    public async Task AtualizarOCarimboNuncaRecua()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var ana = await SemearUsuarioAsync("ana");
        await using var db = CriarContextoSemTenant();
        var repo = new UsuarioRepository(db);

        (await repo.AtualizarSessoesValidasDesdeAsync(ana.Id, Corte)).Should().Be(1);
        (await repo.AtualizarSessoesValidasDesdeAsync(ana.Id, Corte.AddMinutes(-5))).Should().Be(0, "instante anterior não recua");
        (await repo.AtualizarSessoesValidasDesdeAsync(ana.Id, Corte)).Should().Be(0, "o mesmo instante não regrava");
        (await repo.ObterSessaoAsync(ana.Id))!.SessoesValidasDesde.Should().Be(Corte);

        (await repo.AtualizarSessoesValidasDesdeAsync(ana.Id, Corte.AddSeconds(1))).Should().Be(1);
        (await repo.ObterSessaoAsync(ana.Id))!.SessoesValidasDesde.Should().Be(Corte.AddSeconds(1));
    }

    [SkippableFact]
    public async Task UpdateGenericoNaoDesfazOCarimbo()
    {
        // Um login lê o usuário (carimbo nulo), demora no hash e grava a linha inteira. Se outra requisição
        // revogou nesse meio tempo, o login não pode devolver o carimbo ao valor velho.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var ana = await SemearUsuarioAsync("ana");

        await using var dbDoLogin = fixture.CreateDbContext();
        var repoDoLogin = new UsuarioRepository(dbDoLogin);
        var lidoAntes = (await repoDoLogin.GetByIdAsync(ana.Id))!;
        lidoAntes.SessoesValidasDesde.Should().BeNull();

        await using (var dbDaRevogacao = fixture.CreateDbContext())
            await new UsuarioRepository(dbDaRevogacao).AtualizarSessoesValidasDesdeAsync(ana.Id, Corte);

        lidoAntes.AtualizarUltimoAcesso();
        await repoDoLogin.UpdateAsync(lidoAntes);
        await dbDoLogin.SaveChangesAsync();

        await using var conferencia = CriarContextoSemTenant();
        var sessao = await new UsuarioRepository(conferencia).ObterSessaoAsync(ana.Id);
        sessao!.SessoesValidasDesde.Should().Be(Corte);
    }

    [SkippableFact]
    public async Task SaveChangesNuncaGravaOCarimboMesmoComAEntidadeMutada()
    {
        // ResetarSenha e AlterarSenha chamam UpdateAsync(usuario) e depois RevogadorSessoes, que muda o carimbo
        // na entidade em memória. Se o SaveChanges levasse esse valor, duas revogações concorrentes poderiam
        // fazer o corte recuar: só o UPDATE atômico (que nunca recua) grava o carimbo.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var ana = await SemearUsuarioAsync("ana");

        await using var db = fixture.CreateDbContext();
        var repo = new UsuarioRepository(db);
        var usuario = (await repo.GetByIdAsync(ana.Id))!;
        await repo.UpdateAsync(usuario);
        usuario.RevogarSessoes(Corte);                                  // em memória, depois do UpdateAsync

        await using (var outra = fixture.CreateDbContext())             // outra requisição revoga mais tarde
            await new UsuarioRepository(outra).AtualizarSessoesValidasDesdeAsync(ana.Id, Corte.AddMinutes(5));
        await db.SaveChangesAsync();

        await using var conferencia = CriarContextoSemTenant();
        (await new UsuarioRepository(conferencia).ObterSessaoAsync(ana.Id))!.SessoesValidasDesde
            .Should().Be(Corte.AddMinutes(5), "o SaveChanges não pode recuar o corte para o valor da entidade");
    }

    [SkippableFact]
    public async Task RevogarSessoesAtivasRevogaSoDoUsuario()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var ana = await SemearUsuarioAsync("ana");
        var leo = await SemearUsuarioAsync("leo");
        var agora = DateTime.UtcNow;
        var ativaDaAna = Token(ana, expiraEm: agora.AddDays(7));
        var vencidaDaAna = Token(ana, expiraEm: agora.AddDays(-1));
        var jaRevogadaDaAna = Token(ana, expiraEm: agora.AddDays(7));
        jaRevogadaDaAna.Revogar();
        var ativaDoLeo = Token(leo, expiraEm: agora.AddDays(7));
        await using (var seed = fixture.CreateDbContext())
        {
            seed.RefreshTokens.AddRange(ativaDaAna, vencidaDaAna, jaRevogadaDaAna, ativaDoLeo);
            await seed.SaveChangesAsync();
        }

        await using var db = CriarContextoSemTenant();
        var revogados = await new RefreshTokenRepository(db).RevogarSessoesAtivasAsync(ana.Id, agora);

        revogados.Should().Be(1, "só o token ativo da Ana");
        await using var leitura = fixture.CreateDbContext();
        var porId = await leitura.RefreshTokens.AsNoTracking().ToDictionaryAsync(t => t.Id);
        porId[ativaDaAna.Id].Revogado.Should().BeTrue();
        porId[ativaDaAna.Id].RevogadoEm.Should().NotBeNull();
        porId[vencidaDaAna.Id].Revogado.Should().BeFalse("o vencido não é mexido");
        porId[ativaDoLeo.Id].Revogado.Should().BeFalse("o refresh de outro usuário fica como está");
    }

    [SkippableFact]
    public async Task MigracaoAdicionaColunaNula()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var ana = await SemearUsuarioAsync("ana");
        await using var db = fixture.CreateDbContext();

        var colunas = await db.Database.SqlQueryRaw<string>("""
            SELECT data_type || '|' || is_nullable || '|' || COALESCE(column_default, 'sem-default') AS "Value"
            FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = 'usuarios' AND column_name = 'SessoesValidasDesde'
            """).ToListAsync();

        colunas.Should().ContainSingle().Which.Should().Be("timestamp with time zone|YES|sem-default",
            "migração aditiva: coluna nula, sem default, então quem já existe vale como 'nunca revogou'");
        (await new UsuarioRepository(db).ObterSessaoAsync(ana.Id))!.SessoesValidasDesde.Should().BeNull();
    }

    private async Task<Usuario> SemearUsuarioAsync(string nome, bool ativo = true)
    {
        var usuario = Usuario.Criar(nome, $"{nome}-{Guid.NewGuid():N}@casadababa.com", "hash");
        usuario.Ativo = ativo;
        await using var db = fixture.CreateDbContext();
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }

    private static RefreshToken Token(Usuario usuario, DateTime expiraEm) =>
        RefreshToken.Criar(usuario.Id, $"hash-{Guid.NewGuid():N}", expiraEm, null, null);

    // Contexto como a API na hora de validar o token: papel sujeito a RLS (rls_test_client), interceptor de
    // tenant e usuário ainda não autenticado (CurrentTenantId = Guid.Empty), sem bypass.
    private EasyStockDbContext CriarContextoSemTenant()
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.RlsClientConnectionString)
            .AddInterceptors(new SetTenantOnConnectionInterceptor())
            .Options;
        return new EasyStockDbContext(options, Substitute.For<ICurrentUserAccessor>());
    }
}
