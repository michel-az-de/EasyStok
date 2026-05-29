using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos;

namespace EasyStock.Application.UseCases.AlterarAgendamentoPedido;

public sealed record AlterarAgendamentoPedidoCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid PedidoId,
    DateTime? AgendadoParaEm,
    Guid? UsuarioId = null,
    [property: MaxLength(120)] string? UsuarioNome = null,
    [property: MaxLength(20)] string? Origem = "web");

public class AlterarAgendamentoPedidoUseCase(
    IPedidoRepository pedidoRepo,
    IUnitOfWork uow,
    ILogger<AlterarAgendamentoPedidoUseCase> logger)
{
    public async Task<PedidoResult?> ExecuteAsync(AlterarAgendamentoPedidoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.PedidoId, "PedidoId");

        // Normaliza pra UTC: a data vem do cliente com Kind=Unspecified e o Postgres
        // timestamptz rejeita no save (mesma classe de bug já corrigida no CriarPedido).
        var agendado = DataUtc.ParaUtcOpcional(cmd.AgendadoParaEm);
        if (agendado.HasValue && agendado.Value <= DateTime.UtcNow)
            throw new UseCaseValidationException("Data agendada precisa ser no futuro.");

        var pedido = await pedidoRepo.GetByIdAsync(cmd.EmpresaId, cmd.PedidoId);
        if (pedido == null) return null;

        if (pedido.Status == "entregue" || pedido.Status == "cancelado")
            throw new UseCaseValidationException("Não é possível alterar agendamento de pedido entregue ou cancelado.");

        var anterior = pedido.AgendadoParaEm;
        pedido.AgendadoParaEm = agendado;
        pedido.AlteradoEm = DateTime.UtcNow;

        var descricao = cmd.AgendadoParaEm.HasValue
            ? $"Agendado para {cmd.AgendadoParaEm.Value:dd/MM/yyyy HH:mm}"
            : "Agendamento removido (pedido imediato)";

        await pedidoRepo.AddEventoAsync(new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "agendamento_alterado",
            Detalhes = descricao,
            UsuarioId = cmd.UsuarioId,
            UsuarioNome = cmd.UsuarioNome,
            Origem = cmd.Origem,
            OcorridoEm = DateTime.UtcNow
        });

        await pedidoRepo.UpdateAsync(pedido);
        await uow.CommitAsync();

        logger.LogInformation("Pedido {Id} agendamento alterado: {Antes} → {Depois}.",
            pedido.Id, anterior?.ToString("o") ?? "null", cmd.AgendadoParaEm?.ToString("o") ?? "null");

        return CriarPedidoUseCase.Map(pedido);
    }
}
