using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Enums.Storefront;

namespace EasyStock.Application.UseCases.Operacao.Impressao;

public sealed record MontarCanhotoInput(Guid EmpresaId, Guid PedidoId);

/// <summary>
/// Monta o <see cref="CanhotoDto"/> do pedido (S20). Grupos na ordem das linhas (<c>paraServir</c>,
/// <c>prepararEmCasa</c>, <c>outros</c>), itens na ordem em que entraram no pedido. O molho vem da
/// <c>SugestaoMolho</c> do item do cardápio (o pedido guarda só o id do item). Item sem linha e sem catálogo
/// (frete, taxa) não é produção e fica fora. Endereço vem do cadastro do cliente: o pedido não guarda
/// endereço estruturado. Devolve <c>null</c> para pedido inexistente ou de outra empresa.
/// </summary>
public sealed class MontarCanhotoUseCase(
    IPedidoRepository pedidos,
    IClienteRepository clientes,
    IStorefrontRepository storefronts,
    ICardapioItemRepository cardapio)
{
    public const string Rodape = "imprima este canhoto: ele basta para produzir";
    public const string LinhaOutros = "outros";

    private static readonly string[] OrdemLinhas =
    [
        LinhaProduto.ParaServir.ParaContrato(),
        LinhaProduto.PrepararEmCasa.ParaContrato(),
        LinhaOutros,
    ];

    public async Task<CanhotoDto?> ExecuteAsync(MontarCanhotoInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(input.PedidoId, "PedidoId");

        var pedido = await pedidos.GetByIdWithDetailsAsync(input.EmpresaId, input.PedidoId);
        if (pedido is null || pedido.EmpresaId != input.EmpresaId) return null;

        var storefront = await storefronts.GetByEmpresaAsync(input.EmpresaId, ct);
        var molhos = storefront is null
            ? new Dictionary<Guid, string>()
            : (await cardapio.GetTodosDoStorefrontAsync(storefront.Id, ct))
                .Where(c => !string.IsNullOrWhiteSpace(c.SugestaoMolho))
                .ToDictionary(c => c.Id, c => c.SugestaoMolho!);

        var cliente = pedido.ClienteId is { } clienteId
            ? await clientes.GetByIdAsync(input.EmpresaId, clienteId)
            : null;

        var grupos = pedido.Itens
            .Where(EhProducao)
            .OrderBy(i => i.CriadoEm)
            .GroupBy(i => string.IsNullOrWhiteSpace(i.LinhaSnapshot) ? LinhaOutros : i.LinhaSnapshot!)
            .OrderBy(g => OrdemDaLinha(g.Key))
            .Select(g => new CanhotoGrupoDto(g.Key, TituloDaLinha(g.Key), g
                .Select(i => new CanhotoItemDto(
                    i.Nome,
                    Limpo(i.VariacaoRotuloSnapshot),
                    i.Quantidade,
                    i.CardapioItemId is { } id && molhos.TryGetValue(id, out var molho) ? molho.Trim() : null,
                    Limpo(i.Observacao)))
                .ToList()))
            .ToList();

        var pagoEm = pedido.Pagamentos.Count == 0 ? (DateTime?)null : pedido.Pagamentos.Max(p => p.PagoEm);
        var cabecalho = new CanhotoCabecalhoDto(
            storefront?.TituloPublico ?? string.Empty,
            pedido.Id.ToString("N")[..8].ToUpperInvariant(),
            Limpo(pedido.ClienteNome) ?? Limpo(cliente?.Nome),
            Limpo(pedido.ClienteTelefone) ?? Limpo(cliente?.Telefone),
            MontarEndereco(cliente, pedido.ClienteApt),
            pedido.AgendadoParaEm is { } agendado ? HorarioBrasil.ConverterParaBrasilia(agendado) : null,
            pagoEm is { } pago ? HorarioBrasil.ConverterParaBrasilia(pago) : null);

        return new CanhotoDto(cabecalho, grupos, Limpo(pedido.Observacoes), Rodape);
    }

    private static bool EhProducao(PedidoItem i) =>
        !string.IsNullOrWhiteSpace(i.LinhaSnapshot) || i.CardapioItemId is not null || i.ProdutoId is not null;

    private static int OrdemDaLinha(string linha)
    {
        var i = Array.IndexOf(OrdemLinhas, linha);
        return i < 0 ? OrdemLinhas.Length : i;
    }

    private static string TituloDaLinha(string linha) => linha switch
    {
        "paraServir" => "Para servir",
        "prepararEmCasa" => "Preparar em casa",
        _ => "Outros",
    };

    private static string? MontarEndereco(EasyStock.Domain.Entities.Cliente? cliente, string? apt) =>
        EnderecoImpresso.Montar(cliente?.Endereco, cliente?.Complemento, apt, cliente?.Bairro, cliente?.Cidade, cliente?.Cep);

    private static string? Limpo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
