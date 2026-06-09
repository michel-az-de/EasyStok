namespace EasyStock.Admin.Pages.Tickets;

/// <summary>
/// BUG-09: rótulos PT-BR amigáveis para os enums de ticket exibidos nas badges.
/// O Admin não referencia o Domain (fala com a Api por HTTP), então os valores são uma cópia
/// local dos enums do backend (manter em sincronia). Os switches de variante de cor/CSS
/// continuam keyed no valor CRU — este helper troca só o TEXTO exibido (ex.: "EmAtendimento"
/// -> "Em atendimento", "AGUARDANDOCLIENTE" -> "Aguardando cliente").
/// </summary>
public static class TicketLabels
{
    public static string Status(string? v) => Norm(v) switch
    {
        "aberto" => "Aberto",
        "ematendimento" => "Em atendimento",
        "aguardandocliente" => "Aguardando cliente",
        "resolvido" => "Resolvido",
        "fechado" => "Fechado",
        _ => Fallback(v),
    };

    public static string Prioridade(string? v) => Norm(v) switch
    {
        "critica" => "Crítica",
        "alta" => "Alta",
        "normal" => "Normal",
        "baixa" => "Baixa",
        _ => Fallback(v),
    };

    public static string Categoria(string? v) => Norm(v) switch
    {
        "bug" => "Bug",
        "duvida" => "Dúvida",
        "financeiro" => "Financeiro",
        "solicitacao" => "Solicitação",
        "incidente" => "Incidente",
        "bugfixdev" => "Bug-fix Dev",
        "outro" => "Outro",
        _ => Fallback(v),
    };

    private static string Norm(string? v) => (v ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", "");
    private static string Fallback(string? v) => string.IsNullOrWhiteSpace(v) ? "—" : v!.Trim();
}
