using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Domain.Entities.Notifications;

public class RotinaNotificacao
{
    public Guid Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nome { get; set; } = null!;
    public TipoEventoNotificacao TipoEvento { get; set; }
    public TriggerTipoRotina TriggerTipo { get; set; }
    public string? CronExpression { get; set; }
    public string ParametrosJson { get; set; } = "{}";
    public string CanaisOrdemFallbackJson { get; set; } = "[]";
    public string TemplateCodigo { get; set; } = null!;
    public CategoriaConteudoNotificacao Categoria { get; set; } = CategoriaConteudoNotificacao.Operacional;
    public bool Ativa { get; set; }
    public Guid? EmpresaId { get; set; }
    public TimeOnly? JanelaInicio { get; set; }
    public TimeOnly? JanelaFim { get; set; }
    public bool RespeitarFusoLoja { get; set; }
    public DateTime CriadaEm { get; set; }
    public DateTime AtualizadaEm { get; set; }
    public string AtualizadaPor { get; set; } = "system";

    public Empresa? Empresa { get; set; }

    public static RotinaNotificacao Criar(
        string codigo,
        string nome,
        TipoEventoNotificacao tipoEvento,
        TriggerTipoRotina triggerTipo,
        string templateCodigo,
        CategoriaConteudoNotificacao categoria,
        string? cronExpression = null,
        Guid? empresaId = null)
    {
        if (triggerTipo == TriggerTipoRotina.Cron && string.IsNullOrWhiteSpace(cronExpression))
            throw new ArgumentException("Trigger Cron exige CronExpression.", nameof(cronExpression));

        var agora = DateTime.UtcNow;
        return new RotinaNotificacao
        {
            Id = Guid.NewGuid(),
            Codigo = codigo,
            Nome = nome,
            TipoEvento = tipoEvento,
            TriggerTipo = triggerTipo,
            CronExpression = cronExpression,
            TemplateCodigo = templateCodigo,
            Categoria = categoria,
            Ativa = false,
            EmpresaId = empresaId,
            CriadaEm = agora,
            AtualizadaEm = agora
        };
    }

    public void Ativar(string atualizadaPor)
    {
        Ativa = true;
        AtualizadaPor = atualizadaPor;
        AtualizadaEm = DateTime.UtcNow;
    }

    public void Desativar(string atualizadaPor)
    {
        Ativa = false;
        AtualizadaPor = atualizadaPor;
        AtualizadaEm = DateTime.UtcNow;
    }

    public void DefinirParametros(string parametrosJson, string atualizadaPor)
    {
        ParametrosJson = parametrosJson;
        AtualizadaPor = atualizadaPor;
        AtualizadaEm = DateTime.UtcNow;
    }

    public void DefinirFallback(string canaisOrdemFallbackJson, string atualizadaPor)
    {
        CanaisOrdemFallbackJson = canaisOrdemFallbackJson;
        AtualizadaPor = atualizadaPor;
        AtualizadaEm = DateTime.UtcNow;
    }
}
