namespace EasyStock.Web.Models.Api;

public class NfeListItem
{
    public Guid Id { get; set; }
    public string? ChaveAcesso { get; set; }
    public string? Status { get; set; }
    public short Serie { get; set; }
    public long Numero { get; set; }
    public decimal TotalNota { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? DataAutorizacao { get; set; }
}

public class NfeInfo
{
    public Guid Id { get; set; }
    public string? ChaveAcesso { get; set; }
    public string? Status { get; set; }
    public string? Modelo { get; set; }
    public short Serie { get; set; }
    public long Numero { get; set; }
    public string? ProtocoloAutorizacao { get; set; }
    public DateTime? DataAutorizacao { get; set; }
    public string? MotivoRejeicao { get; set; }
    public string? DanfeUrl { get; set; }
    public decimal TotalNota { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }
}

public class NfeItemInfo
{
    public Guid Id { get; set; }
    public int Ordem { get; set; }
    public string NomeSnapshot { get; set; } = "";
    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public string Unidade { get; set; } = "";
    public string? Ncm { get; set; }
    public string? Cfop { get; set; }
    public string? CstOuCsosn { get; set; }
}

public class NfeEventoInfo
{
    public Guid Id { get; set; }
    public string Tipo { get; set; } = "";
    public DateTime OcorridoEm { get; set; }
    public string? UsuarioNome { get; set; }
    public string? Origem { get; set; }
}

public class NfeDetalhe
{
    public NfeInfo Nfe { get; set; } = new();
    public List<NfeItemInfo> Itens { get; set; } = [];
    public List<NfeEventoInfo> Eventos { get; set; } = [];
}
