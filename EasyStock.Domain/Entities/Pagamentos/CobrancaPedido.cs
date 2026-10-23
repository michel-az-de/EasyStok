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
    public const int ContaRecebedoraTamanhoMaximo = 30;
    public const int OrigemCredencialTamanhoMaximo = 20;

    /// <summary>Credencial única da plataforma no <c>.env</c> (I3.2). A E6 acrescenta a credencial da loja.</summary>
    public const string OrigemCredencialGlobal = "global";

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid PedidoId { get; private set; }
    public bool Complementar { get; private set; }
    public void MarcarComplementar() => Complementar = true;

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

    /// <summary>
    /// Conta do Mercado Pago que recebe esta cobrança (<c>collector_id</c> da preferência; I3.2, invariantes 3
    /// e 5). Null na entrega e em cobrança criada antes da I3.2.
    /// </summary>
    public string? ContaRecebedoraId { get; private set; }

    /// <summary>De onde veio a credencial que criou a cobrança (<see cref="OrigemCredencialGlobal"/>). Null na entrega.</summary>
    public string? OrigemCredencial { get; private set; }

    /// <summary>
    /// Desde quando a cobrança está fora da varredura do job porque a credencial atual não alcança a conta dela
    /// (trocada, ausente ou recusada; invariante 4). Continua <see cref="StatusCobrancaPedido.Pendente"/>: o
    /// pedido não é cancelado e o pagamento ainda a confirma. Volta à varredura quando a credencial volta.
    /// </summary>
    public DateTime? AguardandoCredencialDesde { get; private set; }

    public bool AguardandoCredencial => AguardandoCredencialDesde is not null;

    public bool EhOnline => Provedor == ProvedorMercadoPago;
    public bool EstaPendente => Status == StatusCobrancaPedido.Pendente;

    /// <summary>
    /// #1724-7: registro de um pagamento aprovado abaixo do valor da cobrança (<see cref="RegistrarPagamentoAMenor"/>).
    /// Online sem preferência própria: não é link pagável nem quita o pedido; existe para o estorno online achar o
    /// recebimento e devolvê-lo.
    /// </summary>
    public bool RegistraPagamentoAMenor => EhOnline && ReferenciaExterna is null;

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
        Guid? conversaId = null,
        string? contaRecebedoraId = null,
        string origemCredencial = OrigemCredencialGlobal)
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
        c.ContaRecebedoraId = Limitado(contaRecebedoraId, ContaRecebedoraTamanhoMaximo, "Conta recebedora");
        c.OrigemCredencial = Limitado(origemCredencial, OrigemCredencialTamanhoMaximo, "Origem da credencial");
        return c;
    }

    private static string? Limitado(string? valor, int maximo, string campo)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var limpo = valor.Trim();
        if (limpo.Length > maximo) throw new RegraDeDominioVioladaException($"{campo} acima de {maximo} caracteres.");
        return limpo;
    }

    /// <summary>
    /// Tira a cobrança da varredura do job (I3.2): a credencial atual não alcança a conta dela, então não dá
    /// para saber se o cliente pagou. Só online pendente; repetir mantém a primeira marca.
    /// </summary>
    public void AguardarCredencial(DateTime agora)
    {
        if (!EstaPendente || !EhOnline)
            throw new RegraDeDominioVioladaException("Só cobrança online pendente aguarda credencial.");
        AguardandoCredencialDesde ??= Utc(agora);
        AtualizadaEm = Utc(agora);
    }

    /// <summary>
    /// #1724-7: o cliente pagou menos que a <paramref name="cobrada"/> e o dinheiro ficou no Mercado Pago. O valor vira
    /// um registro online já pago, sem preferência (o link continua sendo da cobrada, que segue pendente), com o id do
    /// pagamento: é por ele que o estorno online devolve. Não confirma nem quita o pedido.
    /// </summary>
    public static CobrancaPedido RegistrarPagamentoAMenor(CobrancaPedido cobrada, string pagamentoExternoId, decimal valorPago,
        string metodo, DateTime pagoEm, string motivo)
    {
        ArgumentNullException.ThrowIfNull(cobrada);
        if (!cobrada.EhOnline)
            throw new RegraDeDominioVioladaException("Pagamento a menor só vem de cobrança online.");
        if (valorPago <= 0m || valorPago >= cobrada.Valor)
            throw new RegraDeDominioVioladaException("Pagamento a menor deve ser positivo e abaixo do valor da cobrança.");

        var c = Nova(cobrada.EmpresaId, cobrada.PedidoId, valorPago, ProvedorMercadoPago, pagoEm, cobrada.ConversaId);
        c.Tentativa = cobrada.Tentativa;
        c.ContaRecebedoraId = cobrada.ContaRecebedoraId;
        c.OrigemCredencial = cobrada.OrigemCredencial;
        c.MarcarPaga(pagamentoExternoId, valorPago, metodo, pagoEm);
        c.RegistrarMotivo(motivo, pagoEm);
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
    /// #1610: link online pendente, ainda válido, a até <paramref name="antecedencia"/> de vencer. Fora o que aguarda
    /// credencial (não se sabe se foi pago) e o registro de pagamento a menor (não é link pagável).
    /// </summary>
    public bool VenceEmBreve(DateTime agora, TimeSpan antecedencia)
    {
        var instante = Utc(agora);
        return EstaPendente && EhOnline && !RegistraPagamentoAMenor && !AguardandoCredencial
            && ExpiraEm is { } expira && instante < expira && expira - antecedencia <= instante;
    }

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
        AguardandoCredencialDesde = null;
        PagamentoExternoId = pagamentoExternoId.Trim();
        ValorPago = valorPago;
        MetodoPagamento = metodo;
        PagaEm = Utc(pagaEm);
        AtualizadaEm = PagaEm;
    }

    public void ReabrirPagamentoManual(DateTime agora)
    {
        if (EhOnline || Status != StatusCobrancaPedido.Paga)
            throw new RegraDeDominioVioladaException("Só cobrança manual paga pode ser reaberta.");
        Status = StatusCobrancaPedido.Pendente;
        PagamentoExternoId = null;
        ValorPago = null;
        PagaEm = null;
        MetodoPagamento = null;
        AtualizadaEm = Utc(agora);
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

    /// <summary>
    /// O Mercado Pago informou o estorno (<c>refunded</c>) ou a contestação (<c>charged_back</c>) do pagamento
    /// que pagou esta cobrança (S32). Só cobrança paga é estornada; repetir é no-op (o webhook reenvia).
    /// </summary>
    public void MarcarEstornada(string motivo, DateTime agora)
    {
        if (Status == StatusCobrancaPedido.Estornada) return;
        if (Status != StatusCobrancaPedido.Paga)
            throw new RegraDeDominioVioladaException($"Só cobrança paga é estornada (atual: {Status}).");
        Status = StatusCobrancaPedido.Estornada;
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
