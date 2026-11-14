using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Atendimento;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>criar_pedido</c> (S06 ligada na S11): fecha o pedido da conversa pelo núcleo do checkout (S10,
/// <see cref="CriarPedidoAtendimentoUseCase"/>) sem cobrar (#2544, S73): devolve <c>pedidoId</c> e o resumo. O cliente
/// confere o resumo em imagem (<c>enviar_resumo_pedido</c>) e só com o ok o link sai (<c>enviar_link_pagamento</c>).
///
/// <para>
/// Janela e endereço são opcionais: sem janela, vale a única ativa do dia; sem endereço, o padrão (ou o
/// único) do cliente. Havendo mais de uma opção, devolve a lista para o agente perguntar
/// (<c>listar_janelas</c>, S16, oferece só janelas no prazo; <c>validar_endereco</c> chega na S14).
/// </para>
/// </summary>
public sealed class CriarPedidoFerramenta(
    CriarPedidoAtendimentoUseCase criarPedido,
    IClienteRepository clienteRepository,
    IStorefrontRepository storefrontRepository,
    IJanelaEntregaRepository janelaRepository) : IFerramentaAgente
{
    public string Nome => "criar_pedido";

    public string Descricao =>
        "Cria o pedido do cliente desta conversa, ainda sem link de pagamento. Use os ids de consultar_cardapio. " +
        "Devolve pedidoId, itens, frete, total e entrega; se faltar janela ou endereço, devolve as opções para você " +
        "perguntar ao cliente. Depois use enviar_resumo_pedido com o pedidoId.";

    public string SchemaJson => """
        {"type":"object","properties":{
          "itens":{"type":"array","minItems":1,"items":{"type":"object","properties":{
            "cardapio_item_id":{"type":"string"},
            "quantidade":{"type":"integer","minimum":1},
            "observacao":{"type":"string"}},
            "required":["cardapio_item_id","quantidade"],"additionalProperties":false}},
          "data_entrega":{"type":"string","description":"AAAA-MM-DD"},
          "janela_id":{"type":"string"},
          "endereco_id":{"type":"string"},
          "observacoes":{"type":"string"}},
         "required":["itens","data_entrega"],"additionalProperties":false}
        """;

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        var conversa = contexto.Conversa;
        if (contexto.ClienteIdEsperado is not { } clienteId)
            return Erro("cliente_nao_identificado");

        var itens = LerItens(entrada);
        if (itens is null)
            return Erro("itens_invalidos");

        if (!DateOnly.TryParseExact(FerramentaJson.LerTexto(entrada, "data_entrega"), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dataEntrega))
            return Erro("data_entrega_invalida");

        var cliente = await clienteRepository.GetByIdWithDetailsAsync(contexto.EmpresaId, clienteId);
        if (cliente is null)
            return Erro("cliente_nao_identificado");
        if (cliente.Bloqueado)
            return Erro(ClienteBloqueadoException.CodigoErro); // S24: o motivo é interno

        var enderecoId = Guid.TryParse(FerramentaJson.LerTexto(entrada, "endereco_id"), out var informado)
            ? informado
            : CriarPedidoAtendimentoUseCase.EnderecoPadrao(cliente);
        if (enderecoId is null)
        {
            return FerramentaJson.Serializar(new
            {
                erro = "informe_endereco",
                enderecos = cliente.Enderecos.Select(e => new { id = e.Id, endereco = $"{e.Logradouro}, {e.Numero} {e.Bairro}".Trim() })
            });
        }

        var janelaId = Guid.TryParse(FerramentaJson.LerTexto(entrada, "janela_id"), out var janelaInformada)
            ? janelaInformada
            : (Guid?)null;
        if (janelaId is null)
        {
            var janelas = await JanelasDoDiaAsync(contexto.EmpresaId, dataEntrega, ct);
            if (janelas.Count != 1)
                return FerramentaJson.Serializar(new { erro = "informe_janela", janelas = janelas.Select(j => new { id = j.Id, janela = j.Label }) });
            janelaId = janelas[0].Id;
        }

        PedidoReservado reservado;
        try
        {
            reservado = await criarPedido.ExecuteAsync(new CriarPedidoAtendimentoInput(
                contexto.EmpresaId, conversa.Id, clienteId, itens, janelaId.Value, dataEntrega, enderecoId.Value,
                FerramentaJson.LerTexto(entrada, "observacoes"),
                VinculoRevisaoEsperada: contexto.VinculoRevisaoEsperada,
                SituacaoEsperada: contexto.SituacaoEsperada), ct);
        }
        catch (ClienteBloqueadoException)
        {
            return Erro(ClienteBloqueadoException.CodigoErro);
        }
        catch (ContextoPedidoAtendimentoAlteradoException)
        {
            contexto.Interromper();
            return Erro("contexto_alterado");
        }
        catch (RegraDeDominioVioladaException ex)
        {
            return FerramentaJson.Serializar(new { erro = "pedido_recusado", motivo = ex.Message });
        }

        return FerramentaJson.Serializar(new
        {
            pedidoId = reservado.Pedido.Id,
            itens = reservado.Itens.Select(i => new
            {
                nome = i.Nome,
                quantidade = i.Quantidade,
                subtotal = FerramentaJson.FormatarReais(i.Subtotal),
                observacao = i.Observacao,
            }),
            frete = FerramentaJson.FormatarReais(reservado.ItemFrete.PrecoUnitario),
            total = FerramentaJson.FormatarReais(reservado.Total),
            entrega = dataEntrega.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            orientacao = "Pedido criado, ainda sem link. Use enviar_resumo_pedido com este pedidoId e peça o ok do cliente; " +
                "com o ok, use enviar_link_pagamento.",
        });
    }

    private static string Erro(string codigo) => FerramentaJson.Serializar(new { erro = codigo });

    private static List<ItemPedidoCheckout>? LerItens(JsonElement entrada)
    {
        if (entrada.ValueKind != JsonValueKind.Object
            || !entrada.TryGetProperty("itens", out var lista)
            || lista.ValueKind != JsonValueKind.Array)
            return null;

        var itens = new List<ItemPedidoCheckout>();
        foreach (var item in lista.EnumerateArray())
        {
            if (!Guid.TryParse(FerramentaJson.LerTexto(item, "cardapio_item_id"), out var cardapioItemId)
                || !item.TryGetProperty("quantidade", out var qtd)
                || qtd.ValueKind != JsonValueKind.Number
                || !qtd.TryGetInt32(out var quantidade)
                || quantidade < 1)
                return null;
            itens.Add(new ItemPedidoCheckout(cardapioItemId, quantidade, FerramentaJson.LerTexto(item, "observacao")));
        }
        return itens.Count == 0 ? null : itens;
    }

    private async Task<IReadOnlyList<Domain.Entities.Storefront.JanelaEntrega>> JanelasDoDiaAsync(
        Guid empresaId, DateOnly data, CancellationToken ct)
    {
        var storefront = await storefrontRepository.GetByEmpresaAsync(empresaId, ct);
        if (storefront is null) return [];
        var janelas = await janelaRepository.GetAtivasDoStorefrontAsync(storefront.Id, ct);
        return janelas.Where(j => j.DiaDaSemana == (int)data.DayOfWeek).OrderBy(j => j.HoraInicio).ToList();
    }
}
