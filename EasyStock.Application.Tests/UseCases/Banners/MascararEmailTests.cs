using EasyStock.Application.UseCases.Banners;

namespace EasyStock.Application.Tests.UseCases.Banners;

/// <summary>
/// Máscara de e-mail do console de recebimento (#875) — minimização LGPD. Preserva 1ª e
/// última letra do local + domínio; o completo só sai pelo endpoint auditado de revelação.
/// </summary>
public class MascararEmailTests
{
    [Theory]
    [InlineData("admin@easystok.com", "a***n@easystok.com")]
    [InlineData("felipe.azevedo@gmail.com", "f*****o@gmail.com")] // local longo → 5 estrelas (cap)
    [InlineData("jo@x.com", "j***@x.com")]                        // local de 2 chars
    [InlineData("a@b.com", "a***@b.com")]                         // local de 1 char
    public void Mascara_preserva_primeira_ultima_e_dominio(string email, string esperado)
        => ConsultarRecebimentoBannerUseCase.MascararEmail(email).Should().Be(esperado);

    [Fact]
    public void Vazio_ou_sem_arroba_nao_vaza_nada()
    {
        ConsultarRecebimentoBannerUseCase.MascararEmail("").Should().Be("-");
        ConsultarRecebimentoBannerUseCase.MascararEmail("   ").Should().Be("-");
        ConsultarRecebimentoBannerUseCase.MascararEmail("semarroba").Should().Be("***");
    }
}
