namespace EasyStock.Domain.Constants
{
    public static class Features
    {
        public const string ModuloIA            = "ModuloIA";
        public const string ModuloLotes         = "ModuloLotes";
        public const string ReciboPDF           = "ReciboPDF";
        public const string ListasCompras       = "ListasCompras";
        public const string ApiPublica          = "ApiPublica";
        public const string ModoOffline         = "ModoOffline";
        public const string RelatoriosAvancados = "RelatoriosAvancados";

        public static readonly IReadOnlyList<string> Todas = new[]
        {
            ModuloIA, ModuloLotes, ReciboPDF, ListasCompras, ApiPublica, ModoOffline, RelatoriosAvancados
        };

        public static readonly IReadOnlyDictionary<string, string> Descricoes = new Dictionary<string, string>
        {
            [ModuloIA]            = "Geração de anúncios e textos com IA",
            [ModuloLotes]         = "Rastreamento de lotes e alertas de validade",
            [ReciboPDF]           = "Impressão e envio de recibos em PDF",
            [ListasCompras]       = "Geração automática de listas de compras",
            [ApiPublica]          = "Acesso externo via API key",
            [ModoOffline]         = "Sincronização offline no PWA mobile",
            [RelatoriosAvancados] = "Dashboards e relatórios avançados",
        };
    }
}
