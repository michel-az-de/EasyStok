namespace EasyStock.Api.Configuration
{
    public sealed class EasyStockConfiguracoes
    {
        public int LimiteEstoqueBaixoDefault { get; set; } = 5;
        public int DiasAlertaVencimento { get; set; } = 15;
        public int DiasItemParado { get; set; } = 30;
        public bool NotifEstoqueCritico { get; set; } = true;
        public bool NotifValidade { get; set; } = true;
        public bool NotifParado { get; set; } = true;
        public bool NotifReposicao { get; set; } = true;
        public bool Fifo { get; set; } = true;
        public string Moeda { get; set; } = "BRL";
        public string Timezone { get; set; } = "America/Sao_Paulo";
    }
}
