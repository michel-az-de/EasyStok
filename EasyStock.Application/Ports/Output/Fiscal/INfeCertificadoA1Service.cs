namespace EasyStock.Application.Ports.Output.Fiscal;

/// <summary>
/// Servico de decifragem e validacao de certificados A1. Implementacao em
/// <c>EasyStock.Infra.Integrations.Fiscal/FocusNFe/NfeCertificadoA1Service.cs</c>.
///
/// <para>
/// <b>Seguranca:</b> NUNCA logar os bytes do cert ou senha. NUNCA persistir em
/// disco. NUNCA passar via query string. O DTO retornado deve ser usado IMEDIATAMENTE
/// e descartado (caller nao deve guardar referencia).
/// </para>
/// </summary>
public interface INfeCertificadoA1Service
{
    /// <summary>
    /// Decifra o payload (.pfx + senha) usando KEK rotacionavel e retorna bytes prontos.
    /// </summary>
    /// <exception cref="InvalidOperationException">KEK nao encontrada ou payload corrompido.</exception>
    Task<CertificadoA1Decifrado> DecifrarAsync(CertificadoA1CredencialDto credencial, CancellationToken ct = default);

    /// <summary>
    /// Valida que o cert .pfx + senha sao parsaveis (no upload pelo admin). NAO persiste.
    /// </summary>
    /// <returns>Data de validade do cert se parseavel, exception caso contrario.</returns>
    DateTime ValidarUpload(byte[] pfxBytes, string senha);

    /// <summary>
    /// Cifra um novo .pfx + senha com a KEK ativa, retornando o payload pronto para persistir.
    /// </summary>
    Task<byte[]> CifrarParaArmazenamentoAsync(byte[] pfxBytes, string senha, CancellationToken ct = default);
}

/// <summary>Cert A1 decifrado e pronto para uso pelo gateway. Caller deve usar e descartar.</summary>
public sealed record CertificadoA1Decifrado(byte[] PfxBytes, string Senha, DateTime ValidoAte);
