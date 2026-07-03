using FluentAssertions;

namespace EasyStock.Web.UnitTests.Services;

/// <summary>
/// Trava o espelho do HMAC de handoff de impersonation (issue 802): Admin assina,
/// Web valida — os dois lados nao compartilham projeto, entao a formula ("{token}|{ts}",
/// HMAC-SHA256, hex) e duplicada e este teste garante que nao divergem.
/// </summary>
public class ImpersonationHandoffTests
{
    [Fact]
    public void Assinatura_do_Admin_e_a_esperada_pelo_Web()
    {
        const string secret = "segredo-de-teste";
        const string token = "jwt.de.teste";
        const long ts = 1_760_000_000;

        var doAdmin = EasyStock.Admin.Pages.Tenants.IndexModel.AssinarHandoff(secret, token, ts);
        var doWeb = EasyStock.Web.Controllers.AuthController.ComputarAssinaturaHandoff(secret, token, ts);

        doAdmin.Should().Be(doWeb);
        doAdmin.Should().MatchRegex("^[0-9A-F]{64}$");
    }

    [Fact]
    public void Assinatura_muda_com_token_e_ts()
    {
        const string secret = "segredo-de-teste";
        var a = EasyStock.Admin.Pages.Tenants.IndexModel.AssinarHandoff(secret, "t1", 1);
        var b = EasyStock.Admin.Pages.Tenants.IndexModel.AssinarHandoff(secret, "t2", 1);
        var c = EasyStock.Admin.Pages.Tenants.IndexModel.AssinarHandoff(secret, "t1", 2);
        a.Should().NotBe(b);
        a.Should().NotBe(c);
    }
}
