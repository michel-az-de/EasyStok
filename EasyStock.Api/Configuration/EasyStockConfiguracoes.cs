namespace EasyStock.Api.Configuration
{
    public sealed class EasyStockConfiguracoes
    {
        public int LimiteEstoqueBaixoDefault { get; set; } = 5;
        public int DiasAlertaVencimento { get; set; } = 30;
        public int DiasItemParado { get; set; } = 90;
    }
}
