namespace EasyStock.Domain.Entities;

public class ConfiguracaoLoja
{
    public Guid Id { get; set; }
    public Guid LojaId { get; set; }
    public int DiasAlertaValidade { get; set; } = 15;
    public int DiasAlertaParado { get; set; } = 30;
    public int QuantidadeMinimaPadrao { get; set; } = 5;
    public bool NotificarEstoqueCritico { get; set; } = true;
    public bool NotificarValidade { get; set; } = true;
    public bool NotificarParado { get; set; } = true;
    public bool NotificarReposicao { get; set; } = true;
    public bool FifoAtivo { get; set; } = true;
    public string Moeda { get; set; } = "BRL";
    public string Timezone { get; set; } = "America/Sao_Paulo";
    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }

    public Loja? Loja { get; set; }

    public static ConfiguracaoLoja CriarPadrao(Guid lojaId)
    {
        var agora = DateTime.UtcNow;
        return new ConfiguracaoLoja
        {
            Id = Guid.NewGuid(),
            LojaId = lojaId,
            CriadoEm = agora,
            AlteradoEm = agora
        };
    }

    public void Atualizar(
        int? diasAlertaValidade,
        int? diasAlertaParado,
        int? quantidadeMinimaPadrao,
        bool? notificarEstoqueCritico,
        bool? notificarValidade,
        bool? notificarParado,
        bool? notificarReposicao,
        bool? fifoAtivo,
        string? moeda,
        string? timezone)
    {
        if (diasAlertaValidade.HasValue) DiasAlertaValidade = diasAlertaValidade.Value;
        if (diasAlertaParado.HasValue) DiasAlertaParado = diasAlertaParado.Value;
        if (quantidadeMinimaPadrao.HasValue) QuantidadeMinimaPadrao = quantidadeMinimaPadrao.Value;
        if (notificarEstoqueCritico.HasValue) NotificarEstoqueCritico = notificarEstoqueCritico.Value;
        if (notificarValidade.HasValue) NotificarValidade = notificarValidade.Value;
        if (notificarParado.HasValue) NotificarParado = notificarParado.Value;
        if (notificarReposicao.HasValue) NotificarReposicao = notificarReposicao.Value;
        if (fifoAtivo.HasValue) FifoAtivo = fifoAtivo.Value;
        if (!string.IsNullOrWhiteSpace(moeda)) Moeda = moeda.Trim();
        if (!string.IsNullOrWhiteSpace(timezone)) Timezone = timezone.Trim();
        AlteradoEm = DateTime.UtcNow;
    }

    public void ResetarPadrao()
    {
        DiasAlertaValidade = 15;
        DiasAlertaParado = 30;
        QuantidadeMinimaPadrao = 5;
        NotificarEstoqueCritico = true;
        NotificarValidade = true;
        NotificarParado = true;
        NotificarReposicao = true;
        FifoAtivo = true;
        Moeda = "BRL";
        Timezone = "America/Sao_Paulo";
        AlteradoEm = DateTime.UtcNow;
    }
}
