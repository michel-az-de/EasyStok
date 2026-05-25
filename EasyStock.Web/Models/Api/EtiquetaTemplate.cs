namespace EasyStock.Web.Models.Api;

public class EtiquetaTemplateApi
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = "";
    public string Descricao { get; set; } = "";
    public string Origem { get; set; } = ""; // "Sistema" | "Empresa"
    public string LayoutJson { get; set; } = "{}";
    public bool IsDefault { get; set; }
}
