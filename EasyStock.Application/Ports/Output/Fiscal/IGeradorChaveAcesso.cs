namespace EasyStock.Application.Ports.Output.Fiscal;

/// <summary>
/// Gera a chave de acesso de 44 digitos da NFC-e segundo o layout SEFAZ:
/// <c>cUF (2) + AAMM (4) + CNPJ (14) + Modelo (2) + Serie (3) + nNF (9) + tpEmis (1) + cNF (8) + cDV (1)</c>.
/// O DV final usa modulo 11 sobre os 43 digitos anteriores.
///
/// <para>
/// <b>Determinismo:</b> dado o mesmo input (CNPJ + serie + numero + data + tpEmis + cNF),
/// retorna sempre a mesma chave. O <c>cNF</c> (codigo numerico aleatorio) e responsabilidade
/// do gerador para evitar colisao entre chaves de tenants distintos.
/// </para>
///
/// <para>
/// <b>Idempotencia:</b> chamado apos numero reservado. Use case persiste a chave gerada
/// junto com o NfeDocumento — re-chamar com mesmo input gera mesma chave (sem ambiente, claro:
/// o cNF e parte da chave).
/// </para>
/// </summary>
public interface IGeradorChaveAcesso
{
    /// <summary>
    /// Gera chave de acesso completa (44 digitos) com DV validado.
    /// </summary>
    /// <param name="uf">UF do emitente (ex: "SP"). 2 letras.</param>
    /// <param name="cnpjEmitente">CNPJ sem mascara (14 digitos).</param>
    /// <param name="serie">Serie da NFC-e (1..999).</param>
    /// <param name="numero">Numero NFC-e (1..999.999.999).</param>
    /// <param name="dataEmissao">Data para compor AAMM.</param>
    /// <param name="modeloFiscal">"65" para NFC-e, "55" para NFe.</param>
    /// <param name="tipoEmissao">1=Normal, 9=Contingencia offline. Default=1.</param>
    /// <returns>Chave de 44 digitos.</returns>
    string Gerar(
        string uf,
        string cnpjEmitente,
        short serie,
        long numero,
        DateTime dataEmissao,
        string modeloFiscal = "65",
        byte tipoEmissao = 1);

    /// <summary>Valida que uma chave de 44 digitos tem DV correto.</summary>
    bool ValidarDv(string chaveAcesso);
}
