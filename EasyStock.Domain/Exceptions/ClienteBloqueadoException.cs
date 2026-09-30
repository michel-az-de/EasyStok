namespace EasyStock.Domain.Exceptions
{
    /// <summary>
    /// Cliente bloqueado (S24) tentou fechar pedido. <see cref="Codigo"/> é o que a ferramenta
    /// <c>criar_pedido</c> devolve ao agente; o motivo do bloqueio é interno e não vai na mensagem.
    /// </summary>
    public class ClienteBloqueadoException : RegraDeDominioVioladaException
    {
        public const string CodigoErro = "cliente_bloqueado";

        public ClienteBloqueadoException(Guid clienteId)
            : base($"Cliente {clienteId} está bloqueado.")
        {
            ClienteId = clienteId;
        }

        public Guid ClienteId { get; }

        public string Codigo => CodigoErro;
    }
}
