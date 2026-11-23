using EasyStock.Application.UseCases.Cliente.Foto;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.ListarClientes;

public sealed record ListarClientesQuery(
    Guid EmpresaId,
    int Page = 1,
    int PageSize = 20,
    bool? Ativo = null,
    string? Search = null,
    string? Sort = "nome",
    string? Order = "asc",
    string? Relacionamento = null);

/// <summary>
/// #3244: a linha da lista de Cadastros. Traz todos os campos do <see cref="ClienteResult"/> (o contrato atual não
/// quebra) mais a relação do cliente, calculada no servidor. O <see cref="ClienteResult"/> segue o mesmo nos outros fluxos.
/// </summary>
public sealed record ClienteListaResult(
    Guid Id,
    Guid EmpresaId,
    string Nome,
    string? Apt,
    string? Endereco,
    string? Telefone,
    string? Email,
    string? Documento,
    string? Observacoes,
    int OrderCount,
    DateTime? LastOrderAt,
    bool Ativo,
    DateTime CriadoEm,
    DateTime AlteradoEm,
    string TipoPessoa,
    string? NomeFantasia,
    string? InscricaoEstadual,
    string? FotoUrl,
    decimal GastoTotal,
    int Compras,
    decimal TicketMedio,
    DateTime? PrimeiraCompraEm,
    DateTime? UltimaCompraEm,
    int OcorrenciasAbertas,
    int OcorrenciasTotal,
    int NotasTotal,
    string? UltimaNota,
    IReadOnlyList<string> Tags,
    string Estagio,
    bool EmRisco)
{
    public uint Revisao { get; init; }
}

public sealed record ListarClientesResult(
    IReadOnlyList<ClienteListaResult> Items,
    int Total,
    int Page,
    int PageSize,
    decimal GastoTotalDaCarteira);

public class ListarClientesUseCase(IClienteRepository repo)
{
    public async Task<ListarClientesResult> ExecuteAsync(ListarClientesQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        if (q.Relacionamento is not (null or "lead" or "cliente" or RegraDeRecompra.Relacionamento))
            throw new UseCaseValidationException("Relacionamento deve ser lead, cliente ou recompra.");
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);

        var lista = await repo.ListarComRelacaoAsync(
            q.EmpresaId, page, size, q.Ativo, q.Search, q.Sort, q.Order, q.Relacionamento);

        return new ListarClientesResult(
            lista.Itens.Select(Mapear).ToList(),
            lista.Total, page, size, lista.GastoTotalDaCarteira);
    }

    private static ClienteListaResult Mapear(ClienteRelacao r)
    {
        var c = r.Cliente;
        return new ClienteListaResult(
            c.Id, c.EmpresaId, c.Nome, c.Apt, c.Endereco, c.Telefone, c.Email,
            c.Documento, c.Observacoes, c.OrderCount, c.LastOrderAt, c.Ativo,
            c.CriadoEm, c.AlteradoEm,
            c.TipoPessoa, c.NomeFantasia, c.InscricaoEstadual,
            ClienteFotoUseCase.Url(c),
            r.Gasto, r.Compras, r.Compras == 0 ? 0m : Math.Round(r.Gasto / r.Compras, 2),
            r.PrimeiraCompraEm, r.UltimaCompraEm,
            r.OcorrenciasAbertas, r.OcorrenciasTotal, r.NotasTotal, r.UltimaNota,
            r.Tags, RegraDeEstagio.De(r.Compras), r.EmRisco)
        { Revisao = c.Revisao };
    }
}
