using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

public sealed class LogisticaRepository(EasyStockDbContext db,
    LogisticaViagemUseCase logisticaViagem,
    EasyStock.Application.UseCases.ObterCaixaDia.ObterCaixaDiaUseCase obterCaixa) : ILogisticaRepository
{
    // Uma trava curta por empresa serializa contratação/pagamento/evento e impede dupla reserva.
    public async Task TravarAsync(Guid empresaId, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"logistica:" + empresaId}, 0))", ct);

    public async Task<IReadOnlyList<LogisticaPedido>> PedidosAsync(Guid empresaId, DateOnly? dia, Guid? pedidoId, CancellationToken ct)
    {
        var linhas = await (from pedido in db.Pedidos.AsNoTracking()
            join vaga in db.VagasOcupadas.AsNoTracking() on pedido.Id equals vaga.PedidoId
            join janela in db.JanelasEntrega.AsNoTracking() on vaga.JanelaEntregaId equals janela.Id
            where pedido.EmpresaId == empresaId && vaga.LiberadoEm == null
                && (dia == null || vaga.DataEntrega == dia) && (pedidoId == null || pedido.Id == pedidoId)
            orderby vaga.DataEntrega, janela.HoraInicio, pedido.ClienteNome
            select new
            {
                pedido.Id, pedido.ClienteNome, pedido.EnderecoEntrega, pedido.BairroEntrega, pedido.Status,
                pedido.ClienteTelefone, pedido.EntregaLat, pedido.EntregaLng,
                Bloqueado = pedido.Cliente != null && pedido.Cliente.Bloqueado,
                vaga.DataEntrega, janela.HoraInicio, janela.HoraFim,
                Frete = pedido.Itens.Where(i => i.ProdutoId == null && i.CardapioItemId == null && i.Nome.StartsWith("Entrega ")).Sum(i => i.Quantidade * i.PrecoUnitario)
            }).ToListAsync(ct);
        return linhas.Select(p => new LogisticaPedido(p.Id, CodigoCurto.De(p.Id),
            p.ClienteNome ?? "Cliente", p.EnderecoEntrega ?? "", p.BairroEntrega ?? "",
            DateTime.SpecifyKind(p.DataEntrega.ToDateTime(p.HoraInicio).AddHours(3), DateTimeKind.Utc),
            DateTime.SpecifyKind(p.DataEntrega.ToDateTime(p.HoraFim).AddHours(3), DateTimeKind.Utc),
            p.Status, (int)Math.Round(p.Frete * 100, MidpointRounding.AwayFromZero), p.ClienteTelefone ?? "", p.EntregaLat, p.EntregaLng, p.Bloqueado)).ToList();
    }

    public async Task<IReadOnlyList<DespachoLogistica>> DespachosAsync(Guid empresaId, CancellationToken ct) =>
        await db.Set<DespachoLogistica>().Where(d => d.EmpresaId == empresaId).OrderByDescending(d => d.CriadoEm).ToListAsync(ct);
    public Task<bool> TemViagemAtivaAsync(Guid empresaId, IReadOnlyList<Guid> pedidoIds, CancellationToken ct) =>
        (from parada in db.ParadasViagem join viagem in db.Viagens on parada.ViagemId equals viagem.Id
         where viagem.EmpresaId == empresaId && pedidoIds.Contains(parada.PedidoId)
             && (viagem.Situacao == EasyStock.Domain.Enums.Atendimento.SituacaoViagem.Montando || viagem.Situacao == EasyStock.Domain.Enums.Atendimento.SituacaoViagem.EmRota)
         select parada).AnyAsync(ct);
    public Task<Guid> CriarViagemAsync(Guid empresaId, string provedor, IReadOnlyList<Guid> pedidoIds, CancellationToken ct) =>
        logisticaViagem.CriarAsync(empresaId, provedor, pedidoIds, ct);
    public Task AtualizarViagemAsync(DespachoLogistica despacho, string evento, CancellationToken ct) =>
        logisticaViagem.AplicarAsync(despacho, evento, ct);
    public async Task<decimal> LimiteSaidaAsync(Guid empresaId, DateTime instanteUtc, bool dinheiro, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var dia = await obterCaixa.ExecuteAsync(new(empresaId, EasyStock.Application.Common.HorarioBrasil.DataOperacional(instanteUtc)));
        return dinheiro ? EasyStock.Application.UseCases.Caixa.GavetaCaixa.TetoSaidaEmDinheiro(dia) : dia.SaldoEsperado;
    }
    public Task<DespachoLogistica?> DespachoAsync(Guid empresaId, Guid id, CancellationToken ct) =>
        db.Set<DespachoLogistica>().FirstOrDefaultAsync(d => d.EmpresaId == empresaId && d.Id == id, ct);
    public Task<CotacaoLogistica?> CotacaoAsync(Guid empresaId, Guid id, CancellationToken ct) =>
        db.Set<CotacaoLogistica>().FirstOrDefaultAsync(d => d.EmpresaId == empresaId && d.Id == id, ct);
    public async Task<PoliticaLogistica?> PoliticaAsync(Guid empresaId, CancellationToken ct)
    {
        var politica = await db.Set<PoliticaLogistica>().SingleOrDefaultAsync(d => d.EmpresaId == empresaId, ct);
        if (politica is not null) await db.Entry(politica).ReloadAsync(ct);
        return politica;
    }
    public void Adicionar(CotacaoLogistica cotacao) => db.Add(cotacao);
    public void Adicionar(DespachoLogistica despacho) => db.Add(despacho);
    public void Adicionar(PoliticaLogistica politica) => db.Add(politica);

    public void RegistrarAvisoDemonstracao(Guid empresaId, Guid despachoId, string tipo, string payload)
    {
        var evento = OutboxEventoIntegracao.Criar(empresaId, "logistica.demonstracao." + tipo,
            "despacho_logistica", despachoId, payload);
        // A demonstração preserva intenção/auditoria no outbox, sem dispatcher nem mensagem real.
        evento.Cancelar();
        evento.ErroUltimaTentativa = "Demonstração: envio externo bloqueado por projeto.";
        db.OutboxEventosIntegracao.Add(evento);
    }
}
