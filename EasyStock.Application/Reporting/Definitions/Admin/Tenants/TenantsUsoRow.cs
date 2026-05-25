namespace EasyStock.Application.Reporting.Definitions.Admin.Tenants;

/// <summary>Linha do relatório de uso de tenants — uma empresa.</summary>
public sealed record TenantsUsoRow(
    Guid EmpresaId,
    string EmpresaNome,
    string EmpresaDocumento,
    string Plano,
    string StatusAssinatura,
    DateTime DataCadastro,
    DateTime? DataUltimoLogin,
    int TotalUsuarios,
    int TotalLojas,
    int TotalVendas30Dias,
    decimal ReceitaUltimos30Dias,
    DateTime? DataVencimentoAssinatura);
