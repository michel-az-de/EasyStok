namespace EasyStock.Domain.Exceptions
{
    /// <summary>
    /// A política de hierarquia recusou a ação (#2070). <see cref="Codigo"/> é um dos <c>CodigosDaHierarquia</c>; a API
    /// responde 403 para <c>HIERARQUIA</c> e 409 para os demais, com o código no corpo.
    /// </summary>
    public sealed class HierarquiaRecusadaException(string codigo, string message) : RegraDeDominioVioladaException(message)
    {
        public string Codigo { get; } = codigo;
    }
}
