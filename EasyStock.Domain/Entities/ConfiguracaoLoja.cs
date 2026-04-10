using EasyStock.Domain.Defaults;

namespace EasyStock.Domain.Entities;

public class ConfiguracaoLoja
{
    public Guid Id { get; set; }
    public Guid LojaId { get; set; }
    public int DiasAlertaValidade { get; set; } = OperacionalDefaults.DiasAlertaValidade;
    public int DiasAlertaParado { get; set; } = OperacionalDefaults.DiasAlertaParado;
    public int QuantidadeMinimaPadrao { get; set; } = OperacionalDefaults.QuantidadeMinima;
    public bool NotificarEstoqueCritico { get; set; } = true;
    public bool NotificarValidade { get; set; } = true;
    public bool NotificarParado { get; set; } = true;
    public bool NotificarReposicao { get; set; } = true;
    public bool FifoAtivo { get; set; } = true;
    public string Moeda { get; set; } = OperacionalDefaults.Moeda;
    public string Timezone { get; set; } = OperacionalDefaults.Timezone;
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
        DiasAlertaValidade = OperacionalDefaults.DiasAlertaValidade;
        DiasAlertaParado = OperacionalDefaults.DiasAlertaParado;
        QuantidadeMinimaPadrao = OperacionalDefaults.QuantidadeMinima;
        NotificarEstoqueCritico = true;
        NotificarValidade = true;
        NotificarParado = true;
        NotificarReposicao = true;
        FifoAtivo = true;
        Moeda = OperacionalDefaults.Moeda;
        Timezone = OperacionalDefaults.Timezone;
        AlteradoEm = DateTime.UtcNow;
    }
}
