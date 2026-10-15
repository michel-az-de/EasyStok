namespace EasyStock.Web.Models.Api;

/// <summary>
/// Resposta do step 1 do login em duas etapas (<c>POST auth/lista-empresas</c>): valida as
/// credenciais SEM emitir token e devolve as empresas que o usuario acessa (ADR-0047).
/// </summary>
public record ListaEmpresasLoginApi
{
    public bool IsSuperAdmin { get; init; }
    public List<EmpresaLoginApi> Empresas { get; init; } = [];
}

public record EmpresaLoginApi
{
    public string Id { get; init; } = string.Empty;
    public string Nome { get; init; } = string.Empty;
}
