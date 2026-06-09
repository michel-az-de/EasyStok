using EasyStock.Api.Data;
using EasyStock.Infra.Notifications.Templating;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// Dry-run / smoke de render — GATE pre-deploy do ADR-0030 (P0-1a). Com o publish ressuscitado
/// (fix do Add-then-Update), o avaliador + dispatcher passam a RENDERIZAR e ENVIAR templates de
/// job (fatura/assinatura) que NUNCA renderizaram em producao (o publish sempre crashava antes).
/// Este teste renderiza cada corpo de email embeddado via Scriban com um superset de variaveis e
/// garante que NAO ha erro de sintaxe Scriban (variavel ausente vira "" no Scriban — logo isto
/// pega template quebrado, nao payload incompleto). Vermelho aqui = template que dispararia
/// quebrado ao cliente na estreia.
/// </summary>
public class EmailTemplateRenderSmokeTests
{
    private static readonly string[] Templates =
    {
        // Jobs financeiros / assinatura (reativados pelo fix — maior risco customer-facing)
        "fatura_criada_email_v1", "fatura_vencendo_email_v1", "fatura_paga_email_v1",
        "fatura_vencida_email_v1", "pagamento_falhou_email_v1",
        "assinatura_expirando_email_v1", "assinatura_expirada_dunning_email_v1",
        // Collectors de estoque
        "alerta_estoque_critico_email_v1", "produto_vencendo_email_v1", "produto_vencido_email_v1",
        // Helpdesk
        "ticket_criado_email_v1", "ticket_respondido_admin_email_v1", "ticket_atribuido_email_v1",
        "sla_proximo_vencer_email_v1", "sla_violado_email_v1", "bug_fix_criado_email_v1",
        "convite_csat_email_v1",
        // Relatorios / auth
        "relatorio_pronto_email_v1", "relatorio_falhou_email_v1",
        "reset_senha_email_v1", "confirmacao_email_email_v1",
    };

    public static IEnumerable<object[]> TodosOsTemplates() => Templates.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(TodosOsTemplates))]
    public async Task Template_de_email_renderiza_sem_erro_de_sintaxe(string codigo)
    {
        var body = EmailTemplateLoader.LoadBody(codigo);
        body.Should().NotBeNullOrWhiteSpace($"o template {codigo} deve estar embeddado");

        var renderer = new ScribanRenderer(NullLogger<ScribanRenderer>.Instance);

        // Lanca InvalidOperationException em erro de sintaxe Scriban (ver ScribanRendererTests).
        var render = await renderer.RenderizarAsync(body, VariaveisRepresentativas(), htmlEscape: true);

        render.Should().NotBeNullOrWhiteSpace($"o corpo renderizado de {codigo} nao pode sair vazio");
    }

    // Superset das variaveis usadas pelos templates. Variavel ausente vira "" no Scriban (nao
    // lanca), entao o objetivo aqui e exercitar o caminho de render, nao validar payload por tipo.
    private static Dictionary<string, object?> VariaveisRepresentativas() => new()
    {
        ["titulo"] = "Exemplo de titulo",
        ["titulo_ticket"] = "Ticket exemplo",
        ["empresaNome"] = "Acme Ltda",
        ["clienteNome"] = "Cliente Exemplo",
        ["nome"] = "Felipe",
        ["prioridade"] = "Alta",
        ["nivel"] = "N1",
        ["nivelOrigem"] = "N1",
        ["nivelDestino"] = "N2",
        ["statusAntes"] = "Aberto",
        ["statusDepois"] = "Resolvido",
        ["motivo"] = "Exemplo de motivo",
        ["severidade"] = "Media",
        ["componente"] = "Caixa",
        ["tipoSla"] = "Resposta",
        ["percentual"] = 80,
        ["minutosRestantes"] = 30,
        ["dias"] = 7,
        ["valor"] = "1.234,56",
        ["numero"] = "2026-000123",
        ["dataVencimento"] = "08/06/2026",
        ["planoNome"] = "Pro",
        ["link"] = "https://app.exemplo/x",
        ["url"] = "https://app.exemplo/x",
        ["avaliarUrl"] = "https://app.exemplo/avaliar",
    };
}
