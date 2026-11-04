namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Quais avisos da operação chegam à dona e ao gerente, e com que folga (#2096). Mora na
/// <see cref="ConfiguracaoAtendimento"/> como um documento só. Tempo zero desliga o aviso. O tempo do cliente
/// esperando é o <see cref="ConfiguracaoAtendimento.SlaRespostaMinutos"/>; aqui só o interruptor.
/// </summary>
public sealed record AvisosOperacao
{
    public const int ProntoSemEntregadorPadrao = 15;
    public const int EncomendaProximaPadrao = 2;
    public const int EncomendaSemConfirmacaoPadrao = 24;
    public const int EstoqueVencendoPadrao = 2;
    public const int EstoqueParadoPadrao = 10;

    public static readonly AvisosOperacao Padrao = new();

    public bool ClienteSemResposta { get; init; } = true;
    public bool PedidoAtrasado { get; init; } = true;
    public bool ImpressaoTravada { get; init; } = true;
    public bool CaixaEsquecido { get; init; } = true;

    /// <summary>Minutos com o pedido pronto, para entrega, sem viagem com entregador. 0 desliga; 5 a 240.</summary>
    public int ProntoSemEntregadorMinutos { get; init; } = ProntoSemEntregadorPadrao;

    /// <summary>Horas antes do horário da encomenda para lembrar a equipe. 0 desliga; 1 a 48.</summary>
    public int EncomendaProximaHoras { get; init; } = EncomendaProximaPadrao;

    /// <summary>Horas antes do horário da encomenda sem confirmação do cliente. 0 desliga; 1 a 72.</summary>
    public int EncomendaSemConfirmacaoHoras { get; init; } = EncomendaSemConfirmacaoPadrao;

    /// <summary>Dias até a validade para o item entrar no resumo do estoque. 0 desliga; 1 a 30.</summary>
    public int EstoqueVencendoDias { get; init; } = EstoqueVencendoPadrao;

    /// <summary>Dias de lote no freezer sem baixa para entrar no resumo como "conferir". 0 desliga; 3 a 120.</summary>
    public int EstoqueParadoDias { get; init; } = EstoqueParadoPadrao;

    public bool ReclamacaoCliente { get; init; } = true;
    public bool SuspeitaAtendimento { get; init; } = true;

    /// <summary>Confere as faixas. Lança <see cref="RegraDeDominioVioladaException"/> com a mensagem para a tela.</summary>
    public AvisosOperacao Validado()
    {
        Faixa(ProntoSemEntregadorMinutos, 5, 240, "O aviso de pedido pronto sem entregador", "minutos");
        Faixa(EncomendaProximaHoras, 1, 48, "O aviso de encomenda chegando", "horas");
        Faixa(EncomendaSemConfirmacaoHoras, 1, 72, "O aviso de encomenda sem confirmação", "horas");
        Faixa(EstoqueVencendoDias, 1, 30, "O aviso de estoque perto de vencer", "dias");
        Faixa(EstoqueParadoDias, 3, 120, "O aviso de estoque parado", "dias");
        return this;
    }

    /// <summary>
    /// O aviso pelo código estável do produtor (<c>cliente_sem_resposta</c>, <c>pronto_sem_entregador</c>...). Código
    /// desconhecido fica ligado: aviso novo não some por falta de interruptor.
    /// </summary>
    public bool Ligado(string codigo) => codigo switch
    {
        CodigosAviso.ClienteSemResposta => ClienteSemResposta,
        CodigosAviso.PedidoAtrasado => PedidoAtrasado,
        CodigosAviso.ImpressaoTravada => ImpressaoTravada,
        CodigosAviso.CaixaEsquecido => CaixaEsquecido,
        CodigosAviso.ProntoSemEntregador => ProntoSemEntregadorMinutos > 0,
        CodigosAviso.EncomendaProxima => EncomendaProximaHoras > 0,
        CodigosAviso.EncomendaSemConfirmacao => EncomendaSemConfirmacaoHoras > 0,
        CodigosAviso.EstoqueParaConferir => EstoqueVencendoDias > 0 || EstoqueParadoDias > 0,
        CodigosAviso.ReclamacaoCliente => ReclamacaoCliente,
        CodigosAviso.SuspeitaAtendimento => SuspeitaAtendimento,
        _ => true,
    };

    private static void Faixa(int valor, int minimo, int maximo, string aviso, string unidade)
    {
        if (valor == 0 || (valor >= minimo && valor <= maximo)) return;
        throw new RegraDeDominioVioladaException($"{aviso} vai de {minimo} a {maximo} {unidade}, ou 0 para desligar.");
    }
}

/// <summary>Códigos estáveis dos avisos da operação: vão no payload (chave <c>aviso</c>) e na tela.</summary>
public static class CodigosAviso
{
    public const string ClienteSemResposta = "cliente_sem_resposta";
    public const string PedidoAtrasado = "pedido_atrasado";
    public const string ImpressaoTravada = "impressao_travada";
    public const string CaixaEsquecido = "caixa_esquecido";
    public const string ProntoSemEntregador = "pronto_sem_entregador";
    public const string EncomendaProxima = "encomenda_proxima";
    public const string EncomendaSemConfirmacao = "encomenda_sem_confirmacao";
    public const string EstoqueParaConferir = "estoque_para_conferir";
    public const string ReclamacaoCliente = "reclamacao_cliente";
    public const string SuspeitaAtendimento = "suspeita_atendimento";
}
