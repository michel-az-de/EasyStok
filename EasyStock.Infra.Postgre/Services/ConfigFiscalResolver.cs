using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Services.Fiscal;
using EasyStock.Domain.Exceptions;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Services;

/// <summary>
/// Implementacao basica de <see cref="IConfigFiscalResolver"/>. Compoe o
/// <see cref="ConfigFiscalDto"/> a partir de <c>EmpresaConfiguracaoFiscal</c>
/// + dados da <c>Empresa</c>. Cert A1 ainda nao decifrado nesta versao
/// (F2 ira completar via <see cref="INfeCertificadoA1Service"/>).
///
/// <para>
/// <b>F1 escopo:</b> retorna config para provedor "mock" funcional (sem cert),
/// suficiente para use cases compilarem e rodarem testes unitarios. Producao
/// real exige F2 com decifragem AES-256-GCM.
/// </para>
/// </summary>
public sealed class ConfigFiscalResolver(EasyStockDbContext db) : IConfigFiscalResolver
{
    public async Task<ConfigFiscalDto> ResolveAsync(Guid empresaId, CancellationToken ct = default)
    {
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId obrigatorio.", nameof(empresaId));

        var config = await db.EmpresaConfiguracoesFiscais
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.EmpresaId == empresaId, ct)
            ?? throw new RegraDeDominioVioladaException(
                $"Empresa {empresaId} nao possui configuracao fiscal. Criar via wizard antes de emitir.");

        if (!config.Habilitada)
            throw new RegraDeDominioVioladaException(
                $"Emissao fiscal nao habilitada para empresa {empresaId}.");

        var empresa = await db.Empresas
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == empresaId, ct)
            ?? throw new RegraDeDominioVioladaException($"Empresa {empresaId} nao encontrada.");

        // TODO F2: integrar INfeCertificadoA1Service para decifrar cert A1 via KEK.
        // Por enquanto retorna sem cert — provedor "mock" aceita.
        return new ConfigFiscalDto(
            EmpresaId: empresaId,
            Provedor: config.ProvedorPreferido,
            Ambiente: config.Ambiente,
            RegimeTributario: config.RegimeTributario,
            Cnpj: empresa.Documento ?? throw new RegraDeDominioVioladaException("Empresa sem CNPJ (campo Documento)."),
            InscricaoEstadual: config.InscricaoEstadual,
            InscricaoMunicipal: config.InscricaoMunicipal,
            Endereco: config.Endereco,
            SerieNfce: config.SerieNfce,
            CredencialToken: null,
            CertificadoA1Bytes: null,
            CertificadoA1Senha: null,
            CscId: null,
            CscToken: null);
    }
}
