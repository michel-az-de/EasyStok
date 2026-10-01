using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Operacao.Impressao;

/// <summary>
/// Impresso de expedição do pedido (S49): o que a etiqueta 10×15, o cupom 58 mm e o A4 mostram. Horários
/// já no fuso de Brasília. A formatação de texto (moeda, telefone, datas curtas) fica na Api, por papel.
/// Layout aprovado em <c>docs/plan/atendimento-whatsapp/impressos/pedido-aprovado.html</c>.
/// </summary>
/// <param name="Numero">Número curto do pedido (8 hex maiúsculos), o mesmo do canhoto e do código de barras.</param>
/// <param name="Nota">Texto curto digitado na hora da impressão.</param>
/// <param name="QuantidadeItens">Volumes do pedido para o "7 itens" do total: produtos contados por unidade; item a
/// peso ou volume (ou quantidade fracionada) conta 1; frete e taxa não contam.</param>
public sealed record PedidoImpressoDto(
    PedidoImpressoCasaDto Casa,
    string Numero,
    PedidoImpressoPrazoDto Prazo,
    PedidoImpressoClienteDto Cliente,
    PedidoImpressoEntregaDto? Entrega,
    IReadOnlyList<PedidoImpressoItemDto> Itens,
    string? Observacao,
    string? Nota,
    PedidoImpressoCobrancaDto Cobranca,
    int QuantidadeItens,
    DateTime SolicitadoEm,
    DateTime AlteradoEm,
    DateTime ImpressoEm);

public sealed record PedidoImpressoCasaDto(string Nome, string? Documento, string? Site, string? WhatsApp, string? LogoUrl);

/// <summary>
/// Agendado: <see cref="Entrega"/> é o início da janela (ou o horário agendado), <see cref="EntregaFim"/> o fim
/// quando há janela, e <see cref="ProntoAte"/> vem antes da janela. Imediato: <see cref="ProntoAte"/> é a
/// previsão de pronto e <see cref="Entrega"/> a previsão de saída.
/// </summary>
public sealed record PedidoImpressoPrazoDto(bool Agendado, DateTime Entrega, DateTime? EntregaFim, DateTime ProntoAte);

/// <param name="IdCurto">6 primeiros hex do id do cadastro, maiúsculos; nulo sem cadastro.</param>
/// <param name="Telefone">Completo: o entregador liga (decisão de 01/10/2026). CPF nunca entra.</param>
public sealed record PedidoImpressoClienteDto(string? IdCurto, string? Nome, string? Telefone, string? Endereco);

public sealed record PedidoImpressoEntregaDto(TipoEntregador? Tipo, string? Responsavel);

/// <param name="Nome">Nome do item com a variação entre parênteses, quando houver.</param>
/// <param name="Unidade">Unidade em minúsculas para o papel (<c>un</c> quando o pedido não informa).</param>
public sealed record PedidoImpressoItemDto(
    decimal Quantidade, string Unidade, string Nome, string? Observacao, decimal Unitario, decimal Subtotal);

/// <param name="Pago">Pagamentos somam o total.</param>
/// <param name="Forma">Método da cobrança ou do último pagamento (<c>pix</c>, <c>cartao</c>...), cru.</param>
public sealed record PedidoImpressoCobrancaDto(decimal Total, bool Pago, string? Forma);
