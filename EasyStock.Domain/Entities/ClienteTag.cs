using System.Globalization;
using System.Text;

namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Tag do cliente para campanha e atendimento (S24): <c>vegano</c>, <c>sem_gluten</c>, <c>risco</c>.
    /// Guardada normalizada (minúscula, sem acento, <c>_</c> no lugar de espaço e hífen, até
    /// <see cref="TagTamanhoMaximo"/>) e única por <c>(ClienteId, Tag)</c>. Nasce só por
    /// <see cref="Cliente.AdicionarTag"/>, que faz da repetição um no-op.
    /// </summary>
    public class ClienteTag
    {
        public const int TagTamanhoMaximo = 40;

        /// <summary>
        /// Tags oferecidas ao console e ao agente como ponto de partida. Não viram linha no banco:
        /// tag só existe presa a um cliente.
        /// </summary>
        public static readonly IReadOnlyList<string> Sugeridas =
            ["intolerante_lactose", "vegano", "sem_gluten", "risco", "encomenda"];

        public Guid Id { get; private set; }
        public Guid EmpresaId { get; private set; }
        public Guid ClienteId { get; private set; }
        public string Tag { get; private set; } = null!;

        /// <summary>Quem marcou: <see cref="OrigemClienteTag"/>.</summary>
        public string Origem { get; private set; } = null!;

        public DateTime CriadoEm { get; private set; }

        // EF Core ctor sem parâmetros
        private ClienteTag() { }

        internal static ClienteTag Criar(Guid empresaId, Guid clienteId, string tagNormalizada, string origem, DateTime em)
        {
            if (!OrigemClienteTag.EhValida(origem))
                throw new RegraDeDominioVioladaException($"Origem de tag inválida: '{origem}'.");

            return new ClienteTag
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ClienteId = clienteId,
                Tag = tagNormalizada,
                Origem = origem,
                CriadoEm = em,
            };
        }

        /// <summary>
        /// Minúscula, sem acento; espaço e hífen viram <c>_</c>; qualquer outro símbolo sai.
        /// Devolve vazio quando nada sobra (quem chama decide se isso é erro).
        /// </summary>
        public static string Normalizar(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return string.Empty;

            var decomposta = tag.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposta.Length);
            foreach (var c in decomposta)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (c is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(c);
                else if ((c is '_' or '-' || char.IsWhiteSpace(c)) && sb.Length > 0 && sb[^1] != '_') sb.Append('_');
            }

            return sb.ToString().TrimEnd('_');
        }

        /// <summary>Normaliza e valida; lança quando a tag fica vazia ou passa do limite.</summary>
        public static string NormalizarValidando(string? tag)
        {
            var normalizada = Normalizar(tag);
            if (normalizada.Length == 0)
                throw new RegraDeDominioVioladaException("Tag vazia: use letras ou números.");
            if (normalizada.Length > TagTamanhoMaximo)
                throw new RegraDeDominioVioladaException($"Tag passa de {TagTamanhoMaximo} caracteres.");
            return normalizada;
        }
    }

    /// <summary>Valores de <see cref="ClienteTag.Origem"/>. Constantes, como <see cref="TipoPessoaCliente"/>.</summary>
    public static class OrigemClienteTag
    {
        public const string Dona = "dona";
        public const string Agente = "agente";
        public const string Sistema = "sistema";

        public static bool EhValida(string? valor) => valor is Dona or Agente or Sistema;
    }
}
