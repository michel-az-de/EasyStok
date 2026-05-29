namespace EasyStock.Domain.ValueObjects;

public enum TipoGtin
{
    Ean8,
    Upc12,
    Ean13,
    Gtin14,
    InternoCode128
}

// Codigo de barras conforme padrao GS1 + suporte a codigo interno com prefixo
// "INT-". Centraliza parse + checksum mod10 num unico lugar — antes a validacao
// vivia so client-side em Form.cshtml, deixando o backend aceitar qualquer string.
public sealed record Gtin
{
    public string Valor { get; }
    public TipoGtin Tipo { get; }

    // EAN-13 comecando com '2' = faixa GS1 reservada para uso interno restrito
    // (etiqueta de balanca, codigo de loja). Tratado como interno para fins de
    // distincao entre GTIN oficial de fornecedor e codigo gerado pela empresa.
    public bool EhInterno => Tipo == TipoGtin.InternoCode128
        || (Tipo == TipoGtin.Ean13 && Valor.StartsWith("2", StringComparison.Ordinal));

    private Gtin(string valor, TipoGtin tipo)
    {
        Valor = valor;
        Tipo = tipo;
    }

    public static Gtin Parse(string value)
    {
        var normalizado = value?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(normalizado))
            throw new ArgumentException("Codigo de barras nao pode ser vazio.", nameof(value));

        // Prefixo "INT-" identifica codigo interno em Code-128 (texto livre apos
        // o prefixo). Permite letras, digitos, hifen e underline.
        if (normalizado.StartsWith("INT-", StringComparison.Ordinal))
        {
            var sufixo = normalizado.Substring(4);
            if (sufixo.Length == 0)
                throw new ArgumentException("Codigo interno precisa de conteudo apos 'INT-'.", nameof(value));
            if (normalizado.Length > 50)
                throw new ArgumentException("Codigo interno muito longo (max 50 caracteres).", nameof(value));
            foreach (var ch in sufixo)
            {
                if (!(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_'))
                    throw new ArgumentException("Codigo interno aceita apenas letras, digitos, '-' e '_' apos 'INT-'.", nameof(value));
            }
            return new Gtin(normalizado, TipoGtin.InternoCode128);
        }

        if (!TodosDigitos(normalizado))
            throw new ArgumentException("Codigo de barras deve ser numerico (ou usar prefixo 'INT-' para codigo interno).", nameof(value));

        var tipo = normalizado.Length switch
        {
            8  => TipoGtin.Ean8,
            12 => TipoGtin.Upc12,
            13 => TipoGtin.Ean13,
            14 => TipoGtin.Gtin14,
            _  => throw new ArgumentException(
                $"Codigo de barras deve ter 8, 12, 13 ou 14 digitos (recebido: {normalizado.Length}).", nameof(value))
        };

        if (!ChecksumGs1Valido(normalizado))
            throw new ArgumentException("Codigo de barras invalido: digito verificador nao confere.", nameof(value));

        return new Gtin(normalizado, tipo);
    }

    public static bool TryParse(string? value, out Gtin? gtin)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            gtin = null;
            return false;
        }
        try
        {
            gtin = Parse(value);
            return true;
        }
        catch (ArgumentException)
        {
            gtin = null;
            return false;
        }
    }

    private static bool TodosDigitos(string s)
    {
        foreach (var ch in s)
            if (!char.IsDigit(ch)) return false;
        return true;
    }

    // Checksum padrao GS1: pesos alternados 3 e 1 (da direita para a esquerda,
    // excluindo o digito verificador), soma modulo 10, complemento modulo 10.
    // Funciona uniformemente para EAN-8, UPC-12, EAN-13 e GTIN-14.
    private static bool ChecksumGs1Valido(string digitos)
    {
        var soma = 0;
        for (var i = 0; i < digitos.Length - 1; i++)
        {
            var d = digitos[digitos.Length - 2 - i] - '0';
            soma += d * (i % 2 == 0 ? 3 : 1);
        }
        var esperado = (10 - (soma % 10)) % 10;
        var atual = digitos[digitos.Length - 1] - '0';
        return esperado == atual;
    }

    public static implicit operator string(Gtin g) => g.Valor;

    public override string ToString() => Valor;
}
