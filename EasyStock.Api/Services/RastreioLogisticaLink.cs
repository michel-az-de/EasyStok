using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.UseCases.Atendimento.Entregas;

namespace EasyStock.Api.Services;

public sealed class RastreioLogisticaLink(IConfiguration config, ConfiguracaoLogistica logistica, TimeProvider clock)
{
    public string Criar(Guid id)
    {
        var validade = clock.GetUtcNow().AddDays(7).ToUnixTimeSeconds();
        return $"/api/entregas-demonstracao/{id:N}.{validade}.{Convert.ToHexString(Assinar(id, validade))}";
    }

    public Guid? Validar(string token)
    {
        if (logistica.EmpresaDemonstracaoId == Guid.Empty || token.Length > 160) return null;
        var partes = token.Split('.');
        if (partes.Length != 3 || !Guid.TryParseExact(partes[0], "N", out var id)
            || !long.TryParse(partes[1], NumberStyles.None, CultureInfo.InvariantCulture, out var validade)
            || validade <= clock.GetUtcNow().ToUnixTimeSeconds() || partes[2].Length != 64) return null;
        try { return CryptographicOperations.FixedTimeEquals(Assinar(id, validade), Convert.FromHexString(partes[2])) ? id : null; }
        catch (FormatException) { return null; }
    }

    private byte[] Assinar(Guid id, long validade)
    {
        var segredo = config["Jwt:SecretKey"];
        if (string.IsNullOrWhiteSpace(segredo)) throw new InvalidOperationException("Chave de assinatura de rastreio indisponível.");
        var conteudo = $"logistica-demo-rastreio-v1:{logistica.EmpresaDemonstracaoId:D}:{id:D}:{validade}";
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes(conteudo));
    }
}
