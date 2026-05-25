using System.Text.Json.Serialization;

namespace EasyStock.Web.Models.Api;

public record Configuracoes
{
    [JsonPropertyName("diasAlertaValidade")]
    public int DiasAlertaValidade { get; init; } = 15;

    [JsonPropertyName("diasAlertaParado")]
    public int DiasAlertaParado { get; init; } = 30;

    [JsonPropertyName("quantidadeMinimaPadrao")]
    public int QtyMinPadrao { get; init; } = 5;

    [JsonPropertyName("quantidadeCriticaPadrao")]
    public int QtyCritPadrao { get; init; } = 2;

    [JsonPropertyName("notificarEstoqueCritico")]
    public bool NotifEstoqueCritico { get; init; } = true;

    [JsonPropertyName("notificarValidade")]
    public bool NotifValidade { get; init; } = true;

    [JsonPropertyName("notificarParado")]
    public bool NotifParado { get; init; } = true;

    [JsonPropertyName("notificarReposicao")]
    public bool NotifReposicao { get; init; } = true;

    [JsonPropertyName("fifoAtivo")]
    public bool Fifo { get; init; } = true;
}
