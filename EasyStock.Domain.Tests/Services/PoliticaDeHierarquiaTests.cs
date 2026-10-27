using EasyStock.Domain.Enums;
using EasyStock.Domain.Services;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Services;

/// <summary>
/// #2070 (ADR-0038): a matriz inteira da hierarquia, ator {suporte, dona, Administração} × alvo {si, dona, Administração,
/// Gerência, Operador, sem perfil} × as 9 ações, com o resultado esperado em cada célula. A empresa tem duas donas ativas
/// na matriz, para a trava da última dona não se misturar; ela tem os seus próprios testes abaixo.
/// </summary>
public class PoliticaDeHierarquiaTests
{
    private const string P = "permite";
    private const string M = "permite com motivo";
    private const string H = CodigosDaHierarquia.Hierarquia;
    private const string U = CodigosDaHierarquia.UsuarioProprio;
    private const string DP = CodigosDaHierarquia.DonaProtegida;
    private const string NA = CodigosDaHierarquia.DonaNaoAdmin;
    private const string DS = CodigosDaHierarquia.DonaSuporte;

    private static readonly ParticipanteDaHierarquia Suporte =
        new(Guid.NewGuid(), NivelAcesso.Visualizador, false, VinculoAtivo: false, UsuarioAtivo: true, ContaProvada: true, EhSuporteGlobal: true);
    private static readonly ParticipanteDaHierarquia Dona = Pessoa(NivelAcesso.Admin, dona: true);
    private static readonly ParticipanteDaHierarquia Administracao = Pessoa(NivelAcesso.Admin);

    private static readonly Dictionary<string, ParticipanteDaHierarquia> Atores = new()
    {
        ["suporte"] = Suporte, ["dona"] = Dona, ["administracao"] = Administracao,
    };

    /// <summary>O alvo de cada coluna e o nível que ele teria ao nascer (<see cref="AcaoSobreUsuario.Criar"/>).</summary>
    private static readonly Dictionary<string, (ParticipanteDaHierarquia Alvo, NivelAcesso? NivelAoNascer)> Alvos = new()
    {
        ["dona"] = (Pessoa(NivelAcesso.Admin, dona: true), NivelAcesso.Admin),
        ["administracao"] = (Pessoa(NivelAcesso.Admin), NivelAcesso.Admin),
        ["gerencia"] = (Pessoa(NivelAcesso.Gerente), NivelAcesso.Gerente),
        ["operador"] = (Pessoa(NivelAcesso.Operador), NivelAcesso.Operador),
        ["sem perfil"] = (Pessoa(NivelAcesso.Visualizador), null),
    };

    private static readonly AcaoSobreUsuario[] Acoes =
    [
        AcaoSobreUsuario.Criar, AcaoSobreUsuario.EditarNome, AcaoSobreUsuario.TrocarEmail, AcaoSobreUsuario.TrocarPerfil,
        AcaoSobreUsuario.Desativar, AcaoSobreUsuario.Reativar, AcaoSobreUsuario.ReenviarConvite,
        AcaoSobreUsuario.MarcarDona, AcaoSobreUsuario.DesmarcarDona,
    ];

    // Ordem das colunas: Criar, EditarNome, TrocarEmail, TrocarPerfil, Desativar, Reativar, ReenviarConvite, MarcarDona, DesmarcarDona.
    private static readonly Dictionary<(string Ator, string Alvo), string[]> Esperado = new()
    {
        [("suporte", "si")] = [P, P, P, P, P, P, P, DS, P],
        [("suporte", "dona")] = [P, P, M, P, P, P, P, P, M],
        [("suporte", "administracao")] = [P, P, P, P, P, P, P, P, P],
        [("suporte", "gerencia")] = [P, P, P, P, P, P, P, NA, P],
        [("suporte", "operador")] = [P, P, P, P, P, P, P, NA, P],
        [("suporte", "sem perfil")] = [P, P, P, P, P, P, P, NA, P],

        [("dona", "si")] = [U, P, U, U, U, U, U, U, U],
        [("dona", "dona")] = [P, P, H, DP, DP, P, P, P, P],
        [("dona", "administracao")] = [P, P, P, P, P, P, P, P, P],
        [("dona", "gerencia")] = [P, P, P, P, P, P, P, NA, P],
        [("dona", "operador")] = [P, P, P, P, P, P, P, NA, P],
        [("dona", "sem perfil")] = [P, P, P, P, P, P, P, NA, P],

        [("administracao", "si")] = [U, P, U, U, U, U, U, U, U],
        [("administracao", "dona")] = [H, H, H, H, H, H, H, H, H],
        [("administracao", "administracao")] = [H, H, H, H, H, H, H, H, H],
        [("administracao", "gerencia")] = [P, P, P, P, P, P, P, H, H],
        [("administracao", "operador")] = [P, P, P, P, P, P, P, H, H],
        [("administracao", "sem perfil")] = [P, P, P, P, P, P, P, H, H],
    };

