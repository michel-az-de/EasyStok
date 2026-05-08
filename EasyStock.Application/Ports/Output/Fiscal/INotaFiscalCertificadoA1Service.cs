namespace EasyStock.Application.Ports.Output.Fiscal;

/// <summary>
/// Serviço de alto nivel que decifra o certificado A1 ativo para uso
/// pelo gateway fiscal. Encapsula a chamada ao IKekProvider.
/// Resultado vive APENAS dentro do request — nunca persistir nem cachear
/// entre requests pra limitar exposição.
/// </summary>
public interface INotaFiscalCertificadoA1Service
{
    Task<CertificadoA1Decifrado> ResolverAtivoAsync(Guid empresaId, CancellationToken ct);

    Task RegistrarUploadAsync(
        Guid empresaId,
        byte[] pfxBytes,
        string senha,
        Guid usuarioId,
        CancellationToken ct);
}

public sealed record CertificadoA1Decifrado(
    byte[] Pfx,
    string Senha,
    string NomeTitular,
    string DocumentoTitular,
    DateTime ValidoAte);
