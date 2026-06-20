using EasyStock.Web.Helpers;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Helpers;

// EST-01: texto amigavel do badge de validade — corrige o "-2361 d" cru do QA.
public class FormatHelperTests
{
    [Theory]
    [InlineData(-2361, "vencido há 2361 dias")]
    [InlineData(-1, "vencido há 1 dia")]
    [InlineData(0, "vence hoje")]
    [InlineData(1, "1 dia")]
    [InlineData(5, "5 dias")]
    public void AsValidadeBadge_formata_por_faixa(int dias, string esperado)
    {
        dias.AsValidadeBadge().Should().Be(esperado);
    }

    [Fact]
    public void AsValidadeBadge_nullable_vazio_quando_null()
    {
        ((int?)null).AsValidadeBadge().Should().Be("");
        ((int?)-3).AsValidadeBadge().Should().Be("vencido há 3 dias");
    }

    // SAID-01: oculta IP de infraestrutura interna; mostra so o publico.
    [Theory]
    [InlineData("::ffff:172.18.0.5")] // bridge docker (IPv4-mapped)
    [InlineData("172.18.0.5")]        // bridge docker
    [InlineData("10.0.0.5")]          // privado 10/8
    [InlineData("192.168.1.10")]      // privado 192.168/16
    [InlineData("172.16.0.1")]        // privado 172.16/12 (limite inferior)
    [InlineData("172.31.255.254")]    // privado 172.16/12 (limite superior)
    [InlineData("127.0.0.1")]         // loopback
    [InlineData("::1")]               // loopback IPv6
    [InlineData("169.254.1.1")]       // link-local
    [InlineData("fe80::1")]           // link-local IPv6
    [InlineData("nao-eh-ip")]         // nao parseavel
    [InlineData("")]
    [InlineData(null)]
    public void AsIpPublico_oculta_interno_e_invalido(string? ip)
    {
        ip.AsIpPublico().Should().BeNull();
    }

    [Theory]
    [InlineData("8.8.8.8", "8.8.8.8")]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("172.32.0.1", "172.32.0.1")]              // fora do range privado 172.16-31
    [InlineData("200.150.10.20, 172.18.0.5", "200.150.10.20")] // XFF: primeiro token (cliente real)
    public void AsIpPublico_retorna_ip_publico(string ip, string esperado)
    {
        ip.AsIpPublico().Should().Be(esperado);
    }
}
