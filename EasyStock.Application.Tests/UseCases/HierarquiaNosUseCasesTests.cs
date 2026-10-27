using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.Tests.Services.Auth;
using EasyStock.Application.UseCases.AnonimizarMeusDados;
using EasyStock.Application.UseCases.AtribuirPerfilUsuario;
using EasyStock.Application.UseCases.AtualizarUsuario;
using EasyStock.Application.UseCases.CriarUsuario;
using EasyStock.Application.UseCases.DesativarUsuario;
using EasyStock.Application.UseCases.ListarUsuarios;
using EasyStock.Application.UseCases.MarcarDona;
using EasyStock.Application.UseCases.ReativarUsuario;
using EasyStock.Application.UseCases.ReenviarConvite;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// #2070: a política de hierarquia nos casos de uso. Ator e alvo vêm do "banco" (o repositório), nunca do JWT; a trava
/// roda dentro da transação; a guarda de tenant vem antes da política; cada recusa fica na auditoria com Sucesso=false.
/// </summary>
public class HierarquiaNosUseCasesTests
{
    private readonly CenarioDeAcesso _c = new();
    private readonly ICurrentUserAccessor _atual = Substitute.For<ICurrentUserAccessor>();
    private readonly IUsuarioPerfilRepository _usuariosPerfis = Substitute.For<IUsuarioPerfilRepository>();
    private readonly IPerfilRepository _perfis = Substitute.For<IPerfilRepository>();
    private readonly IAssinaturaEmpresaRepository _assinaturas = Substitute.For<IAssinaturaEmpresaRepository>();
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly List<string> _ordem = [];
    private int _donas = 2;

