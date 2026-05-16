using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Services.Fiscal;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EasyStock.Infra.Postgre.Services;

/// <summary>
/// Resolver de configuracao fiscal por tenant com cache curto (60s) para evitar
/// round-trips repetidos em rajadas de emissao. Decifra token Focus e cert A1 da
/// <see cref="CredencialIntegracao"/> ativa do tenant.
///
/// <para>
/// <b>Cache:</b> TTL 60s por tenant. Invalida implicitamente — se admin atualizar
/// config/cert via API, a proxima emissao apos 60s pega novidade. Em update urgente,
/// reiniciar API/Worker (cache em memoria).
/// </para>
///
/// <para>
/// <b>Cert A1:</b> quando habilitada e disponivel, decifra via <see cref="INfeCertificadoA1Service"/>.
/// Se ausente em ambiente Production, lanca excecao. Sandbox aceita sem cert.
/// </para>
/// </summary>
public sealed class ConfigFiscalResolver(
    EasyStockDbContext db,
    INfeCertificadoA1Service certService,
    IMemoryCache cache) : IConfigFiscalResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public async Task<ConfigFiscalDto> ResolveAsync(Guid empresaId, CancellationToken ct = default)
    {
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId obrigatório.", nameof(empresaId));

        var cacheKey = $"fiscal:cfg:{empresaId:N}";
        if (cache.TryGetValue<ConfigFiscalDto>(cacheKey, out var cached) && cached is not null)
            return cached;

        var config = await db.EmpresaConfiguracoesFiscais
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.EmpresaId == empresaId, ct)
            ?? throw new RegraDeDominioVioladaException(
                $"Empresa {empresaId} não possui configuração fiscal. Crie a configuração antes de emitir.");

        if (!config.Habilitada)
            throw new RegraDeDominioVioladaException(
                "Emissão fiscal não habilitada para este tenant. Habilite via POST /api/configuracao-fiscal/habilitar.");

        var empresa = await db.Empresas
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == empresaId, ct)
            ?? throw new RegraDeDominioVioladaException($"Empresa {empresaId} não encontrada.");

        var cnpj = empresa.Documento
            ?? throw new RegraDeDominioVioladaException("Empresa sem CNPJ cadastrado (campo Documento vazio).");

        // Token Focus + cert A1 — busca credenciais ativas Fiscal por tenant
        string? credencialToken = null;
        byte[]? certA1Bytes = null;
        string? certA1Senha = null;
        string? cscId = null;
        string? cscToken = null;

        var credenciaisFiscais = await db.Set<CredencialIntegracao>()
            .AsNoTracking()
            .Where(c => c.EmpresaId == empresaId
                     && c.Categoria == CategoriaIntegracao.Fiscal
                     && c.Ativo)
            .ToListAsync(ct);

        foreach (var cred in credenciaisFiscais)
        {
            // Token Focus armazenado como providerKey "focus-token"
            if (string.Equals(cred.ProviderKey, "focus-token", StringComparison.OrdinalIgnoreCase))
            {
                credencialToken = await DecifrarStringSimplesAsync(cred, ct);
                continue;
            }

            // Cert A1 — providerKey "sefaz" (criado pelo upload via ConfiguracaoFiscalController)
            if (string.Equals(cred.ProviderKey, "sefaz", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var dto = new CertificadoA1CredencialDto(
                        cred.Id, cred.EmpresaId, cred.KekId, cred.PayloadCifrado,
                        cred.Iv, cred.Tag, cred.ValidoAte);

                    var decifrado = await certService.DecifrarAsync(dto, ct);
                    certA1Bytes = decifrado.PfxBytes;
                    certA1Senha = decifrado.Senha;
                }
                catch (Exception)
                {
                    // Em sandbox/mock, cert A1 pode estar ausente ou ilegível — não bloqueia.
                    // Em Production, validação acontece abaixo.
                }
                continue;
            }
        }

        if (config.Ambiente == AmbienteIntegracao.Production)
        {
            if (string.IsNullOrEmpty(credencialToken))
                throw new RegraDeDominioVioladaException(
                    "Token do gateway fiscal não configurado para produção. Cadastre a credencial 'focus-token' do tenant.");
            if (certA1Bytes is null || certA1Bytes.Length == 0)
                throw new RegraDeDominioVioladaException(
                    "Certificado A1 obrigatório em produção. Faça upload via POST /api/configuracao-fiscal/certificado.");
        }

        var dto2 = new ConfigFiscalDto(
            EmpresaId: empresaId,
            Provedor: config.ProvedorPreferido,
            Ambiente: config.Ambiente,
            RegimeTributario: config.RegimeTributario,
            Cnpj: cnpj,
            InscricaoEstadual: config.InscricaoEstadual,
            InscricaoMunicipal: config.InscricaoMunicipal,
            Endereco: config.Endereco,
            SerieNfce: config.SerieNfce,
            CredencialToken: credencialToken,
            CertificadoA1Bytes: certA1Bytes,
            CertificadoA1Senha: certA1Senha,
            CscId: cscId,
            CscToken: cscToken);

        cache.Set(cacheKey, dto2, CacheTtl);
        return dto2;
    }

    /// <summary>
    /// Decifra payload contendo string simples (ex: token Focus persistido como UTF-8 cifrado).
    /// Para .pfx use <see cref="INfeCertificadoA1Service.DecifrarAsync"/>.
    /// </summary>
    private async Task<string?> DecifrarStringSimplesAsync(CredencialIntegracao cred, CancellationToken ct)
    {
        // Reusa decifragem do cert service (mesma KEK + DataProtection); aceita payload arbitrário.
        try
        {
            var dto = new CertificadoA1CredencialDto(
                cred.Id, cred.EmpresaId, cred.KekId, cred.PayloadCifrado,
                cred.Iv, cred.Tag, cred.ValidoAte);
            var decifrado = await certService.DecifrarAsync(dto, ct);
            // Para tokens simples, o "envelope" pode conter PfxBytes=UTF-8(token) e Senha vazia
            return decifrado.PfxBytes.Length > 0 ? System.Text.Encoding.UTF8.GetString(decifrado.PfxBytes) : null;
        }
        catch
        {
            return null;
        }
    }
}
