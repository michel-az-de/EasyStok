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

    private const string CategoriaValor = "valor sem origem em ferramenta";

    [LoggerMessage(EventoPortaoBarrou, LogLevel.Warning,
        "Assistente: conversa {ConversaId} não segue no automático, portão {Portao}.")]
    public static partial void PortaoBarrou(ILogger logger, Guid conversaId, string portao);

    [LoggerMessage(EventoRetomadaDispensada, LogLevel.Warning,
        "Assistente: conversa {ConversaId} segue com a equipe, retomada dispensada pelo portão {Portao}.")]
    public static partial void RetomadaDispensada(ILogger logger, Guid conversaId, string portao);

    [LoggerMessage(EventoGuardaBarrou, LogLevel.Warning,
        "Assistente: resposta barrada na conversa {ConversaId} ({Categoria}); conversa devolvida à equipe.")]
    public static partial void GuardaBarrou(ILogger logger, Guid conversaId, string categoria);

    [LoggerMessage(EventoTurnoInterrompido, LogLevel.Warning,
        "Assistente: turno da conversa {ConversaId} interrompido sem resposta ({Desfecho}).")]
    public static partial void TurnoInterrompido(ILogger logger, Guid conversaId, string desfecho);

    /// <summary>O motivo de valor carrega o número que o modelo escreveu; o log leva só a categoria.</summary>
    public static string CategoriaDaGuarda(string motivo) =>
        motivo.StartsWith("valor ", StringComparison.Ordinal) ? CategoriaValor : motivo;
}
