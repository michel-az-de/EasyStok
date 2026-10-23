using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Entregas;

/// <param name="NumeroCurto">8 primeiros caracteres do id em maiúsculas: a referência que a equipe usa para achar o pedido.</param>
/// <param name="NumeroComanda">#1883: "Comanda N", o número da conversa dona do pedido. Nulo quando nenhuma conversa é dona dele.</param>
/// <param name="NumeroDoDia">#1641-1: número do dia (S53); nulo sem número.</param>
public sealed record AgendaPedido(Guid PedidoId, string NumeroCurto, string? ClienteNome, string Status, long? NumeroComanda = null,
    int? NumeroDoDia = null);
public sealed record AgendaJanela(Guid JanelaId, string Label, TimeOnly Inicio, TimeOnly Fim,
    int Capacidade, int Ocupadas, bool Ativa, string? Bloqueio, IReadOnlyList<AgendaPedido> Pedidos);
public sealed record AgendaDia(DateOnly Data, string? Bloqueio, IReadOnlyList<AgendaJanela> Janelas);
public sealed record AgendaEntregasResult(bool LojaDisponivel, IReadOnlyList<AgendaDia> Dias);

public sealed class ListarAgendaEntregasUseCase(
    IStorefrontRepository storefrontRepository,
    IJanelaEntregaRepository janelaRepository,
    IBloqueioEntregaRepository bloqueioRepository,
    IVagaOcupadaRepository vagaRepository,
    IAgendaPedidosQuery pedidoQuery)
{
    public async Task<AgendaEntregasResult> ExecuteAsync(Guid empresaId, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        if (de == default || ate == default || ate < de || ate.DayNumber - de.DayNumber > 60)
            throw new UseCaseValidationException("Informe um período válido de até 60 dias.");

        var loja = await storefrontRepository.GetByEmpresaAsync(empresaId, ct);
        if (loja is null || !loja.Ativo) return new AgendaEntregasResult(false, []);

        var janelas = await janelaRepository.GetTodasDoStorefrontAsync(loja.Id, ct);
        var bloqueios = await bloqueioRepository.GetByStorefrontPeriodoAsync(loja.Id, de, ate, ct);
        var ocupadas = await vagaRepository.ContarPorJanelaPeriodoAsync(janelas.Select(j => j.Id).ToList(), de, ate, ct);
        var pedidos = await pedidoQuery.ListarAsync(empresaId, loja.Id, de, ate, ct);
        var porJanela = pedidos.ToLookup(p => (p.Data, p.JanelaId));
        var dias = new List<AgendaDia>();

        for (var data = de; ; data = data.AddDays(1))
        {
            var doDia = bloqueios.Where(b => b.Data == data).ToList();
            var fechado = doDia.FirstOrDefault(b => b.JanelaEspecificaId is null)?.Motivo;
            var faixas = janelas
                .Where(j => j.DiaDaSemana == (int)data.DayOfWeek)
                .Select(j => new { Janela = j, Ocupadas = ocupadas.GetValueOrDefault((j.Id, data)) })
                .Where(x => x.Janela.Ativa || x.Ocupadas > 0)
                .OrderBy(x => x.Janela.HoraInicio)
                .Select(x => new AgendaJanela(
                    x.Janela.Id, x.Janela.Label, x.Janela.HoraInicio, x.Janela.HoraFim,
                    x.Janela.CapacidadeMaxima, x.Ocupadas, x.Janela.Ativa,
                    doDia.FirstOrDefault(b => b.JanelaEspecificaId == x.Janela.Id)?.Motivo ?? fechado,
                    porJanela[(data, x.Janela.Id)].Select(p => new AgendaPedido(
                        p.PedidoId, p.PedidoId.ToString("N")[..8].ToUpperInvariant(), p.ClienteNome, p.Status, p.NumeroComanda,
                        p.NumeroDoDia)).ToList()))
                .ToList();
            dias.Add(new AgendaDia(data, fechado, faixas));
            if (data == ate) break;
        }

        return new AgendaEntregasResult(true, dias);
    }
}
