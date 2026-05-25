using System.ComponentModel.DataAnnotations;
using EasyStock.Domain.Fiscal;

namespace EasyStock.Api.Models.Fiscal;

/// <summary>
/// Atualiza os dados do emitente fiscal (regime, IE/IM, endereco) — passo 1
/// do wizard de configuracao. Idempotente: pode ser chamado varias vezes para
/// editar campos individuais.
/// </summary>
public sealed record AtualizarDadosEmitenteRequest(
    [property: Required] RegimeTributario RegimeTributario,
    [property: MaxLength(50)] string? InscricaoEstadual,
    [property: MaxLength(50)] string? InscricaoMunicipal,
    EnderecoFiscalInput? Endereco);

public sealed record EnderecoFiscalInput(
    [property: MaxLength(200)] string? Logradouro,
    [property: MaxLength(20)] string? Numero,
    [property: MaxLength(60)] string? Complemento,
    [property: MaxLength(100)] string? Bairro,
    [property: MaxLength(100)] string? Cidade,
    [property: MaxLength(2)] string? Uf,
    [property: MaxLength(10)] string? Cep);
