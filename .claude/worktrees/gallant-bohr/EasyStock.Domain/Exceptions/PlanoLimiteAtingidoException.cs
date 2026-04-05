namespace EasyStock.Domain.Exceptions
{
    public class PlanoLimiteAtingidoException(string recurso) : RegraDeDominioVioladaException($"Limite do plano atingido para o recurso: {recurso}.")
    {
    }
}
