namespace EasyStock.Web.Models.ViewModels.Preferencias;

public class ConsentimentoDto
{
    public string Canal { get; set; } = "";
    public string Categoria { get; set; } = "";
    public bool OptIn { get; set; }
}

public class PreferenciasViewModel
{
    // [canal][categoria] = optIn
    private readonly Dictionary<string, Dictionary<string, bool>> _state = new(StringComparer.OrdinalIgnoreCase);

    public static readonly string[] Canais = ["Email", "Sms", "WhatsApp", "InApp"];
    public static readonly string[] Categorias = ["Transacional", "Operacional", "Marketing"];

    public void SetOptIn(string canal, string categoria, bool optIn)
    {
        if (!_state.ContainsKey(canal))
            _state[canal] = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        _state[canal][categoria] = optIn;
    }

    public bool IsOptIn(string canal, string categoria)
    {
        if (_state.TryGetValue(canal, out var cats) && cats.TryGetValue(categoria, out var v))
            return v;
        // Transacional defaults to opt-in (cannot be blocked by opt-out per LGPD legitimate interest)
        return categoria == "Transacional";
    }
}

