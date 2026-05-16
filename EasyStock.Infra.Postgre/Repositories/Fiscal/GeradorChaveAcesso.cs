using EasyStock.Application.Ports.Output.Fiscal;

namespace EasyStock.Infra.Postgre.Repositories.Fiscal;

/// <summary>
/// Implementacao de <see cref="IGeradorChaveAcesso"/> seguindo layout SEFAZ NT 2014.004.
/// Estrutura da chave (44 digitos):
/// <code>
/// cUF(2) + AAMM(4) + CNPJ(14) + Modelo(2) + Serie(3) + nNF(9) + tpEmis(1) + cNF(8) + cDV(1) = 44
/// </code>
/// </summary>
public sealed class GeradorChaveAcesso : IGeradorChaveAcesso
{
    private static readonly Dictionary<string, int> CodigoUf = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AC"] = 12, ["AL"] = 27, ["AP"] = 16, ["AM"] = 13, ["BA"] = 29, ["CE"] = 23,
        ["DF"] = 53, ["ES"] = 32, ["GO"] = 52, ["MA"] = 21, ["MT"] = 51, ["MS"] = 50,
        ["MG"] = 31, ["PA"] = 15, ["PB"] = 25, ["PR"] = 41, ["PE"] = 26, ["PI"] = 22,
        ["RJ"] = 33, ["RN"] = 24, ["RS"] = 43, ["RO"] = 11, ["RR"] = 14, ["SC"] = 42,
        ["SP"] = 35, ["SE"] = 28, ["TO"] = 17,
    };

    public string Gerar(
        string uf,
        string cnpjEmitente,
        short serie,
        long numero,
        DateTime dataEmissao,
        string modeloFiscal = "65",
        byte tipoEmissao = 1)
    {
        if (string.IsNullOrWhiteSpace(uf) || !CodigoUf.TryGetValue(uf.Trim(), out var cUf))
            throw new ArgumentException($"UF invalida: '{uf}'.", nameof(uf));

        var cnpj = SomenteDigitos(cnpjEmitente);
        if (cnpj.Length != 14)
            throw new ArgumentException("CNPJ deve ter 14 digitos.", nameof(cnpjEmitente));

        if (serie is <= 0 or > 999)
            throw new ArgumentOutOfRangeException(nameof(serie), "Serie deve estar em 1..999.");
        if (numero is <= 0 or > 999_999_999)
            throw new ArgumentOutOfRangeException(nameof(numero), "Numero deve estar em 1..999.999.999.");
        if (modeloFiscal is not ("55" or "65"))
            throw new ArgumentException("Modelo fiscal suportado: 55 (NFe) ou 65 (NFC-e).", nameof(modeloFiscal));
        if (tipoEmissao is not (1 or 9))
            throw new ArgumentException("TipoEmissao suportado: 1 (Normal) ou 9 (Contingencia offline).", nameof(tipoEmissao));

        var aamm = dataEmissao.ToString("yyMM");
        var cNf = GerarCNumerico(cnpj, serie, numero, dataEmissao);

        var chave43 =
            cUf.ToString("D2") +
            aamm +
            cnpj +
            modeloFiscal +
            serie.ToString("D3") +
            numero.ToString("D9") +
            tipoEmissao.ToString("D1") +
            cNf;

        if (chave43.Length != 43)
            throw new InvalidOperationException($"Chave parcial deveria ter 43 digitos; obteve {chave43.Length}.");

        var dv = CalcularDvModulo11(chave43);
        return chave43 + dv;
    }

    public bool ValidarDv(string chaveAcesso)
    {
        if (string.IsNullOrWhiteSpace(chaveAcesso) || chaveAcesso.Length != 44) return false;
        if (!chaveAcesso.All(char.IsDigit)) return false;

        var dvCalculado = CalcularDvModulo11(chaveAcesso[..43]);
        return dvCalculado == chaveAcesso[43].ToString();
    }

    /// <summary>
    /// Gera os 8 digitos do cNF (codigo numerico aleatorio dentro da chave).
    /// Determinismo baseado em (cnpj + serie + numero + data) — re-chamar com
    /// mesmo input gera mesmo cNF (idempotente, importante se use case re-emitir).
    /// </summary>
    private static string GerarCNumerico(string cnpj, short serie, long numero, DateTime dataEmissao)
    {
        unchecked
        {
            // Hash deterministico simples (FNV-1a) — nao precisa ser cripto seguro, so
            // tem que ser estavel e distribuir os bits razoavelmente.
            ulong hash = 14695981039346656037UL;
            foreach (var ch in cnpj)
            {
                hash ^= ch;
                hash *= 1099511628211UL;
            }
            hash ^= (ulong)serie;
            hash *= 1099511628211UL;
            hash ^= (ulong)numero;
            hash *= 1099511628211UL;
            hash ^= (ulong)dataEmissao.Ticks;
            hash *= 1099511628211UL;

            var cNf = (hash % 100_000_000UL).ToString("D8");
            return cNf;
        }
    }

    /// <summary>Algoritmo SEFAZ: DV = 11 - (soma_ponderada mod 11). 10 ou 11 -> 0.</summary>
    private static string CalcularDvModulo11(string str)
    {
        if (str.Length != 43)
            throw new ArgumentException("Esperado 43 digitos para calcular DV.", nameof(str));

        var pesos = new[] { 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        var soma = 0;
        var pesoIdx = 0;

        for (var i = str.Length - 1; i >= 0; i--)
        {
            var dig = str[i] - '0';
            soma += dig * pesos[pesoIdx];
            pesoIdx = (pesoIdx + 1) % pesos.Length;
        }

        var resto = soma % 11;
        var dv = 11 - resto;
        if (dv >= 10) dv = 0;
        return dv.ToString();
    }

    private static string SomenteDigitos(string input) =>
        new(input.Where(char.IsDigit).ToArray());
}
