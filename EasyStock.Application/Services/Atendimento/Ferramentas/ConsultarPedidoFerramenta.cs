using System.Text.Json;
using EasyStock.Application.UseCases.ObterPedidoDetalhes;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>consultar_pedido(pedido_id?)</c>: status e previsão do pedido. Sem id, usa o pedido em andamento
/// da conversa ou o último do cliente. Só devolve pedido do cliente da conversa: o id vem do LLM e
/// não pode abrir pedido de outra pessoa.
/// </summary>
public sealed class ConsultarPedidoFerramenta(
    IPedidoRepository pedidoRepository,
    ObterPedidoDetalhesUseCase obterPedidoDetalhes,
    IConfiguracaoAtendimentoRepository configuracaoRepository, SaldoFinanceiroPedido saldos,
    ICreditoClienteComandaRepository creditos, ComandaClienteUseCase? comanda = null,
    ILinkCardapioConversaRepository? links = null) : IFerramentaAgente
{
    public string Nome => "consultar_pedido";

    public string Descricao =>
        "Consulta status, itens e previsão de um pedido do cliente desta conversa. Sem pedido_id, usa o pedido " +
        "em andamento ou o último pedido do cliente. Horários em horário de Brasília.";

    public string SchemaJson =>
        """{"type":"object","properties":{"pedido_id":{"type":"string","description":"Id do pedido, se o cliente informou"}},"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        var conversa = contexto.Conversa;
        if (conversa.ClienteId is not { } clienteId)
            return FerramentaJson.Serializar(new { erro = "cliente_sem_pedidos" });

        var pedidoId = Guid.TryParse(FerramentaJson.LerTexto(entrada, "pedido_id"), out var informado)
            ? informado
            : conversa.PedidoEmAndamentoId;
        if (pedidoId is null)
        {
            var ultimos = await pedidoRepository.ListByClienteAsync(contexto.EmpresaId, clienteId, max: 1);
            pedidoId = ultimos.FirstOrDefault()?.Id;
        }

        if (pedidoId is null)
            return FerramentaJson.Serializar(new { erro = "cliente_sem_pedidos" });

        var detalhe = await obterPedidoDetalhes.ExecuteAsync(new ObterPedidoDetalhesQuery(contexto.EmpresaId, pedidoId.Value, MaxEventos: 20));
        if (detalhe is null || detalhe.Pedido.ClienteId != clienteId)
            return FerramentaJson.Serializar(new { erro = "pedido_nao_encontrado" });

        var configuracao = await configuracaoRepository.GetByEmpresaIdAsync(contexto.EmpresaId);
        var p = detalhe.Pedido;
        var pedido = await pedidoRepository.GetByIdWithDetailsAsync(contexto.EmpresaId, p.Id);
        if (pedido is null || pedido.EmpresaId != contexto.EmpresaId || pedido.ClienteId != clienteId)
            return FerramentaJson.Serializar(new { erro = "pedido_nao_encontrado" });
        var financeiro = await saldos.ConsultarAsync(pedido, ct);
        ComandaClienteResult? estado = null;
        if (links is not null && comanda is not null)
        {
            var ultimo = (await links.ListarDaConversaAsync(contexto.EmpresaId, conversa.Id, contexto.Agora.AddDays(-1), ct))
                .FirstOrDefault(l => l.PedidoRegistradoId == pedido.Id);
            if (ultimo is not null) estado = await comanda.EstadoAsync(ultimo, ct);
        }

        var cupons = await creditos.ListarAsync(contexto.EmpresaId, clienteId, ct);
        var titulo = conversa.NumeroComanda > 0 && conversa.PedidoEmAndamentoId == p.Id
            ? $"Comanda #{conversa.NumeroComanda}" : $"Pedido registrado em {Brasilia(p.CriadoEm)}";
        // #2126: devolução e cupom zerados fora de um acerto de devolução são ruído para o cliente.
        var temDevolucao = financeiro.Devolvido > 0 || financeiro.Reservado > 0 || estado?.Acerto?.Tipo == "estorno";
        var resposta = new List<string>
        {
            $"{titulo}. Situação registrada: {StatusPedidoVocabulario.RotuloLojista(StatusPedidoMapper.Parse(pedido.Status))}.",
            $"Total: {FerramentaJson.FormatarReais(p.Total)}. Cobertura atual: {FerramentaJson.FormatarReais(financeiro.Alocado)}. Falta pagar: {FerramentaJson.FormatarReais(Math.Max(0, p.Total - financeiro.Alocado))}.",
            temDevolucao
                ? $"Dinheiro recebido registrado: {FerramentaJson.FormatarReais(financeiro.Recebido)}. Devoluções registradas: {FerramentaJson.FormatarReais(financeiro.Devolvido)}. Reservado para devolução: {FerramentaJson.FormatarReais(financeiro.Reservado)}."
                : $"Dinheiro recebido registrado: {FerramentaJson.FormatarReais(financeiro.Recebido)}.",
        };
        if (financeiro.ConvertidoEmCupom > 0 || financeiro.CupomAplicado > 0)
            resposta.Add($"Crédito emitido em cupom: {FerramentaJson.FormatarReais(financeiro.ConvertidoEmCupom)}. Crédito aplicado ao pedido: {FerramentaJson.FormatarReais(financeiro.CupomAplicado)}.");
        resposta.Add(
            estado?.Pedido?.Janela is { } janela
                ? $"Entrega agendada para {janela.Data:dd/MM/yyyy}, das {janela.HoraInicio:HH:mm} às {janela.HoraFim:HH:mm}."
                : p.AgendadoParaEm is { } agendado
                    ? $"Data de entrega registrada: {HorarioBrasil.ConverterParaBrasilia(agendado):dd/MM/yyyy}. A equipe pode conferir a janela com você."
                    : "Sem previsão de entrega registrada.");
        if (estado?.Acerto is { Tipo: "estorno" } acerto)
            foreach (var devolucao in acerto.Devolucoes)
                resposta.Add($"Solicitação de devolução desta alteração: {FerramentaJson.FormatarReais(devolucao.Valor)}, " +
                    (devolucao.Situacao switch { "confirmado" => "registrada", "recusado" => "recusada, não confirmada pelo provedor", _ => "pendente" }) + ".");
        if (estado?.Acerto is { Tipo: "cupom", CodigoCupom: not null, SaldoCupom: { } saldo } cupom)
            resposta.Add($"Cupom {cupom.CodigoCupom}. Saldo atual: {FerramentaJson.FormatarReais(saldo)}.");
        if (estado?.Envio?.CupomAplicado is { } aplicado)
            resposta.Add($"Cupom {aplicado.Codigo}: crédito aplicado de {FerramentaJson.FormatarReais(aplicado.Valor)}. Saldo atual: {FerramentaJson.FormatarReais(aplicado.SaldoCupom)}.");
        foreach (var cupomDisponivel in cupons)
            resposta.Add($"Cupom disponível {cupomDisponivel.Codigo}: {FerramentaJson.FormatarReais(cupomDisponivel.Saldo)}.");
        if (financeiro.Devolvido > 0 || financeiro.Reservado > 0 || estado?.Acerto?.Tipo == "estorno")
            resposta.Add("O registro de devolução no sistema não comprova o crédito na sua conta. A equipe pode conferir isso com você.");
        contexto.RegistrarRespostaVerificada(string.Join("\n", resposta));

        return FerramentaJson.Serializar(new
        {
            pedidoId = p.Id,
            numeroComanda = conversa.NumeroComanda,
            status = p.Status,
            total = FerramentaJson.FormatarReais(p.Total),
            pago = FerramentaJson.FormatarReais(financeiro.Alocado),
            recebido = FerramentaJson.FormatarReais(financeiro.Recebido),
            faltaPagar = FerramentaJson.FormatarReais(Math.Max(0, p.Total - financeiro.Alocado)),
            financeiro,
            acerto = estado?.Acerto,
            cupomAplicado = estado?.Envio?.CupomAplicado,
            cupons = cupons.Select(c => new { codigo = c.Codigo, saldo = c.Saldo }),
            criadoEm = Brasilia(p.CriadoEm),
            agendadoPara = p.AgendadoParaEm is { } a ? Brasilia(a) : null,
            entregueEm = p.EntreguEm is { } e ? Brasilia(e) : null,
            canceladoEm = p.CanceladoEm is { } c ? Brasilia(c) : null,
            previsao = p.AgendadoParaEm is null ? "sem previsão registrada" : null,
            tempoPreparoMinutos = configuracao?.TempoPreparoPadraoMinutos
                ?? Domain.Entities.Atendimento.ConfiguracaoAtendimento.CriarPadrao(contexto.EmpresaId).TempoPreparoPadraoMinutos,
            itens = detalhe.Itens.Select(i => new { nome = i.Nome, quantidade = i.Quantidade, unidade = i.Unidade })
        });
    }

    private static string Brasilia(DateTime utc) => HorarioBrasil.ConverterParaBrasilia(utc).ToString("dd/MM/yyyy HH:mm");
}
