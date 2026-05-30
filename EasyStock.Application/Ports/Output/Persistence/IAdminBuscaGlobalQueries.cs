namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Read model da busca global cross-tenant do back-office (Cmd+K). Concentra as
/// projeções ILIKE de Clientes/Lojas/Usuários que antes viviam direto no
/// AdminBuscaGlobalController (F7). Retorna dados crus — o mascaramento de PII,
/// a montagem de URLs e a auditoria ficam na camada de apresentação.
/// </summary>
public interface IAdminBuscaGlobalQueries
{
    Task<AdminBuscaGlobalResultado> BuscarAsync(
        string padrao, string? padraoDigitos, int limite, CancellationToken ct = default);
}

public sealed record AdminBuscaGlobalResultado(
    IReadOnlyList<BuscaClienteRow> Clientes,
    IReadOnlyList<BuscaLojaRow> Lojas,
    IReadOnlyList<BuscaUsuarioRow> Usuarios);

public sealed record BuscaClienteRow(Guid Id, string Nome, string? Documento);

public sealed record BuscaLojaRow(Guid Id, string Nome, Guid EmpresaId, string EmpresaNome, bool Ativa);

public sealed record BuscaUsuarioRow(Guid Id, string Nome, string Email, bool Ativo, Guid? EmpresaId, string? EmpresaNome);
