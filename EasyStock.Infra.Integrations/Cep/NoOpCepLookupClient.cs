using EasyStock.Application.Ports.Output.Lookup;

namespace EasyStock.Infra.Integrations.Cep;

/// <summary>
/// Implementação no-op de <see cref="ICepLookupClient"/>. Sempre retorna
/// <see langword="null"/> — usada quando a feature flag
/// <c>ENABLE_VIACEP_LOOKUP=false</c> (default em dev e em ambientes sem
/// acesso à internet/ViaCEP). O use case de frete trata o null como
/// "sem bairro" e segue só pelo CEP range.
/// </summary>
public sealed class NoOpCepLookupClient : ICepLookupClient
{
    public Task<CepLookupResult?> LookupAsync(string cep, CancellationToken ct = default) =>
        Task.FromResult<CepLookupResult?>(null);
}
