namespace EasyStock.Domain.Exceptions
{
    /// <summary>
    /// #2695: o domínio das contas internas (<see cref="Entities.Usuario.DominioDeSistema"/>) não é de pessoa. Uma pessoa
    /// com esse e-mail se confundiria com a conta interna do sync móvel.
    /// </summary>
    public class EmailReservadoException()
        : RegraDeDominioVioladaException("O domínio do e-mail é reservado às contas internas do sistema.");
}
