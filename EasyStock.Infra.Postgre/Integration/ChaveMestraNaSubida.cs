using System.Security.Cryptography;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Postgre.Integration;

/// <summary>A chave-mestra falta, é inválida ou não decifra uma linha ativa do cofre. Em Production o host não sobe.</summary>
public sealed class ChaveMestraInvalidaException(string mensagem) : InvalidOperationException(mensagem);

/// <summary>
/// Fail-fast da chave-mestra (I4.2, #1708). Só em Production: API e Worker não sobem sem KEK corrente, nem se
/// alguma linha ativa do cofre não decifra com as KEKs que o processo conhece. Antes disso, uma linha com
/// <c>kek_id</c> desconhecido deixava a API verde e o envio quebrado. Fora de Production vale o aviso da I4.1.
/// A mensagem cita variável, id da KEK e id da linha; nunca o valor da chave nem o conteúdo decifrado.
/// </summary>
public static class ChaveMestraNaSubida
{
    public static bool EhProducao(string? ambiente) =>
        string.Equals(ambiente, "Production", StringComparison.OrdinalIgnoreCase);

    /// <summary>Lança se, em Production, a KEK corrente falta ou é inválida.</summary>
    public static void ExigirEmProducao(IConfiguration configuration, string? ambiente, string host)
    {
        if (EhProducao(ambiente) && ChaveMestraConfiguracao.Diagnosticar(configuration) is { } motivo)
            throw new ChaveMestraInvalidaException($"{host} não sobe em Production: {motivo}");
    }

    /// <summary>Lança se, em Production, alguma linha ativa do cofre não resolve a KEK ou não decifra.</summary>
    public static async Task ExigirLinhasAtivasLegiveisAsync(
        ICredencialIntegracaoRepository repo, IConfiguration configuration, string? ambiente, string host,
        CancellationToken ct = default)
    {
        if (!EhProducao(ambiente)) return;

        var ilegiveis = new List<string>();
        foreach (var linha in await repo.ListarAtivasAsync(ct))
        {
            byte[]? chave = null;
            try
            {
                chave = IntegrationCredentialResolver.ResolveKek(configuration, linha.KekId);
                CryptographicOperations.ZeroMemory(
                    IntegrationCredentialResolver.DecryptAesGcm(linha.PayloadCifrado, linha.Iv, linha.Tag, chave));
            }
            catch (Exception ex) when (ex is InvalidOperationException or CryptographicException)
            {
                var motivo = ex is CryptographicException ? "não decifra" : "KEK desconhecida ou inválida";
                ilegiveis.Add($"linha {linha.Id} (kek '{linha.KekId}', {linha.ProviderKey}): {motivo}");
            }
            finally
            {
                if (chave is not null) CryptographicOperations.ZeroMemory(chave);
            }
        }

        if (ilegiveis.Count > 0)
            throw new ChaveMestraInvalidaException(
                $"{host} não sobe em Production: {ilegiveis.Count} credencial(is) ativa(s) que a chave-mestra não lê. "
                + string.Join("; ", ilegiveis)
                + $". Confira {ChaveMestraConfiguracao.VarKekId}/{ChaveMestraConfiguracao.VarKek} e a KEK anterior.");
    }

    /// <summary>
    /// As duas verificações, na subida do host. Lê o cofre de todas as lojas num escopo próprio e curto, pela
    /// porta de bypass da RLS: é verificação de plataforma, sem tenant por natureza.
    /// </summary>
    public static async Task ExigirAsync(IServiceProvider services, string? ambiente, string host, CancellationToken ct = default)
    {
        if (!EhProducao(ambiente)) return;

        using var escopo = services.CreateScope();
        var configuration = escopo.ServiceProvider.GetRequiredService<IConfiguration>();
        ExigirEmProducao(configuration, ambiente, host);

        using var semRls = escopo.ServiceProvider.GetRequiredService<IRowLevelSecurityBypass>().Begin();
        await ExigirLinhasAtivasLegiveisAsync(
            escopo.ServiceProvider.GetRequiredService<ICredencialIntegracaoRepository>(), configuration, ambiente, host, ct);
    }
}
