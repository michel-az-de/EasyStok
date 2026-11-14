namespace EasyStock.Application.Services.Atendimento;

/// <summary>Etapa do roteiro do Assistente: nome, instrução curta e liga e desliga (#2516). A S68 guarda a lista por loja.</summary>
public sealed record EtapaRoteiroAtendimento(string Nome, string Instrucao, bool Ativo = true);

/// <summary>
/// Roteiro padrão do atendimento (#2516), na ordem em que entra no prompt. A frente de conduta é dona do conteúdo; a
/// configuração por loja (S68, <c>RoteiroJson</c>) só guarda e edita.
/// </summary>
public static class RoteiroAtendimento
{
    public static IReadOnlyList<EtapaRoteiroAtendimento> Padrao { get; } =
    [
        new("Entender o pedido",
            "Descubra o que o cliente quer. A comanda pessoal enviada no primeiro contato vale a conversa toda."),
        new("Itens",
            "Item dito na conversa: confirme sabor, tamanho e quantidade em uma frase e siga para o que falta; nunca diga que anotou ou registrou, " +
            "pois quem marca é o cliente no link ou criar_pedido. Não reenvie a comanda."),
        new("Endereço",
            "Peça o endereço com CEP e número, valide com validar_endereco, repita ao cliente e, com a confirmação dele, salve com confirmar_endereco. " +
            "Fora da área, salve o endereço do mesmo jeito e use escalar_para_dona para combinar entrega e frete."),
        new("Data e janela",
            "Pergunte a data e use listar_janelas; o cliente escolhe uma das opções enviadas."),
        new("Fechar o pedido",
            "Pela comanda, o cliente confirma no link. Se ele pedir para fechar por aqui e consultar_pedido não mostrar pedido em andamento, " +
            "siga até criar_pedido neste atendimento: nunca diga que faz por aqui e pare."),
        new("Acompanhar",
            "Status, entrega e pagamento vêm de consultar_pedido; responda só com o que ela devolver."),
    ];
}
