using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Mensagem automática por gatilho (S42, protótipo <c>dominio/automacao.js</c>): uma regra por gatilho e
/// empresa. Desligar é o rollback. O texto aceita as variáveis de <see cref="ModeloTextoAtendimento"/>.
/// </summary>
public class RegraAutomatica
{
    public const int TextoTamanhoMaximo = 1024;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public GatilhoAutomacao Gatilho { get; private set; }
    public bool Ligada { get; private set; }
    public string Texto { get; private set; } = null!;
    public DateTime CriadaEm { get; private set; }
    public DateTime AlteradaEm { get; private set; }

    private RegraAutomatica() { }

    public static RegraAutomatica Criar(Guid empresaId, GatilhoAutomacao gatilho, string texto, bool ligada, DateTime agora)
    {
        if (empresaId == Guid.Empty) throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");
        if (!Enum.IsDefined(gatilho)) throw new RegraDeDominioVioladaException($"Gatilho inválido: {(int)gatilho}.");
        var regra = new RegraAutomatica { Id = Guid.NewGuid(), EmpresaId = empresaId, Gatilho = gatilho, CriadaEm = agora };
        regra.Alterar(texto, ligada, agora);
        return regra;
    }

    public void Alterar(string texto, bool ligada, DateTime agora)
    {
        Texto = RespostaPronta.Obrigatorio(texto, "Texto", TextoTamanhoMaximo);
        Ligada = ligada;
        AlteradaEm = agora;
    }

    /// <summary>
    /// #1610: texto do lembrete do link quando a dona ainda não gravou a regra. Só usa <c>{pedido}</c>, que o disparo
    /// sempre preenche: <c>{nome}</c> pode faltar, e variável sem valor não sai.
    /// </summary>
    public const string TextoPadraoCobrancaVencendo =
        "Oi! O link de pagamento do pedido {pedido} vence em poucos minutos. Se ainda quiser garantir, é só pagar por ele. "
        + "Se precisar de outro link, me chama aqui.";

    /// <summary>
    /// Texto de "Pagamento confirmado" enquanto a dona não grava o dela, com o que o aviso S13 de pago diz (nome e
    /// entrega). Pedido sem horário agendado não tem <c>{faixa}</c>: a automática não sai e o aviso S13 avisa.
    /// </summary>
    public const string TextoPadraoPagamentoConfirmado =
        "Oi, {nome}! Recebemos seu pagamento e seu pedido está confirmado para {faixa}. Obrigada pela preferência!";

    /// <summary>
    /// Regra que vale enquanto a empresa não gravou a do gatilho (não é persistida). Nascem ligadas "Pagamento
    /// confirmado" (decisão do Felipe, 23/10, #1765-4) e o lembrete do link (#1610); as demais nascem desligadas
    /// (nulo). Salvar a regra, ligada ou desligada, substitui o padrão.
    /// </summary>
    public static RegraAutomatica? Padrao(Guid empresaId, GatilhoAutomacao gatilho, DateTime agora) =>
        gatilho switch
        {
            GatilhoAutomacao.PagamentoConfirmado => Criar(empresaId, gatilho, TextoPadraoPagamentoConfirmado, ligada: true, agora),
            GatilhoAutomacao.CobrancaVencendo => Criar(empresaId, gatilho, TextoPadraoCobrancaVencendo, ligada: true, agora),
            _ => null
        };

    public string Renderizar(IReadOnlyDictionary<string, string?> valores) =>
        ModeloTextoAtendimento.Renderizar(Texto, valores);

    /// <summary>
    /// Na primeira mensagem de uma conversa sai só uma automática: loja fechada na mão vence fora do
    /// horário, que vence o primeiro contato.
    /// </summary>
    public static GatilhoAutomacao GatilhoDaPrimeiraEntrada(bool lojaAberta, bool fechadaNaMao) =>
        lojaAberta ? GatilhoAutomacao.PrimeiroContato
        : fechadaNaMao ? GatilhoAutomacao.LojaFechada
        : GatilhoAutomacao.ForaDoHorario;
}
