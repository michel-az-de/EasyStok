namespace EasyStock.Application.Ports.Output.Atendimento;

public interface ICatalogoModelosAtendimento
{
    Task<IReadOnlyList<ModeloAtendimento>> ListarAsync(Guid empresaId, CancellationToken ct = default);
}

public sealed record ModeloAtendimento(
    string Nome, string Idioma, string Categoria, string Corpo, int QuantidadeParametros,
    string? Cabecalho = null, string? Rodape = null, string? Indisponivel = null);

/// <param name="faltaConexao">
/// #1765-9: a empresa não conectou o WhatsApp (ou a credencial é de outro número). Não é falha da Meta: a API responde
/// 409 com a frase para a dona; só a falha de consulta à Meta segue 502.
/// </param>
public sealed class CatalogoModelosException(string mensagem, bool faltaConexao = false) : Exception(mensagem)
{
    public bool FaltaConexao { get; } = faltaConexao;

    public static CatalogoModelosException SemConexao(string mensagem) => new(mensagem, faltaConexao: true);
}
