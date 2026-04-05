namespace EasyStock.Web.Models.ViewModels.Assinatura;

public class AssinaturaViewModel
{
    public EasyStock.Web.Models.Api.Assinatura? PlanoAtual { get; set; }
    public List<PlanoInfo> Planos { get; set; } = [];
    public List<FaturaInfo> Faturas { get; set; } = [];
}

public class PlanoInfo
{
    public string Nome { get; set; } = string.Empty;
    public decimal Preco { get; set; }
    public List<string> Features { get; set; } = [];
    public bool Recomendado { get; set; }
}

public class FaturaInfo
{
    public DateOnly Data { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public string Status { get; set; } = string.Empty;
}