    public HierarquiaNosUseCasesTests()
    {
        _atual.EmpresaId.Returns(_empresaId);
        _atual.Ip.Returns("198.51.100.9");
        _c.Vinculos.TravarHierarquiaAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _ordem.Add(_c.UnitOfWork.EmTransacao ? "trava na transação" : "trava fora da transação");
            return Task.CompletedTask;
        });
        _c.Vinculos.ContarDonasAtivasAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _ordem.Add("conta donas");
            return _donas;
        });
        _c.Vinculos.DefinirDonaAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _ordem.Add($"define dona em {(call.ArgAt<Guid>(1) == _empresaId ? "A" : "B")}");
            var vinculo = _c.Usuarios.GetByIdAsync(call.ArgAt<Guid>(0)).Result!.Empresas.Single(e => e.EmpresaId == call.ArgAt<Guid>(1));
            if (vinculo.EhDona == call.ArgAt<bool>(2)) return 0;
            vinculo.EhDona = call.ArgAt<bool>(2);
            return 1;
        });
        // A lista lê a conta inteira de cada linha em lote, com todas as empresas: aqui, o que o GetByIdAsync tem.
        _c.Usuarios.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>()).Returns(call =>
            (IReadOnlyList<Usuario>)call.Arg<IReadOnlyCollection<Guid>>()
                .Select(id => _c.Usuarios.GetByIdAsync(id).Result).OfType<Usuario>().ToList());
    }

    // ── pessoas da empresa ────────────────────────────────────────────────────────────────────

    private Usuario Pessoa(string nome, NivelAcesso? nivel, bool dona = false, bool convidada = false, bool provada = true)
    {
        var usuario = convidada
            ? Usuario.CriarConvidado(nome, $"{nome.ToLowerInvariant()}@exemplo.test")
            : Usuario.Criar(nome, $"{nome.ToLowerInvariant()}@exemplo.test", "$2a$11$hash");
        usuario.EmailConfirmado = provada && !convidada;
        var vinculo = new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = _empresaId, Ativo = true, EhDona = dona };
        usuario.Empresas.Add(vinculo);
        if (nivel is { } n)
        {
            var perfil = new Perfil { Id = Guid.NewGuid(), Nome = n.ToString(), Nivel = n, EmpresaId = _empresaId };
            usuario.Perfis.Add(new UsuarioPerfil { Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = _empresaId, PerfilId = perfil.Id, Perfil = perfil });
            _usuariosPerfis.ListarPorUsuarioEEmpresaAsync(usuario.Id, _empresaId).Returns(usuario.Perfis.ToList());
        }
        _c.Usuarios.GetByIdAsync(usuario.Id).Returns(usuario);
        _c.Usuarios.GetByEmailAsync(usuario.Email).Returns(usuario);
        _c.Vinculos.GetByUsuarioEEmpresaAsync(usuario.Id, _empresaId).Returns(vinculo);
        return usuario;
    }

    /// <summary>Dá à pessoa um vínculo, e um perfil, numa segunda empresa.</summary>
    private static Guid NaOutraEmpresa(Usuario usuario, NivelAcesso nivel, bool dona = false, Guid? empresa = null)
    {
        var outra = empresa ?? Guid.NewGuid();
        CenarioDeAcesso.DarPerfil(usuario, outra, nivel, dona);
        return outra;
    }

    private Usuario Dona(string nome = "Baba") => Pessoa(nome, NivelAcesso.Admin, dona: true);
    private Usuario Administracao(string nome = "Gestora") => Pessoa(nome, NivelAcesso.Admin);
    private Usuario Operador(string nome = "Ana") => Pessoa(nome, NivelAcesso.Operador);

    private Usuario Suporte()
    {
        var suporte = Usuario.Criar("Suporte", "suporte@exemplo.test", "hash");
        CenarioDeAcesso.TornarSuperAdmin(suporte);
        _c.Usuarios.GetByIdAsync(suporte.Id).Returns(suporte);
        return suporte;
    }

    /// <summary>O JWT diz Admin (ou SuperAdmin para o suporte); quem decide é o que o repositório devolve.</summary>
    private void Como(Usuario ator)
    {
        _atual.UsuarioId.Returns(ator.Id);
        _atual.Nivel.Returns(ator.EhSuperAdmin() ? NivelAcesso.SuperAdmin : NivelAcesso.Admin);
    }

    private Perfil PerfilDaEmpresa(NivelAcesso nivel)
    {
        var perfil = new Perfil { Id = Guid.NewGuid(), Nome = nivel.ToString(), Nivel = nivel, EmpresaId = _empresaId };
        _perfis.GetByIdAsync(perfil.Id).Returns(perfil);
        return perfil;
    }

    private HierarquiaDeUsuarios Hierarquia() => _c.Hierarquia(_atual);

    private DesativarUsuarioUseCase Desativar() => new(
        _c.Usuarios, _c.Vinculos, _c.Revogador(), _c.Convites(), Hierarquia(), _atual, _c.Auditoria, _c.UnitOfWork,
        Substitute.For<ILogger<DesativarUsuarioUseCase>>());

    private ReativarUsuarioUseCase Reativar() => new(
        _c.Usuarios, _c.Vinculos, _assinaturas, Hierarquia(), _atual, _c.Auditoria, _c.UnitOfWork,
        Substitute.For<ILogger<ReativarUsuarioUseCase>>());

    private AtribuirPerfilUsuarioUseCase Atribuir() => new(
        _c.Usuarios, _usuariosPerfis, _c.Vinculos, _perfis, _c.Revogador(), Hierarquia(), _atual, _c.Auditoria, _c.UnitOfWork,
        Substitute.For<ILogger<AtribuirPerfilUsuarioUseCase>>());

    private AtualizarUsuarioUseCase Atualizar()
    {
        var contato = new TrocaDeContatoFixture(usuarios: _c.Usuarios, unitOfWork: _c.UnitOfWork);
        return new(_c.Usuarios, _atual, _c.UnitOfWork, contato.Servico, _c.Convites(), Hierarquia(), _c.Auditoria,
            Substitute.For<ILogger<AtualizarUsuarioUseCase>>());
    }

    private ReenviarConviteUseCase Reenviar() => new(
        _c.Usuarios, _atual, _c.Convites(), Hierarquia(), _c.Tenant, _c.Auditoria, _c.UnitOfWork,
        Substitute.For<ILogger<ReenviarConviteUseCase>>());

    private MarcarDonaUseCase Marcar() => new(
        _c.Usuarios, _c.Vinculos, Hierarquia(), _atual, _c.Auditoria, _c.UnitOfWork, Substitute.For<ILogger<MarcarDonaUseCase>>());

    private CriarUsuarioUseCase Criar() => new(
        _c.Usuarios, _assinaturas, _c.Vinculos, _usuariosPerfis, _perfis, _c.Consentimentos, _c.Convites(), Hierarquia(), _atual,
        _c.Auditoria, _c.UnitOfWork, Substitute.For<IPasswordHasher>(), Substitute.For<ILogger<CriarUsuarioUseCase>>());

    private async Task<HierarquiaRecusadaException> DeveRecusar(Func<Task> acao, string codigo)
    {
        var recusa = (await acao.Should().ThrowAsync<HierarquiaRecusadaException>()).Which;
        recusa.Codigo.Should().Be(codigo);
        return recusa;
    }

    /// <summary>A recusa fica na auditoria que a dona lê: Sucesso=false e o motivo em português, nunca o código técnico.</summary>
    private void RecusaAuditada(Usuario? alvo, string codigo)
    {
        var linha = _c.Auditorias.Should().ContainSingle(a => a.Acao == RastroDeUsuarios.HierarquiaRecusada).Subject;
        linha.Sucesso.Should().BeFalse();
        linha.EmpresaId.Should().Be(_empresaId);
        if (alvo is not null) linha.UsuarioId.Should().Be(alvo.Id);
        linha.Detalhes.Should().StartWith("Pedido para ").And.NotContain(codigo);
    }

    // ── A1, A2: a Administração não alcança a dona nem outra Administração ───────────────────

    [Fact]
    public async Task A1_Administracao_nao_troca_o_email_de_outra_Administracao_com_convite_pendente()
    {
        var gestora = Administracao();
        var convidada = Pessoa("Lia", NivelAcesso.Admin, convidada: true);
        Como(gestora);

        await DeveRecusar(() => Atualizar().ExecuteAsync(new AtualizarUsuarioCommand(convidada.Id, "Lia", "invasor@exemplo.test")),
            CodigosDominio.Hierarquia);

        convidada.Email.Should().Be("lia@exemplo.test", "antes a troca era imediata e o convite novo ia para o invasor");
        _c.Eventos.Should().BeEmpty();
        _c.Tokens.Linhas.Should().BeEmpty();
        RecusaAuditada(convidada, CodigosDominio.Hierarquia);
    }

    [Fact]
    public async Task A2_Administracao_nao_rebaixa_nem_desativa_outra_Administracao_nem_a_dona()
    {
        var gestora = Administracao();
        var outra = Administracao("Rita");
        var dona = Dona();
        var gerencia = PerfilDaEmpresa(NivelAcesso.Gerente);
        Como(gestora);

        foreach (var alvo in new[] { outra, dona })
        {
            await DeveRecusar(() => Atribuir().ExecuteAsync(new AtribuirPerfilUsuarioCommand(alvo.Id, _empresaId, gerencia.Id, null)),
                CodigosDominio.Hierarquia);
            await DeveRecusar(() => Desativar().ExecuteAsync(new DesativarUsuarioCommand(alvo.Id, _empresaId)), CodigosDominio.Hierarquia);
            alvo.Empresas.Single().Ativo.Should().BeTrue();
        }

        await _usuariosPerfis.DidNotReceiveWithAnyArgs().RemoveAsync(default!);
        _c.Auditorias.Should().HaveCount(4).And.OnlyContain(a => a.Acao == RastroDeUsuarios.HierarquiaRecusada && !a.Sucesso);
    }

    [Fact]
    public async Task Administracao_cuida_da_Gerencia_para_baixo_mas_nao_da_perfil_de_nivel_Admin()
    {
        var gestora = Administracao();
        var ana = Operador();
        Como(gestora);

        await DeveRecusar(() => Atribuir().ExecuteAsync(
            new AtribuirPerfilUsuarioCommand(ana.Id, _empresaId, PerfilDaEmpresa(NivelAcesso.Admin).Id, null)), CodigosDominio.Hierarquia);
        await Atribuir().ExecuteAsync(new AtribuirPerfilUsuarioCommand(ana.Id, _empresaId, PerfilDaEmpresa(NivelAcesso.Gerente).Id, null));
        await Desativar().ExecuteAsync(new DesativarUsuarioCommand(ana.Id, _empresaId));

        ana.Empresas.Single().Ativo.Should().BeFalse();
        _c.Auditorias.Select(a => a.Acao).Should().Equal(
            RastroDeUsuarios.HierarquiaRecusada, RastroDeUsuarios.PerfilTrocado, RastroDeUsuarios.Desativado);
    }

    [Fact]
    public async Task Dona_nao_rebaixa_nem_desativa_outra_dona()
    {
        var baba = Dona();
        var socia = Dona("Socia");
        Como(baba);

        await DeveRecusar(() => Desativar().ExecuteAsync(new DesativarUsuarioCommand(socia.Id, _empresaId)), CodigosDominio.DonaProtegida);
        await DeveRecusar(() => Atribuir().ExecuteAsync(
            new AtribuirPerfilUsuarioCommand(socia.Id, _empresaId, PerfilDaEmpresa(NivelAcesso.Operador).Id, null)), CodigosDominio.DonaProtegida);

        socia.Empresas.Single().Ativo.Should().BeTrue();
    }

    [Fact]
    public async Task Ator_rebaixado_com_JWT_antigo_e_recusado_pelo_que_o_banco_diz()
    {
        var rebaixada = Pessoa("Ex", NivelAcesso.Gerente); // o JWT dela ainda diz Admin
        var ana = Operador();
        Como(rebaixada);
        _atual.Nivel.Returns(NivelAcesso.Admin);

        await DeveRecusar(() => Desativar().ExecuteAsync(new DesativarUsuarioCommand(ana.Id, _empresaId)), CodigosDominio.Hierarquia);
        ana.Empresas.Single().Ativo.Should().BeTrue();
    }

    // ── trava, releitura e guarda de tenant ───────────────────────────────────────────────────

    [Fact]
    public async Task A_trava_roda_dentro_da_transacao_antes_da_contagem()
    {
        Como(Dona());
        var ana = Operador();

        await Desativar().ExecuteAsync(new DesativarUsuarioCommand(ana.Id, _empresaId));

        _ordem.Should().Equal("trava na transação", "conta donas");
    }

    [Fact]
    public async Task A_guarda_de_tenant_roda_antes_da_politica()
    {
        Como(Dona());
        var deFora = Usuario.Criar("Ze", "ze@exemplo.test", "hash");
        _c.Usuarios.GetByIdAsync(deFora.Id).Returns(deFora);

        var acao = () => Desativar().ExecuteAsync(new DesativarUsuarioCommand(deFora.Id, _empresaId));

        await acao.Should().ThrowAsync<UseCaseValidationException>().WithMessage("Associacao do usuario com a empresa nao encontrada.");
        _ordem.Should().BeEmpty("sem vínculo com a empresa nem se chega à trava");
        _c.Auditorias.Should().BeEmpty();
    }

    // ── a última dona e o suporte ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Suporte_nao_desativa_nem_rebaixa_a_ultima_dona()
    {
        _donas = 1;
        var dona = Dona();
        Como(Suporte());

        await DeveRecusar(() => Desativar().ExecuteAsync(new DesativarUsuarioCommand(dona.Id, _empresaId)), CodigosDominio.UltimaDona);
        await DeveRecusar(() => Atribuir().ExecuteAsync(
            new AtribuirPerfilUsuarioCommand(dona.Id, _empresaId, PerfilDaEmpresa(NivelAcesso.Gerente).Id, null)), CodigosDominio.UltimaDona);

        dona.Empresas.Single().Should().BeEquivalentTo(new { Ativo = true, EhDona = true });
    }

    [Fact]
    public async Task Suporte_rebaixa_dona_que_nao_e_a_ultima_so_com_motivo_e_ela_perde_a_marca()
    {
        var dona = Dona();
        var gerencia = PerfilDaEmpresa(NivelAcesso.Gerente);
        Como(Suporte());

        // Rebaixar tira a marca, o mesmo efeito de tirar a marca: a mesma justificativa, que vai para a auditoria da plataforma.
        var semMotivo = () => Atribuir().ExecuteAsync(new AtribuirPerfilUsuarioCommand(dona.Id, _empresaId, gerencia.Id, null));
        (await semMotivo.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be("MOTIVO_OBRIGATORIO");
        dona.Empresas.Single().EhDona.Should().BeTrue();

        var resultado = await Atribuir().ExecuteAsync(
            new AtribuirPerfilUsuarioCommand(dona.Id, _empresaId, gerencia.Id, null, Motivo: " pedido formal da titular "));

        resultado.Should().Be(new AtribuirPerfilUsuarioResult(SuporteRebaixouDona: true, Motivo: "pedido formal da titular"));
        dona.Empresas.Single().EhDona.Should().BeFalse("toda dona tem nível Admin");
        _c.Auditorias.Select(a => a.Acao).Should().Equal(RastroDeUsuarios.DonaDesmarcada, RastroDeUsuarios.PerfilTrocado);
    }

    [Fact]
    public async Task Dona_rebaixando_a_Administracao_nao_vai_para_a_auditoria_da_plataforma()
    {
        var gestora = Administracao();
        Como(Dona());

        var resultado = await Atribuir().ExecuteAsync(
            new AtribuirPerfilUsuarioCommand(gestora.Id, _empresaId, PerfilDaEmpresa(NivelAcesso.Gerente).Id, null));

        resultado.Should().Be(new AtribuirPerfilUsuarioResult(SuporteRebaixouDona: false, Motivo: null));
    }

    // ── a Administração e a dona única (o caso real) ─────────────────────────────────────────

    [Fact]
    public async Task Administracao_sobre_a_unica_dona_recebe_HIERARQUIA_e_o_texto_manda_falar_com_a_dona()
    {
        _donas = 1;
        var dona = Dona();
        Como(Administracao());

        var desativar = await DeveRecusar(() => Desativar().ExecuteAsync(new DesativarUsuarioCommand(dona.Id, _empresaId)),
            CodigosDominio.Hierarquia);
        await DeveRecusar(() => Atribuir().ExecuteAsync(
            new AtribuirPerfilUsuarioCommand(dona.Id, _empresaId, PerfilDaEmpresa(NivelAcesso.Gerente).Id, null)), CodigosDominio.Hierarquia);
        await DeveRecusar(() => Marcar().ExecuteAsync(new MarcarDonaCommand(dona.Id, _empresaId, false)), CodigosDominio.Hierarquia);

        desativar.Message.Should().Be("Você não pode mudar este acesso: fale com a dona.");
        dona.Empresas.Single().Should().BeEquivalentTo(new { Ativo = true, EhDona = true });
    }

    [Fact]
    public async Task O_texto_da_recusa_diz_com_quem_falar_conforme_quem_pediu()
    {
        var socia = Dona("Socia");
        var baba = Dona();
        var gestora = Administracao();
        var rita = Administracao("Rita");

        Como(baba);
        var daDona = await DeveRecusar(() => Atualizar().ExecuteAsync(new AtualizarUsuarioCommand(socia.Id, "Socia", "nova@exemplo.test")),
            CodigosDominio.Hierarquia);
        var propriaDaDona = () => Desativar().ExecuteAsync(new DesativarUsuarioCommand(baba.Id, _empresaId));
        (await propriaDaDona.Should().ThrowAsync<UseCaseValidationException>()).Which.Message
            .Should().Be("Você não pode mudar o seu próprio acesso por aqui: fale com o suporte.");

        Como(gestora);
        var propriaDaAdministracao = () => Desativar().ExecuteAsync(new DesativarUsuarioCommand(gestora.Id, _empresaId));
        (await propriaDaAdministracao.Should().ThrowAsync<UseCaseValidationException>()).Which.Message
            .Should().Be("Você não pode mudar o seu próprio acesso por aqui: fale com a dona.");
        _donas = 0;
        var semDona = await DeveRecusar(() => Desativar().ExecuteAsync(new DesativarUsuarioCommand(rita.Id, _empresaId)),
            CodigosDominio.Hierarquia);

        daDona.Message.Should().Be("Você não pode mudar este acesso: fale com o suporte.");
        semDona.Message.Should().Be("Você não pode mudar este acesso: fale com o suporte.", "sem dona, só o suporte resolve");
    }

    [Fact]
    public async Task O_rastro_da_recusa_diz_o_motivo_em_portugues_sem_o_codigo()
    {
        var dona = Dona();
        Como(Administracao());

        await DeveRecusar(() => Desativar().ExecuteAsync(new DesativarUsuarioCommand(dona.Id, _empresaId)), CodigosDominio.Hierarquia);

        _c.Auditorias.Should().ContainSingle().Which.Detalhes.Should()
            .Be("Pedido para desativar o acesso recusado: sem autoridade sobre este acesso; feito por Gestora");
    }

    // ── nome, e-mail e convite são da conta: valem em toda empresa da pessoa ─────────────────

    [Fact]
    public async Task Suporte_com_sessao_noutra_empresa_troca_o_email_da_dona_so_com_motivo()
    {
        var dona = Pessoa("Titular", nivel: null);
        dona.Empresas.Clear();
        NaOutraEmpresa(dona, NivelAcesso.Admin, dona: true); // dona só da outra empresa; a sessão do suporte é desta
        Como(Suporte());

        var semMotivo = () => Atualizar().ExecuteAsync(new AtualizarUsuarioCommand(dona.Id, "Titular", "nova@exemplo.test"));
        (await semMotivo.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be("MOTIVO_OBRIGATORIO");
        dona.EmailPendente.Should().BeNull();

        var resultado = await Atualizar().ExecuteAsync(
            new AtualizarUsuarioCommand(dona.Id, "Titular", "nova@exemplo.test", Motivo: "titular perdeu a caixa antiga"));

        resultado.Should().Be(new AtualizarUsuarioResult(true, "titular perdeu a caixa antiga"));
    }

    [Fact]
    public async Task Administracao_nao_edita_o_nome_de_quem_e_dona_ou_Administracao_noutra_empresa()
    {
        var donaLa = Operador("Ana");
        NaOutraEmpresa(donaLa, NivelAcesso.Admin, dona: true);
        var administracaoLa = Operador("Rui");
        NaOutraEmpresa(administracaoLa, NivelAcesso.Admin);
        Como(Administracao());

        foreach (var alvo in new[] { donaLa, administracaoLa })
        {
            await DeveRecusar(() => Atualizar().ExecuteAsync(new AtualizarUsuarioCommand(alvo.Id, "Outro nome", null)),
                CodigosDominio.Hierarquia);
            alvo.Nome.Should().NotBe("Outro nome");
        }
    }

    [Fact]
    public async Task Administracao_nao_troca_na_hora_o_email_de_convidado_que_tambem_e_de_outra_empresa()
    {
        var convidada = Pessoa("Lia", NivelAcesso.Operador, convidada: true);
        NaOutraEmpresa(convidada, NivelAcesso.Operador);
        Como(Administracao());

        var acao = () => Atualizar().ExecuteAsync(new AtualizarUsuarioCommand(convidada.Id, "Lia", "invasor@exemplo.test"));

        await acao.Should().ThrowAsync<UsuarioNaoAutorizadoException>().WithMessage("*próprio usuário ou ao suporte*");
        convidada.Email.Should().Be("lia@exemplo.test", "o convite novo iria para o endereço trocado, valendo também na outra empresa");
        _c.Tokens.Linhas.Should().BeEmpty();
    }

    [Fact]
    public async Task Convite_de_quem_e_dona_noutra_empresa_vai_so_por_email()
    {
        var convidada = Pessoa("Nova", NivelAcesso.Operador, convidada: true);
        NaOutraEmpresa(convidada, NivelAcesso.Admin, dona: true);
        convidada.DefinirTelefone(Domain.ValueObjects.TelefoneE164.From("+5511999991234"));
        _c.DarOptInDeWhatsApp(convidada);
        Como(Dona());

        await Reenviar().ExecuteAsync(new ReenviarConviteCommand(convidada.Id));

        _c.Tokens.Linhas.Should().ContainSingle().Which.Canal.Should().Be("Email");
    }

    [Fact]
    public async Task O_mesmo_email_com_outra_caixa_de_letra_e_so_editar_o_nome()
    {
        var socia = Dona("Socia");
        Como(Dona());

        await Atualizar().ExecuteAsync(new AtualizarUsuarioCommand(socia.Id, "Socia Silva", "  Socia@Exemplo.TEST "));

        socia.Nome.Should().Be("Socia Silva");
        socia.EmailPendente.Should().BeNull();
        _c.Auditorias.Should().BeEmpty("não houve recusa nem troca de e-mail");
    }

    // ── B3: atribuir exige vínculo ativo ──────────────────────────────────────────────────────

    [Fact]
    public async Task B3_atribuir_perfil_a_quem_esta_desativado_e_recusado()
    {
        Como(Dona());
        var ana = Operador();
        ana.Empresas.Single().Ativo = false;

        var acao = () => Atribuir().ExecuteAsync(new AtribuirPerfilUsuarioCommand(ana.Id, _empresaId, PerfilDaEmpresa(NivelAcesso.Gerente).Id, null));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be("USUARIO_DESATIVADO");
        await _usuariosPerfis.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    // ── reativar e reenviar ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Administracao_nao_reativa_outra_Administracao()
    {
        var outra = Administracao("Rita");
        outra.Empresas.Single().Ativo = false;
        Como(Administracao());

        await DeveRecusar(() => Reativar().ExecuteAsync(new ReativarUsuarioCommand(outra.Id, _empresaId)), CodigosDominio.Hierarquia);
        outra.Empresas.Single().Ativo.Should().BeFalse();
    }

    [Fact]
    public async Task Dona_nao_reativa_outra_dona_que_o_suporte_desativou()
    {
        var socia = Dona("Socia");
        socia.Empresas.Single().Ativo = false;
        Como(Dona());

        await DeveRecusar(() => Reativar().ExecuteAsync(new ReativarUsuarioCommand(socia.Id, _empresaId)), CodigosDominio.DonaProtegida);
        socia.Empresas.Single().Ativo.Should().BeFalse();
    }

    [Fact]
    public async Task Administracao_nao_reenvia_o_convite_da_dona()
    {
        var convidada = Pessoa("Nova", NivelAcesso.Admin, dona: true, convidada: true);
        Como(Administracao());

        await DeveRecusar(() => Reenviar().ExecuteAsync(new ReenviarConviteCommand(convidada.Id)), CodigosDominio.Hierarquia);
        _c.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task O_convite_da_dona_vai_so_por_email_mesmo_com_atestado_de_WhatsApp()
    {
        var convidada = Pessoa("Nova", NivelAcesso.Admin, dona: true, convidada: true);
        convidada.DefinirTelefone(Domain.ValueObjects.TelefoneE164.From("+5511999991234"));
        _c.DarOptInDeWhatsApp(convidada);
        Como(Dona());

        await Reenviar().ExecuteAsync(new ReenviarConviteCommand(convidada.Id));

        _c.Tokens.Linhas.Should().ContainSingle().Which.Canal.Should().Be("Email");
    }

    // ── marcar e desmarcar a dona ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dona_marca_alguem_da_Administracao_com_conta_provada()
    {
        var gestora = Administracao();
        Como(Dona());

        var resultado = await Marcar().ExecuteAsync(new MarcarDonaCommand(gestora.Id, _empresaId, Dona: true));

        resultado.Should().Be(new MarcarDonaResult(true, false, null));
        gestora.Empresas.Single().EhDona.Should().BeTrue();
        _c.Auditorias.Should().ContainSingle(a => a.Acao == RastroDeUsuarios.DonaMarcada && a.Detalhes == "Marcada como dona por Baba");
    }

    [Fact]
    public async Task Marcar_quem_so_aceitou_pelo_WhatsApp_e_recusado()
    {
        var soWhatsApp = Pessoa("Lia", NivelAcesso.Admin, provada: false);
        soWhatsApp.ConviteAceitoVia = "+55•••1234";
        Como(Dona());

        await DeveRecusar(() => Marcar().ExecuteAsync(new MarcarDonaCommand(soWhatsApp.Id, _empresaId, true)),
            CodigosDominio.DonaContaNaoProvada);
        soWhatsApp.Empresas.Single().EhDona.Should().BeFalse();
    }

    [Fact]
    public async Task Administracao_nao_marca_nem_desmarca()
    {
        var outra = Administracao("Rita");
        var dona = Dona();
        Como(Administracao());

        await DeveRecusar(() => Marcar().ExecuteAsync(new MarcarDonaCommand(outra.Id, _empresaId, true)), CodigosDominio.Hierarquia);
        await DeveRecusar(() => Marcar().ExecuteAsync(new MarcarDonaCommand(dona.Id, _empresaId, false)), CodigosDominio.Hierarquia);
    }

    [Fact]
    public async Task Suporte_tira_a_marca_so_com_motivo()
    {
        var dona = Dona();
        Como(Suporte());

        var semMotivo = () => Marcar().ExecuteAsync(new MarcarDonaCommand(dona.Id, _empresaId, false, "curto"));
        (await semMotivo.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be("MOTIVO_OBRIGATORIO");
        dona.Empresas.Single().EhDona.Should().BeTrue();

        var resultado = await Marcar().ExecuteAsync(new MarcarDonaCommand(dona.Id, _empresaId, false, "  pedido formal da titular  "));

        resultado.Should().Be(new MarcarDonaResult(true, true, "pedido formal da titular"));
        dona.Empresas.Single().EhDona.Should().BeFalse();
    }

    [Fact]
    public async Task Com_zero_donas_o_suporte_marca_a_primeira()
    {
        _donas = 0;
        var gestora = Administracao();
        Como(Suporte());

        var resultado = await Marcar().ExecuteAsync(new MarcarDonaCommand(gestora.Id, _empresaId, true));

        resultado.Mudou.Should().BeTrue();
        resultado.PeloSuporte.Should().BeTrue();
    }

    [Fact]
    public async Task O_suporte_nunca_recebe_a_marca()
    {
        var suporte = Suporte();
        suporte.Empresas.Add(new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = suporte.Id, EmpresaId = _empresaId, Ativo = true });
        Como(Dona());

        var acao = () => Marcar().ExecuteAsync(new MarcarDonaCommand(suporte.Id, _empresaId, true));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be(CodigosDominio.DonaSuporte);
    }

    // ── cadastrar a dona ──────────────────────────────────────────────────────────────────────

    private CriarUsuarioCommand NovaDona(Guid? perfilId, string email = "titular@exemplo.test", string? senha = null) =>
        new(_empresaId, "Titular", email, senha, perfilId, null, "+5511999991234", AtestaOptInWhatsApp: true, Dona: true);

    [Fact]
    public async Task Dona_cadastra_outra_dona_com_convite_so_por_email()
    {
        Como(Dona());
        var adicionados = new List<UsuarioEmpresa>();
        _c.Vinculos.AddAsync(Arg.Do<UsuarioEmpresa>(adicionados.Add)).Returns(Task.CompletedTask);

        await Criar().ExecuteAsync(NovaDona(PerfilDaEmpresa(NivelAcesso.Admin).Id));

        adicionados.Should().ContainSingle().Which.EhDona.Should().BeTrue();
        _c.Tokens.Linhas.Should().ContainSingle().Which.Canal.Should().Be("Email", "a conta da dona é provada pela caixa de e-mail");
        _c.ConsentimentosGravados.Should().BeEmpty("o atestado de WhatsApp não vale para a dona");
        _c.Auditorias.Select(a => a.Acao).Should().Equal(RastroDeUsuarios.Convidado, RastroDeUsuarios.DonaMarcada);
    }

    [Fact]
    public async Task Administracao_nao_cadastra_dona_nem_Administracao()
    {
        Como(Administracao());

        await DeveRecusar(() => Criar().ExecuteAsync(NovaDona(PerfilDaEmpresa(NivelAcesso.Admin).Id)), CodigosDominio.Hierarquia);
        await DeveRecusar(() => Criar().ExecuteAsync(
            new CriarUsuarioCommand(_empresaId, "Rita", "rita@exemplo.test", null, PerfilDaEmpresa(NivelAcesso.Admin).Id, null)),
            CodigosDominio.Hierarquia);
        await _c.Usuarios.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task Dona_nova_exige_perfil_de_nivel_Admin()
    {
        Como(Dona());

        await DeveRecusar(() => Criar().ExecuteAsync(NovaDona(PerfilDaEmpresa(NivelAcesso.Gerente).Id)), CodigosDominio.DonaNaoAdmin);
        await DeveRecusar(() => Criar().ExecuteAsync(NovaDona(perfilId: null)), CodigosDominio.DonaNaoAdmin);
    }

    [Fact]
    public async Task Dona_nova_exige_email_novo_e_a_resposta_nao_sugere_marcar_a_conta()
    {
        var existente = Operador("Ocupada");
        Como(Dona());

        var recusa = await DeveRecusar(() => Criar().ExecuteAsync(NovaDona(PerfilDaEmpresa(NivelAcesso.Admin).Id, existente.Email)),
            CodigosDominio.DonaEmailExistente);

        recusa.Message.Should().NotContainEquivalentOf("marcar");
        await _c.Usuarios.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task Dona_nova_nao_nasce_com_senha()
    {
        Como(Dona());

        var acao = () => Criar().ExecuteAsync(NovaDona(PerfilDaEmpresa(NivelAcesso.Admin).Id, senha: "Senha@12345"));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be("DONA_SO_POR_CONVITE");
    }

    // ── e-mail da dona ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Email_da_dona_so_o_suporte_troca_e_com_motivo()
    {
        var dona = Dona();
        Como(Dona("Socia"));
        await DeveRecusar(() => Atualizar().ExecuteAsync(new AtualizarUsuarioCommand(dona.Id, "Baba", "nova@exemplo.test")),
            CodigosDominio.Hierarquia);

        Como(Suporte());
        var semMotivo = () => Atualizar().ExecuteAsync(new AtualizarUsuarioCommand(dona.Id, "Baba", "nova@exemplo.test"));
        (await semMotivo.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be("MOTIVO_OBRIGATORIO");
        dona.EmailPendente.Should().BeNull();

        var resultado = await Atualizar().ExecuteAsync(
            new AtualizarUsuarioCommand(dona.Id, "Baba", "nova@exemplo.test", Motivo: "titular perdeu a caixa antiga"));

        resultado.Should().Be(new AtualizarUsuarioResult(true, "titular perdeu a caixa antiga"));
        dona.EmailPendente.Should().Be("nova@exemplo.test");
    }

    [Fact]
    public async Task Dona_edita_o_nome_de_outra_dona()
    {
        var socia = Dona("Socia");
        Como(Dona());

        var resultado = await Atualizar().ExecuteAsync(new AtualizarUsuarioCommand(socia.Id, "Socia Silva", null));

        socia.Nome.Should().Be("Socia Silva");
        resultado.SuporteTrocouEmailDaDona.Should().BeFalse();
    }

    // ── anonimizar ────────────────────────────────────────────────────────────────────────────

    private AnonimizarMeusDadosUseCase Anonimizar(IRefreshTokenRepository refresh) => new(
        _c.Usuarios, refresh, _c.Tokens, Substitute.For<IEmailConfirmationTokenRepository>(), _c.Consentimentos,
        Substitute.For<IPreferenciaNotificacaoRepository>(), Hierarquia(), _atual, _c.UnitOfWork,
        Substitute.For<ILogger<AnonimizarMeusDadosUseCase>>());

    [Fact]
    public async Task A_ultima_dona_nao_se_anonimiza()
    {
        _donas = 1;
        var dona = Dona();
        Como(dona);
        var refresh = Substitute.For<IRefreshTokenRepository>();

        await DeveRecusar(() => Anonimizar(refresh).ExecuteAsync(new AnonimizarMeusDadosCommand("ANONIMIZAR")), CodigosDominio.UltimaDona);

        dona.Nome.Should().Be("Baba");
        dona.Empresas.Single().EhDona.Should().BeTrue();
        await refresh.DidNotReceiveWithAnyArgs().DeleteAllByUsuarioIdAsync(default);
        RecusaAuditada(dona, CodigosDominio.UltimaDona);
    }

    [Fact]
    public async Task A_dona_que_nao_e_a_ultima_perde_a_marca_e_se_anonimiza()
    {
        var dona = Dona();
        Como(dona);

        await Anonimizar(Substitute.For<IRefreshTokenRepository>()).ExecuteAsync(new AnonimizarMeusDadosCommand("ANONIMIZAR"));

        dona.Empresas.Single().EhDona.Should().BeFalse();
        dona.Nome.Should().Be("[Anonimizado]");
        _c.Auditorias.Should().ContainSingle(a => a.Acao == RastroDeUsuarios.DonaDesmarcada);
        // A trava de novo, já da mesma transação, liga o tenant da empresa antes de escrever nela.
        _ordem.Should().Equal("trava na transação", "conta donas", "trava na transação", "define dona em A");
    }

    [Fact]
    public async Task Anonimizar_recusado_numa_empresa_nao_tira_a_marca_de_nenhuma()
    {
        // Dona em A (com outra dona) e em B (a única). A vem antes na ordem das travas: antes, A comitava a retirada da
        // marca e só então B recusava, e a pessoa perdia a marca em A sem ter a conta anonimizada.
        var dona = Dona();
        var empresaB = Guid.NewGuid();
        while (empresaB.CompareTo(_empresaId) <= 0) empresaB = Guid.NewGuid();
        NaOutraEmpresa(dona, NivelAcesso.Admin, dona: true, empresaB);
        _c.Vinculos.TravarHierarquiaAsync(empresaB, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _ordem.Add(_c.UnitOfWork.EmTransacao ? "trava B na transação" : "trava B fora da transação");
            return Task.CompletedTask;
        });
        _c.Vinculos.ContarDonasAtivasAsync(empresaB, Arg.Any<CancellationToken>()).Returns(1);
        Como(dona);
        var refresh = Substitute.For<IRefreshTokenRepository>();

        var acao = () => Anonimizar(refresh).ExecuteAsync(new AnonimizarMeusDadosCommand("ANONIMIZAR"));

        (await acao.Should().ThrowAsync<HierarquiaRecusadaException>()).Which.Codigo.Should().Be(CodigosDominio.UltimaDona);
        dona.Empresas.Should().OnlyContain(e => e.EhDona, "nenhuma empresa perdeu a marca");
        _ordem.Should().Equal("trava na transação", "conta donas", "trava B na transação");
        _c.Auditorias.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Acao = RastroDeUsuarios.HierarquiaRecusada, Sucesso = false, EmpresaId = (Guid?)empresaB });
        await refresh.DidNotReceiveWithAnyArgs().DeleteAllByUsuarioIdAsync(default);
    }

    // ── leituras: dona, ações por linha e perfis atribuíveis ──────────────────────────────────

    [Fact]
    public async Task Lista_traz_a_marca_e_as_acoes_que_a_Administracao_pode_na_linha()
    {
        var dona = Dona();
        var ana = Operador();
        var semPerfil = Pessoa("Bia", nivel: null);
        Como(Administracao());
        _c.Usuarios.GetByEmpresaAsync(_empresaId, 1, 20).Returns((new[] { dona, ana, semPerfil }.AsEnumerable(), 3));

        var (lista, _) = await new ListarUsuariosUseCase(_c.Usuarios, Hierarquia()).ExecuteAsync(new ListarUsuariosQuery(_empresaId));
        var linhas = lista.ToList();

        linhas[0].Dona.Should().BeTrue();
        linhas[0].Acoes.Should().Be(AcoesSobreUsuario.Nenhuma, "a Administração não alcança a dona");
        linhas[1].Dona.Should().BeFalse();
        linhas[1].Acoes.Should().Be(new AcoesSobreUsuario(
            EditarNome: true, TrocarEmail: true, TrocarPerfil: true, Desativar: true, Reativar: true,
            ReenviarConvite: false, MarcarDona: false, DesmarcarDona: false));
        linhas[2].Nivel.Should().Be("Visualizador", "B2: sem perfil entra como Visualizador, e a lista diz o mesmo");
    }

    [Fact]
    public async Task Lista_para_a_dona_oferece_marcar_a_Administracao_e_tirar_a_marca_da_outra_dona()
    {
        var socia = Dona("Socia");
        var gestora = Administracao();
        var convidada = Pessoa("Nova", NivelAcesso.Operador, convidada: true);
        Como(Dona());
        _c.Usuarios.GetByEmpresaAsync(_empresaId, 1, 20).Returns((new[] { socia, gestora, convidada }.AsEnumerable(), 3));

        var (lista, _) = await new ListarUsuariosUseCase(_c.Usuarios, Hierarquia()).ExecuteAsync(new ListarUsuariosQuery(_empresaId));
        var linhas = lista.ToList();

        // Marcar e tirar a marca valem pela autoridade, como desativar e reativar: repetir é um 2xx que não muda nada, e o
        // console escolhe o botão pelo campo dona da linha.
        linhas[0].Acoes!.Should().BeEquivalentTo(new { DesmarcarDona = true, MarcarDona = true, TrocarEmail = false, Desativar = false });
        linhas[1].Acoes!.Should().BeEquivalentTo(new { MarcarDona = true, DesmarcarDona = true, TrocarPerfil = true });
        linhas[2].Acoes!.ReenviarConvite.Should().BeTrue();
        linhas[2].Acoes!.MarcarDona.Should().BeFalse("Operador não vira dona");
    }

    [Fact]
    public async Task Acoes_da_lista_levam_as_mesmas_precondicoes_da_rota()
    {
        var deDuasEmpresas = Operador("Duas");
        NaOutraEmpresa(deDuasEmpresas, NivelAcesso.Operador);
        var convidadaDesligada = Pessoa("Desligada", NivelAcesso.Operador, convidada: true);
        convidadaDesligada.Empresas.Single().Ativo = false;
        var donaLa = Operador("DonaLa");
        NaOutraEmpresa(donaLa, NivelAcesso.Admin, dona: true);
        Como(Administracao());
        _c.Usuarios.GetByEmpresaAsync(_empresaId, 1, 20)
            .Returns((new[] { deDuasEmpresas, convidadaDesligada, donaLa }.AsEnumerable(), 3));

        var linhas = (await new ListarUsuariosUseCase(_c.Usuarios, Hierarquia()).ExecuteAsync(new ListarUsuariosQuery(_empresaId)))
            .Usuarios.ToList();

        linhas[0].Acoes!.TrocarEmail.Should().BeFalse("o e-mail de quem também é de outra empresa, só a pessoa ou o suporte troca");
        linhas[0].Acoes!.EditarNome.Should().BeTrue();
        linhas[1].Acoes!.ReenviarConvite.Should().BeFalse("o reenvio exige o acesso ativo nesta empresa");
        linhas[2].Acoes!.Should().BeEquivalentTo(new { EditarNome = false, TrocarEmail = false, ReenviarConvite = false, Desativar = true },
            "nome e e-mail são da conta, e ela é dona na outra empresa");
    }

    [Fact]
    public async Task Para_o_suporte_a_ultima_dona_ainda_troca_de_perfil_dentro_da_Administracao()
    {
        _donas = 1;
        var dona = Dona();
        Como(Suporte());
        _c.Usuarios.GetByEmpresaAsync(_empresaId, 1, 20).Returns((new[] { dona }.AsEnumerable(), 1));

        var linha = (await new ListarUsuariosUseCase(_c.Usuarios, Hierarquia()).ExecuteAsync(new ListarUsuariosQuery(_empresaId)))
            .Usuarios.Single();

        linha.Acoes!.TrocarPerfil.Should().BeTrue("para outro perfil de nível Admin a API aceita");
        linha.Acoes!.Desativar.Should().BeFalse("a última dona não sai");
        linha.Acoes!.DesmarcarDona.Should().BeFalse();
    }

    [Fact]
    public async Task Perfis_atribuiveis_dizem_o_que_quem_pede_pode_dar()
    {
        _perfis.GetByEmpresaAsync(_empresaId).Returns(new[]
        {
            new Perfil { Id = Guid.NewGuid(), Nome = "Administração", Nivel = NivelAcesso.Admin, EmpresaId = _empresaId },
            new Perfil { Id = Guid.NewGuid(), Nome = "Gerência", Nivel = NivelAcesso.Gerente, EmpresaId = _empresaId },
        });

        Como(Administracao());
        var daAdministracao = await new ListarPerfisAtribuiveisUseCase(_perfis, Hierarquia()).ExecuteAsync(_empresaId);
        Como(Dona());
        var daDona = await new ListarPerfisAtribuiveisUseCase(_perfis, Hierarquia()).ExecuteAsync(_empresaId);

        daAdministracao.Select(p => (p.Nome, p.Atribuivel)).Should().Equal(("Administração", false), ("Gerência", true));
        daDona.Select(p => p.Atribuivel).Should().Equal(true, true);
    }

    /// <summary>Os códigos do domínio, com nome curto para as asserções.</summary>
    private static class CodigosDominio
    {
        public const string Hierarquia = Domain.Services.CodigosDaHierarquia.Hierarquia;
        public const string DonaProtegida = Domain.Services.CodigosDaHierarquia.DonaProtegida;
        public const string UltimaDona = Domain.Services.CodigosDaHierarquia.UltimaDona;
        public const string DonaNaoAdmin = Domain.Services.CodigosDaHierarquia.DonaNaoAdmin;
        public const string DonaContaNaoProvada = Domain.Services.CodigosDaHierarquia.DonaContaNaoProvada;
        public const string DonaSuporte = Domain.Services.CodigosDaHierarquia.DonaSuporte;
        public const string DonaEmailExistente = Domain.Services.CodigosDaHierarquia.DonaEmailExistente;
    }
}
