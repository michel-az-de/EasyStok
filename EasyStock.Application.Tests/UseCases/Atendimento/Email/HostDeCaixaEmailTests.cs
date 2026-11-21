using System.Net;
using EasyStock.Application.UseCases.Atendimento.Email;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Email;

/// <summary>#2741: faixas reservadas recusadas; prefixos de tradução valem pelo IPv4 embutido.</summary>
public class HostDeCaixaEmailTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.1.2.3")]
    [InlineData("127.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.250")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.254")]
    [InlineData("192.0.0.8")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("fec0::1")]
    [InlineData("feff::1")]
    [InlineData("ff02::1")]
    [InlineData("ff00::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    [InlineData("2002:a00:1::1")]
    [InlineData("2002:7f00:1::")]
    [InlineData("2002:c0a8:101::1")]
    [InlineData("::10.0.0.1")]
    [InlineData("::7f00:1")]
    [InlineData("::a9fe:a9fe")]
    [InlineData("::c0a8:101")]
    [InlineData("::ffff:0:a00:1")]
    [InlineData("::ffff:0:7f00:1")]
    [InlineData("2001:0:a00:1::f7f7:f7f7")]
    [InlineData("2001:0:808:808::f5ff:fffe")]
    public void IpInterno_FaixaReservada_Recusa(string endereco) =>
        HostDeCaixaEmail.IpInterno(IPAddress.Parse(endereco)).Should().BeTrue();

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("198.17.255.255")]
    [InlineData("198.20.0.1")]
    [InlineData("192.0.1.1")]
    [InlineData("223.255.255.255")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::808:808")]
    [InlineData("2002:808:808::1")]
    [InlineData("::808:808")]
    [InlineData("2001:0:808:808::f7f7:f7f7")]
    [InlineData("::ffff:0:808:808")]
    public void IpInterno_Publico_Aceita(string endereco) =>
        HostDeCaixaEmail.IpInterno(IPAddress.Parse(endereco)).Should().BeFalse();
}
