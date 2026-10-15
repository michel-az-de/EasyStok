namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Nota interna datada sobre o cliente (S24), opcionalmente presa a um pedido ou a uma mensagem.
    /// É da equipe: <b>nunca</b> é projetada para o cliente. Só chega ao agente pelo dossiê, marcada
    /// <c>[interno]</c> (S06); o teste de arquitetura <c>NotaInternaNaoVazaParaStorefront</c> barra a
    /// leitura de <see cref="Texto"/> no storefront e nas ferramentas do agente.
    /// </summary>
    public class ClienteNota
    {
        public const int TextoTamanhoMaximo = 500;
        public const int AutorTamanhoMaximo = 120;

        public Guid Id { get; private set; }
        public Guid EmpresaId { get; private set; }
        public Guid ClienteId { get; private set; }
        public string Texto { get; private set; } = null!;
        public Guid? PedidoId { get; private set; }
        public Guid? MensagemId { get; private set; }

        /// <summary>Quem escreveu: nome do usuário do console, ou <c>agente</c>.</summary>
        public string Autor { get; private set; } = null!;

        public DateTime CriadoEm { get; private set; }

        // EF Core ctor sem parâmetros
        private ClienteNota() { }

        public static ClienteNota Criar(
            Guid empresaId, Guid clienteId, string texto, string autor, DateTime em,
            Guid? pedidoId = null, Guid? mensagemId = null)
        {
            if (empresaId == Guid.Empty)
                throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");
            if (clienteId == Guid.Empty)
                throw new RegraDeDominioVioladaException("ClienteId é obrigatório.");

            var textoLimpo = texto?.Trim() ?? string.Empty;
            if (textoLimpo.Length == 0)
                throw new RegraDeDominioVioladaException("Nota vazia.");
            if (textoLimpo.Length > TextoTamanhoMaximo)
                throw new RegraDeDominioVioladaException($"Nota passa de {TextoTamanhoMaximo} caracteres.");

            var autorLimpo = autor?.Trim() ?? string.Empty;
            if (autorLimpo.Length == 0)
                throw new RegraDeDominioVioladaException("Autor da nota é obrigatório.");
            if (autorLimpo.Length > AutorTamanhoMaximo) autorLimpo = autorLimpo[..AutorTamanhoMaximo];

            return new ClienteNota
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ClienteId = clienteId,
                Texto = textoLimpo,
                Autor = autorLimpo,
                PedidoId = pedidoId,
                MensagemId = mensagemId,
                CriadoEm = em,
            };
        }
    }
}
