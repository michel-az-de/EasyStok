using EasyStock.Api.Data;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Notifications.Templating;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// #2074: o catálogo que o seed grava cobre todo tipo que o código publica. Tipo publicado sem rotina fecha em silêncio
/// ("Nenhuma rotina ativa"); canal da rotina sem template é pulado; template com variável que o produtor não envia sai
/// com buraco. Estes testes pegam os três antes de chegar à loja.
/// </summary>
public class CatalogoMensagensCompletoTests
{
    /// <summary>Tipos que têm produtor em código de produção (medido em 26/10, issue #2074).</summary>
    public static IEnumerable<object[]> TiposComProdutor() => new[]
    {
        TipoEventoNotificacao.ResetSenha, TipoEventoNotificacao.SenhaAlterada, TipoEventoNotificacao.ConviteAcesso,
        TipoEventoNotificacao.ContatoAlterado, TipoEventoNotificacao.ConfirmacaoEmail,
        TipoEventoNotificacao.IncidenteSistema, TipoEventoNotificacao.PrazoEstourado, TipoEventoNotificacao.ResumoDiario,
        TipoEventoNotificacao.ProdutoVencendo, TipoEventoNotificacao.CaixaAbertoEsquecido,
        TipoEventoNotificacao.PedidoAgendadoHoje, TipoEventoNotificacao.PedidoAgendadoEm1Hora,
        TipoEventoNotificacao.PedidoAgendadoEm10Minutos, TipoEventoNotificacao.RelatorioPronto,
        TipoEventoNotificacao.RelatorioFalhou, TipoEventoNotificacao.ConversaEscalada, TipoEventoNotificacao.LembreteVencido,
        TipoEventoNotificacao.LojaFechadaNoHorario, TipoEventoNotificacao.ContatoNovoNoBalcao,
        TipoEventoNotificacao.PedidoPagoNoBalcao, TipoEventoNotificacao.PedidoMudouEtapaNoBalcao,
        TipoEventoNotificacao.CobrancaAguardandoEquipe, TipoEventoNotificacao.PagamentoEmMediacao,
        TipoEventoNotificacao.PagamentoSemAvisoAoCliente,
        TipoEventoNotificacao.ContaPagarVencendo, TipoEventoNotificacao.ContaPagarVencida,
        TipoEventoNotificacao.ContaReceberVencendo, TipoEventoNotificacao.ContaReceberVencida,
        TipoEventoNotificacao.PedidoPagoConfirmado, TipoEventoNotificacao.PedidoEmPreparo,
        TipoEventoNotificacao.PedidoSaiuParaEntrega, TipoEventoNotificacao.PedidoEntregue,
        TipoEventoNotificacao.PedidoReagendado, TipoEventoNotificacao.ComandaAtualizada,
        TipoEventoNotificacao.AvaliacaoSolicitada, TipoEventoNotificacao.ReembolsoEfetuado,
    }.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(TiposComProdutor))]
    public void Tipo_com_produtor_tem_rotina_e_template_em_cada_canal(TipoEventoNotificacao tipo)
    {
        var rotina = NotificacoesGlobaisSeed.BuildDefaultRotinas().Should()
            .ContainSingle(r => r.TipoEvento == tipo, $"{tipo} é publicado e sem rotina fecha em silêncio").Subject;
        // O resumo diário nasce como molde inativo: a N12 liga por empresa.
        if (tipo != TipoEventoNotificacao.ResumoDiario)
            rotina.Ativa.Should().BeTrue($"a rotina de {tipo} precisa nascer ativa");

        var templates = NotificacoesGlobaisSeed.BuildDefaultTemplates().ToList();
        templates.Should().Contain(t => t.Codigo == rotina.TemplateCodigo, $"a rotina de {tipo} aponta para um template");

        var canais = JsonSerializer.Deserialize<string[]>(rotina.CanaisOrdemFallbackJson)!
            .Select(Enum.Parse<CanalNotificacao>);
        foreach (var canal in canais)
            templates.Should().Contain(t => t.TipoEvento == tipo && t.Canal == canal,
                $"o canal {canal} da rotina de {tipo} sem template é pulado");
    }

    [Theory]
    [InlineData(TipoEventoNotificacao.PrazoEstourado)]
    [InlineData(TipoEventoNotificacao.ResumoDiario)]
    public void Aviso_de_plataforma_operacional_chega_ao_sino(TipoEventoNotificacao tipo)
    {
        NotificacoesGlobaisSeed.BuildDefaultTemplates().Should()
            .Contain(t => t.TipoEvento == tipo && t.Canal == CanalNotificacao.InApp,
                "o InApp automático da categoria Operacional só entra com template InApp do tipo");
    }

    [Fact]
    public void Todo_html_embutido_e_usado_por_um_template_do_catalogo()
    {
        var embutidos = typeof(EmailTemplateLoader).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("EmailTemplates.", StringComparison.Ordinal) && n.EndsWith(".html", StringComparison.Ordinal))
            .Select(n => n["EmailTemplates.".Length..^".html".Length])
            .ToList();
        var corpos = NotificacoesGlobaisSeed.BuildDefaultTemplates()
            .Where(t => t.Canal == CanalNotificacao.Email)
            .Select(t => t.CorpoTemplate)
            .ToList();

        foreach (var arquivo in embutidos)
            corpos.Should().Contain(EmailTemplateLoader.LoadBody(arquivo), $"{arquivo}.html está embutido e ninguém o usa");
    }

    [Fact]
    public async Task Todo_template_de_email_do_catalogo_renderiza()
    {
        var renderer = new ScribanRenderer(NullLogger<ScribanRenderer>.Instance);
        foreach (var t in NotificacoesGlobaisSeed.BuildDefaultTemplates().Where(t => t.Canal == CanalNotificacao.Email))
        {
            var corpo = await renderer.RenderizarAsync(t.CorpoTemplate, new Dictionary<string, object?>(), htmlEscape: true);
            corpo.Should().NotBeNullOrWhiteSpace($"{t.Codigo} não pode renderizar vazio");
        }
    }

    [Theory]
    [InlineData(TipoEventoNotificacao.ContaPagarVencendo, CanalNotificacao.InApp)]
    [InlineData(TipoEventoNotificacao.ContaPagarVencida, CanalNotificacao.Email)]
    [InlineData(TipoEventoNotificacao.ContaReceberVencendo, CanalNotificacao.Email)]
    [InlineData(TipoEventoNotificacao.ContaReceberVencida, CanalNotificacao.InApp)]
    public async Task Conta_vencendo_mostra_descricao_valor_e_data_do_payload_do_job(TipoEventoNotificacao tipo, CanalNotificacao canal)
    {
        var rotina = NotificacoesGlobaisSeed.BuildDefaultRotinas().Single(r => r.TipoEvento == tipo);
        rotina.ParametrosJson.Should().Contain("\"audiencia\":\"admins\"", "o job publica sem destinatário; quem recebe são os donos");

        var template = NotificacoesGlobaisSeed.BuildDefaultTemplates().Single(t => t.TipoEvento == tipo && t.Canal == canal);
        var vars = new Dictionary<string, object?>
        {
            ["descricao_texto"] = "Aluguel da cozinha", ["numero"] = 2, ["valor_texto"] = "R$ 1.250,00",
            ["vencimento_texto"] = "28/10/2026", ["situacao_texto"] = "vence em 3 dias", ["titulo_texto"] = "Conta a pagar vencendo",
        };
        var renderer = new ScribanRenderer(NullLogger<ScribanRenderer>.Instance);
        var corpo = await renderer.RenderizarAsync(template.CorpoTemplate, vars, htmlEscape: canal == CanalNotificacao.Email);
        corpo.Should().Contain("Aluguel da cozinha").And.Contain("R$ 1.250,00").And.Contain("28/10/2026");
        (await renderer.RenderizarAsync(template.AssuntoTemplate, vars)).Should().Contain("Aluguel da cozinha");
    }

    [Fact]
    public async Task Produto_vencendo_usa_as_chaves_que_o_coletor_envia()
    {
        var template = NotificacoesGlobaisSeed.BuildDefaultTemplates()
            .Where(t => t.TipoEvento == TipoEventoNotificacao.ProdutoVencendo && t.Canal == CanalNotificacao.Email)
            .MaxBy(t => t.Versao)!;
        // Chaves de ColetorProdutosVencendo.
        var vars = new Dictionary<string, object?>
        {
            ["nomeProduto"] = "Bolo de cenoura", ["expiraEm"] = "30/10/2026", ["diasRestantes"] = 4, ["quantidade"] = 6,
        };
        var renderer = new ScribanRenderer(NullLogger<ScribanRenderer>.Instance);
        (await renderer.RenderizarAsync(template.CorpoTemplate, vars, htmlEscape: true))
            .Should().Contain("Bolo de cenoura").And.Contain("30/10/2026");
        (await renderer.RenderizarAsync(template.AssuntoTemplate, vars)).Should().Contain("Bolo de cenoura").And.Contain("4");
    }

    [Theory]
    [InlineData("relatorio_pronto_email_v1")]
    [InlineData("relatorio_falhou_email_v1")]
    public void Relatorio_nao_depende_de_variavel_que_o_worker_nao_envia(string codigo)
    {
        NotificacoesGlobaisSeed.BuildDefaultTemplates().Single(t => t.Codigo == codigo)
            .CorpoTemplate.Should().NotContain("primeiroNome");
    }

    [Fact]
    public void Convite_diz_a_validade_e_o_que_fazer_depois()
    {
        var convite = NotificacoesGlobaisSeed.BuildDefaultTemplates()
            .Single(t => t.TipoEvento == TipoEventoNotificacao.ConviteAcesso && t.Canal == CanalNotificacao.Email);
        // O "entrar com Google" só entra junto com o botão na tela do convite (#2070).
        convite.CorpoTemplate.Should().Contain("{{ expira_em_dias }} dias").And.Contain("convite novo").And.NotContain("Google");
        convite.Versao.Should().BeGreaterThan(2, "o seed só atualiza o banco existente quando a versão sobe");
    }

    [Theory]
    [InlineData("comanda_atualizada_whatsapp_v1", "pedido_atualizado")]
    [InlineData("pedido_reagendado_whatsapp_v1", "pedido_reagendado")]
    public void Aviso_ao_cliente_tem_modelo_para_fora_da_janela(string codigo, string modelo)
    {
        var t = NotificacoesGlobaisSeed.BuildDefaultTemplates().Single(x => x.Codigo == codigo);
        t.MetadadosJson.Should().Contain($"\"template\":\"{modelo}\"").And.Contain("\"idioma\":\"pt_BR\"");
        t.Versao.Should().BeGreaterThan(1, "o seed só atualiza o banco existente quando a versão sobe");
    }

    [Theory]
    [InlineData(TipoEventoNotificacao.ContaPagarVencendo, "d3", "Conta a pagar vencendo", "vence em 3 dias")]
    [InlineData(TipoEventoNotificacao.ContaReceberVencendo, "d1", "Conta a receber vencendo", "vence amanhã")]
    [InlineData(TipoEventoNotificacao.ContaPagarVencida, "vencida", "Conta a pagar vencida", "venceu em 28/10/2026")]
    public async Task Payload_do_job_de_contas_preenche_o_template(
        TipoEventoNotificacao tipo, string variante, string titulo, string situacao)
    {
        var json = EasyStock.Api.BackgroundServices.PayloadVencimentoConta.Montar(
            tipo, Guid.NewGuid(), Guid.NewGuid(), 2, 1250m, new DateTime(2026, 10, 28), "Aluguel da cozinha", variante);

        var vars = EasyStock.Application.Services.Notifications.ConstrutorMensagemOutbox.LerVariaveis(json, null);
        var template = NotificacoesGlobaisSeed.BuildDefaultTemplates()
            .Single(t => t.TipoEvento == tipo && t.Canal == CanalNotificacao.InApp);
        var renderer = new ScribanRenderer(NullLogger<ScribanRenderer>.Instance);

        (await renderer.RenderizarAsync(template.AssuntoTemplate, vars)).Should().Be($"{titulo}: Aluguel da cozinha");
        (await renderer.RenderizarAsync(template.CorpoTemplate, vars)).Should()
            .Contain(situacao).And.Contain("1.250,00").And.Contain("28/10/2026").And.Contain("parcela 2");
    }

    [Fact]
    public void Payload_do_job_sem_descricao_usa_o_lado_da_conta()
    {
        var json = EasyStock.Api.BackgroundServices.PayloadVencimentoConta.Montar(
            TipoEventoNotificacao.ContaReceberVencida, Guid.NewGuid(), Guid.NewGuid(), 1, 10m, new DateTime(2026, 10, 1), null, "vencida");
        json.Should().Contain("\"descricao_texto\":\"Conta a receber\"");
    }
}
