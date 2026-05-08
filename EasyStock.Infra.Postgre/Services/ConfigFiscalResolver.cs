using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Fiscal;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums.Fiscal;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Infra.Postgre.Services;

/// <summary>
/// Implementação do IConfigFiscalResolver. Lê dados do emitente da
/// Empresa + Loja existentes; campos endereço/IE que ainda não existem
/// no schema vêm de "FocusNFe:DefaultsEmitente:*" em appsettings ou de
/// override por loja em uma migration futura. Pra tenants em produção,
/// configurar todos os campos.
/// </summary>
public sealed class ConfigFiscalResolver(
    IEmpresaRepository empresaRepo,
    ILojaRepository lojaRepo,
    IConfiguration config) : IConfigFiscalResolver
{
    public async Task<ConfigFiscalDto> ResolverAsync(Guid empresaId, Guid lojaId, CancellationToken ct)
    {
        var empresa = await empresaRepo.GetByIdAsync(empresaId)
            ?? throw new UseCaseValidationException($"Empresa {empresaId} não encontrada.");

        var loja = await lojaRepo.GetByIdAsync(empresaId, lojaId)
            ?? throw new UseCaseValidationException($"Loja {lojaId} não encontrada para empresa {empresaId}.");

        var defaults = config.GetSection("FocusNFe:DefaultsEmitente");

        var cnpj = LimparDigitos(empresa.Documento ?? defaults["Cnpj"] ?? "")
            ?? throw new UseCaseValidationException("CNPJ do emitente não configurado.");
        if (cnpj.Length != 14)
            throw new UseCaseValidationException("CNPJ do emitente deve ter 14 dígitos.");

        var ie = defaults["InscricaoEstadual"] ?? "ISENTO";
        var serieStr = config["FocusNFe:DefaultsEmitente:Serie"] ?? "1";
        var serie = int.Parse(serieStr);

        var ambiente = ParseAmbiente(config["FocusNFe:DefaultsEmitente:Ambiente"]);
        var regime = ParseRegime(config["FocusNFe:DefaultsEmitente:RegimeTributario"]);

        return new ConfigFiscalDto(
            EmpresaId: empresaId,
            LojaId: lojaId,
            Ambiente: ambiente,
            Serie: serie,
            CnpjEmitente: cnpj,
            InscricaoEstadualEmitente: ie,
            NomeEmitente: empresa.Nome,
            UfEmitente: defaults["Uf"] ?? "SP",
            UfCodigoIbge: defaults["UfCodigoIbge"] ?? "35",
            CepEmitente: LimparDigitos(defaults["Cep"] ?? "01000000") ?? "01000000",
            LogradouroEmitente: defaults["Logradouro"] ?? "Rua",
            NumeroEnderecoEmitente: defaults["Numero"] ?? "0",
            ComplementoEnderecoEmitente: defaults["Complemento"],
            BairroEmitente: defaults["Bairro"] ?? "Centro",
            MunicipioEmitente: defaults["Municipio"] ?? "São Paulo",
            MunicipioCodigoIbge: defaults["MunicipioCodigoIbge"] ?? "3550308",
            RegimeTributario: regime,
            TokenFocus: "",
            CscId: defaults["CscId"],
            Csc: defaults["Csc"],
            WebhookSecret: config["FocusNFe:WebhookSecret"]);
    }

    private static string? LimparDigitos(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        return new string(raw.Where(char.IsDigit).ToArray());
    }

    private static AmbienteSefaz ParseAmbiente(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return AmbienteSefaz.Homologacao;
        return raw.Equals("Producao", StringComparison.OrdinalIgnoreCase)
            ? AmbienteSefaz.Producao
            : AmbienteSefaz.Homologacao;
    }

    private static RegimeTributario ParseRegime(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return RegimeTributario.SimplesNacional;
        return raw switch
        {
            "1" or "SimplesNacional" => RegimeTributario.SimplesNacional,
            "2" or "SimplesNacionalExcessoSubLimite" => RegimeTributario.SimplesNacionalExcessoSubLimite,
            "3" or "RegimeNormal" => RegimeTributario.RegimeNormal,
            "4" or "MEI" or "SimplesNacionalMEI" => RegimeTributario.SimplesNacionalMEI,
            _ => RegimeTributario.SimplesNacional,
        };
    }
}
