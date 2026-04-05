namespace EasyStock.Web.Models.Api;

public record Configuracoes
{
    public int DiasAlertaValidade { get; init; } = 15;
    public int DiasAlertaParado { get; init; } = 30;
    public int QtyMinPadrao { get; init; } = 5;
    public bool NotifEstoqueCritico { get; init; } = true;
    public bool NotifValidade { get; init; } = true;
    public bool NotifParado { get; init; } = true;
    public bool NotifReposicao { get; init; } = true;
    public bool Fifo { get; init; } = true;
}
