using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Launcher;
using EasyStock.Web.Navigation;
using EasyStock.Web.Services;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Navigation;

/// <summary>
/// Montagem do portal (ADR-0046) sem HttpContext nem Api: saudacao por faixa de hora,
/// tom do cabecalho, badges dos cards. O portal so mostra numero que veio de dado real —
/// a primeira versao exibia "2 contas a vencer hoje" sem consultar nada (#1007).
/// </summary>
public class LauncherViewModelBuilderTests
{
    private static readonly DateTime Manha = new(2026, 8, 8, 9, 0, 0);

    private static LauncherViewModel Montar(
        DateTime? agora = null,
        string? nome = "Felipe Azevedo",
        DashboardResumoApi? dash = null,
        ResumoDiaApi? dia = null,
        MenuBadges? badges = null,
        DashboardFinanceiroApi? financeiro = null) =>
        LauncherViewModelBuilder.Montar(
            agora ?? Manha, nome, dash, dia, badges ?? MenuBadges.Zero,
            [], ModuloDefinition.Modulos, financeiro);

    // ── saudacao ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(6, "Bom dia")]
    [InlineData(11, "Bom dia")]
    [InlineData(12, "Boa tarde")]
    [InlineData(17, "Boa tarde")]
    [InlineData(18, "Boa noite")]
    [InlineData(23, "Boa noite")]
    public void Saudacao_segue_a_faixa_de_hora(int hora, string esperado)
    {
        var vm = Montar(agora: new DateTime(2026, 8, 8, hora, 0, 0));

        vm.Saudacao.Should().StartWith(esperado);
    }

