namespace EasyStock.Domain.Exceptions
{
    public class ConflitoConcorrenciaException(string entidade, string? detalhe = null)
        : RegraDeDominioVioladaException(
            string.IsNullOrWhiteSpace(detalhe)
                ? $"Conflito de concorrencia ao atualizar '{entidade}'. Recarregue os dados e tente novamente."
                : $"Conflito de concorrencia ao atualizar '{entidade}'. {detalhe}")
    {
    }
}
