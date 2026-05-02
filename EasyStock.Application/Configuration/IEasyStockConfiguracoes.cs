namespace EasyStock.Application.Configuration;

public interface IEasyStockConfiguracoes
{
    int LimiteEstoqueBaixoDefault { get; }
    int DiasAlertaVencimento { get; }
    int DiasItemParado { get; }
    bool NotifEstoqueCritico { get; }
    bool NotifValidade { get; }
    bool NotifParado { get; }
    bool NotifReposicao { get; }
    bool Fifo { get; }
    string Moeda { get; }
    string Timezone { get; }
}
