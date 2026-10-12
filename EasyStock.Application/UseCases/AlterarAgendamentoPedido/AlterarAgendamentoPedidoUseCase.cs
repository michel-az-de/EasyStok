using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.Pedidos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;

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
    ILogger<AlterarAgendamentoPedidoUseCase> logger,
    CalculadoraInicioPrevistoPedido inicioPrevisto,
    IVagaOcupadaRepository vagas)
{
    public Task<PedidoResult?> ExecuteAsync(AlterarAgendamentoPedidoCommand cmd) =>
        uow.ExecuteInTransactionSemRetryAsync(async ct =>
        {
            await pedidoRepo.TravarAsync(cmd.EmpresaId, cmd.PedidoId, ct);
            return await AlterarAsync(cmd);
        });

    private async Task<PedidoResult?> AlterarAsync(AlterarAgendamentoPedidoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.PedidoId, "PedidoId");

        // Normaliza pra UTC: a data vem do cliente com Kind=Unspecified e o Postgres
        // timestamptz rejeita no save (mesma classe de bug já corrigida no CriarPedido).
        var agendado = DataUtc.ParaUtcOpcional(cmd.AgendadoParaEm);
        if (agendado.HasValue && agendado.Value <= DateTime.UtcNow)
            throw new UseCaseValidationException("Data agendada precisa ser no futuro.");

        // issue 958: era GetByIdAsync (sem Include de Itens/Pagamentos) — o unico dos 14
        // call-sites de CriarPedidoUseCase.Map() sem details, entao o PATCH de agendamento
        // respondia totalPago=0 e itensCount=0 pro caller.
        var pedido = await pedidoRepo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.PedidoId);
        if (pedido == null || pedido.EmpresaId != cmd.EmpresaId) return null;

        var atual = (await vagas.GetByPedidoIdsAsync([pedido.Id])).GetValueOrDefault(pedido.Id);
        if (atual.Vaga is { LiberadoEm: null })
            throw new UseCaseValidationException("Este pedido tem uma vaga de entrega. Use a troca de janela para reagendar.");

        if (pedido.Status == "entregue" || pedido.Status == "cancelado")
            throw new UseCaseValidationException("Não é possível alterar agendamento de pedido entregue ou cancelado.");

        var anterior = pedido.AgendadoParaEm;
        pedido.AgendadoParaEm = agendado;
        pedido.AlteradoEm = DateTime.UtcNow;
        // S21: janela nova, início previsto novo (e o aviso de atraso volta a valer).
        pedido.DefinirInicioPrevisto(await inicioPrevisto.CalcularAsync(pedido));

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
