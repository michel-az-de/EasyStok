using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.Storefront.Agendamento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.AlterarAgendamentoPedido;

public sealed record TrocarJanelaPedidoCommand(Guid EmpresaId, Guid PedidoId, Guid JanelaId, DateOnly Data,
    bool AvisarCliente = false, Guid? UsuarioId = null, string? UsuarioNome = null);

public sealed record JanelaPedidoResult(Guid JanelaId, DateOnly Data, string Label, TimeOnly HoraInicio, TimeOnly HoraFim)
{
    public static JanelaPedidoResult De(VagaOcupada vaga, JanelaEntrega janela) =>
        new(janela.Id, vaga.DataEntrega, janela.Label, janela.HoraInicio, janela.HoraFim);
}

public sealed record JanelasPedidoResult(bool LojaDisponivel, int PrazoMinimoMinutos,
    IReadOnlyList<JanelaDisponivelDto> Janelas, JanelaPedidoResult? Atual);

public sealed record TrocarJanelaPedidoResult(bool Alterado, JanelaPedidoResult Janela, bool AvisoEnfileirado);

public sealed class TrocarJanelaPedidoUseCase(
    IPedidoRepository pedidos,
    IVagaOcupadaRepository vagas,
    IStorefrontRepository storefronts,
    IPrazoPreparoPedidoQueries prazos,
    ListarJanelasDisponiveisUseCase listarJanelas,
    CalculadoraInicioPrevistoPedido inicioPrevisto,
    AvisoStatusPedidoCliente aviso,
    IOperacaoEventPublisher eventos,
    IUnitOfWork uow)
{
    public async Task<JanelasPedidoResult?> ListarAsync(Guid empresaId, Guid pedidoId,
        DateOnly? dataInicio = null, DateOnly? dataFim = null, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var pedido = await pedidos.GetByIdWithDetailsAsync(empresaId, pedidoId);
        if (pedido is null || pedido.EmpresaId != empresaId) return null;
        var atual = (await vagas.GetByPedidoIdsAsync([pedidoId], ct)).GetValueOrDefault(pedidoId);
        var resultado = await DisponiveisAsync(pedido, dataInicio, dataFim, ct);
        return resultado with
        {
            Janelas = resultado.Janelas.Where(j => !j.Esgotado).ToList(),
            Atual = atual.Vaga is { LiberadoEm: null } ? JanelaPedidoResult.De(atual.Vaga, atual.Janela) : null
        };
    }

    public async Task<TrocarJanelaPedidoResult?> ExecuteAsync(TrocarJanelaPedidoCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.PedidoId, "PedidoId");
        UseCaseGuards.EnsureNotEmpty(cmd.JanelaId, "JanelaId");
        if (cmd.Data == default) throw new UseCaseValidationException("Informe a data de entrega.");

        var resultado = await uow.ExecuteInTransactionSemRetryAsync<TrocarJanelaPedidoResult?>(async token =>
        {
            await pedidos.TravarAsync(cmd.EmpresaId, cmd.PedidoId, token);
            var pedido = await pedidos.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.PedidoId);
            if (pedido is null || pedido.EmpresaId != cmd.EmpresaId) return null;
            if (pedido.Status is "entregue" or "cancelado")
                throw new UseCaseValidationException("Não é possível reagendar pedido entregue ou cancelado.");

            var anterior = (await vagas.GetByPedidoIdsAsync([pedido.Id], token)).GetValueOrDefault(pedido.Id);
            // Reenvio da mesma troca não libera vaga nem duplica auditoria ou aviso.
            if (anterior.Vaga is { LiberadoEm: null } v && v.JanelaEntregaId == cmd.JanelaId && v.DataEntrega == cmd.Data)
                return new(false, JanelaPedidoResult.De(v, anterior.Janela), false);

            var disponiveis = await DisponiveisAsync(pedido, cmd.Data, cmd.Data, token);
            var janela = disponiveis.Janelas.FirstOrDefault(j => j.JanelaId == cmd.JanelaId);
            if (janela is null)
                throw new UseCaseValidationException("Janela indisponível para este pedido. Confira a data, os bloqueios e o prazo de preparo.");
            if (janela.Esgotado) throw new JanelaSemVagasException("Esta janela ficou sem vagas. Escolha outra janela.");

            await vagas.LiberarPorPedidoAsync(pedido.Id, "reagendado", token);
            // A unique de vaga ativa exige gravar a liberação antes do INSERT. Ambos continuam
            // na mesma transação: lotação concorrente ou qualquer falha restaura a vaga anterior.
            await uow.CommitAsync();
            await vagas.OcuparAsync(janela.JanelaId, janela.Data, pedido.Id, token);
            pedido.AgendadoParaEm = HorarioBrasil.InstanteUtc(janela.Data, janela.HoraInicio);
            pedido.AlteradoEm = DateTime.UtcNow;
            pedido.DefinirInicioPrevisto(await inicioPrevisto.CalcularAsync(pedido, token));

            var eventoId = Guid.NewGuid();
            var faixa = $"{janela.Data:dd/MM/yyyy}, {janela.HoraInicio:HH:mm} às {janela.HoraFim:HH:mm}";
            var enfileirado = cmd.AvisarCliente && await aviso.EnfileirarAsync(
                TipoEventoNotificacao.PedidoReagendado, cmd.EmpresaId, pedido.Id, $"reagendado:{eventoId:N}", token,
                respeitaPreferenciaAvisos: true, previsao: faixa);
            await pedidos.AddEventoAsync(new PedidoEvento
            {
                Id = eventoId, PedidoId = pedido.Id, Tipo = "agendamento_alterado",
                Detalhes = $"Janela alterada para {faixa}. Aviso: {(enfileirado ? "enfileirado" : cmd.AvisarCliente ? "não enfileirado" : "não solicitado")}.",
                UsuarioId = cmd.UsuarioId, UsuarioNome = cmd.UsuarioNome, Origem = "console", OcorridoEm = DateTime.UtcNow
            });
            await pedidos.UpdateAsync(pedido);
            await uow.CommitAsync();
            return new(true, new(janela.JanelaId, janela.Data, janela.Label, janela.HoraInicio, janela.HoraFim), enfileirado);
        }, ct);

        if (resultado is { Alterado: true })
            await eventos.PublicarAsync(EventosOperacao.PedidoReagendado, cmd.EmpresaId,
                new { cmd.PedidoId, resultado.Janela }, ct);
        return resultado;
    }

    private async Task<JanelasPedidoResult> DisponiveisAsync(Domain.Entities.Pedido pedido, DateOnly? inicio, DateOnly? fim, CancellationToken ct)
    {
        var storefront = await storefronts.GetByEmpresaAsync(pedido.EmpresaId, ct);
        if (storefront is null || storefront.EmpresaId != pedido.EmpresaId || !storefront.Ativo)
            return new(false, 0, [], null);
        var leitura = await prazos.ObterAsync(pedido.EmpresaId, pedido.Id, ct)
            ?? throw new UseCaseValidationException("Não foi possível consultar o prazo do pedido.");
        var prazo = CalculadoraPrazoPedido.PrazoMinimo(leitura.TemposPreparoMinutos,
            leitura.TempoPreparoPadraoMinutos, leitura.RespiroMinutos);
        var janelas = await listarJanelas.ExecuteAsync(new(storefront.Slug, inicio, fim, null, prazo), ct);
        return new(true, prazo, janelas, null);
    }
}
