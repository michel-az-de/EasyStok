using EasyStock.Domain.Enums.Pagamentos;

namespace EasyStock.Domain.ValueObjects.Pix;

/// <summary>
/// Chave Pix da loja (#2233), validada e normalizada pelo tipo, no formato que o DICT e o BR Code usam: CPF e CNPJ só
/// com dígitos, e-mail em minúsculas, telefone <c>+55DDDNÚMERO</c> e chave aleatória (EVP) como UUID em minúsculas.
///
/// <para>
/// A chave é dado da loja que não deve circular: nenhuma mensagem de erro a repete e <see cref="ToString"/> a
/// esconde, para que um log estruturado ou uma interpolação descuidada não a grave.
/// </para>
/// </summary>
public sealed record ChavePix
{
    /// <summary>Limite do subcampo 01 do campo 26 do BR Code (chave).</summary>
    public const int TamanhoMaximo = 77;

    public TipoChavePix Tipo { get; }
    public string Valor { get; }

    private ChavePix(TipoChavePix tipo, string valor)
    {
        Tipo = tipo;
        Valor = valor;
    }

    public static ChavePix Criar(TipoChavePix tipo, string? valor)
    {
        var bruto = (valor ?? string.Empty).Trim();
        if (bruto.Length == 0) throw Invalida(tipo);

        var normalizado = tipo switch
        {
            TipoChavePix.Cpf => Documento(bruto, 11, Cpf.EhValido),
            TipoChavePix.Cnpj => Documento(bruto, 14, Cnpj.EhValido),
            TipoChavePix.Email => Email(bruto),
            TipoChavePix.Telefone => Telefone(bruto),
            TipoChavePix.Aleatoria => Guid.TryParseExact(bruto, "D", out var evp) ? evp.ToString("D") : null,
            _ => null,
        };
        if (normalizado is null || normalizado.Length > TamanhoMaximo) throw Invalida(tipo);
        return new ChavePix(tipo, normalizado);
    }

    /// <summary>Esconde o valor: a chave não sai em log nem em texto interpolado.</summary>
    public override string ToString() => $"ChavePix {{ Tipo = {Tipo}, Valor = *** }}";

    private static RegraDeDominioVioladaException Invalida(TipoChavePix tipo) =>
        new($"Chave Pix inválida para o tipo {tipo}.");

    private static string? Documento(string bruto, int digitos, Func<string?, bool> valido)
    {
        var numeros = new string(bruto.Where(char.IsAsciiDigit).ToArray());
        return numeros.Length == digitos && valido(numeros) ? numeros : null;
    }

    private static string? Email(string bruto)
    {
        var email = EmailAddress.TryFrom(bruto)?.Value.ToLowerInvariant();
        return email is not null && email.All(c => c is > ' ' and <= '~') ? email : null;
    }

    private static string? Telefone(string bruto)
    {
        var comDdi = bruto.StartsWith('+');
        var numeros = new string(bruto.Where(char.IsAsciiDigit).ToArray());
        if (comDdi || numeros.Length > 11)
        {
            if (!numeros.StartsWith("55")) return null;
            numeros = numeros[2..];
        }
        // DDD de 11 a 99 seguido de 8 (fixo) ou 9 (celular) dígitos.
        return numeros.Length is 10 or 11 && numeros[0] != '0' && numeros[1] != '0' ? "+55" + numeros : null;
    }
}
