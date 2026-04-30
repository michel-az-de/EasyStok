namespace EasyStock.Web.Models.ViewModels.Assinatura;

public class AssinaturaViewModel
{
    public List<PlanoInfo> Planos { get; set; } = [];
    public string? LimiteAtingidoRecurso { get; set; }
    public bool MostrarUpgradeWall => LimiteAtingidoRecurso is not null;
}

public class PlanoInfo
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public int LimiteLojas { get; set; }
    public int LimiteUsuarios { get; set; }
    public int LimiteProdutos { get; set; }
    public decimal PrecoMensal { get; set; }

    public static string FormatarLimite(int valor) =>
        valor == -1 ? "Ilimitado" : valor.ToString("N0");
}
