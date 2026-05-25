using EasyStock.Domain.Defaults;
using EasyStock.Application.Configuration;

namespace EasyStock.Api.Configuration
{
    public sealed class EasyStockConfiguracoes : IEasyStockConfiguracoes
    {
        public int LimiteEstoqueBaixoDefault { get; set; } = OperacionalDefaults.QuantidadeMinima;
        public int DiasAlertaVencimento { get; set; } = OperacionalDefaults.DiasAlertaValidade;
        public int DiasItemParado { get; set; } = OperacionalDefaults.DiasAlertaParado;
        public bool NotifEstoqueCritico { get; set; } = true;
        public bool NotifValidade { get; set; } = true;
        public bool NotifParado { get; set; } = true;
        public bool NotifReposicao { get; set; } = true;
        public bool Fifo { get; set; } = true;
        public string Moeda { get; set; } = OperacionalDefaults.Moeda;
        public string Timezone { get; set; } = OperacionalDefaults.Timezone;
    }
}
