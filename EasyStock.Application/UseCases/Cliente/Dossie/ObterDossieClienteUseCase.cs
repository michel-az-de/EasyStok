using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.ClienteCrm;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using ClienteEntidade = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.UseCases.Cliente.Dossie;

public sealed record ObterDossieClienteQuery(Guid EmpresaId, Guid ClienteId);

/// <summary>
/// Dossiê do cliente (S25): cadastro, endereços, tags, notas internas, últimos pedidos, item favorito,
/// sinal de mesmo domicílio e conversas recentes numa projeção só. Pela conversa, sem cliente
/// vinculado, devolve o dossiê mínimo com o nome do perfil e o telefone do canal.
/// </summary>
public sealed class ObterDossieClienteUseCase(
    IClienteRepository clientes,
    IClienteCrmRepository crm,
    IHistoricoPedidosClienteQueries pedidos,
    IDomicilioQueries domicilio,
    IConversaRepository conversas)
{
    public const int UltimosPedidos = 10;
    public const int ConversasRecentes = 5;
    public const int MaximoNotas = 20;

    /// <summary>Teto de pedidos lidos para favorito, total e última compra (projeção leve: nome e quantidade).</summary>
    public const int MaximoPedidosAnalisados = 1000;

    public async Task<DossieClienteDto?> ExecuteAsync(ObterDossieClienteQuery query, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(query.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(query.ClienteId, "ClienteId");

        var cliente = await clientes.GetByIdWithDetailsAsync(query.EmpresaId, query.ClienteId);
        if (cliente is null) return null;

        var recentes = await conversas.ListarPorClienteAsync(query.EmpresaId, cliente.Id, ConversasRecentes, ct);
        var pedidoDaConversa = recentes.FirstOrDefault(c => c.EstaAberta && c.PedidoEmAndamentoId is not null)?.PedidoEmAndamentoId;
        return await MontarAsync(query.EmpresaId, cliente, recentes, pedidoDaConversa, ct);
    }

    /// <summary>
    /// Mesma projeção resolvida pela conversa. Null só quando a conversa não é da empresa; conversa de
    /// lead (sem cliente) devolve o dossiê mínimo.
    /// </summary>
    public async Task<DossieClienteDto?> ObterPorConversaAsync(Guid empresaId, Guid conversaId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);

        var conversa = await conversas.ObterPorIdAsync(empresaId, conversaId, ct);
        if (conversa is null) return null;

        var cliente = conversa.ClienteId is { } clienteId
            ? await clientes.GetByIdWithDetailsAsync(empresaId, clienteId)
            : null;
        if (cliente is null) return Minimo(conversa);

        var recentes = await conversas.ListarPorClienteAsync(empresaId, cliente.Id, ConversasRecentes, ct);
        return await MontarAsync(empresaId, cliente, recentes, conversa.PedidoEmAndamentoId, ct);
    }

    private async Task<DossieClienteDto> MontarAsync(
        Guid empresaId, ClienteEntidade cliente, IReadOnlyList<Conversa> recentes, Guid? pedidoEmAndamentoId, CancellationToken ct)
    {
        var comTags = await crm.ObterComTagsAsync(empresaId, cliente.Id, ct);
        var notas = await crm.ListarNotasAsync(empresaId, cliente.Id, MaximoNotas, ct);
        var historico = await pedidos.ListarAsync(empresaId, cliente.Id, MaximoPedidosAnalisados, ct);
        var mesmoDomicilio = await domicilio.ListarMesmoDomicilioAsync(empresaId, cliente.Id, ct);

        var compras = historico.Where(EhCompra).ToList();

        return new DossieClienteDto(
            new DossieClienteDados(cliente.Id, cliente.Nome, cliente.Telefone, cliente.Email, cliente.Observacoes, cliente.MotivoBloqueio),
            cliente.Enderecos
                .OrderByDescending(e => e.Padrao).ThenByDescending(e => e.CriadoEm)
                .Select(e => new ClienteEnderecoResult(
                    e.Id, e.ClienteId, e.Tipo, e.Logradouro, e.Numero, e.Complemento,
                    e.Bairro, e.Cidade, e.Estado, e.Cep, e.Pais, e.Referencia, e.Padrao, e.CriadoEm))
                .ToList(),
            (comTags ?? cliente).Tags.OrderBy(t => t.Tag, StringComparer.Ordinal).Select(ClienteTagResult.De).ToList(),
            notas.Select(ClienteNotaResult.De).ToList(),
            historico.Take(UltimosPedidos).ToList(),
            ItemFavorito(compras),
            compras.Count == 0 ? null : compras.Max(p => p.CriadoEm),
            compras.Count,
            cliente.Bloqueado,
            PreferenciasClienteResult.De(cliente),
            mesmoDomicilio,
            recentes.Select(Resumo).ToList(),
            pedidoEmAndamentoId is { } pedidoId ? historico.FirstOrDefault(p => p.Id == pedidoId) : null);
    }

    /// <summary>Lead sem cadastro: só o que o canal informou.</summary>
    private static DossieClienteDto Minimo(Conversa conversa)
    {
        var telefone = conversa.Canal is CanalConversa.WhatsApp or CanalConversa.Sms ? conversa.ContatoIdExterno : null;
        return new DossieClienteDto(
            new DossieClienteDados(null, conversa.ContatoNome, telefone, null, null, null),
            [], [], [], [], null, null, 0, false, null, [], [Resumo(conversa)], null);
    }

    /// <summary>Cancelado e rascunho não são compra: ficam na lista, fora do favorito e da contagem.</summary>
    private static bool EhCompra(PedidoResumoCliente pedido) =>
        pedido.Status is not (StatusPedidoMapper.Cancelado or StatusPedidoMapper.Rascunho);

    /// <summary>O item presente em mais pedidos; empate vai para a maior quantidade somada.</summary>
    private static ItemFavoritoDossie? ItemFavorito(IEnumerable<PedidoResumoCliente> compras) =>
        compras
            .SelectMany(p => p.Itens.Select(i => (Pedido: p.Id, Item: i)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Item.Nome))
            .GroupBy(x => x.Item.Nome.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new ItemFavoritoDossie(
                g.First().Item.Nome.Trim(), g.Select(x => x.Pedido).Distinct().Count(), g.Sum(x => x.Item.Quantidade)))
            .OrderByDescending(f => f.Pedidos)
            .ThenByDescending(f => f.Quantidade)
            .FirstOrDefault();

    private static ConversaRecenteDossie Resumo(Conversa c) =>
        new(c.Id, c.Canal, c.Situacao, c.IniciadaEm, c.UltimaMensagemEm);
}
