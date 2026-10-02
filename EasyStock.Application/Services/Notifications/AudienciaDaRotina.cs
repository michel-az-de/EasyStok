using System.Text.Json;

namespace EasyStock.Application.Services.Notifications;

/// <summary>Quem recebe a rotina de plataforma, lido de <c>ParametrosJson.audiencia</c> (N4).</summary>
public enum AudienciaNotificacao
{
    /// <summary>O usuário do <c>usuarioId</c> do payload.</summary>
    Usuario,

    /// <summary>Usuários ativos com perfil Admin na empresa do evento.</summary>
    Admins,

    /// <summary>Usuários ativos com perfil Admin ou Gerente na empresa do evento.</summary>
    Gestores,

    /// <summary>Superadmins da plataforma. Só vale em rotina global.</summary>
    Superadmins
}

/// <summary>Leitura da audiência da rotina.</summary>
public static class AudienciaDaRotina
{
    /// <summary>Chave de <c>ParametrosJson</c> com a audiência (N4).</summary>
    public const string Parametro = "audiencia";

    /// <summary>A audiência declarada, ou <c>null</c> quando não há chave, o valor é desconhecido ou o JSON é inválido.</summary>
    public static AudienciaNotificacao? Ler(string? parametrosJson)
    {
        if (string.IsNullOrWhiteSpace(parametrosJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(parametrosJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(Parametro, out var valor)
                && valor.ValueKind == JsonValueKind.String)
            {
                return valor.GetString()?.Trim().ToLowerInvariant() switch
                {
                    "usuario" => AudienciaNotificacao.Usuario,
                    "admins" => AudienciaNotificacao.Admins,
                    "gestores" => AudienciaNotificacao.Gestores,
                    "superadmins" => AudienciaNotificacao.Superadmins,
                    _ => null
                };
            }
        }
        catch (JsonException) { /* parâmetros inválidos: sem audiência */ }

        return null;
    }
}
