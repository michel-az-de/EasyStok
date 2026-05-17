namespace EasyStock.Domain.Reporting;

/// <summary>Ciclo de vida de uma execução de relatório.</summary>
public enum ReportStatus : short
{
    /// <summary>Aguardando Worker iniciar o processamento.</summary>
    Pending = 0,

    /// <summary>Worker está executando a query e gerando o arquivo.</summary>
    Running = 1,

    /// <summary>Arquivo gerado com sucesso e disponível para download.</summary>
    Succeeded = 2,

    /// <summary>Cancelado pelo usuário (a partir de Pending ou Running).</summary>
    Canceled = 3,

    /// <summary>Falhou de forma terminal (esgotou tentativas ou erro irrecuperável).</summary>
    Failed = 4,

    /// <summary>Cancelamento solicitado enquanto Running; aguardando handler parar.</summary>
    Canceling = 5,
}
