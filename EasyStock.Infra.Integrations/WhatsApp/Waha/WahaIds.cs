namespace EasyStock.Infra.Integrations.WhatsApp.Waha;

/// <summary>
/// Ids do WAHA. Conversa: <c>5541999990000@c.us</c> (telefone), <c>123456789012345@lid</c> (id oculto do WhatsApp),
/// <c>...@g.us</c> (grupo). Mensagem: <c>{deMim}_{conversa}_{id}[_{participante}]</c>, por exemplo
/// <c>false_5541999990000@c.us_3EB0A1B2C3</c>.
/// </summary>
public static class WahaIds
{
    public const string SufixoTelefone = "@c.us";
    public const string SufixoLid = "@lid";
    private const string SufixoTelefoneMotor = "@s.whatsapp.net";

    public static bool EhTelefone(string? jid) =>
        jid is not null && (jid.EndsWith(SufixoTelefone, StringComparison.Ordinal) || jid.EndsWith(SufixoTelefoneMotor, StringComparison.Ordinal));

    public static bool EhLid(string? jid) => jid is not null && jid.EndsWith(SufixoLid, StringComparison.Ordinal);

    /// <summary>Conversa com uma pessoa (telefone ou id oculto). Grupo, status, canal e lista de transmissão ficam de fora.</summary>
    public static bool EhConversaIndividual(string? jid) => EhTelefone(jid) || EhLid(jid);

    /// <summary>Parte do usuário, só dígitos, sem o aparelho (<c>5541999990000:12@s.whatsapp.net</c> vira <c>5541999990000</c>).</summary>
    public static string Usuario(string? jid)
    {
        if (string.IsNullOrWhiteSpace(jid)) return string.Empty;
        var arroba = jid.IndexOf('@');
        var usuario = arroba < 0 ? jid : jid[..arroba];
        var aparelho = usuario.IndexOf(':');
        if (aparelho >= 0) usuario = usuario[..aparelho];
        return new string(usuario.Where(char.IsAsciiDigit).ToArray());
    }

    /// <summary>Conversa do WAHA para o contato do EasyStok: só dígitos vira telefone; o que já tem sufixo segue igual.</summary>
    public static string ConversaDoContato(string contato, bool ehLid = false)
    {
        if (contato.Contains('@', StringComparison.Ordinal)) return contato;
        var digitos = new string(contato.Where(char.IsAsciiDigit).ToArray());
        return digitos + (ehLid ? SufixoLid : SufixoTelefone);
    }

    public sealed record IdMensagem(bool DeMim, string Conversa, string Id);

    /// <summary>Lê o id serializado; <c>null</c> quando não está no formato do WAHA.</summary>
    public static IdMensagem? LerIdMensagem(string? serializado)
    {
        if (string.IsNullOrWhiteSpace(serializado)) return null;
        var partes = serializado.Split('_');
        if (partes.Length is not (3 or 4)) return null;
        if (partes[0] is not ("true" or "false")) return null;
        if (partes[1].Length == 0 || partes[2].Length == 0) return null;
        return new IdMensagem(partes[0] == "true", partes[1], partes[2]);
    }

    public static string MontarIdMensagem(bool deMim, string conversa, string id) =>
        $"{(deMim ? "true" : "false")}_{conversa}_{id}";
}
