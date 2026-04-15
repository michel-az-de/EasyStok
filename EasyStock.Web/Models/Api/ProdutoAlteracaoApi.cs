namespace EasyStock.Web.Models.Api;

public class ProdutoAlteracaoApi
{
    public Guid Id { get; set; }
    public string Acao { get; set; } = null!;
    public Guid UsuarioId { get; set; }
    public string? UsuarioNome { get; set; }
    public string? AlteracoesJson { get; set; }
    public DateTime AlteradoEm { get; set; }
}
