using EasyStock.Domain.Fiscal;
using EasyStock.Domain.Integration;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.Ports.Output.Fiscal;

/// <summary>
/// DTO de configuracao fiscal passado ao <see cref="IGatewayFiscal"/>. Snapshot
/// imutavel da <see cref="EmpresaConfiguracaoFiscal"/> no momento do envio, com
/// credencial ja resolvida (token Focus, cert A1, etc.) — adapter nao re-busca
/// no banco a cada chamada.
///
/// <para>
/// Resolver: <see cref="EasyStock.Application.Services.Fiscal.IConfigFiscalResolver"/>
/// (a ser criado em F1) compoe este DTO a partir de <see cref="EmpresaConfiguracaoFiscal"/>
/// + <see cref="EasyStock.Domain.Integration.CredencialIntegracao"/> decifrada.
/// </para>
/// </summary>
public sealed record ConfigFiscalDto(
    Guid EmpresaId,
    string Provedor,
    AmbienteIntegracao Ambiente,
    RegimeTributario RegimeTributario,
    string Cnpj,
    string? InscricaoEstadual,
    string? InscricaoMunicipal,
    Endereco? Endereco,
    short SerieNfce,
    string? CredencialToken,
    byte[]? CertificadoA1Bytes,
    string? CertificadoA1Senha,
    string? CscId,
    string? CscToken);
