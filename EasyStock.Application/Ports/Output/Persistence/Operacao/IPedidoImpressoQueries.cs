using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence.Operacao;

/// <summary>
/// Leitura do impresso do pedido (S49): tudo o que o papel de expedição mostra, numa consulta só, sem
/// tracking. <c>EmpresaId</c> no WHERE além do RLS (ADR-0010).
/// </summary>
public interface IPedidoImpressoQueries
{
    /// <summary>Nulo para pedido inexistente ou de outra empresa.</summary>
    Task<PedidoImpressoLeitura?> ObterAsync(Guid empresaId, Guid pedidoId, CancellationToken ct = default);
}

/// <param name="Janela">Vaga ativa (<c>VagaOcupada → JanelaEntrega</c>), a mesma leitura do KDS.</param>
/// <param name="FormaCobranca">Método da cobrança mais recente (<c>CobrancaPedido.MetodoPagamento</c>).</param>
/// <param name="Entrega">Parada da viagem mais recente do pedido (S44); nulo sem viagem.</param>
/// <param name="TempoPreparoPadraoMinutos"><c>ConfiguracaoAtendimento.TempoPreparoPadraoMinutos</c> (padrão 60).</param>
public sealed record PedidoImpressoLeitura(
    Guid Id,
    DateTime CriadoEm,
    DateTime AlteradoEm,
    DateTime? AgendadoParaEm,
    PedidoImpressoJanelaLeitura? Janela,
    PedidoImpressoCasaLeitura Casa,
    PedidoImpressoClienteLeitura Cliente,
    string? Observacoes,
    decimal Total,
    IReadOnlyList<PedidoImpressoPagamentoLeitura> Pagamentos,
    string? FormaCobranca,
    PedidoImpressoEntregaLeitura? Entrega,
    int TempoPreparoPadraoMinutos,
    IReadOnlyList<PedidoImpressoItemLeitura> Itens);

public sealed record PedidoImpressoJanelaLeitura(DateOnly Data, TimeOnly Inicio, TimeOnly Fim);

/// <param name="Nome"><c>Storefront.TituloPublico</c>, senão o nome da empresa.</param>
/// <param name="Documento"><c>Empresa.Documento</c> (CNPJ) como gravado.</param>
/// <param name="Site"><c>Storefront.DominioCustom</c>.</param>
/// <param name="WhatsApp"><c>Storefront.WhatsappPedidos</c>.</param>
public sealed record PedidoImpressoCasaLeitura(
    string Nome,
    string? Documento,
    string? Site,
    string? WhatsApp,
    string? LogoUrl);

/// <param name="Nome">Nome do pedido, senão do cadastro.</param>
/// <param name="Telefone">Telefone do pedido, senão do cadastro.</param>
/// <param name="Apt"><c>Pedido.ClienteApt</c>, usado quando o cadastro não tem complemento.</param>
public sealed record PedidoImpressoClienteLeitura(
    Guid? Id,
    string? Nome,
    string? Telefone,
    string? Endereco,
    string? Complemento,
    string? Apt,
    string? Bairro,
    string? Cidade,
    string? Cep);

public sealed record PedidoImpressoPagamentoLeitura(decimal Valor, DateTime PagoEm, string Metodo);

/// <param name="Tipo">Tipo do entregador cadastrado; nulo quando a parada não tem entregador.</param>
/// <param name="Nome">Nome do entregador (cadastro ou o digitado na parada).</param>
public sealed record PedidoImpressoEntregaLeitura(TipoEntregador? Tipo, string? Nome);

public sealed record PedidoImpressoItemLeitura(
    string Nome,
    string? Variacao,
    decimal Quantidade,
    decimal PrecoUnitario,
    decimal Subtotal,
    string? Observacao);
