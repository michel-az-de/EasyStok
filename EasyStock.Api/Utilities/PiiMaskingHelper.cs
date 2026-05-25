namespace EasyStock.Api.Utilities;

public static class PiiMaskingHelper
{
    /// <summary>
    /// Mascarar email para proteção de PII em exports/logs.
    /// "admin@company.com" → "a***@company.com"
    /// </summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return string.Empty;

        var parts = email.Split('@');
        if (parts.Length != 2)
            return "***@***"; // Email inválido

        var localPart = parts[0];
        var domain = parts[1];

        if (localPart.Length <= 1)
            return $"*@{domain}";

        var masked = $"{localPart[0]}***@{domain}";
        return masked;
    }

    /// <summary>
    /// Mascarar IP address para proteção de PII.
    /// "192.168.1.100" → "192.168.*.*"
    /// "2001:0db8:85a3:0000" → "2001:0db8:****:****"
    /// </summary>
    public static string MaskIpAddress(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return string.Empty;

        if (ip.Contains('.'))
        {
            // IPv4: mask last octet
            var parts = ip.Split('.');
            if (parts.Length == 4)
                return $"{parts[0]}.{parts[1]}.*.*";
        }

        if (ip.Contains(':'))
        {
            // IPv6: mask last 2 groups
            var parts = ip.Split(':');
            if (parts.Length >= 4)
                return $"{parts[0]}:{parts[1]}:{parts[2]}:****";
        }

        return "***";
    }
}
