using System.ComponentModel.DataAnnotations;

namespace EasyStock.Api.Models.Fiscal;

/// <summary>
/// Escolhe o provedor SEFAZ usado pelo tenant. Aceita lowercase: "mock", "focus", "enotas".
/// "mock" so e permitido em ambiente Sandbox (regra de dominio em
/// <see cref="EasyStock.Domain.Fiscal.EmpresaConfiguracaoFiscal.Habilitar"/>).
/// </summary>
public sealed record EscolherProvedorRequest(
    [property: Required, MaxLength(20)] string Provedor);
