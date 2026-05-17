using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting;

/// <summary>
/// Metadados estáticos de um relatório — sem lógica de execução.
/// Implementado por cada relatório concreto (ex: VendasPorPeriodoDefinition).
/// </summary>
public interface IReportDefinition
{
    /// <summary>Chave técnica única (ex: "vendas.por-periodo").</summary>
    string Key { get; }

    /// <summary>Categoria de negócio para agrupamento no catálogo.</summary>
    ReportCategoria Categoria { get; }

    /// <summary>Contexto de execução — Tenant ou AdminSaaS.</summary>
    ReportContexto Contexto { get; }

    /// <summary>Label exibido na UI (pt-BR, ex: "Vendas por período").</summary>
    string Label { get; }

    /// <summary>Descrição curta (~80 chars) exibida no card do catálogo.</summary>
    string Descricao { get; }

    /// <summary>Formatos de saída suportados por este relatório.</summary>
    IReadOnlyList<ReportFormat> FormatosSuportados { get; }

    /// <summary>Retenção do artefato gerado (default 30 dias).</summary>
    TimeSpan Retencao { get; }

    /// <summary>
    /// Permissão requerida no formato "relatorios.{categoria}.{recurso}"
    /// ou "admin.relatorios.{categoria}.{recurso}" para AdminSaaS.
    /// </summary>
    string PermissaoRequerida { get; }

    /// <summary>Máximo de tentativas antes de marcar como Failed definitivo.</summary>
    int MaxTentativas { get; }

    /// <summary>Versão semântica do handler (MAJOR.MINOR.PATCH).</summary>
    string SemanticVersion { get; }

    /// <summary>Estimativa de linhas máximas (null = desconhecido).</summary>
    long? EstimatedMaxRows { get; }

    /// <summary>Se true, o relatório pode ser usado como gatilho (Fase 4).</summary>
    bool AvailableForTriggers { get; }

    /// <summary>Chave de ícone do design system (ex: "bar-chart-2").</summary>
    string IconKey { get; }

    /// <summary>Tipo CLR dos parâmetros de entrada.</summary>
    Type ParamsType { get; }

    /// <summary>Tipo CLR da linha de saída (TRow).</summary>
    Type RowType { get; }
}
