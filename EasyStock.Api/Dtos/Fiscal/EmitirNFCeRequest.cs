using System.ComponentModel.DataAnnotations;
using EasyStock.Domain.Enums.Fiscal;

namespace EasyStock.Api.Dtos.Fiscal;

public sealed record EmitirNFCeRequest(
    [Required] Guid PedidoId,
    [Required] Guid LojaId,
    string? ClienteCpfCnpj,
    [Required] List<EmitirNFCePagamentoBody> Pagamentos,
    string? Origem);

public sealed record EmitirNFCePagamentoBody(
    [Required] FormaPagamentoFiscal FormaPagamento,
    [Required, Range(0.01, double.MaxValue)] decimal Valor,
    string? BandeiraCartao = null,
    string? CnpjCredenciadora = null,
    string? Nsu = null);

public sealed record CancelarNotaRequest(
    [Required, MinLength(15), MaxLength(255)] string Justificativa);

public sealed record InutilizarRequest(
    [Required] Guid LojaId,
    [Required, Range(1, 999)] int Serie,
    [Required, Range(1, 999_999_999)] int NumeroInicial,
    [Required, Range(1, 999_999_999)] int NumeroFinal,
    [Required, Range(2000, 9999)] int Ano,
    [Required, MinLength(15), MaxLength(255)] string Justificativa);
