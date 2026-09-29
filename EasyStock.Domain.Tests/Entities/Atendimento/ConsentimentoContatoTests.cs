using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>
/// Consentimento do cliente final por canal e finalidade (S38, ADR-0051): marketing exige opt-in
/// no canal; transacional passa, salvo revogação explícita; "SAIR" revoga só o canal de onde veio.
/// </summary>
public class ConsentimentoContatoTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cliente = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    private static ConsentimentoContato Marketing(CanalConversa canal, SituacaoConsentimento situacao) =>
        ConsentimentoContato.Registrar(Empresa, Cliente, canal, FinalidadeContato.Marketing, situacao, "console", Agora);

    [Fact]
    public void MarketingSemConsentimento_Nega()
    {
        PoliticaConsentimento.PodeEnviar([], CanalConversa.Sms, FinalidadeContato.Marketing).Should().BeFalse();
    }

    [Fact]
    public void MarketingConcedidoNoCanal_Permite_EmOutroCanalNao()
    {
        var consentimentos = new[] { Marketing(CanalConversa.Email, SituacaoConsentimento.Concedido) };

        PoliticaConsentimento.PodeEnviar(consentimentos, CanalConversa.Email, FinalidadeContato.Marketing).Should().BeTrue();
        PoliticaConsentimento.PodeEnviar(consentimentos, CanalConversa.WhatsApp, FinalidadeContato.Marketing).Should().BeFalse();
    }

    [Fact]
    public void TransacionalSemRegistro_Permite_RevogadoNega()
    {
        PoliticaConsentimento.PodeEnviar([], CanalConversa.WhatsApp, FinalidadeContato.Transacional).Should().BeTrue();

        var revogado = ConsentimentoContato.Registrar(Empresa, Cliente, CanalConversa.WhatsApp,
            FinalidadeContato.Transacional, SituacaoConsentimento.Revogado, "cliente", Agora);
        PoliticaConsentimento.PodeEnviar([revogado], CanalConversa.WhatsApp, FinalidadeContato.Transacional).Should().BeFalse();
    }

    [Fact]
    public void SairRevogaSoOCanal()
    {
        var whats = Marketing(CanalConversa.WhatsApp, SituacaoConsentimento.Concedido);
        var email = Marketing(CanalConversa.Email, SituacaoConsentimento.Concedido);

        whats.Alterar(SituacaoConsentimento.Revogado, "palavra_sair", Agora.AddMinutes(5));

        PoliticaConsentimento.PodeEnviar([whats, email], CanalConversa.WhatsApp, FinalidadeContato.Marketing).Should().BeFalse();
        PoliticaConsentimento.PodeEnviar([whats, email], CanalConversa.Email, FinalidadeContato.Marketing).Should().BeTrue();
        whats.Origem.Should().Be("palavra_sair");
        whats.AtualizadoEm.Should().Be(Agora.AddMinutes(5));
    }

    [Theory]
    [InlineData("SAIR", true)]
    [InlineData("  sair ", true)]
    [InlineData("Sair.", true)]
    [InlineData("parar", true)]
    [InlineData("STOP", true)]
    [InlineData("pârar!", true)]
    [InlineData("quero sair do grupo", false)]
    [InlineData("sairá hoje?", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EhPedidoDeOptOut_SoAPalavraSozinha(string? texto, bool esperado)
    {
        PoliticaConsentimento.EhPedidoDeOptOut(texto).Should().Be(esperado);
    }

    [Fact]
    public void Registrar_SemCliente_Lanca()
    {
        var act = () => ConsentimentoContato.Registrar(Empresa, Guid.Empty, CanalConversa.Email,
            FinalidadeContato.Marketing, SituacaoConsentimento.Concedido, "console", Agora);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }
}
