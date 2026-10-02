using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Por onde o convite foi aceito (N9), já mascarado para <c>usuarios.ConviteAceitoVia</c>. Nunca guarda o telefone
    /// inteiro: só o código do país e os quatro últimos dígitos.
    /// </summary>
    public static class ViaDoConvite
    {
        public const string Email = "e-mail";
        public const string Google = "Google";

        /// <summary><c>+5511999991234</c> vira <c>+55•••1234</c>.</summary>
        public static string WhatsApp(TelefoneE164 telefone)
        {
            ArgumentNullException.ThrowIfNull(telefone);
            var digitos = new string(telefone.Value.Where(char.IsDigit).ToArray());
            return $"+{digitos[..2]}•••{digitos[^4..]}";
        }
    }
}
