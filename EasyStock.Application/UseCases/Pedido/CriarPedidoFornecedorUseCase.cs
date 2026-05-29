namespace EasyStock.Application.UseCases.Pedido;

public sealed record CriarPedidoFornecedorCommand(
    Guid EmpresaId,
    Guid FornecedorId,
    DateTime DataPedido,
    DateTime? PrevisaoEntrega,
    decimal? ValorEstimado,
    string? Canal,
    string? Observacoes);

public sealed record CriarPedidoFornecedorResult(Guid PedidoId);

public class CriarPedidoFornecedorUseCase(
    IPedidoFornecedorRepository pedidoRepository,
    IFornecedorRepository fornecedorRepository,
    IUnitOfWork unitOfWork,
    ILogger<CriarPedidoFornecedorUseCase> logger)
{
    public async Task<CriarPedidoFornecedorResult> ExecuteAsync(CriarPedidoFornecedorCommand command)
    {
        ValidateCommand(command);

        // Tenant isolation: fornecedor precisa pertencer à empresa do request.
        var fornecedor = await fornecedorRepository.GetByIdAsync(command.EmpresaId, command.FornecedorId)
            ?? throw new UseCaseValidationException("Fornecedor não pertence a esta empresa.");

        var pedido = new PedidoFornecedor
        {
            Id = Guid.NewGuid(),
            EmpresaId = command.EmpresaId,
            FornecedorId = command.FornecedorId,
            DataPedido = command.DataPedido,
            PrevisaoEntrega = command.PrevisaoEntrega,
            ValorEstimado = command.ValorEstimado,
            Status = StatusPedidoFornecedor.Aberto,
            Canal = command.Canal,
            Observacoes = command.Observacoes,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };

        await pedidoRepository.AddAsync(pedido);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Pedido {PedidoId} criado para fornecedor {FornecedorId}.", pedido.Id, pedido.FornecedorId);
        return new CriarPedidoFornecedorResult(pedido.Id);
    }

    private static void ValidateCommand(CriarPedidoFornecedorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(command.FornecedorId, "FornecedorId");

        if (command.DataPedido == default)
        {
            throw new ArgumentException("DataPedido deve ser uma data válida.", nameof(command.DataPedido));
        }

        if (command.PrevisaoEntrega.HasValue && command.PrevisaoEntrega.Value < command.DataPedido)
        {
            throw new ArgumentException("PrevisaoEntrega não pode ser anterior à DataPedido.", nameof(command.PrevisaoEntrega));
        }

        if (command.ValorEstimado.HasValue && command.ValorEstimado.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command.ValorEstimado), command.ValorEstimado, "ValorEstimado não pode ser negativo.");
        }
    }
}
