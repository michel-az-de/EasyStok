using EasyStock.Domain.Enums.Pagamentos;

namespace EasyStock.Domain.Entities.Pagamentos;

/// <summary>
/// Cobrança de um pedido (S11, doc 02; Mercado Pago como gateway único de pedidos, doc 08 opção A).
///
/// <para>
/// Online (<see cref="ProvedorMercadoPago"/>): uma preferência do Checkout Pro com
/// <c>external_reference = PedidoId</c>, link (<c>init_point</c>) e expiração. O job reemite uma vez
/// (<see cref="Tentativa"/> 2) quando o pedido veio da conversa; depois cancela o pedido.
/// Na entrega (<see cref="ProvedorNaEntrega"/>): sem link nem expiração; maquininha ou dinheiro na entrega.
/// </para>
///
/// <para>
/// Um pedido pode ter várias cobranças (troca de forma, reemissão); no máximo uma fica
/// <see cref="StatusCobrancaPedido.Pendente"/> por vez, regra dos use cases. <see cref="Provedor"/> é
/// string para não fechar a porta a outro gateway. O instante vem sempre por parâmetro.
/// </para>
/// </summary>
public class CobrancaPedido
{
    public const string ProvedorMercadoPago = "mercadopago";
    public const string ProvedorNaEntrega = "na_entrega";
    public const int ProvedorTamanhoMaximo = 30;
    public const int ReferenciaTamanhoMaximo = 120;
    public const int LinkTamanhoMaximo = 1000;
    public const int MetodoTamanhoMaximo = 20;
    public const int MotivoTamanhoMaximo = 500;
    public const int TentativaMaxima = 2;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid PedidoId { get; private set; }

    /// <summary>Conversa de atendimento onde o link foi enviado; null para pedido do site.</summary>
    public Guid? ConversaId { get; private set; }

    public string Provedor { get; private set; } = null!;

    /// <summary>Id da preferência no Mercado Pago; null na entrega.</summary>
    public string? ReferenciaExterna { get; private set; }

    /// <summary><c>init_point</c> do Checkout Pro; null na entrega.</summary>
    public string? LinkPagamento { get; private set; }

    public decimal Valor { get; private set; }
    public DateTime? ExpiraEm { get; private set; }
    public StatusCobrancaPedido Status { get; private set; }
    public DateTime? PagaEm { get; private set; }
    public decimal? ValorPago { get; private set; }

    /// <summary>Id do pagamento no Mercado Pago (necessário para o estorno, S27).</summary>
    public string? PagamentoExternoId { get; private set; }

    /// <summary><c>pix</c>, <c>credito</c>, <c>debito</c> ou <c>outro</c>.</summary>
    public string? MetodoPagamento { get; private set; }

    public int Tentativa { get; private set; }
    public DateTime CriadaEm { get; private set; }
    public DateTime? AtualizadaEm { get; private set; }

    /// <summary>Por que a cobrança foi cancelada ou por que um pagamento não a confirmou.</summary>
    public string? Motivo { get; private set; }

    public bool EhOnline => Provedor == ProvedorMercadoPago;
    public bool EstaPendente => Status == StatusCobrancaPedido.Pendente;

    // EF Core
    private CobrancaPedido() { }

    public static CobrancaPedido CriarOnline(
        Guid empresaId,
        Guid pedidoId,
        decimal valor,
        string referenciaExterna,
        string linkPagamento,
        DateTime expiraEm,
        int tentativa,
        DateTime agora,
        Guid? conversaId = null)
    {
        if (string.IsNullOrWhiteSpace(referenciaExterna))
            throw new RegraDeDominioVioladaException("Cobrança online exige a referência da preferência.");
        if (string.IsNullOrWhiteSpace(linkPagamento))
            throw new RegraDeDominioVioladaException("Cobrança online exige o link de pagamento.");
        if (tentativa < 1 || tentativa > TentativaMaxima)
            throw new RegraDeDominioVioladaException($"Tentativa da cobrança deve ser de 1 a {TentativaMaxima}.");
        if (Utc(expiraEm) <= Utc(agora))
            throw new RegraDeDominioVioladaException("A expiração da cobrança deve ser futura.");

        var c = Nova(empresaId, pedidoId, valor, ProvedorMercadoPago, agora, conversaId);
        c.ReferenciaExterna = referenciaExterna.Trim();
        c.LinkPagamento = linkPagamento.Trim();
        c.ExpiraEm = Utc(expiraEm);
        c.Tentativa = tentativa;
        return c;
    }

