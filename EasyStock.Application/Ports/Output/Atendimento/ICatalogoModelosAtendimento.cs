using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Ports.Output.Atendimento;

public interface ICatalogoModelosAtendimento
{
    Task<IReadOnlyList<ModeloAtendimento>> ListarAsync(Guid empresaId, CancellationToken ct = default);

    /// <summary>
    /// #3205: a empresa usa a ponte web, onde a lista é a das mensagens padrão locais e nenhum modelo sai como template da
    /// Meta. Quem agenda ou dispara modelo consulta isto, sem chamar a Meta.
    /// </summary>
    Task<bool> UsaModelosLocaisAsync(Guid empresaId, CancellationToken ct = default) => Task.FromResult(false);
}

public sealed record ModeloAtendimento(
    string Nome, string Idioma, string Categoria, string Corpo, int QuantidadeParametros,
    string? Cabecalho = null, string? Rodape = null, string? Indisponivel = null, OrigemModelo Origem = OrigemModelo.Meta);

/// <param name="faltaConexao">
/// #1765-9: a empresa não conectou o WhatsApp (ou a credencial é de outro número). Não é falha da Meta: a API responde
/// 409 com a frase para a dona; só a falha de consulta à Meta segue 502.
/// </param>
public sealed class CatalogoModelosException(string mensagem, bool faltaConexao = false) : Exception(mensagem)
{
    public bool FaltaConexao { get; } = faltaConexao;

    public static CatalogoModelosException SemConexao(string mensagem) => new(mensagem, faltaConexao: true);
}
