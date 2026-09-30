using EasyStock.Application.UseCases.Operacao.Kds;

namespace EasyStock.Application.UseCases.Atendimento.Esteira;

/// <param name="Passo">Status de destino no vocabulário do mapper (ex.: "preparando", "pronto").</param>
/// <param name="OcorreuEm">Horário anotado no papel; vira o horário real da transição.</param>
public sealed record LinhaLotePapel(Guid PedidoId, string Passo, DateTime OcorreuEm);

public sealed record LancarLotePapelCommand(
    Guid EmpresaId,
    IReadOnlyList<LinhaLotePapel> Linhas,
    Guid? UsuarioId = null) : ICommand;

/// <param name="Linha">Posição da linha no lote enviado (base 1), para a tela apontar o erro.</param>
public sealed record ResultadoLinhaLotePapel(int Linha, Guid PedidoId, string Passo, DateTime OcorreuEm,
    bool Sucesso, string? Status, string? Motivo);

public sealed record ResultadoLotePapel(int Aplicados, int Rejeitados, IReadOnlyList<ResultadoLinhaLotePapel> Linhas);

/// <summary>
/// Lote de papel (S46, US-042): a conexão caiu, a dona anotou os passos no papel e lança tudo depois.
/// Aplica na ordem de <see cref="LinhaLotePapel.OcorreuEm"/>, pela máquina de estados do pedido,
/// gravando o horário real; linha inválida é reportada sem abortar o resto. Toda transição sai com a
/// origem <see cref="OrigemLotePapel"/>, que os avisos ao cliente usam para não mandar nada retroativo.
/// </summary>
public class LancarLotePapelUseCase(AtualizarStatusPedidosEmLoteUseCase lote)
{
    public const string OrigemLotePapel = "lote_papel";

    public async Task<ResultadoLotePapel> ExecuteAsync(LancarLotePapelCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        if (cmd.Linhas is null || cmd.Linhas.Count == 0)
            throw new UseCaseValidationException("Lote vazio.");

        // OrderBy é estável: dois passos com o mesmo horário mantêm a ordem em que foram anotados.
        var ordenadas = cmd.Linhas
            .Select((l, i) => (Linha: i + 1, Item: l))
            .OrderBy(x => x.Item.OcorreuEm)
            .ToList();

        var resultados = await lote.ExecuteAsync(new AtualizarStatusPedidosEmLoteCommand(
            cmd.EmpresaId,
            ordenadas.Select(x => new KdsStatusLoteItem(x.Item.PedidoId, x.Item.Passo, x.Item.OcorreuEm)).ToList(),
            cmd.UsuarioId,
            OrigemLotePapel), ct);

        var linhas = ordenadas.Zip(resultados, (o, r) => new ResultadoLinhaLotePapel(
                o.Linha, o.Item.PedidoId, o.Item.Passo, o.Item.OcorreuEm, r.Sucesso, r.Status, r.Erro))
            .OrderBy(l => l.Linha)
            .ToList();

        return new ResultadoLotePapel(linhas.Count(l => l.Sucesso), linhas.Count(l => !l.Sucesso), linhas);
    }
}
