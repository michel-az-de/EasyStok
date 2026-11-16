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
                   || (b[0] == 192 && b[1] == 168)
                   || b[0] >= 224
                   || (b[0] == 198 && b[1] is 18 or 19)
                   || (b[0] == 192 && b[1] == 0 && b[2] == 0);
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            if (EhNat64(b) || EhCompativelOuSiit(b))
                return IpInterno(new IPAddress(b[12..16]));
            if (b[0] == 0x20 && b[1] == 0x02)
                return IpInterno(new IPAddress(b[2..6]));
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00)
                return IpInterno(new IPAddress(b[4..8])) || IpInterno(new IPAddress(ClienteTeredo(b)));

            return IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.IPv6None) || ip.Equals(IPAddress.IPv6Any)
                   || (b[0] & 0xFE) == 0xFC
                   || (b[0] == 0xFE && (b[1] & 0xC0) is 0x80 or 0xC0)
                   || b[0] == 0xFF;
        }

        return true;
    }

    /// <summary>::/96 (IPv4 compatível) e ::ffff:0:0:0/96 (SIIT): o IPv4 vai nos 4 últimos bytes.</summary>
    private static bool EhCompativelOuSiit(byte[] b) =>
        b[0..8].All(x => x == 0)
        && ((b[8] == 0 && b[9] == 0 && b[10] == 0 && b[11] == 0)
            || (b[8] == 0xFF && b[9] == 0xFF && b[10] == 0 && b[11] == 0));

    /// <summary>Teredo 2001:0::/32: o IPv4 do cliente vem invertido (XOR) nos 4 últimos bytes.</summary>
    private static byte[] ClienteTeredo(byte[] b) => b[12..16].Select(x => (byte)~x).ToArray();

    /// <summary>64:ff9b::/96: o IPv4 de destino vai nos 4 últimos bytes e vale a mesma conferência do IPv4.</summary>
    private static bool EhNat64(byte[] b) =>
        b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b[4..12].All(x => x == 0);
}
