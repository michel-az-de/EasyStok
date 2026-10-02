namespace EasyStock.Domain.Enums.Notifications;

/// <summary>Parte da plataforma que o aviso de incidente (N10) nomeia. Enum fechado: nenhum texto livre entra no aviso.</summary>
public enum ComponenteIncidente
{
    Api = 1,
    Banco = 2,
    Redis = 3,
    Erros5xx = 4,
    /// <summary>Porta para o vigia de integração da F16 (token da Meta etc.). A N10 só a entrega.</summary>
    Integracao = 5,
}

/// <summary>Para onde o componente foi: abriu o problema ou voltou ao normal.</summary>
public enum EstadoIncidente
{
    ComProblema = 1,
    Normalizado = 2,
}

public enum SeveridadeIncidente
{
    Media = 1,
    Alta = 2,
    Critica = 3,
}