    public static IEnumerable<object[]> Celulas() =>
        from par in Esperado
        from i in Enumerable.Range(0, Acoes.Length)
        select new object[] { par.Key.Ator, par.Key.Alvo, Acoes[i], par.Value[i] };

    [Fact]
    public void A_matriz_tem_as_162_celulas() => Celulas().Should().HaveCount(3 * 6 * 9);

    [Theory]
    [MemberData(nameof(Celulas))]
    public void Matriz(string ator, string alvo, AcaoSobreUsuario acao, string esperado)
    {
        var quem = Atores[ator];
        var (sobre, nivelAoNascer) = alvo == "si" ? (quem, quem.Nivel) : Alvos[alvo];
        if (acao == AcaoSobreUsuario.Criar && alvo != "si")
            sobre = ParticipanteDaHierarquia.Novo(nivelAoNascer, dona: alvo == "dona");
        var nivelNovo = acao switch
        {
            AcaoSobreUsuario.TrocarPerfil => NivelAcesso.Operador,
            AcaoSobreUsuario.Criar => (NivelAcesso?)nivelAoNascer,
            _ => null,
        };

        var decisao = PoliticaDeHierarquia.Avaliar(quem, sobre, acao, donasAtivas: 2, nivelNovo);

        Descrever(decisao).Should().Be(esperado, $"{ator} → {alvo}: {acao}");
    }

    [Theory]
    [InlineData(AcaoSobreUsuario.DesmarcarDona)]
    [InlineData(AcaoSobreUsuario.Desativar)]
    public void Suporte_nao_tira_a_ultima_dona(AcaoSobreUsuario acao) =>
        Descrever(PoliticaDeHierarquia.Avaliar(Suporte, Dona, acao, donasAtivas: 1))
            .Should().Be(CodigosDaHierarquia.UltimaDona);

    [Fact]
    public void Suporte_nao_rebaixa_a_ultima_dona_mas_troca_para_outro_perfil_de_administracao()
    {
        Descrever(PoliticaDeHierarquia.Avaliar(Suporte, Dona, AcaoSobreUsuario.TrocarPerfil, 1, NivelAcesso.Gerente))
            .Should().Be(CodigosDaHierarquia.UltimaDona);
        Descrever(PoliticaDeHierarquia.Avaliar(Suporte, Dona, AcaoSobreUsuario.TrocarPerfil, 1, NivelAcesso.Admin))
            .Should().Be(P);
    }

    [Fact]
    public void Quem_perdeu_a_marca_na_corrida_recebe_ULTIMA_DONA_e_nao_HIERARQUIA()
    {
        // Duas donas se desmarcando: a segunda, relida depois da trava, já é Administração e a outra é a última.
        Descrever(PoliticaDeHierarquia.Avaliar(Administracao, Dona, AcaoSobreUsuario.DesmarcarDona, donasAtivas: 1))
            .Should().Be(CodigosDaHierarquia.UltimaDona);
    }

    [Fact]
    public void Dona_que_nao_e_ativa_nao_conta_para_a_ultima_dona()
    {
        var desativada = Dona with { VinculoAtivo = false };
        Descrever(PoliticaDeHierarquia.Avaliar(Suporte, desativada, AcaoSobreUsuario.DesmarcarDona, donasAtivas: 1))
            .Should().Be(M);
    }

