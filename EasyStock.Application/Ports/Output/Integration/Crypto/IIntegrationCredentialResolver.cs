using EasyStock.Domain.Integration;

namespace EasyStock.Application.Ports.Output.Integration.Crypto;

/// <summary>
/// Port pra resolução de credenciais de integração externa por tenant.
/// Adapters (em <c>EasyStock.Infra.Integrations</c>) implementam a parte
/// de cifragem/decifragem AES-GCM com KEK do KMS configurado.
///
/// <para>
/// Use cases e sagas devem injetar este port — nunca acessar
/// <c>CredencialIntegracao</c> diretamente do repositório nem manipular
/// payload cifrado.
/// </para>
///
/// <para>
/// O payload <c>T</c> é o schema específico do provider (ex:
/// <c>MercadoPagoCredencial</c> com AccessToken, ClientSecret etc.).
/// O resolver desserializa o JSON decifrado pra <c>T</c>.
/// </para>
/// </summary>
public interface IIntegrationCredentialResolver
{
    /// <summary>
    /// Obtém a credencial decifrada pra um tenant + provider + ambiente.
    /// Retorna null se não existir, estiver inativa ou expirada — caller
    /// decide se cai pra stub ou levanta erro.
    /// </summary>
    Task<T?> ObterAsync<T>(
        Guid empresaId,
        string providerKey,
        AmbienteIntegracao ambiente,
        CancellationToken ct = default) where T : class;

    /// <summary>
    /// Salva (cria ou substitui) a credencial pra um tenant + provider +
    /// ambiente. Cifra o payload <c>T</c> antes de persistir e desativa
    /// versão anterior se houver.
    /// </summary>
    Task SalvarAsync<T>(
        Guid empresaId,
        CategoriaIntegracao categoria,
        string providerKey,
        AmbienteIntegracao ambiente,
        T payload,
        Guid criadoPorUsuarioId,
        DateTime? validoAte = null,
        CancellationToken ct = default) where T : class;

    /// <summary>
    /// Re-cifra todas as credenciais com a KEK identificada por
    /// <paramref name="novoKekId"/>. Operação batch usada em rotação anual
    /// ou em incidentes de suspeita de comprometimento de KEK.
    /// </summary>
    Task RotacionarKekAsync(string novoKekId, CancellationToken ct = default);
}
