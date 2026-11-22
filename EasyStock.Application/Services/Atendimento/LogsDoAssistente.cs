namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #3121: rastro das decisões do Assistente que tiram a conversa do fluxo automático. Produção só grava de Warning para
/// cima, então estas linhas são Warning, com evento estável para busca. Só códigos e ids: motivo escrito pelo modelo,
/// texto de mensagem, nome e telefone não entram. A conversa e a correlação do turno vêm do escopo aberto em
/// <c>ProcessarTurnoAgenteUseCase</c>.
/// </summary>
public static partial class LogsDoAssistente
{
    public const int EventoPortaoBarrou = 31211;
    public const int EventoRetomadaDispensada = 31212;
    public const int EventoGuardaBarrou = 31213;
    public const int EventoTurnoInterrompido = 31214;
    public const int EventoDicionarioDescartado = 31215;

    public const string CategoriaValor = "valor sem origem em ferramenta";
    public const string CategoriaOutra = "outro motivo";
    public const string CategoriaFerramentaFalhou = "ferramenta falhou no turno";

    /// <summary>Motivos fixos da guarda e do turno. Lista fechada: motivo novo com conteúdo não chega ao log por descuido.</summary>
    private static readonly HashSet<string> CategoriasFixas = new(StringComparer.Ordinal)
    {
        "o agente não produziu resposta",
        "texto com chamada de ferramenta no lugar da resposta",
        "promete encaminhamento sem acionar a equipe",
        "afirma situação do pedido sem a ferramenta confirmar",
        "afirma pagamento ou devolução sem a ferramenta confirmar",
        "promete frete grátis ou desconto, que é decisão da equipe",
        "afirma frete combinado sem a ferramenta confirmar",
        "link sem origem em ferramenta",
        CategoriaFerramentaFalhou,
    };

    public static IReadOnlyCollection<string> CategoriasDaGuarda => CategoriasFixas;

    [LoggerMessage(EventoPortaoBarrou, LogLevel.Warning,
        "Assistente: conversa {ConversaId} não segue no automático, portão {Portao}.")]
    public static partial void PortaoBarrou(ILogger logger, Guid conversaId, string portao);

    [LoggerMessage(EventoRetomadaDispensada, LogLevel.Warning,
        "Assistente: conversa {ConversaId} segue com a equipe, retomada dispensada pelo portão {Portao}.")]
    public static partial void RetomadaDispensada(ILogger logger, Guid conversaId, string portao);

    [LoggerMessage(EventoGuardaBarrou, LogLevel.Warning,
        "Assistente: resposta barrada na conversa {ConversaId} ({Categoria}).")]
    public static partial void GuardaBarrou(ILogger logger, Guid conversaId, string categoria);

    [LoggerMessage(EventoTurnoInterrompido, LogLevel.Warning,
        "Assistente: turno da conversa {ConversaId} interrompido sem resposta ({Desfecho}).")]
    public static partial void TurnoInterrompido(ILogger logger, Guid conversaId, string desfecho);

    [LoggerMessage(EventoDicionarioDescartado, LogLevel.Warning,
        "Assistente: dicionário da loja gravado não serve e ficou fora do turno da conversa {ConversaId}.")]
    public static partial void DicionarioDescartado(ILogger logger, Guid conversaId);

    /// <summary>O motivo de valor carrega o número que o modelo escreveu; o log leva só a categoria.</summary>
    public static string CategoriaDaGuarda(string motivo) =>
        CategoriasFixas.Contains(motivo) ? motivo
        : motivo.StartsWith("valor ", StringComparison.Ordinal) ? CategoriaValor : CategoriaOutra;
}
