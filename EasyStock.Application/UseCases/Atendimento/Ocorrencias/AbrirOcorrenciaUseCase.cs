using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Ocorrencias;

public sealed record AbrirOcorrenciaInput(
    Guid EmpresaId,
    Guid PedidoId,
    OrigemOcorrencia Origem,
    CategoriaOcorrencia Categoria,
    string Relato,
    Guid? ConversaId);

/// <summary>
/// Abre a ocorrência do pedido (S27, US-049): avaliação negativa (S28), ferramenta
/// <c>abrir_ocorrencia</c> do agente (S06) ou a dona no console. Com conversa, escala para a dona
/// (<see cref="IEscaladorConversa"/>, S07) na mesma unidade de trabalho; depois do commit publica
/// <c>ocorrencia.aberta</c> (S18). Pedido sem cliente não abre: a ocorrência vai para o cadastro.
/// </summary>
public sealed class AbrirOcorrenciaUseCase(
    IOcorrenciaRepository repo,
    IPedidoRepository pedidos,
    IConversaRepository conversas,
    IEscaladorConversa escalador,
    IOperacaoEventPublisher operacaoEventos,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    public async Task<OcorrenciaDto> ExecuteAsync(AbrirOcorrenciaInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(input.PedidoId, "PedidoId");

        var pedido = await pedidos.GetByIdAsync(input.EmpresaId, input.PedidoId);
        if (pedido is null || pedido.EmpresaId != input.EmpresaId)
            throw new UseCaseValidationException("Pedido não encontrado.");
        if (pedido.ClienteId is not { } clienteId || clienteId == Guid.Empty)
            throw new UseCaseValidationException("Pedido sem cliente não abre ocorrência.");

        Conversa? conversa = null;
        if (input.ConversaId is { } conversaId)
        {
            conversa = await conversas.ObterPorIdAsync(input.EmpresaId, conversaId, ct)
                ?? throw new UseCaseValidationException("Conversa não encontrada.");
        }

        var agora = relogio.GetUtcNow().UtcDateTime;
        Ocorrencia ocorrencia;
        try
        {
            ocorrencia = Ocorrencia.Abrir(input.EmpresaId, pedido.Id, clienteId, conversa?.Id,
                input.Origem, input.Categoria, input.Relato, agora);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        await repo.AddAsync(ocorrencia, ct);
        if (conversa is not null)
            await escalador.EscalarAsync(input.EmpresaId, conversa, MotivoEscalada(ocorrencia), agora, ct);
        await unitOfWork.CommitAsync();

        var dto = OcorrenciaDto.De(ocorrencia);
        await operacaoEventos.PublicarAsync(EventosOperacao.OcorrenciaAberta, input.EmpresaId,
            new OcorrenciaAbertaOperacao(ocorrencia.Id, pedido.Id, ocorrencia.ConversaId, dto.Origem, dto.Categoria), ct);
        return dto;
    }

    private static string MotivoEscalada(Ocorrencia o) =>
        $"ocorrência aberta ({OcorrenciaDto.Snake(o.Categoria.ToString())}): {o.Relato}";
}
