using EasyStock.Application.Ports.Output.Notifications;

namespace EasyStock.Application.Services.Notifications;

/// <summary>Quem recebe os avisos da equipe também escreve para o número da loja (#2211).</summary>
public interface IContatoDaEquipeAvisada
{
    /// <summary>
    /// O contato da conversa (telefone ou wa_id) é de alguém da equipe inscrito num aviso da equipe? Nesse caso o robô
    /// não responde e a escalada não gera outro aviso: a resposta da pessoa ao aviso não vira atendimento nem laço.
    /// </summary>
    Task<bool> EhInscritoAsync(Guid empresaId, string? contato, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ContatoDaEquipeAvisada(IAudienciaUsuarios usuarios, IPreferenciaNotificacaoRepository preferencias)
    : IContatoDaEquipeAvisada
{
    private static readonly NivelAcesso[] NiveisDaLoja =
        [NivelAcesso.Admin, NivelAcesso.Gerente, NivelAcesso.Operador, NivelAcesso.Visualizador];

    public async Task<bool> EhInscritoAsync(Guid empresaId, string? contato, CancellationToken ct = default)
    {
        if (Normalizar(contato) is null) return false;

        var mesmos = (await usuarios.ListarDaEmpresaAsync(empresaId, NiveisDaLoja, ct))
            .Where(u => MesmoTelefone(u.Telefone, contato))
            .Select(u => u.Id)
            .ToList();
        if (mesmos.Count == 0) return false;

        foreach (var codigo in AvisosDaEquipe.CodigosDeRotina)
        {
            if ((await preferencias.ListarDaRotinaAsync(empresaId, codigo, mesmos, ct)).Any(p => p.Habilitada))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Mesmo celular brasileiro com ou sem <c>+55</c> e com ou sem o nono dígito (o wa_id antigo vem sem ele): compara
    /// DDD e os 8 últimos dígitos.
    /// </summary>
    public static bool MesmoTelefone(string? a, string? b)
    {
        var x = Normalizar(a);
        var y = Normalizar(b);
        return x is not null && y is not null && x[..2] == y[..2] && x[^8..] == y[^8..];
    }

    private static string? Normalizar(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone)) return null;
        var digitos = new string(telefone.Where(char.IsAsciiDigit).ToArray());
        if (digitos.Length >= 12 && digitos.StartsWith("55", StringComparison.Ordinal)) digitos = digitos[2..];
        return digitos.Length is 10 or 11 ? digitos : null;
    }
}
