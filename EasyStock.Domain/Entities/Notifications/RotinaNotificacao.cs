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

    public void DefinirCronExpression(string cronExpression, string atualizadaPor)
    {
        CronExpression = cronExpression;
        AtualizadaPor = atualizadaPor;
        AtualizadaEm = DateTime.UtcNow;
    }

    public void DefinirFallback(string canaisOrdemFallbackJson, string atualizadaPor)
    {
        CanaisOrdemFallbackJson = canaisOrdemFallbackJson;
        AtualizadaPor = atualizadaPor;
        AtualizadaEm = DateTime.UtcNow;
    }

    /// <summary>
    /// Janela diária de envio (N13). Fora dela o dispatcher adia a mensagem; as duas pontas são obrigatórias e
    /// <paramref name="inicio"/> precisa ser diferente de <paramref name="fim"/>.
    /// </summary>
    public void DefinirJanela(TimeOnly inicio, TimeOnly fim)
    {
        if (inicio == fim)
            throw new ArgumentException("A janela precisa de início diferente do fim.", nameof(fim));

        JanelaInicio = inicio;
        JanelaFim = fim;
        AtualizadaEm = DateTime.UtcNow;
    }

    /// <summary>A rotina é do sistema, isto é, nenhuma pessoa a alterou desde o seed (N13)?</summary>
    public bool EhDoSistema => AtualizadaPor == "system";

    /// <summary>A rotina já tem o conteúdo da entrada do catálogo (N13)? Não compara <c>Ativa</c>: quem liga ou desliga é a pessoa.</summary>
    public bool EquivaleAoCatalogo(RotinaNotificacao catalogo) =>
        Nome == catalogo.Nome
        && TipoEvento == catalogo.TipoEvento
        && TemplateCodigo == catalogo.TemplateCodigo
        && Categoria == catalogo.Categoria
        && CanaisOrdemFallbackJson == catalogo.CanaisOrdemFallbackJson
        && ParametrosJson == catalogo.ParametrosJson
        && JanelaInicio == catalogo.JanelaInicio
        && JanelaFim == catalogo.JanelaFim;

    /// <summary>Copia o conteúdo da entrada do catálogo (N13), sem tocar em <c>Ativa</c>. Fica como atualização do sistema.</summary>
    public void AplicarCatalogo(RotinaNotificacao catalogo)
    {
        Nome = catalogo.Nome;
        TipoEvento = catalogo.TipoEvento;
        TemplateCodigo = catalogo.TemplateCodigo;
        Categoria = catalogo.Categoria;
        CanaisOrdemFallbackJson = catalogo.CanaisOrdemFallbackJson;
        ParametrosJson = catalogo.ParametrosJson;
        JanelaInicio = catalogo.JanelaInicio;
        JanelaFim = catalogo.JanelaFim;
        AtualizadaPor = "system";
        AtualizadaEm = DateTime.UtcNow;
    }
}
