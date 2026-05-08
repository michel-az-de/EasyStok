using System;
using System.Linq;
using EasyStock.Domain.Enums.Fiscal;

namespace EasyStock.Domain.ValueObjects.Fiscal;

/// <summary>
/// Chave de acesso de 44 dígitos da NFe/NFC-e segundo layout 4.00.
/// Estrutura: UF(2) + AAMM(4) + CNPJ(14) + Mod(2) + Serie(3) + nNF(9) + tpEmis(1) + cNF(8) + cDV(1).
/// O DV é calculado por módulo 11 com pesos 2..9 cíclicos da direita pra esquerda.
/// </summary>
public sealed record ChaveAcessoNFe
{
    public string Valor { get; }

    private ChaveAcessoNFe(string valor)
    {
        Valor = valor;
    }

    public static ChaveAcessoNFe Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Chave de acesso é obrigatória.", nameof(raw));

        var s = new string(raw.Where(char.IsDigit).ToArray());
        if (s.Length != 44)
            throw new ArgumentException($"Chave deve ter 44 dígitos. Recebido: {s.Length}.", nameof(raw));

        if (!ValidarDigitoVerificador(s))
            throw new ArgumentException("Dígito verificador inválido.", nameof(raw));

        return new ChaveAcessoNFe(s);
    }

    public static ChaveAcessoNFe Construir(
        string ufCodigoIbge,
        DateTime dhEmi,
        string cnpj,
        ModeloDocumentoFiscal modelo,
        int serie,
        int numero,
        TipoEmissao tpEmis,
        string codigoNumericoOitoDigitos)
    {
        if (string.IsNullOrWhiteSpace(ufCodigoIbge) || ufCodigoIbge.Length != 2)
            throw new ArgumentException("UF (código IBGE) deve ter 2 dígitos.", nameof(ufCodigoIbge));

        var cnpjDigitos = new string((cnpj ?? "").Where(char.IsDigit).ToArray());
        if (cnpjDigitos.Length != 14)
            throw new ArgumentException("CNPJ deve ter 14 dígitos.", nameof(cnpj));

        if (serie is <= 0 or > 999)
            throw new ArgumentOutOfRangeException(nameof(serie), "Série deve estar entre 1 e 999.");

        if (numero is <= 0 or > 999_999_999)
            throw new ArgumentOutOfRangeException(nameof(numero), "Número fora do intervalo 1..999.999.999.");

        var cNum = new string((codigoNumericoOitoDigitos ?? "").Where(char.IsDigit).ToArray());
        if (cNum.Length is < 1 or > 8)
            throw new ArgumentException("Código numérico deve ter até 8 dígitos.", nameof(codigoNumericoOitoDigitos));
        cNum = cNum.PadLeft(8, '0');

        var aamm = dhEmi.ToString("yyMM");
        var mod = ((short)modelo).ToString("D2");
        var ser = serie.ToString("D3");
        var nNF = numero.ToString("D9");
        var tp = ((byte)tpEmis).ToString();

        var semDv = $"{ufCodigoIbge}{aamm}{cnpjDigitos}{mod}{ser}{nNF}{tp}{cNum}";
        if (semDv.Length != 43)
            throw new InvalidOperationException($"Chave sem DV deve ter 43 dígitos, foi gerada com {semDv.Length}.");

        var dv = CalcularDigitoVerificador(semDv);
        return new ChaveAcessoNFe(semDv + dv);
    }

    public string Uf => Valor.Substring(0, 2);
    public string AnoMes => Valor.Substring(2, 4);
    public string Cnpj => Valor.Substring(6, 14);
    public string Modelo => Valor.Substring(20, 2);
    public string Serie => Valor.Substring(22, 3);
    public string Numero => Valor.Substring(25, 9);
    public string TipoEmissaoStr => Valor.Substring(34, 1);
    public string CodigoNumerico => Valor.Substring(35, 8);
    public string DigitoVerificador => Valor.Substring(43, 1);

    public string Formatada =>
        string.Join(' ', Enumerable.Range(0, 11).Select(i => Valor.Substring(i * 4, 4)));

    public override string ToString() => Valor;

    internal static char CalcularDigitoVerificador(string semDv)
    {
        if (semDv.Length != 43)
            throw new ArgumentException("Esperado 43 dígitos antes do DV.", nameof(semDv));

        var pesos = new[] { 2, 3, 4, 5, 6, 7, 8, 9 };
        var soma = 0;
        for (var i = 0; i < semDv.Length; i++)
        {
            var digito = semDv[semDv.Length - 1 - i] - '0';
            var peso = pesos[i % pesos.Length];
            soma += digito * peso;
        }

        var resto = soma % 11;
        var dv = 11 - resto;
        if (dv >= 10) dv = 0;
        return (char)('0' + dv);
    }

    private static bool ValidarDigitoVerificador(string chave44)
    {
        if (chave44.Length != 44) return false;
        var calculado = CalcularDigitoVerificador(chave44.Substring(0, 43));
        return calculado == chave44[43];
    }
}
