using FluentAssertions;

namespace EasyStock.Api.UnitTests.Data;

/// <summary>
/// Garante que os 21 templates HTML embeddados como recurso continuam declarados
/// no <c>EasyStock.Api.csproj</c> e visíveis via reflection. Se alguém renomear/remover
/// um arquivo .html sem atualizar o seed, esse teste fica vermelho.
/// </summary>
public class EmailTemplateCompletenessTests
{
    private static readonly string[] ExpectedTemplates =
    {
        "assinatura_expirando_email_v1",
        "assinatura_expirada_dunning_email_v1",
        "alerta_estoque_critico_email_v1",
        "produto_vencendo_email_v1",
        "ticket_respondido_admin_email_v1",
        "sla_violado_email_v1",
        "bug_fix_criado_email_v1",
        "reset_senha_email_v1",
        "confirmacao_email_email_v1",
        "produto_vencido_email_v1",
        "ticket_criado_email_v1",
        "relatorio_pronto_email_v1",
        "relatorio_falhou_email_v1",
        "ticket_atribuido_email_v1",
        "sla_proximo_vencer_email_v1",
        "convite_csat_email_v1",
        "fatura_criada_email_v1",
        "fatura_vencendo_email_v1",
        "fatura_paga_email_v1",
        "fatura_vencida_email_v1",
        "pagamento_falhou_email_v1",
    };

    [Fact]
    public void All21EmailTemplates_AreEmbeddedAsResources()
    {
        // Arrange: localiza o assembly da Api via tipo conhecido (não-genérico).
        var apiAssembly = typeof(EasyStock.Api.Http.EasyStockControllerBase).Assembly;
        var resourceNames = apiAssembly.GetManifestResourceNames();

        // Act: monta lista esperada com prefixo de LogicalName.
        var expectedFull = ExpectedTemplates
            .Select(name => $"EmailTemplates.{name}.html")
            .ToArray();

        // Assert: nenhum dos 21 esperados pode estar faltando.
        var missing = expectedFull.Except(resourceNames).ToArray();
        missing.Should().BeEmpty(
            "EasyStock.Api.csproj deve incluir <EmbeddedResource Include=\"Data\\Templates\\Email\\*.html\"> " +
            $"com <LogicalName>EmailTemplates.%(Filename).html</LogicalName>. Faltando: {string.Join(", ", missing)}");
    }
}
