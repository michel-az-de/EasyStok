namespace EasyStock.Application.Ports.Output.Fiscal;

/// <summary>
/// Repositorio de Certificados Digitais A1 cifrados em rest. O .pfx + senha
/// vivem em <see cref="EasyStock.Domain.Integration.CredencialIntegracao"/>
/// cifrada via KEK rotacionavel (AES-256-GCM). Este repositorio expoe a busca
/// por tenant — a decifragem real e responsabilidade do <see cref="INfeCertificadoA1Service"/>.
/// </summary>
public interface ICertificadoA1Repository
{
    /// <summary>
    /// Busca a credencial do certificado A1 vinculado a configuracao fiscal da empresa.
    /// Retorna null se nao houver certificado configurado (caso valido em sandbox/mock).
    /// </summary>
    Task<CertificadoA1CredencialDto?> GetByEmpresaIdAsync(Guid empresaId, CancellationToken ct = default);
}

/// <summary>
/// DTO com a credencial cifrada do cert A1 lido do banco. Conteudo ainda nao
/// foi decifrado — uso real exige passar pelo <see cref="INfeCertificadoA1Service"/>.
/// Os campos <see cref="Iv"/> e <see cref="Tag"/> sao parametros do AES-256-GCM
/// (nonce 12 bytes + auth tag 16 bytes) armazenados separados do payload.
/// </summary>
public sealed record CertificadoA1CredencialDto(
    Guid CredencialId,
    Guid EmpresaId,
    string KekId,
    byte[] PayloadCifrado,
    byte[] Iv,
    byte[] Tag,
    DateTime? ValidoAte);