    public static CobrancaPedido CriarNaEntrega(
        Guid empresaId, Guid pedidoId, decimal valor, DateTime agora, Guid? conversaId = null)
    {
        var c = Nova(empresaId, pedidoId, valor, ProvedorNaEntrega, agora, conversaId);
        c.Tentativa = 1;
        return c;
    }

    private static CobrancaPedido Nova(
        Guid empresaId, Guid pedidoId, decimal valor, string provedor, DateTime agora, Guid? conversaId)
    {
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");
        if (pedidoId == Guid.Empty)
            throw new RegraDeDominioVioladaException("PedidoId é obrigatório.");
        if (valor <= 0m)
            throw new RegraDeDominioVioladaException("Valor da cobrança deve ser maior que zero.");
        if (conversaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("ConversaId não pode ser Guid.Empty; use null.");

        return new CobrancaPedido
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            PedidoId = pedidoId,
            ConversaId = conversaId,
            Provedor = provedor,
            Valor = valor,
            Status = StatusCobrancaPedido.Pendente,
            CriadaEm = Utc(agora),
        };
    }

    /// <summary>Link online pendente cujo prazo já passou.</summary>
    public bool Venceu(DateTime agora) =>
        EstaPendente && EhOnline && ExpiraEm is { } expira && expira <= Utc(agora);

    /// <summary>
    /// Pagamento confirmado pelo provedor. Aceita também cobrança já <c>Cancelada</c> ou <c>Expirada</c>:
    /// dinheiro recebido vence (S11, troca de forma). Mesmo pagamento de novo é no-op; outro pagamento
    /// numa cobrança já paga é recusado.
    /// </summary>
    public void MarcarPaga(string pagamentoExternoId, decimal valorPago, string metodo, DateTime pagaEm)
    {
        if (string.IsNullOrWhiteSpace(pagamentoExternoId))
            throw new RegraDeDominioVioladaException("Id do pagamento é obrigatório.");

        if (Status == StatusCobrancaPedido.Paga)
        {
            if (PagamentoExternoId == pagamentoExternoId) return;
            throw new RegraDeDominioVioladaException("Cobrança já paga por outro pagamento.");
        }
        if (Status == StatusCobrancaPedido.Estornada)
            throw new RegraDeDominioVioladaException("Cobrança estornada não volta a ser paga.");

        Status = StatusCobrancaPedido.Paga;
        PagamentoExternoId = pagamentoExternoId.Trim();
        ValorPago = valorPago;
        MetodoPagamento = metodo;
        PagaEm = Utc(pagaEm);
        AtualizadaEm = PagaEm;
    }

    public void Expirar(DateTime agora)
    {
        if (!EstaPendente)
            throw new RegraDeDominioVioladaException($"Só cobrança pendente expira (atual: {Status}).");
        Status = StatusCobrancaPedido.Expirada;
        AtualizadaEm = Utc(agora);
    }

    public void Cancelar(string motivo, DateTime agora)
    {
        if (!EstaPendente)
            throw new RegraDeDominioVioladaException($"Só cobrança pendente é cancelada (atual: {Status}).");
        Status = StatusCobrancaPedido.Cancelada;
        RegistrarMotivo(motivo, agora);
    }

    /// <summary>Grava por que um pagamento não confirmou a cobrança (valor menor, pedido cancelado).</summary>
    public void RegistrarMotivo(string motivo, DateTime agora)
    {
        var texto = string.IsNullOrWhiteSpace(motivo) ? "sem_motivo" : motivo.Trim();
        Motivo = texto.Length > MotivoTamanhoMaximo ? texto[..MotivoTamanhoMaximo] : texto;
        AtualizadaEm = Utc(agora);
    }

    /// <summary>
    /// Traduz o pagamento do Mercado Pago para o método de <c>PedidoPagamento</c>:
    /// <c>payment_method_id = pix</c> → pix; <c>payment_type_id</c> <c>credit_card</c> → credito,
    /// <c>debit_card</c> → debito; o resto → outro.
    /// </summary>
    public static string MapearMetodo(string? paymentMethodId, string? paymentTypeId)
    {
        if (string.Equals(paymentMethodId, "pix", StringComparison.OrdinalIgnoreCase)) return "pix";
        return paymentTypeId?.ToLowerInvariant() switch
        {
            "credit_card" => "credito",
            "debit_card" or "prepaid_card" => "debito",
            _ => "outro",
        };
    }

    private static DateTime Utc(DateTime instante) =>
        instante.Kind == DateTimeKind.Utc ? instante : DateTime.SpecifyKind(instante, DateTimeKind.Utc);
}