    [Theory]
    [InlineData(NivelAcesso.Gerente)]
    [InlineData(NivelAcesso.Operador)]
    [InlineData(NivelAcesso.Visualizador)]
    public void Ator_rebaixado_nao_pode_nada_nem_com_quem_esta_abaixo(NivelAcesso nivel)
    {
        var rebaixado = Administracao with { Nivel = nivel };
        foreach (var acao in Acoes)
            Descrever(PoliticaDeHierarquia.Avaliar(rebaixado, Pessoa(NivelAcesso.Visualizador), acao, 2))
                .Should().Be(H, $"{nivel} em {acao}");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Administracao_desativada_nao_pode_nada(bool vinculoAtivo, bool usuarioAtivo)
    {
        var desligada = Administracao with { VinculoAtivo = vinculoAtivo, UsuarioAtivo = usuarioAtivo };
        Descrever(PoliticaDeHierarquia.Avaliar(desligada, Pessoa(NivelAcesso.Operador), AcaoSobreUsuario.Desativar, 2))
            .Should().Be(H);
    }

    [Fact]
    public void Administracao_nao_da_perfil_de_nivel_Admin()
    {
        Descrever(PoliticaDeHierarquia.Avaliar(Administracao, Pessoa(NivelAcesso.Operador), AcaoSobreUsuario.TrocarPerfil, 2, NivelAcesso.Admin))
            .Should().Be(H);
        Descrever(PoliticaDeHierarquia.Avaliar(Administracao, Pessoa(NivelAcesso.Operador), AcaoSobreUsuario.TrocarPerfil, 2, NivelAcesso.Gerente))
            .Should().Be(P);
    }

    [Fact]
    public void Marcar_exige_conta_provada()
    {
        var soPeloWhatsApp = Pessoa(NivelAcesso.Admin) with { ContaProvada = false };
        Descrever(PoliticaDeHierarquia.Avaliar(Dona, soPeloWhatsApp, AcaoSobreUsuario.MarcarDona, 2))
            .Should().Be(CodigosDaHierarquia.DonaContaNaoProvada);
        Descrever(PoliticaDeHierarquia.Avaliar(Suporte, soPeloWhatsApp, AcaoSobreUsuario.MarcarDona, 0))
            .Should().Be(CodigosDaHierarquia.DonaContaNaoProvada);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Marcar_exige_vinculo_e_usuario_ativos(bool vinculoAtivo, bool usuarioAtivo)
    {
        var alvo = Pessoa(NivelAcesso.Admin) with { VinculoAtivo = vinculoAtivo, UsuarioAtivo = usuarioAtivo };
        Descrever(PoliticaDeHierarquia.Avaliar(Suporte, alvo, AcaoSobreUsuario.MarcarDona, 1))
            .Should().Be(NA);
    }

    [Fact]
    public void Suporte_nunca_e_alvo_da_marca_nem_pela_dona() =>
        Descrever(PoliticaDeHierarquia.Avaliar(Dona, Suporte, AcaoSobreUsuario.MarcarDona, 2)).Should().Be(DS);

    [Fact]
    public void So_suporte_mexe_no_acesso_do_suporte() =>
        Descrever(PoliticaDeHierarquia.Avaliar(Dona, Suporte, AcaoSobreUsuario.Desativar, 2)).Should().Be(H);

    [Fact]
    public void Com_zero_donas_so_o_suporte_marca_a_primeira()
    {
        var alvo = Pessoa(NivelAcesso.Admin);
        Descrever(PoliticaDeHierarquia.Avaliar(Suporte, alvo, AcaoSobreUsuario.MarcarDona, 0)).Should().Be(P);
        Descrever(PoliticaDeHierarquia.Avaliar(Administracao, alvo, AcaoSobreUsuario.MarcarDona, 0)).Should().Be(H);
    }

    [Theory]
    [InlineData(NivelAcesso.Gerente)]
    [InlineData(null)]
    public void Criar_dona_exige_perfil_de_nivel_Admin(NivelAcesso? nivel)
    {
        var nova = ParticipanteDaHierarquia.Novo(nivel, dona: true);
        Descrever(PoliticaDeHierarquia.Avaliar(Suporte, nova, AcaoSobreUsuario.Criar, 0, nivel)).Should().Be(NA);
        Descrever(PoliticaDeHierarquia.Avaliar(Dona, nova, AcaoSobreUsuario.Criar, 1, nivel)).Should().Be(NA);
    }

    [Fact]
    public void Anonimizar_a_ultima_dona_e_recusado_e_com_outra_dona_passa()
    {
        Descrever(PoliticaDeHierarquia.AvaliarAnonimizacao(Dona, donasAtivas: 1)).Should().Be(CodigosDaHierarquia.UltimaDona);
        Descrever(PoliticaDeHierarquia.AvaliarAnonimizacao(Dona, donasAtivas: 2)).Should().Be(P);
        Descrever(PoliticaDeHierarquia.AvaliarAnonimizacao(Administracao, donasAtivas: 1)).Should().Be(P);
    }

    [Theory]
    [InlineData("suporte", NivelAcesso.Admin, true)]
    [InlineData("dona", NivelAcesso.Admin, true)]
    [InlineData("administracao", NivelAcesso.Admin, false)]
    [InlineData("administracao", NivelAcesso.Gerente, true)]
    [InlineData("administracao", NivelAcesso.Operador, true)]
    public void Quem_pode_dar_cada_nivel(string ator, NivelAcesso nivel, bool pode) =>
        PoliticaDeHierarquia.PodeDarNivel(Atores[ator], nivel).Should().Be(pode);

    [Fact]
    public void Gerencia_nao_da_perfil_nenhum() =>
        PoliticaDeHierarquia.PodeDarNivel(Pessoa(NivelAcesso.Gerente), NivelAcesso.Operador).Should().BeFalse();

    private static ParticipanteDaHierarquia Pessoa(NivelAcesso nivel, bool dona = false) =>
        new(Guid.NewGuid(), nivel, dona, VinculoAtivo: true, UsuarioAtivo: true, ContaProvada: true);

    private static string Descrever(DecisaoDaHierarquia decisao) =>
        decisao.Permitido ? (decisao.ExigeMotivo ? M : P) : decisao.Codigo!;
}
