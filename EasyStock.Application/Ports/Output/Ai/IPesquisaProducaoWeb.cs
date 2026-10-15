namespace EasyStock.Application.Ports.Output.Ai;

public sealed record FonteProducaoWeb(string Titulo, string Url, string Texto, DateTimeOffset ConsultadaEm);

public interface IPesquisaProducaoWeb
{
    Task<IReadOnlyList<FonteProducaoWeb>> BuscarInsumosAsync(string texto, CancellationToken ct);
    Task<IReadOnlyList<FonteProducaoWeb>> BuscarReceitasAsync(string texto, CancellationToken ct);
}
