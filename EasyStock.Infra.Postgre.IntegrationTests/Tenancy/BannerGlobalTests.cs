using EasyStock.Domain.Entities.Banners;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Tenancy;

/// <summary>
/// Persistência dos banners globais (#869). Prova que <c>banners</c> e
/// <c>banner_confirmacoes</c> NÃO são pegos pelo RLS (não têm EmpresaId) e que a
/// confirmação vale independente da empresa ativa da sessão — a correção do P0 que,
/// na v1 (confirmação com EmpresaId + RLS), fazia o banner obrigatório reaparecer
/// para sempre ao trocar de empresa.
/// </summary>
public class BannerGlobalTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    // ── #1 — banner global é legível sem contexto de tenant ─────────────────
    [SkippableFact]
    public async Task Banner_global_e_legivel_sem_contexto_de_tenant()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var banner = await SeedBannerAsync();

        // Login NOSUPERUSER, SEM SET app.empresa_id: 'produtos' retornaria 0 (fail-closed),
        // mas 'banners' não tem policy RLS (não tem EmpresaId) → é legível por todos.
        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();

        var visivel = await ctx.Banners.AsNoTracking().AnyAsync(b => b.Id == banner.Id);
        visivel.Should().BeTrue("banner é broadcast global sem EmpresaId, isento de RLS");
    }

    // ── #2 — P0: confirmação persiste ao trocar de empresa ativa ────────────
    [SkippableFact]
    public async Task Confirmacao_persiste_ao_trocar_de_empresa_ativa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var usuario = Guid.NewGuid();
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        var banner = await SeedBannerAsync(b => { b.Ativo = true; b.ExigeConfirmacao = true; });

        await RegistrarAsync(banner.Id, usuario, BannerInteracaoTipo.Confirmado);

        // Consulta como cliente RLS sob a empresa A: o banner não deve retornar.
        (await AtivosNaoConfirmadosSobEmpresaAsync(usuario, empresaA))
            .Should().NotContain(b => b.Id == banner.Id);

        // Troca a empresa ativa para B (novo login/token): AINDA não deve retornar —
        // a confirmação é global (keyed por UsuarioId), não some ao trocar de empresa.
        (await AtivosNaoConfirmadosSobEmpresaAsync(usuario, empresaB))
            .Should().NotContain(b => b.Id == banner.Id, "confirmação é tenant-independente (fix P0)");
    }

    // ── #3 — obrigatório só some com Confirmado ─────────────────────────────
    [SkippableFact]
    public async Task Obrigatorio_so_some_com_confirmado_nao_com_visto()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var usuario = Guid.NewGuid();
        var banner = await SeedBannerAsync(b => { b.Ativo = true; b.ExigeConfirmacao = true; });

        await RegistrarAsync(banner.Id, usuario, BannerInteracaoTipo.Visto);
        (await AtivosNaoConfirmadosAsync(usuario))
            .Should().Contain(b => b.Id == banner.Id, "Visto não dispensa banner obrigatório");

        await RegistrarAsync(banner.Id, usuario, BannerInteracaoTipo.Confirmado);
        (await AtivosNaoConfirmadosAsync(usuario))
            .Should().NotContain(b => b.Id == banner.Id, "Confirmado dispensa o obrigatório");
    }

    // ── #4 — não-obrigatório some com qualquer interação ────────────────────
    [SkippableFact]
    public async Task Nao_obrigatorio_some_com_visto()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var usuario = Guid.NewGuid();
        var banner = await SeedBannerAsync(b => { b.Ativo = true; b.ExigeConfirmacao = false; });

        (await AtivosNaoConfirmadosAsync(usuario)).Should().Contain(b => b.Id == banner.Id);

        await RegistrarAsync(banner.Id, usuario, BannerInteracaoTipo.Visto);
        (await AtivosNaoConfirmadosAsync(usuario)).Should().NotContain(b => b.Id == banner.Id);
    }

    // ── #5 — fora da janela não entra na consulta ───────────────────────────
    [SkippableFact]
    public async Task Banner_inativo_ou_fora_da_janela_nao_aparece()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var usuario = Guid.NewGuid();
        var agora = DateTime.UtcNow;

        var inativo = await SeedBannerAsync(b => b.Ativo = false);
        var expirado = await SeedBannerAsync(b => { b.Ativo = true; b.FimEm = agora.AddHours(-1); });
        var futuro = await SeedBannerAsync(b => { b.Ativo = true; b.InicioEm = agora.AddHours(1); });

        var ids = (await AtivosNaoConfirmadosAsync(usuario)).Select(b => b.Id).ToList();
        ids.Should().NotContain(inativo.Id);
        ids.Should().NotContain(expirado.Id);
        ids.Should().NotContain(futuro.Id);
    }

    // ── #6 — janela cruzando meia-noite BRT: grava UTC, filtra no limiar ────
    [SkippableFact]
    public async Task Janela_cruzando_meia_noite_BRT_filtra_por_utc()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var usuario = Guid.NewGuid();

        // 00:00 de 10/jul BRT (UTC-3) == 03:00 UTC. Banner começa nesse instante.
        var inicioUtc = new DateTime(2026, 7, 10, 3, 0, 0, DateTimeKind.Utc);
        var banner = await SeedBannerAsync(b => { b.Ativo = true; b.InicioEm = inicioUtc; });

        // 23:59 BRT (== 02:59 UTC): antes da meia-noite → ainda não ativo.
        (await AtivosNaoConfirmadosAsync(usuario, inicioUtc.AddMinutes(-1)))
            .Should().NotContain(b => b.Id == banner.Id);

        // 00:01 BRT (== 03:01 UTC): depois da meia-noite → ativo.
        (await AtivosNaoConfirmadosAsync(usuario, inicioUtc.AddMinutes(1)))
            .Should().Contain(b => b.Id == banner.Id);
    }

    // ── #7 — guard atômico de NotificadoEm marca uma vez ────────────────────
    [SkippableFact]
    public async Task Guard_notificado_marca_uma_vez_e_segunda_chamada_retorna_false()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var banner = await SeedBannerAsync(b => { b.Ativo = true; b.NotificarAoPublicar = true; });

        await using var ctx = fixture.CreateDbContext();
        var repo = new BannerRepository(ctx);
        var agora = DateTime.UtcNow;

        var primeira = await repo.MarcarNotificadoSeIneditoAsync(banner.Id, agora);
        var segunda = await repo.MarcarNotificadoSeIneditoAsync(banner.Id, agora.AddHours(1));

        primeira.Should().BeTrue("primeira ativação enfileira a notificação");
        segunda.Should().BeFalse("re-ativar não renotifica (guard NotificadoEm IS NULL)");

        var recarregado = await ctx.Banners.AsNoTracking().FirstAsync(b => b.Id == banner.Id);
        recarregado.NotificadoEm.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task Registrar_confirmacao_e_idempotente()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var usuario = Guid.NewGuid();
        var banner = await SeedBannerAsync(b => b.Ativo = true);

        var primeira = await RegistrarAsync(banner.Id, usuario, BannerInteracaoTipo.Confirmado);
        var segunda = await RegistrarAsync(banner.Id, usuario, BannerInteracaoTipo.Confirmado);

        primeira.Should().BeTrue();
        segunda.Should().BeFalse("segunda confirmação não duplica");

        await using var ctx = fixture.CreateDbContext();
        (await new BannerConfirmacaoRepository(ctx).ContarAsync(banner.Id)).Should().Be(1);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────
    private async Task<Banner> SeedBannerAsync(Action<BannerSeed>? ajustar = null)
    {
        var seed = new BannerSeed();
        ajustar?.Invoke(seed);

        var banner = Banner.Criar(new BannerConteudo
        {
            TituloInterno = "Banner de teste",
            Tipo = BannerTipo.Imagem,
            ImagemStorageKey = "banners/teste.png",
            ImagemUrl = "https://cdn.easystok.com/banners/teste.png",
            Ativo = seed.Ativo,
            ExigeConfirmacao = seed.ExigeConfirmacao,
            NotificarAoPublicar = seed.NotificarAoPublicar,
            InicioEm = seed.InicioEm,
            FimEm = seed.FimEm
        });

        await using var ctx = fixture.CreateDbContext();
        ctx.Banners.Add(banner);
        await ctx.SaveChangesAsync();
        return banner;
    }

    private async Task<bool> RegistrarAsync(Guid bannerId, Guid usuarioId, BannerInteracaoTipo tipo)
    {
        await using var ctx = fixture.CreateDbContext();
        var inserido = await new BannerConfirmacaoRepository(ctx).RegistrarAsync(bannerId, usuarioId, tipo);
        await ctx.SaveChangesAsync();
        return inserido;
    }

    private async Task<IReadOnlyList<Banner>> AtivosNaoConfirmadosAsync(Guid usuario, DateTime? agora = null)
    {
        await using var ctx = fixture.CreateDbContext();
        return await new BannerRepository(ctx)
            .ListarAtivosNaoConfirmadosAsync(usuario, agora ?? DateTime.UtcNow);
    }

    private async Task<IReadOnlyList<Banner>> AtivosNaoConfirmadosSobEmpresaAsync(Guid usuario, Guid empresa)
    {
        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.ExecuteSqlRawAsync("SELECT set_config('app.empresa_id', {0}, false)", empresa.ToString());
        return await new BannerRepository(ctx)
            .ListarAtivosNaoConfirmadosAsync(usuario, DateTime.UtcNow);
    }

    private sealed class BannerSeed
    {
        public bool Ativo { get; set; }
        public bool ExigeConfirmacao { get; set; }
        public bool NotificarAoPublicar { get; set; }
        public DateTime? InicioEm { get; set; }
        public DateTime? FimEm { get; set; }
    }
}
