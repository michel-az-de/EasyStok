using System.Net;
using System.Net.Sockets;

namespace EasyStock.Application.UseCases.Atendimento.Email;

/// <summary>
/// Quais servidores a caixa de e-mail do atendimento pode usar. A API abre a conexão a partir da rede dela: host da
/// rede interna (loopback, faixa privada, link-local, CGNAT, serviço do compose) viraria varredura de portas.
/// </summary>
public static class HostDeCaixaEmail
{
    /// <summary>Nome que só existe na rede interna: <c>localhost</c>, <c>*.localhost</c> e nome sem ponto.</summary>
    public static bool NomeInterno(string host)
    {
        var nome = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (IPAddress.TryParse(nome, out var ip))
            return IpInterno(ip);
        return nome == "localhost" || nome.EndsWith(".localhost", StringComparison.Ordinal) || !nome.Contains('.');
    }

    public static bool IpInterno(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] is 0 or 10 or 127
                   || (b[0] == 100 && b[1] is >= 64 and <= 127)
                   || (b[0] == 169 && b[1] == 254)
                   || (b[0] == 172 && b[1] is >= 16 and <= 31)
                   || (b[0] == 192 && b[1] == 168);
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            return IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.IPv6None) || ip.Equals(IPAddress.IPv6Any)
                   || (b[0] & 0xFE) == 0xFC
                   || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80);
        }

        return true;
    }
}
