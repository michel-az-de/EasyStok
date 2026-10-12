namespace EasyStock.Application.Ports.Output.Atendimento;

public interface ICatalogoModelosAtendimento
{
    Task<IReadOnlyList<ModeloAtendimento>> ListarAsync(Guid empresaId, CancellationToken ct = default);
}

public sealed record ModeloAtendimento(
    string Nome, string Idioma, string Categoria, string Corpo, int QuantidadeParametros,
    string? Cabecalho = null, string? Rodape = null, string? Indisponivel = null);

public sealed class CatalogoModelosException(string mensagem) : Exception(mensagem);