    [Fact]
    public void Saudacao_usa_so_o_primeiro_nome()
    {
        Montar(nome: "Felipe Azevedo Lima").Saudacao.Should().Contain("Felipe").And.NotContain("Azevedo");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nome_ausente_nao_vira_saudacao_quebrada(string? nome)
    {
        // Antes saia "Bom dia, . 👋" quando o claim de nome nao vinha no token.
        var vm = Montar(nome: nome);

        vm.Saudacao.Should().NotContain(", .");
        vm.Saudacao.Should().StartWith("Bom dia");
    }

    // ── tom e mensagem do cabecalho ──────────────────────────────────

    [Fact]
    public void Sem_dashboard_o_tom_fica_neutro()
    {
        var vm = Montar(dash: null);

        vm.StatusTone.Should().Be("ok");
        vm.StatusMsg.Should().BeEmpty();
    }

    [Fact]
    public void Alertas_de_estoque_deixam_o_tom_critico()
    {
        var vm = Montar(
            dash: new DashboardResumoApi { AlertasEstoqueBaixo = 2, AlertasVencidos = 1 },
            badges: new MenuBadges(0, 2, 1));

        vm.StatusTone.Should().Be("critical");
        vm.StatusMsg.Should().Be("3 alertas precisam de atenção.");
    }

    [Fact]
    public void Mensagem_respeita_o_singular()
    {
        var vm = Montar(
            dash: new DashboardResumoApi { AlertasEstoqueBaixo = 1 },
            badges: new MenuBadges(0, 1, 0));

        vm.StatusMsg.Should().Be("1 alerta precisa de atenção.");
    }

    [Fact]
    public void Itens_parados_sem_alerta_critico_deixam_o_tom_de_aviso()
    {
        var vm = Montar(dash: new DashboardResumoApi { AlertasItensParados = 4 });

        vm.StatusTone.Should().Be("warning");
        vm.StatusMsg.Should().Contain("4 itens parados");
    }

    [Fact]
    public void Estoque_sem_alerta_nenhum_e_reportado_como_saudavel()
    {
        var vm = Montar(dash: new DashboardResumoApi());

        vm.StatusTone.Should().Be("ok");
        vm.StatusMsg.Should().Contain("saudável");
    }

    // ── cards de modulo ──────────────────────────────────────────────

    [Fact]
    public void Portal_lista_todos_os_modulos()
    {
        Montar().Modulos.Select(m => m.Key).Should().Equal(ModuloDefinition.Modulos.Select(m => m.Key));
    }

    [Fact]
    public void Card_de_operacao_conta_pedidos_em_aberto()
    {
        var card = Montar(badges: new MenuBadges(PedidosAbertos: 5, 0, 0))
            .Modulos.Single(m => m.Key == "operacao");

        card.BadgeCount.Should().Be(5);
        card.BadgeType.Should().Be("crit");
        card.StatusText.Should().Be("5 pedidos em aberto");
    }

    [Fact]
    public void Card_de_producao_soma_criticos_e_vencidos()
    {
        var card = Montar(badges: new MenuBadges(0, ProdutosCriticos: 2, LotesVencidos: 3))
            .Modulos.Single(m => m.Key == "producao");

        card.BadgeCount.Should().Be(5);
        card.BadgeType.Should().Be("warn");
        card.StatusText.Should().Be("5 alertas");
    }

    [Fact]
    public void Card_sem_fonte_de_dado_nao_inventa_numero()
    {
        // Financeiro fora do ar: nada de badge zero — zero seria uma afirmacao que nao medimos.
        var card = Montar(badges: new MenuBadges(9, 9, 9)).Modulos.Single(m => m.Key == "financeiro");

        card.BadgeCount.Should().Be(0);
        card.StatusText.Should().BeEmpty();
    }

    [Fact]
    public void Card_de_financeiro_conta_parcelas_vencidas_hoje()
    {
        var card = Montar(financeiro: new DashboardFinanceiroApi { QtdParcelasVencidasHoje = 3 })
            .Modulos.Single(m => m.Key == "financeiro");

        card.BadgeCount.Should().Be(3);
        card.BadgeType.Should().Be("warn");
        card.StatusText.Should().Be("3 parcelas vencidas hoje");
    }

    [Fact]
    public void Financeiro_sem_parcela_vencida_fica_em_dia()
    {
        Montar(financeiro: new DashboardFinanceiroApi()).Modulos
            .Single(m => m.Key == "financeiro").StatusText.Should().Be("Em dia");
    }

    // ── missoes de hoje ──────────────────────────────────────────────

    [Fact]
    public void Missoes_saem_dos_dados_que_o_sistema_ja_tem()
    {
        var vm = Montar(
            badges: new MenuBadges(PedidosAbertos: 3, ProdutosCriticos: 1, LotesVencidos: 2),
            dia: new ResumoDiaApi(),
            financeiro: new DashboardFinanceiroApi { QtdParcelasVencidasHoje = 4 });

        vm.Missoes.Select(m => m.Chave).Should().Equal(
            "pedidos", "validade", "estoque-critico", "caixa", "parcelas-vencidas");
        vm.Missoes.Single(m => m.Chave == "pedidos").Pendentes.Should().Be(3);
        vm.Missoes.Single(m => m.Chave == "validade").Pendentes.Should().Be(2);
        vm.Missoes.Single(m => m.Chave == "estoque-critico").Pendentes.Should().Be(1);
        vm.Missoes.Single(m => m.Chave == "parcelas-vencidas").Pendentes.Should().Be(4);
        vm.Missoes.Should().OnlyContain(m => !m.Concluida);
    }

    [Fact]
    public void Missao_conclui_quando_o_dado_zera()
    {
        var vm = Montar(dia: new ResumoDiaApi { CaixaAbertaHoje = true }, badges: MenuBadges.Zero,
            financeiro: new DashboardFinanceiroApi());

        vm.Missoes.Should().OnlyContain(m => m.Concluida);
        vm.Missoes.Should().OnlyContain(m => m.Pendentes == 0);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Missao_do_caixa_conclui_com_caixa_aberto_ou_ja_fechado(bool aberta, bool fechada)
    {
        var vm = Montar(dia: new ResumoDiaApi { CaixaAbertaHoje = aberta, CaixaFechadaHoje = fechada });

        vm.Missoes.Single(m => m.Chave == "caixa").Concluida.Should().BeTrue();
    }

    [Fact]
    public void Caixa_nao_tocado_hoje_e_missao_pendente()
    {
        var missao = Montar(dia: new ResumoDiaApi()).Missoes.Single(m => m.Chave == "caixa");

        missao.Concluida.Should().BeFalse();
        missao.Pendentes.Should().Be(1);
    }

    [Fact]
    public void Fonte_indisponivel_tira_a_missao_em_vez_de_marca_la_como_feita()
    {
        // Missao ausente e honesto; missao "concluida" sem dado seria mentira.
        var vm = Montar(dia: null, financeiro: null);

        vm.Missoes.Select(m => m.Chave).Should().NotContain("caixa");
        vm.Missoes.Select(m => m.Chave).Should().NotContain("parcelas-vencidas");
        vm.Missoes.Should().HaveCount(3);
    }

    [Fact]
    public void Cards_sem_alerta_reportam_tudo_em_ordem()
    {
        var vm = Montar(badges: MenuBadges.Zero);

        vm.Modulos.Single(m => m.Key == "operacao").StatusText.Should().Be("Tudo em ordem");
        vm.Modulos.Single(m => m.Key == "producao").StatusText.Should().Be("Tudo em ordem");
    }

    // ── pulso do dia ─────────────────────────────────────────────────

    [Fact]
    public void Pulso_do_dia_espelha_o_resumo()
    {
        var vm = Montar(dia: new ResumoDiaApi
        {
            FaturamentoHoje = 1234.50m,
            PedidosEntreguesHoje = 7,
            CaixaAbertaHoje = true,
            SaldoCaixaAtual = 300m,
            PedidosPendentes = 2,
        });

        vm.FaturamentoHoje.Should().Be(1234.50m);
        vm.PedidosEntreguesHoje.Should().Be(7);
        vm.CaixaAbertoHoje.Should().BeTrue();
        vm.SaldoCaixaAtual.Should().Be(300m);
        vm.PedidosPendentes.Should().Be(2);
    }

    [Fact]
    public void Sem_resumo_do_dia_o_pulso_fica_zerado()
    {
        var vm = Montar(dia: null);

        vm.FaturamentoHoje.Should().Be(0);
        vm.PedidosEntreguesHoje.Should().Be(0);
        vm.CaixaAbertoHoje.Should().BeFalse();
    }
}
