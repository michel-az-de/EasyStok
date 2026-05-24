namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Lançada quando o INSERT atômico de <see cref="EasyStock.Domain.Entities.Storefront.VagaOcupada"/>
/// não cria linha — janela esgotada para a data solicitada (ADR-0014 §Solução 1).
///
/// <para>
/// Herda de <see cref="RegraDeDominioVioladaException"/> para fluir pelo mesmo
/// handler global de exceções de domínio (mapeado para HTTP 409 ou 400 conforme o caso).
/// </para>
/// </summary>
public class JanelaSemVagasException : RegraDeDominioVioladaException
{
    public JanelaSemVagasException()
        : base("Janela de entrega esgotada.")
    {
    }

    public JanelaSemVagasException(string message)
        : base(message)
    {
    }

    public JanelaSemVagasException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
