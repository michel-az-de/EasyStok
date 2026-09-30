namespace EasyStock.Domain.Entities.Campanhas;

/// <summary>Valores de <see cref="CampanhaDestinatario.MotivoExclusao"/>. Constantes, como <see cref="OrigemClienteTag"/>.</summary>
public static class MotivoExclusaoCampanha
{
    public const string Restricao = "restricao";
    public const string LimiteSemanal = "limite_semanal";
    public const string SemConsentimento = "sem_consentimento";
    public const string Bloqueado = "bloqueado";
    public const string SemTelefone = "sem_telefone";

    /// <summary>A campanha foi cancelada antes da vez do cliente.</summary>
    public const string Cancelada = "cancelada";

    public const int TamanhoMaximo = 20;

    public static IReadOnlyList<string> Todos { get; } =
        [Restricao, LimiteSemanal, SemConsentimento, Bloqueado, SemTelefone, Cancelada];

    public static bool EhValido(string? valor) => valor is not null && Todos.Contains(valor);
}
