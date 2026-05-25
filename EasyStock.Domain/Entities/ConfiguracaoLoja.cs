using EasyStock.Domain.Defaults;

namespace EasyStock.Domain.Entities;

public class ConfiguracaoLoja
{
    public Guid Id { get; set; }
    public Guid LojaId { get; set; }
    public int DiasAlertaValidade { get; set; } = OperacionalDefaults.DiasAlertaValidade;
    public int DiasAlertaParado { get; set; } = OperacionalDefaults.DiasAlertaParado;
    public int QuantidadeMinimaPadrao { get; set; } = OperacionalDefaults.QuantidadeMinima;
    public int QuantidadeCriticaPadrao { get; set; } = OperacionalDefaults.QuantidadeCritica;
    public bool NotificarEstoqueCritico { get; set; } = true;
    public bool NotificarValidade { get; set; } = true;
    public bool NotificarParado { get; set; } = true;
    public bool NotificarReposicao { get; set; } = true;
    public bool FifoAtivo { get; set; } = true;
    public string Moeda { get; set; } = OperacionalDefaults.Moeda;
    public string Timezone { get; set; } = OperacionalDefaults.Timezone;

    // Geracao automatica financeiro (CAP/CAR) — opt-in por loja
    public bool GerarContaReceberAutomaticaDePedido { get; set; } = false;
    public bool GerarContaPagarAutomaticaDePedidoFornecedor { get; set; } = false;
    public string StatusPedidoQueGeraContaReceber { get; set; } = "entregue";

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
        string? timezone,
        int? quantidadeCriticaPadrao = null,
        bool? gerarContaReceberAutomaticaDePedido = null,
        bool? gerarContaPagarAutomaticaDePedidoFornecedor = null,
        string? statusPedidoQueGeraContaReceber = null)
    {
        if (diasAlertaValidade.HasValue) DiasAlertaValidade = diasAlertaValidade.Value;
        if (diasAlertaParado.HasValue) DiasAlertaParado = diasAlertaParado.Value;
        if (quantidadeMinimaPadrao.HasValue) QuantidadeMinimaPadrao = quantidadeMinimaPadrao.Value;
        if (quantidadeCriticaPadrao.HasValue) QuantidadeCriticaPadrao = quantidadeCriticaPadrao.Value;
        if (notificarEstoqueCritico.HasValue) NotificarEstoqueCritico = notificarEstoqueCritico.Value;
        if (notificarValidade.HasValue) NotificarValidade = notificarValidade.Value;
        if (notificarParado.HasValue) NotificarParado = notificarParado.Value;
        if (notificarReposicao.HasValue) NotificarReposicao = notificarReposicao.Value;
        if (fifoAtivo.HasValue) FifoAtivo = fifoAtivo.Value;
        if (!string.IsNullOrWhiteSpace(moeda)) Moeda = moeda.Trim();
        if (!string.IsNullOrWhiteSpace(timezone)) Timezone = timezone.Trim();
        if (gerarContaReceberAutomaticaDePedido.HasValue) GerarContaReceberAutomaticaDePedido = gerarContaReceberAutomaticaDePedido.Value;
        if (gerarContaPagarAutomaticaDePedidoFornecedor.HasValue) GerarContaPagarAutomaticaDePedidoFornecedor = gerarContaPagarAutomaticaDePedidoFornecedor.Value;
        if (!string.IsNullOrWhiteSpace(statusPedidoQueGeraContaReceber)) StatusPedidoQueGeraContaReceber = statusPedidoQueGeraContaReceber.Trim();
        AlteradoEm = DateTime.UtcNow;
    }

    public void ResetarPadrao()
    {
        DiasAlertaValidade = OperacionalDefaults.DiasAlertaValidade;
        DiasAlertaParado = OperacionalDefaults.DiasAlertaParado;
        QuantidadeMinimaPadrao = OperacionalDefaults.QuantidadeMinima;
        QuantidadeCriticaPadrao = OperacionalDefaults.QuantidadeCritica;
        NotificarEstoqueCritico = true;
        NotificarValidade = true;
        NotificarParado = true;
        NotificarReposicao = true;
        FifoAtivo = true;
        Moeda = OperacionalDefaults.Moeda;
        Timezone = OperacionalDefaults.Timezone;
        GerarContaReceberAutomaticaDePedido = false;
        GerarContaPagarAutomaticaDePedidoFornecedor = false;
        StatusPedidoQueGeraContaReceber = "entregue";
        AlteradoEm = DateTime.UtcNow;
    }
}
