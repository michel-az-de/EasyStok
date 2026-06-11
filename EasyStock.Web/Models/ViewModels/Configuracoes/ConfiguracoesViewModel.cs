namespace EasyStock.Web.Models.ViewModels.Configuracoes;

public class ConfiguracoesViewModel
{
    public int DiasAlertaValidade { get; set; } = 15;
    public int DiasAlertaParado { get; set; } = 30;
    public int QtyMinPadrao { get; set; } = 5;
    public int QtyCritPadrao { get; set; } = 2;
    public bool NotifEstoqueCritico { get; set; } = true;
    public bool NotifValidade { get; set; } = true;
    public bool NotifParado { get; set; } = true;
    public bool NotifReposicao { get; set; } = true;
    public bool Fifo { get; set; } = true;
    public bool KdsHabilitado { get; set; } = false;
}
