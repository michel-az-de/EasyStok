using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums.Fiscal;

namespace EasyStock.Application.UseCases.Fiscal.EmitirNotaFiscalConsumidor;

public sealed record EmitirNotaFiscalConsumidorCommand(
    Guid EmpresaId,
    Guid PedidoId,
    Guid LojaId,
    string? ClienteCpfCnpj,
    IReadOnlyList<EmitirNotaFiscalPagamentoInput> Pagamentos,
    string? Origem,
    Guid? UsuarioId) : ICommand;

public sealed record EmitirNotaFiscalPagamentoInput(
    FormaPagamentoFiscal FormaPagamento,
    decimal Valor,
    string? BandeiraCartao = null,
    string? CnpjCredenciadora = null,
    string? Nsu = null);
