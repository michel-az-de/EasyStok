using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.UseCases.AnonimizarMeusDados;
using EasyStock.Application.UseCases.AtribuirPerfilUsuario;
using EasyStock.Application.UseCases.AtualizarUsuario;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.CriarUsuario;
using EasyStock.Application.UseCases.DesativarUsuario;
using EasyStock.Application.UseCases.MarcarDona;
using EasyStock.Application.UseCases.ReenviarConvite;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Services;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Auth;

/// <summary>
/// #2070 (M1): a empresa nunca fica sem dona ativa, em Postgres real e com RLS (login NOSUPERUSER, o interceptor de
/// tenant de produção). Cada corrida usa duas conexões e uma barreira logo depois da contagem de donas: sem nada que as
/// serialize, as duas leem o mesmo estado antes de qualquer uma escrever. Com a trava da hierarquia
/// (<c>pg_advisory_xact_lock</c>), a segunda espera a primeira comitar, relê e recusa. Cada corrida roda também sem a
/// trava e mostra o invariante quebrando: é a prova de que o verde vem da trava, não da sorte do agendador. Os caminhos
/// do suporte que escrevem dentro da transação (cadastrar a dona, trocar o e-mail dela, reenviar o convite) rodam aqui
/// também, porque os testes HTTP usam o superuser e não passam pela RLS.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class HierarquiaConcorrenciaTests(PostgreSqlDatabaseFixture fixture)
{
    /// <summary>Quanto a primeira corrida espera a outra na barreira. Com a trava, a outra não chega: espera inteira e segue.</summary>
    private static readonly TimeSpan EsperaNaBarreira = TimeSpan.FromSeconds(3);

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Duas_donas_se_desmarcando(bool comTrava)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var c = await SemearAsync();
        var barreira = new Barreira(2, EsperaNaBarreira);

        var resultados = await Task.WhenAll(
            Task.Run(() => Capturar(() => MarcarAsync(c, c.DonaA, c.DonaB, dona: false, barreira, comTrava))),
            Task.Run(() => Capturar(() => MarcarAsync(c, c.DonaB, c.DonaA, dona: false, barreira, comTrava))));

        if (comTrava)
        {
            // Quem perdeu, relida depois da trava, já é Administração: a outra dona está fora do alcance dela (403), e a
            // resposta não manda marcar outra dona nem conta quantas existem.
            UmaPassaEAOutraRecebe(resultados, CodigosDaHierarquia.Hierarquia);
            (await DonasAtivasAsync(c.EmpresaId)).Should().Be(1);
        }
        else
        {
            resultados.Should().OnlyContain(r => r == null, "sem a trava, as duas leem duas donas e as duas escrevem");
            (await DonasAtivasAsync(c.EmpresaId)).Should().Be(0, "é exatamente o furo M1 que a trava fecha");
        }
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rebaixar_contra_marcar(bool comTrava)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var c = await SemearAsync();
        var barreira = new Barreira(2, EsperaNaBarreira);

        var resultados = await Task.WhenAll(
            Task.Run(() => Capturar(() => RebaixarAsync(c, c.DonaA, c.Gestora, barreira, comTrava))),
            Task.Run(() => Capturar(() => MarcarAsync(c, c.DonaA, c.Gestora, dona: true, barreira, comTrava))));

        var estado = await EstadoAsync(c.EmpresaId, c.Gestora.Id);
        var donas = await DonasAtivasAsync(c.EmpresaId);
        if (comTrava)
        {
            // Rebaixar primeiro: marcar quem já é Gerência é DONA_NAO_ADMIN, e as donas seguem A e B. Marcar primeiro:
            // rebaixar a dona é DONA_PROTEGIDA, e a gestora é a terceira dona, na Administração.
            UmaPassaEAOutraRecebe(resultados, CodigosDaHierarquia.DonaNaoAdmin, CodigosDaHierarquia.DonaProtegida);
            var rebaixarVenceu = ((HierarquiaRecusadaException)resultados.Single(r => r is not null)!).Codigo == CodigosDaHierarquia.DonaNaoAdmin;
            estado.Should().Be(rebaixarVenceu ? (false, NivelAcesso.Gerente) : (true, NivelAcesso.Admin));
            donas.Should().Be(rebaixarVenceu ? 2 : 3);
        }
        else
        {
            resultados.Should().OnlyContain(r => r == null, "sem a trava, as duas leem a gestora na Administração sem a marca");
            estado.Should().Be((true, NivelAcesso.Gerente), "sem a trava fica uma dona de Gerência: o invariante quebra");
            donas.Should().Be(2, "a marca na Gerência não conta como dona ativa");
        }
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Desativar_uma_dona_e_anonimizar_a_outra(bool comTrava)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var c = await SemearAsync();
        var barreira = new Barreira(2, EsperaNaBarreira);

        var resultados = await Task.WhenAll(
            Task.Run(() => Capturar(() => DesativarAsync(c, c.Suporte, c.DonaA, barreira, comTrava))),
            Task.Run(() => Capturar(() => AnonimizarAsync(c, c.DonaB, barreira, comTrava))));

        if (comTrava)
        {
            UmaPassaEAOutraRecebe(resultados, CodigosDaHierarquia.UltimaDona);
            (await DonasAtivasAsync(c.EmpresaId)).Should().Be(1);
        }
        else
        {
            resultados.Should().OnlyContain(r => r == null, "sem a trava, as duas contam duas donas ativas");
            (await DonasAtivasAsync(c.EmpresaId)).Should().Be(0, "uma desativada e a outra anonimizada: a empresa fica sem dona");
        }
    }

    // ── os caminhos do suporte que escrevem na transação, sob RLS ─────────────────────────────

    [SkippableFact]
    public async Task Suporte_cadastra_a_dona_sob_RLS_com_convite_e_auditoria()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var c = await SemearAsync();
        var email = $"titular-{Guid.NewGuid():N}@exemplo.test";

        await using (var db = ContextoComRls(c.EmpresaId))
        {
            var (atual, vinculos, hierarquia) = Montar(db, c, c.Suporte, Sozinho(), comTrava: true);
            await new CriarUsuarioUseCase(new UsuarioRepository(db), new AssinaturaEmpresaRepository(db), vinculos,
                    new UsuarioPerfilRepository(db), new PerfilRepository(db), Substitute.For<IConsentimentoRepository>(), Convites(db),
                    hierarquia, atual, new AuditLogRepository(db), db, Substitute.For<IPasswordHasher>(),
                    NullLogger<CriarUsuarioUseCase>.Instance)
                .ExecuteAsync(new CriarUsuarioCommand(c.EmpresaId, "Titular", email, null, c.PerfilAdministracao, null, Dona: true));
        }

        await using var conferencia = fixture.CreateDbContext();
        var titular = await conferencia.Usuarios.IgnoreQueryFilters().SingleAsync(u => u.Email == email);
        (await conferencia.UsuariosEmpresas.IgnoreQueryFilters().SingleAsync(v => v.UsuarioId == titular.Id))
            .Should().BeEquivalentTo(new { EmpresaId = c.EmpresaId, Ativo = true, EhDona = true });
        (await conferencia.ResetTokens.Where(t => t.UsuarioId == titular.Id).Select(t => new { t.Finalidade, t.Canal }).ToListAsync())
            .Should().BeEquivalentTo([new { Finalidade = FinalidadeResetToken.Convite, Canal = (string?)"Email" }]);
        (await conferencia.AuditLogs.IgnoreQueryFilters().Where(a => a.UsuarioId == titular.Id && a.EmpresaId == c.EmpresaId)
                .Select(a => a.Acao).ToListAsync())
            .Should().BeEquivalentTo([RastroDeUsuarios.Convidado, RastroDeUsuarios.DonaMarcada]);
        (await DonasAtivasAsync(c.EmpresaId)).Should().Be(3, "a convidada pendente conta");
    }

    [SkippableFact]
    public async Task Suporte_troca_sob_RLS_o_email_da_dona_so_com_motivo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var c = await SemearAsync();
        var novo = $"nova-{Guid.NewGuid():N}@exemplo.test";

        await using (var db = ContextoComRls(c.EmpresaId))
        {
            var semMotivo = () => Atualizar(db, c).ExecuteAsync(new AtualizarUsuarioCommand(c.DonaA.Id, "dona-a", novo));
            (await semMotivo.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be("MOTIVO_OBRIGATORIO");
        }

        AtualizarUsuarioResult resultado;
        await using (var db = ContextoComRls(c.EmpresaId))
            resultado = await Atualizar(db, c).ExecuteAsync(
                new AtualizarUsuarioCommand(c.DonaA.Id, "dona-a", novo, Motivo: "titular perdeu a caixa antiga"));

        resultado.Should().Be(new AtualizarUsuarioResult(true, "titular perdeu a caixa antiga"));
        await using var conferencia = fixture.CreateDbContext();
        (await conferencia.Usuarios.IgnoreQueryFilters().SingleAsync(u => u.Id == c.DonaA.Id))
            .Should().BeEquivalentTo(new { Email = c.DonaA.Email, EmailPendente = novo });
        (await conferencia.EmailConfirmationTokens.CountAsync(t => t.UsuarioId == c.DonaA.Id)).Should().Be(1);
        (await conferencia.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.UsuarioId == c.DonaA.Id && a.Acao == "admin-troca-email-solicitada"))
            .Should().Be(1);
    }

    [SkippableFact]
    public async Task Suporte_troca_sob_RLS_o_email_da_dona_convidada_e_reenvia_o_convite()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var c = await SemearAsync();
        var convidada = await SemearConvidadaAsync(c, dona: true);
        var novo = $"titular-{Guid.NewGuid():N}@exemplo.test";

        await using (var db = ContextoComRls(c.EmpresaId))
            await Atualizar(db, c).ExecuteAsync(
                new AtualizarUsuarioCommand(convidada, "Titular", novo, Motivo: "endereço digitado errado no cadastro"));
        await using (var db = ContextoComRls(c.EmpresaId))
        {
            var (atual, _, hierarquia) = Montar(db, c, c.Suporte, Sozinho(), comTrava: true);
            await new ReenviarConviteUseCase(new UsuarioRepository(db), atual, Convites(db), hierarquia, Substitute.For<ITenantContextAccessor>(),
                    new AuditLogRepository(db), db, NullLogger<ReenviarConviteUseCase>.Instance)
                .ExecuteAsync(new ReenviarConviteCommand(convidada));
        }

        await using var conferencia = fixture.CreateDbContext();
        (await conferencia.Usuarios.IgnoreQueryFilters().SingleAsync(u => u.Id == convidada)).Email.Should().Be(novo,
            "o convidado nunca teve acesso: a troca é imediata");
        var convites = await conferencia.ResetTokens.Where(t => t.UsuarioId == convidada && t.Finalidade == FinalidadeResetToken.Convite)
            .Select(t => new { t.Usado, t.Canal }).ToListAsync();
        convites.Should().HaveCount(2).And.OnlyContain(t => t.Canal == "Email", "a dona só recebe por e-mail");
        convites.Count(t => !t.Usado).Should().Be(1, "o reenvio revoga o convite aberto e emite outro");
        (await conferencia.AuditLogs.IgnoreQueryFilters().Where(a => a.UsuarioId == convidada && a.EmpresaId == c.EmpresaId)
                .Select(a => a.Acao).ToListAsync())
            .Should().Contain("convite-reenviado");
    }

    [SkippableFact]
    public async Task Contagem_de_donas_ativas_usa_a_empresa_explicita_e_o_predicado_do_dominio()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var c = await SemearAsync();
        await using (var db = fixture.CreateDbContext())
        {
            // A gestora com a marca mas no nível Gerência não conta; a dona B desativada também não.
            var gestora = await db.UsuariosEmpresas.IgnoreQueryFilters().SingleAsync(v => v.UsuarioId == c.Gestora.Id);
            gestora.EhDona = true;
            var perfil = await db.UsuariosPerfis.IgnoreQueryFilters().SingleAsync(p => p.UsuarioId == c.Gestora.Id);
            perfil.PerfilId = c.PerfilGerente;
            var donaB = await db.UsuariosEmpresas.IgnoreQueryFilters().SingleAsync(v => v.UsuarioId == c.DonaB.Id);
            donaB.Ativo = false;
            await db.SaveChangesAsync();
        }
        var outra = await SemearAsync();

        await using var rls = ContextoComRls(c.EmpresaId);
        var repositorio = new UsuarioEmpresaRepository(rls);

        (await repositorio.ContarDonasAtivasAsync(c.EmpresaId)).Should().Be(1);
        (await repositorio.ContarDonasAtivasAsync(outra.EmpresaId)).Should().Be(0, "a RLS da sessão não enxerga a outra empresa");
    }

    [SkippableFact]
    public async Task Travar_a_hierarquia_fora_de_transacao_falha_alto()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await using var db = fixture.CreateDbContext();

        var travar = () => new UsuarioEmpresaRepository(db).TravarHierarquiaAsync(Guid.NewGuid());

        await travar.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exige transação aberta*");
    }

    [SkippableFact]
    public async Task Convite_aberto_e_so_o_nao_usado_e_nao_vencido()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var c = await SemearAsync();
        var agora = DateTime.UtcNow;
        await using (var db = fixture.CreateDbContext())
        {
            db.ResetTokens.Add(ResetToken.Criar(c.DonaA.Id, $"vencido-{Guid.NewGuid():N}", agora.AddMinutes(-1), null, null,
                FinalidadeResetToken.Convite, canal: "Email"));
            var usado = ResetToken.Criar(c.DonaB.Id, $"usado-{Guid.NewGuid():N}", agora.AddHours(1), null, null,
                FinalidadeResetToken.Convite, canal: "Email");
            usado.Usado = true;
            db.ResetTokens.Add(usado);
            db.ResetTokens.Add(ResetToken.Criar(c.Gestora.Id, $"aberto-{Guid.NewGuid():N}", agora.AddHours(1), null, null,
                FinalidadeResetToken.Convite, canal: "Email"));
            db.ResetTokens.Add(ResetToken.Criar(c.Suporte.Id, $"reset-{Guid.NewGuid():N}", agora.AddHours(1), null, null,
                FinalidadeResetToken.Reset));
            await db.SaveChangesAsync();
        }

        await using var leitura = fixture.CreateDbContext();
        var tokens = new ResetTokenRepository(leitura);

        (await tokens.TemConviteAbertoAsync(c.DonaA.Id, agora)).Should().BeFalse("vencido");
        (await tokens.TemConviteAbertoAsync(c.DonaB.Id, agora)).Should().BeFalse("revogado ou usado");
        (await tokens.TemConviteAbertoAsync(c.Gestora.Id, agora)).Should().BeTrue();
        (await tokens.TemConviteAbertoAsync(c.Suporte.Id, agora)).Should().BeFalse("redefinição de senha não é convite");
    }

    // ── as operações, cada uma na sua conexão com RLS ─────────────────────────────────────────

    private AtualizarUsuarioUseCase Atualizar(EasyStockDbContext db, Cenario c)
    {
        var (atual, _, hierarquia) = Montar(db, c, c.Suporte, Sozinho(), comTrava: true);
        var usuarios = new UsuarioRepository(db);
        var contato = new TrocaDeContatoService(usuarios, new EmailConfirmationTokenRepository(db), Substitute.For<INotificadorService>(),
            Substitute.For<IEmpresaPadraoResolver>(), Substitute.For<ITenantContextAccessor>(), atual, Substitute.For<IPasswordHasher>(),
            db, new ConfigurationBuilder().Build(), TimeProvider.System, NullLogger<TrocaDeContatoService>.Instance);
        return new AtualizarUsuarioUseCase(usuarios, atual, db, contato, Convites(db), hierarquia, new AuditLogRepository(db),
            NullLogger<AtualizarUsuarioUseCase>.Instance);
    }

    /// <summary>Convites com o link configurado (o cadastro e o reenvio exigem a base do link) e o motor de notificação falso.</summary>
    private static ConvitesDeAcesso Convites(EasyStockDbContext db) => new(
        new ResetTokenRepository(db), Substitute.For<INotificadorService>(), Substitute.For<IConsentimentoRepository>(),
        Substitute.For<IEmpresaRepository>(),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:LinkConvite"] = "https://app.exemplo.test/#/convite?t={0}",
        }).Build(),
        TimeProvider.System, NullLogger<ConvitesDeAcesso>.Instance);

    /// <summary>Barreira de um participante só: quem não corre contra ninguém passa direto.</summary>
    private static Barreira Sozinho() => new(1, TimeSpan.Zero);

    private async Task MarcarAsync(Cenario c, Usuario ator, Usuario alvo, bool dona, Barreira barreira, bool comTrava)
    {
        await using var db = ContextoComRls(c.EmpresaId);
        var (atual, vinculos, hierarquia) = Montar(db, c, ator, barreira, comTrava);
        await new MarcarDonaUseCase(new UsuarioRepository(db), vinculos, hierarquia, atual, new AuditLogRepository(db), db,
                NullLogger<MarcarDonaUseCase>.Instance)
            .ExecuteAsync(new MarcarDonaCommand(alvo.Id, c.EmpresaId, dona));
    }

    private async Task RebaixarAsync(Cenario c, Usuario ator, Usuario alvo, Barreira barreira, bool comTrava)
    {
        await using var db = ContextoComRls(c.EmpresaId);
        var (atual, vinculos, hierarquia) = Montar(db, c, ator, barreira, comTrava);
        var usuarios = new UsuarioRepository(db);
        await new AtribuirPerfilUsuarioUseCase(usuarios, new UsuarioPerfilRepository(db), vinculos, new PerfilRepository(db),
                Revogador(db), hierarquia, atual, new AuditLogRepository(db), db, NullLogger<AtribuirPerfilUsuarioUseCase>.Instance)
            .ExecuteAsync(new AtribuirPerfilUsuarioCommand(alvo.Id, c.EmpresaId, c.PerfilGerente, null));
    }

    private async Task DesativarAsync(Cenario c, Usuario ator, Usuario alvo, Barreira barreira, bool comTrava)
    {
        await using var db = ContextoComRls(c.EmpresaId);
        var (atual, vinculos, hierarquia) = Montar(db, c, ator, barreira, comTrava);
        var convites = new ConvitesDeAcesso(new ResetTokenRepository(db), Substitute.For<INotificadorService>(),
            Substitute.For<IConsentimentoRepository>(), Substitute.For<IEmpresaRepository>(), new ConfigurationBuilder().Build(),
            TimeProvider.System, NullLogger<ConvitesDeAcesso>.Instance);
        await new DesativarUsuarioUseCase(new UsuarioRepository(db), vinculos, Revogador(db), convites, hierarquia, atual,
                new AuditLogRepository(db), db, NullLogger<DesativarUsuarioUseCase>.Instance)
            .ExecuteAsync(new DesativarUsuarioCommand(alvo.Id, c.EmpresaId));
    }

    private async Task AnonimizarAsync(Cenario c, Usuario propria, Barreira barreira, bool comTrava)
    {
        await using var db = ContextoComRls(c.EmpresaId);
        var (atual, _, hierarquia) = Montar(db, c, propria, barreira, comTrava);
        await new AnonimizarMeusDadosUseCase(new UsuarioRepository(db), new RefreshTokenRepository(db), new ResetTokenRepository(db),
                new EmailConfirmationTokenRepository(db), Substitute.For<IConsentimentoRepository>(),
                Substitute.For<IPreferenciaNotificacaoRepository>(), hierarquia, atual, db, NullLogger<AnonimizarMeusDadosUseCase>.Instance)
            .ExecuteAsync(new AnonimizarMeusDadosCommand("ANONIMIZAR"));
    }

    private static (ICurrentUserAccessor Atual, IUsuarioEmpresaRepository Vinculos, HierarquiaDeUsuarios Hierarquia) Montar(
        EasyStockDbContext db, Cenario c, Usuario ator, Barreira barreira, bool comTrava)
    {
        var atual = new UsuarioAtual(ator.Id, c.EmpresaId, ator.Id == c.Suporte.Id ? NivelAcesso.SuperAdmin : NivelAcesso.Admin);
        var vinculos = new VinculosComBarreira(new UsuarioEmpresaRepository(db), db, barreira, comTrava);
        var hierarquia = new HierarquiaDeUsuarios(new UsuarioRepository(db), vinculos, atual, new AuditLogRepository(db), db,
            NullLogger<HierarquiaDeUsuarios>.Instance);
        return (atual, vinculos, hierarquia);
    }

    private static RevogadorSessoes Revogador(EasyStockDbContext db) => new(
        new UsuarioRepository(db), new RefreshTokenRepository(db), Substitute.For<ICacheService>(), TimeProvider.System,
        NullLogger<RevogadorSessoes>.Instance);

    /// <summary>
    /// Conexão do login NOSUPERUSER com o interceptor de produção: o tenant da empresa em cada abertura e o bypass só
    /// quando o repositório pede (a leitura do suporte global, fora da transação).
    /// </summary>
    private EasyStockDbContext ContextoComRls(Guid empresaId)
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.RlsClientConnectionString)
            .AddInterceptors(new SetTenantOnConnectionInterceptor())
            .Options;
        var db = new EasyStockDbContext(options);
        db.SetMobileTenantContext(empresaId);
        return db;
    }

    // ── cenário e leituras de conferência (superuser, sem RLS) ────────────────────────────────

    private sealed record Cenario(
        Guid EmpresaId, Usuario DonaA, Usuario DonaB, Usuario Gestora, Usuario Suporte, Guid PerfilGerente, Guid PerfilAdministracao);

    private async Task<Cenario> SemearAsync()
    {
        await using var db = fixture.CreateDbContext();
        var agora = DateTime.UtcNow;
        var empresa = new Empresa
        {
            Id = Guid.NewGuid(), Nome = "Hierarquia teste", Documento = Guid.NewGuid().ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora,
        };
        db.Empresas.Add(empresa);
        var administracao = new Perfil { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Administração", Nivel = NivelAcesso.Admin, CriadoEm = agora };
        var gerencia = new Perfil { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Gerência", Nivel = NivelAcesso.Gerente, CriadoEm = agora };
        db.Perfis.AddRange(administracao, gerencia);
        var perfilDoSuporte = await db.Perfis.IgnoreQueryFilters().FirstOrDefaultAsync(Perfil.SuporteGlobal);
        if (perfilDoSuporte is null)
        {
            perfilDoSuporte = new Perfil { Id = Guid.NewGuid(), Nome = "SuperAdmin", Nivel = NivelAcesso.SuperAdmin, CriadoEm = agora };
            db.Perfis.Add(perfilDoSuporte);
        }

        Usuario Pessoa(string nome, bool dona)
        {
            var usuario = Usuario.Criar(nome, $"{nome}-{Guid.NewGuid():N}@exemplo.test", "hash-de-teste");
            usuario.EmailConfirmado = true;
            db.Usuarios.Add(usuario);
            db.UsuariosEmpresas.Add(new UsuarioEmpresa
            {
                Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = empresa.Id, Ativo = true, EhDona = dona, CriadoEm = agora,
            });
            db.UsuariosPerfis.Add(new UsuarioPerfil
            {
                Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = empresa.Id, PerfilId = administracao.Id, AtribuidoEm = agora,
            });
            return usuario;
        }

        var donaA = Pessoa("dona-a", dona: true);
        var donaB = Pessoa("dona-b", dona: true);
        var gestora = Pessoa("gestora", dona: false);
        var suporte = Usuario.Criar("Suporte", $"suporte-{Guid.NewGuid():N}@exemplo.test", "hash-de-teste");
        suporte.EmailConfirmado = true;
        db.Usuarios.Add(suporte);
        db.UsuariosPerfis.Add(new UsuarioPerfil
        {
            Id = Guid.NewGuid(), UsuarioId = suporte.Id, EmpresaId = Guid.Empty, PerfilId = perfilDoSuporte.Id, AtribuidoEm = agora,
        });
        await db.SaveChangesAsync();
        return new Cenario(empresa.Id, donaA, donaB, gestora, suporte, gerencia.Id, administracao.Id);
    }

    /// <summary>Convidada pendente da Administração, com um convite aberto por e-mail, como o cadastro a deixaria.</summary>
    private async Task<Guid> SemearConvidadaAsync(Cenario c, bool dona)
    {
        await using var db = fixture.CreateDbContext();
        var agora = DateTime.UtcNow;
        var convidada = Usuario.CriarConvidado("Convidada", $"convidada-{Guid.NewGuid():N}@exemplo.test");
        db.Usuarios.Add(convidada);
        db.UsuariosEmpresas.Add(new UsuarioEmpresa
        {
            Id = Guid.NewGuid(), UsuarioId = convidada.Id, EmpresaId = c.EmpresaId, Ativo = true, EhDona = dona, CriadoEm = agora,
        });
        db.UsuariosPerfis.Add(new UsuarioPerfil
        {
            Id = Guid.NewGuid(), UsuarioId = convidada.Id, EmpresaId = c.EmpresaId, PerfilId = c.PerfilAdministracao, AtribuidoEm = agora,
        });
        await db.SaveChangesAsync();
        return convidada.Id;
    }

    private async Task<int> DonasAtivasAsync(Guid empresaId)
    {
        await using var db = fixture.CreateDbContext();
        return await new UsuarioEmpresaRepository(db).ContarDonasAtivasAsync(empresaId);
    }

    private async Task<(bool EhDona, NivelAcesso Nivel)> EstadoAsync(Guid empresaId, Guid usuarioId)
    {
        await using var db = fixture.CreateDbContext();
        var ehDona = await db.UsuariosEmpresas.IgnoreQueryFilters()
            .Where(v => v.EmpresaId == empresaId && v.UsuarioId == usuarioId).Select(v => v.EhDona).SingleAsync();
        var nivel = await db.UsuariosPerfis.IgnoreQueryFilters()
            .Where(p => p.EmpresaId == empresaId && p.UsuarioId == usuarioId).Select(p => p.Perfil!.Nivel).SingleAsync();
        return (ehDona, nivel);
    }

    private static void UmaPassaEAOutraRecebe(Exception?[] resultados, params string[] codigos)
    {
        resultados.Count(r => r is null).Should().Be(1, "exatamente uma das duas passa: {0}", string.Join(" | ", resultados.Select(r => r?.Message)));
        var recusa = resultados.Single(r => r is not null);
        recusa.Should().BeOfType<HierarquiaRecusadaException>();
        ((HierarquiaRecusadaException)recusa!).Codigo.Should().BeOneOf(codigos);
    }

    private static async Task<Exception?> Capturar(Func<Task> acao)
    {
        try
        {
            await acao();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    // ── barreira e decorador da contagem ──────────────────────────────────────────────────────

    /// <summary>
    /// Encontro de uso único: cada corrida espera a outra chegar, até <c>espera</c>. Sem trava as duas chegam e seguem
    /// juntas; com a trava a segunda está presa no lock, a primeira esgota a espera, escreve e comita.
    /// </summary>
    private sealed class Barreira(int participantes, TimeSpan espera)
    {
        private int _chegaram;
        private readonly TaskCompletionSource _todos = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ChegarAsync()
        {
            if (Interlocked.Increment(ref _chegaram) >= participantes) _todos.TrySetResult();
            await Task.WhenAny(_todos.Task, Task.Delay(espera));
        }
    }

    /// <summary>
    /// O repositório real com a barreira logo depois da contagem de donas, que vem depois da trava e da releitura.
    /// Sem trava, liga só o tenant da transação (o que a trava real também faz) e não pega o lock.
    /// </summary>
    private sealed class VinculosComBarreira(
        UsuarioEmpresaRepository real, EasyStockDbContext db, Barreira barreira, bool comTrava) : IUsuarioEmpresaRepository
    {
        public Task AddAsync(UsuarioEmpresa usuarioEmpresa) => real.AddAsync(usuarioEmpresa);
        public Task<UsuarioEmpresa?> GetByUsuarioEEmpresaAsync(Guid usuarioId, Guid empresaId) => real.GetByUsuarioEEmpresaAsync(usuarioId, empresaId);
        public Task<IReadOnlyList<UsuarioEmpresa>> GetByUsuarioIdAsync(Guid usuarioId) => real.GetByUsuarioIdAsync(usuarioId);
        public Task UpdateAsync(UsuarioEmpresa usuarioEmpresa) => real.UpdateAsync(usuarioEmpresa);
        public Task<int> DefinirDonaAsync(Guid usuarioId, Guid empresaId, bool dona, CancellationToken ct = default) =>
            real.DefinirDonaAsync(usuarioId, empresaId, dona, ct);

        public Task TravarHierarquiaAsync(Guid empresaId, CancellationToken ct = default) =>
            comTrava
                ? real.TravarHierarquiaAsync(empresaId, ct)
                : db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {empresaId.ToString()}, true)", ct);

        public async Task<int> ContarDonasAtivasAsync(Guid empresaId, CancellationToken ct = default)
        {
            var donas = await real.ContarDonasAtivasAsync(empresaId, ct);
            await barreira.ChegarAsync();
            return donas;
        }
    }

    private sealed class UsuarioAtual(Guid usuarioId, Guid empresaId, NivelAcesso nivel) : ICurrentUserAccessor
    {
        public Guid EmpresaId => empresaId;
        public bool IsAuthenticated => true;
        public Guid UsuarioId => usuarioId;
        public NivelAcesso Nivel => nivel;
        public bool TemPermissao(Permissao permissao) => true;
    }
}
