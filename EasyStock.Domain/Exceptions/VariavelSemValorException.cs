namespace EasyStock.Domain.Exceptions;

/// <summary>Variável usada no texto sem valor na conversa (S42): nunca sai <c>{nome}</c> literal.</summary>
public sealed class VariavelSemValorException(string variavel)
    : RegraDeDominioVioladaException($"A variável {{{variavel}}} não tem valor nesta conversa.")
{
    public string Variavel { get; } = variavel;
}
