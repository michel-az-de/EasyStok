using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.PrimeirosPassos;
using EasyStock.Domain.Services;
using Microsoft.Extensions.Time.Testing;

namespace EasyStock.Application.Tests.UseCases.PrimeirosPassos;

/// <summary>
/// #2070: estado da configuração inicial no servidor. GET diz o que falta e quem pode o quê; PUT só marca; adiar e
/// concluir são da dona ativa ou do suporte global, e a Administração recebe HIERARQUIA.
/// </summary>
public class ConfiguracaoInicialUseCasesTests
{
    private static readonly DateTime Agora = new(2026, 10, 26, 15, 30, 0, DateTimeKind.Utc);
    private static readonly Guid EmpresaId = Guid.NewGuid();

    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IEmpresaRepository _empresas = Substitute.For<IEmpresaRepository>();
    private readonly IConfiguracaoInicialRepository _passos = Substitute.For<IConfiguracaoInicialRepository>();
    private readonly IRetratoDaConfiguracaoInicialQuery _retratos = Substitute.For<IRetratoDaConfiguracaoInicialQuery>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Agora));
    private readonly Empresa _empresa = Empresa.Criar("Casa da Baba", "11111111000191");

    public ConfiguracaoInicialUseCasesTests()
    {
        _empresa.Id = EmpresaId;
        _empresas.GetByIdAsync(EmpresaId).Returns(_empresa);
        _passos.ListarPassosAsync(EmpresaId, Arg.Any<CancellationToken>()).Returns(new List<PassoMarcado>());
        _retratos.ObterAsync(EmpresaId, Arg.Any<CancellationToken>()).Returns(new RetratoDaConfiguracaoInicial(
            false, 0, false, false, 0, 0, 0, false, new Dictionary<NivelAcesso, int>()));
    }

    // ── Pessoas ──────────────────────────────────────────────────────────────

    private Usuario Pessoa(
        NivelAcesso nivel = NivelAcesso.Admin, bool dona = false, bool vinculoAtivo = true, bool contaAtiva = true,
        Guid? empresaDoVinculo = null)
    {
        var empresa = empresaDoVinculo ?? EmpresaId;
        var usuario = Usuario.Criar("Pessoa de teste", $"pessoa-{Guid.NewGuid():N}@exemplo.test", "hash");
        usuario.Ativo = contaAtiva;
        usuario.Empresas.Add(new UsuarioEmpresa { UsuarioId = usuario.Id, EmpresaId = empresa, Ativo = vinculoAtivo, EhDona = dona });
        usuario.Perfis.Add(new UsuarioPerfil
        {
            UsuarioId = usuario.Id, EmpresaId = empresa,
            Perfil = new Perfil { Id = Guid.NewGuid(), EmpresaId = empresa, Nome = "Perfil", Nivel = nivel }
        });
        _usuarios.GetByIdAsync(usuario.Id).Returns(usuario);
        return usuario;
    }

    private Usuario Suporte()
    {
        var usuario = Usuario.Criar("Suporte", $"suporte-{Guid.NewGuid():N}@exemplo.test", "hash");
        usuario.Perfis.Add(new UsuarioPerfil
        {
            UsuarioId = usuario.Id, EmpresaId = EmpresaId,
            Perfil = new Perfil { Id = Guid.NewGuid(), EmpresaId = null, Nome = "SuperAdmin", Nivel = NivelAcesso.SuperAdmin }
        });
        _usuarios.GetByIdAsync(usuario.Id).Returns(usuario);
        return usuario;
    }

    private static ICurrentUserAccessor Sessao(Usuario usuario, NivelAcesso nivel = NivelAcesso.Admin, Guid? empresaId = null,
        Permissao[]? semPermissao = null)
    {
        var sessao = Substitute.For<ICurrentUserAccessor>();
        sessao.UsuarioId.Returns(usuario.Id);
        sessao.EmpresaId.Returns(empresaId ?? EmpresaId);
        sessao.Nivel.Returns(nivel);
        sessao.IsAuthenticated.Returns(true);
        sessao.TemPermissao(Arg.Any<Permissao>()).Returns(c => semPermissao?.Contains((Permissao)c[0]) != true);
        return sessao;
    }

    private AutoridadeNaConfiguracaoInicial Autoridade() => new(_usuarios);

    private ObterConfiguracaoInicialUseCase Obter(ICurrentUserAccessor sessao) =>
        new(_empresas, _passos, _retratos, Autoridade(), sessao);

    // ── Autoridade: dona ativa ou suporte global ──────────────────────────────

    [Fact]
    public async Task DonaAtivaEhDonaENaoSuporte()
    {
        var dona = Pessoa(dona: true);

        var autoridade = await Autoridade().AvaliarAsync(dona.Id, EmpresaId);

        autoridade.Should().Be(new AutoridadeNaConfiguracao(EhDona: true, EhSuporteGlobal: false));
        autoridade.PodeDecidir.Should().BeTrue();
    }

    public static TheoryData<string, NivelAcesso, bool, bool, bool, bool> NaoSaoDonaAtiva => new()
    {
        { "Administração sem a marca", NivelAcesso.Admin, false, true, true, false },
        { "marca com vínculo desativado", NivelAcesso.Admin, true, false, true, false },
        { "marca com a conta desativada", NivelAcesso.Admin, true, true, false, false },
        { "marca com perfil de Gerência", NivelAcesso.Gerente, true, true, true, false },
        { "marca em outra empresa", NivelAcesso.Admin, true, true, true, true },
    };

    [Theory]
    [MemberData(nameof(NaoSaoDonaAtiva))]
    public async Task SoADonaAtivaDaEmpresaDoPedidoContaComoDona(
        string caso, NivelAcesso nivel, bool marca, bool vinculoAtivo, bool contaAtiva, bool outraEmpresa)
    {
        var pessoa = Pessoa(nivel, marca, vinculoAtivo, contaAtiva, outraEmpresa ? Guid.NewGuid() : null);

        var autoridade = await Autoridade().AvaliarAsync(pessoa.Id, EmpresaId);

        autoridade.Should().Be(new AutoridadeNaConfiguracao(false, false), caso);
    }

    [Fact]
    public async Task SuporteGlobalEhSuporteENuncaDona()
    {
        var suporte = Suporte();

        var autoridade = await Autoridade().AvaliarAsync(suporte.Id, EmpresaId);

        autoridade.Should().Be(new AutoridadeNaConfiguracao(EhDona: false, EhSuporteGlobal: true));
    }

    [Fact]
    public async Task PerfilDaEmpresaComNivelSuperAdminNaoEhSuporteGlobal()
    {
        var pessoa = Pessoa(NivelAcesso.SuperAdmin);

        (await Autoridade().AvaliarAsync(pessoa.Id, EmpresaId)).Should().Be(new AutoridadeNaConfiguracao(false, false));
    }

    [Fact]
    public async Task UsuarioInexistenteNaoDecide() =>
        (await Autoridade().AvaliarAsync(Guid.NewGuid(), EmpresaId)).Should().Be(new AutoridadeNaConfiguracao(false, false));

    // ── Disponibilidade: a mesma regra das acoes de me/modulos ────────────────

    [Fact]
    public void AdministradorComTudoLiberadoTemTodosOsPassos()
    {
        var sessao = Sessao(Pessoa());

        ConfiguracaoInicial.Passos.Select(p => DisponibilidadeDosPassos.Avaliar(p.Id, sessao))
            .Should().AllBeEquivalentTo(new DisponibilidadeDoPasso(true, null));
    }

    [Theory]
    [InlineData("cardapio", Permissao.AcessarModuloCardapio, "O módulo Cardápio não está liberado no seu perfil.")]
    [InlineData("producao", Permissao.AcessarModuloProducao, "O módulo Produção não está liberado no seu perfil.")]
    [InlineData("horarios", Permissao.AcessarModuloAtendimento, "O módulo Atendimento não está liberado no seu perfil.")]
    [InlineData("atendimento", Permissao.AcessarModuloAtendimento, "O módulo Atendimento não está liberado no seu perfil.")]
    [InlineData("agente", Permissao.AcessarModuloAtendimento, "O módulo Atendimento não está liberado no seu perfil.")]
    [InlineData("respostas", Permissao.AcessarModuloAtendimento, "O módulo Atendimento não está liberado no seu perfil.")]
    [InlineData("respostas", Permissao.AtenderConversas, "Seu perfil não atende conversas.")]
    [InlineData("entregadores", Permissao.AcessarModuloEntregas, "O módulo Entregas não está liberado no seu perfil.")]
    [InlineData("site-e-impressos", Permissao.AcessarModuloConfiguracoes, "O módulo Configurações não está liberado no seu perfil.")]
    [InlineData("equipe", Permissao.AcessarModuloConfiguracoes, "O módulo Configurações não está liberado no seu perfil.")]
    [InlineData("dados-da-loja", Permissao.AcessarModuloConfiguracoes, "O módulo Configurações não está liberado no seu perfil.")]
    [InlineData("estoque", Permissao.AcessarModuloConfiguracoes, "O módulo Configurações não está liberado no seu perfil.")]
    public void PassoSemOModuloOuAPermissaoDaRotaFicaIndisponivelComMotivo(string passo, Permissao falta, string motivo) =>
        DisponibilidadeDosPassos.Avaliar(passo, Sessao(Pessoa(), semPermissao: [falta]))
            .Should().Be(new DisponibilidadeDoPasso(false, motivo));

    [Theory]
    [InlineData("horarios", false)]
    [InlineData("atendimento", false)]
    [InlineData("agente", false)]
    [InlineData("site-e-impressos", false)]
    [InlineData("janelas", false)]
    [InlineData("frete", false)]
    [InlineData("cardapio", true)]
    [InlineData("respostas", true)]
    [InlineData("producao", true)]
    [InlineData("entregadores", true)]
    public void NivelGerenteSoAlteraOQueAsAcoesDaoAGerente(string passo, bool disponivel) =>
        DisponibilidadeDosPassos.Avaliar(passo, Sessao(Pessoa(NivelAcesso.Gerente), NivelAcesso.Gerente))
            .Should().Be(disponivel
                ? new DisponibilidadeDoPasso(true, null)
                : new DisponibilidadeDoPasso(false, "Seu perfil não altera esta parte."));

    [Theory]
    [InlineData("boas-vindas")]
    [InlineData("impressora")]
    [InlineData("avisos")]
    [InlineData("pronto")]
    public void PassoSemRotaPropriaFicaSempreDisponivel(string passo)
    {
        var semNada = Enum.GetValues<Permissao>();

        DisponibilidadeDosPassos.Avaliar(passo, Sessao(Pessoa(NivelAcesso.Visualizador), NivelAcesso.Visualizador, semPermissao: semNada))
            .Should().Be(new DisponibilidadeDoPasso(true, null));
    }

    [Fact]
    public void SemEmpresaNoTokenNenhumModuloEstaLiberado() =>
        DisponibilidadeDosPassos.Avaliar("cardapio", Sessao(Pessoa(), empresaId: Guid.Empty))
            .Should().Be(new DisponibilidadeDoPasso(false, "O módulo Cardápio não está liberado no seu perfil."));

    // ── GET ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DonaComAConfiguracaoPendenteAbreSozinhaEVeOContratoCompleto()
    {
        var dona = Pessoa(dona: true);
        _passos.ListarPassosAsync(EmpresaId, Arg.Any<CancellationToken>()).Returns(new List<PassoMarcado>
        {
            new("horarios", SituacaoDoPasso.Conferido, Agora.AddMinutes(-5), dona.Id, "Dona da casa"),
            new("frete", SituacaoDoPasso.Depois, Agora.AddMinutes(-2), dona.Id, "Dona da casa"),
            new("passo-antigo", SituacaoDoPasso.Conferido, Agora, dona.Id, "Dona da casa"),
        });

        var resultado = await Obter(Sessao(dona)).ExecuteAsync();

        resultado.Pendente.Should().BeTrue();
        resultado.AbrirSozinho.Should().BeTrue();
        resultado.EhDona.Should().BeTrue();
        resultado.PodeConcluir.Should().BeTrue();
        resultado.ConcluidaEm.Should().BeNull();
        resultado.AdiadaEm.Should().BeNull();
        resultado.Passos.Select(p => (p.Passo, p.Parte, p.Ordem)).Should().Equal(
            ConfiguracaoInicial.Passos.Select(p => (p.Id, p.Parte.ToString().ToLowerInvariant(), p.Ordem)));
        resultado.Passos.Select(p => p.Parte).Distinct().Should().Equal("essencial", "ajustes", "revisao");

        var horarios = resultado.Passos.Single(p => p.Passo == "horarios");
        horarios.Should().Be(new PassoDaConfiguracaoInicialResult("horarios", "essencial", 2, "conferido", Agora.AddMinutes(-5),
            "Dona da casa", true, null, horarios.Prontidao));
        horarios.Prontidao!.Should().BeEquivalentTo(new ProntidaoDoPassoResult(false, [ProntidaoDaConfiguracaoInicial.FaltaSalvarHorarios]));
        resultado.Passos.Single(p => p.Passo == "frete").Situacao.Should().Be("depois");

        var naoMarcado = resultado.Passos.Single(p => p.Passo == "cardapio");
        (naoMarcado.Situacao, naoMarcado.Em, naoMarcado.PorNome).Should().Be(((string?)null, (DateTime?)null, (string?)null));

        resultado.Passos.Where(p => p.Prontidao is not null).Select(p => p.Passo)
            .Should().Equal("horarios", "site-e-impressos", "cardapio", "janelas", "frete", "equipe");
    }

    [Fact]
    public async Task AdministracaoNaoAbreSozinhaNemConclui()
    {
        var resultado = await Obter(Sessao(Pessoa())).ExecuteAsync();

        (resultado.Pendente, resultado.AbrirSozinho, resultado.EhDona, resultado.PodeConcluir)
            .Should().Be((true, false, false, false));
    }

    [Fact]
    public async Task DonaQueAdiouNaoAbreSozinha()
    {
        _empresa.AdiarConfiguracaoInicial(Agora.AddDays(-1));

        var resultado = await Obter(Sessao(Pessoa(dona: true))).ExecuteAsync();

        (resultado.Pendente, resultado.AbrirSozinho, resultado.AdiadaEm).Should().Be((true, false, Agora.AddDays(-1)));
    }

    [Fact]
    public async Task ConfiguracaoConcluidaNaoFicaPendente()
    {
        var dona = Pessoa(dona: true);
        _empresa.ConcluirConfiguracaoInicial(dona.Id, Agora.AddHours(-3));

        var resultado = await Obter(Sessao(dona)).ExecuteAsync();

        (resultado.Pendente, resultado.AbrirSozinho, resultado.ConcluidaEm).Should().Be((false, false, Agora.AddHours(-3)));
    }

    [Fact]
    public async Task SuporteConcluiMasNaoEhDonaNemAbreSozinho()
    {
        var resultado = await Obter(Sessao(Suporte(), NivelAcesso.SuperAdmin)).ExecuteAsync();

        (resultado.EhDona, resultado.PodeConcluir, resultado.AbrirSozinho).Should().Be((false, true, false));
    }

    [Fact]
    public async Task GetComEmpresaVaziaEhRecusado() =>
        await FluentActions.Awaiting(() => Obter(Sessao(Pessoa(), empresaId: Guid.Empty)).ExecuteAsync())
            .Should().ThrowAsync<UseCaseValidationException>().WithMessage("EmpresaId é obrigatório.");

    // ── PUT ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MarcarGravaOPassoDaEmpresaDoTokenComAutorEInstante()
    {
        var admin = Pessoa();

        await new MarcarPassoConfiguracaoInicialUseCase(_passos, Sessao(admin), _relogio).ExecuteAsync("janelas", "depois");

        await _passos.Received(1).MarcarAsync(Arg.Is<PassoConfiguracaoInicial>(p =>
            p.EmpresaId == EmpresaId && p.Passo == "janelas" && p.Situacao == SituacaoDoPasso.Depois
            && p.PorUsuarioId == admin.Id && p.Em == Agora), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("caixa")]
    [InlineData("Janelas")]
    [InlineData("")]
    public async Task PassoForaDoCatalogoEhErroDeValidacaoSemGravar(string passo)
    {
        var marcar = () => new MarcarPassoConfiguracaoInicialUseCase(_passos, Sessao(Pessoa()), _relogio)
            .ExecuteAsync(passo, "conferido");

        (await marcar.Should().ThrowAsync<UseCaseValidationException>())
            .Which.Code.Should().Be(MarcarPassoConfiguracaoInicialUseCase.CodigoPassoDesconhecido);
        await _passos.DidNotReceiveWithAnyArgs().MarcarAsync(default!, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pulado")]
    [InlineData("Conferido")]
    public async Task SituacaoForaDoContratoEhErroDeValidacaoSemGravar(string? situacao)
    {
        var marcar = () => new MarcarPassoConfiguracaoInicialUseCase(_passos, Sessao(Pessoa()), _relogio)
            .ExecuteAsync("janelas", situacao);

        (await marcar.Should().ThrowAsync<UseCaseValidationException>())
            .Which.Code.Should().Be(MarcarPassoConfiguracaoInicialUseCase.CodigoSituacaoInvalida);
        await _passos.DidNotReceiveWithAnyArgs().MarcarAsync(default!, default);
    }

    [Fact]
    public async Task MarcarComEmpresaVaziaEhRecusado()
    {
        var marcar = () => new MarcarPassoConfiguracaoInicialUseCase(_passos, Sessao(Pessoa(), empresaId: Guid.Empty), _relogio)
            .ExecuteAsync("janelas", "conferido");

        await marcar.Should().ThrowAsync<UseCaseValidationException>().WithMessage("EmpresaId é obrigatório.");
        await _passos.DidNotReceiveWithAnyArgs().MarcarAsync(default!, default);
    }

    // ── Adiar e concluir ─────────────────────────────────────────────────────

    private AdiarConfiguracaoInicialUseCase Adiar(ICurrentUserAccessor sessao) =>
        new(_empresas, Autoridade(), sessao, _unitOfWork, _relogio);

    private ConcluirConfiguracaoInicialUseCase Concluir(ICurrentUserAccessor sessao) =>
        new(_empresas, Autoridade(), sessao, _unitOfWork, _relogio);

    [Fact]
    public async Task AdministracaoRecebeHierarquiaAoAdiarSemGravar()
    {
        var adiar = () => Adiar(Sessao(Pessoa())).ExecuteAsync();

        (await adiar.Should().ThrowAsync<UseCaseValidationException>())
            .Which.Code.Should().Be(AutoridadeNaConfiguracaoInicial.CodigoHierarquia);
        _empresa.ConfiguracaoInicialAdiadaEm.Should().BeNull();
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task AdministracaoRecebeHierarquiaAoConcluirSemGravar()
    {
        var concluir = () => Concluir(Sessao(Pessoa())).ExecuteAsync();

        (await concluir.Should().ThrowAsync<UseCaseValidationException>())
            .Which.Code.Should().Be(AutoridadeNaConfiguracaoInicial.CodigoHierarquia);
        _empresa.ConfiguracaoInicialConcluidaEm.Should().BeNull();
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DonaAdiaComOInstanteDoRelogio()
    {
        await Adiar(Sessao(Pessoa(dona: true))).ExecuteAsync();

        _empresa.ConfiguracaoInicialAdiadaEm.Should().Be(Agora);
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DonaConcluiMesmoComPassosParaDepois()
    {
        var dona = Pessoa(dona: true);
        _passos.ListarPassosAsync(EmpresaId, Arg.Any<CancellationToken>()).Returns(new List<PassoMarcado>
        {
            new("frete", SituacaoDoPasso.Depois, Agora, dona.Id, "Dona da casa"),
        });

        await Concluir(Sessao(dona)).ExecuteAsync();

        (_empresa.ConfiguracaoInicialConcluidaEm, _empresa.ConfiguracaoInicialConcluidaPorId).Should().Be(((DateTime?)Agora, (Guid?)dona.Id));
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task SuporteGlobalConclui()
    {
        var suporte = Suporte();

        await Concluir(Sessao(suporte, NivelAcesso.SuperAdmin)).ExecuteAsync();

        _empresa.ConfiguracaoInicialConcluidaPorId.Should().Be(suporte.Id);
    }

    [Fact]
    public async Task AdiarComEmpresaVaziaEhRecusado() =>
        await FluentActions.Awaiting(() => Adiar(Sessao(Pessoa(dona: true), empresaId: Guid.Empty)).ExecuteAsync())
            .Should().ThrowAsync<UseCaseValidationException>().WithMessage("EmpresaId é obrigatório.");
}
