using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Domain.ValueObjects
{
    /// <summary>
    /// Ficha técnica extraída de Produto.AtributosJson.
    /// TryParse é tolerante: campos ausentes/inválidos geram warnings, não exceção.
    /// </summary>
    public sealed record ProdutoFichaTecnica
    {
        public decimal? PorcaoG { get; init; }
        public decimal? Kcal { get; init; }
        public decimal? CarbsG { get; init; }
        public decimal? ProteinaG { get; init; }
        public decimal? GorduraG { get; init; }
        public decimal? GorduraSaturadaG { get; init; }
        public decimal? FibrasG { get; init; }
        public decimal? SodioMg { get; init; }

        public string? ModoPreparo { get; init; }
        public IReadOnlyList<string> Ingredientes { get; init; } = [];
        public IReadOnlyList<string> Alergenos { get; init; } = [];
        public string? AlergenosOutros { get; init; }

        public static (ProdutoFichaTecnica? ficha, IReadOnlyList<string> warnings) TryParse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return (null, []);

            var warnings = new List<string>();

            try
            {
                var doc = JsonSerializer.Deserialize<FichaTecnicaRaw>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (doc is null)
                    return (null, ["AtributosJson inválido."]);

                var nutri = doc.Nutricional;
                var ficha = new ProdutoFichaTecnica
                {
                    PorcaoG          = nutri?.PorcaoG,
                    Kcal             = nutri?.Kcal,
                    CarbsG           = nutri?.CarbsG,
                    ProteinaG        = nutri?.ProteinaG,
                    GorduraG         = nutri?.GorduraG,
                    GorduraSaturadaG = nutri?.GorduraSaturadaG,
                    FibrasG          = nutri?.FibrasG,
                    SodioMg          = nutri?.SodioMg,
                    ModoPreparo      = doc.ModoPreparo,
                    Ingredientes     = doc.Ingredientes ?? [],
                    Alergenos        = doc.Alergenos ?? [],
                    AlergenosOutros  = doc.AlergenosOutros,
                };

                return (ficha, warnings);
            }
            catch (JsonException ex)
            {
                warnings.Add($"Erro ao ler ficha técnica: {ex.Message}");
                return (null, warnings);
            }
        }

        public string ToJson() => JsonSerializer.Serialize(new FichaTecnicaRaw
        {
            Nutricional = new NutricionalRaw
            {
                PorcaoG          = PorcaoG,
                Kcal             = Kcal,
                CarbsG           = CarbsG,
                ProteinaG        = ProteinaG,
                GorduraG         = GorduraG,
                GorduraSaturadaG = GorduraSaturadaG,
                FibrasG          = FibrasG,
                SodioMg          = SodioMg,
            },
            ModoPreparo     = ModoPreparo,
            Ingredientes    = new List<string>(Ingredientes),
            Alergenos       = new List<string>(Alergenos),
            AlergenosOutros = AlergenosOutros,
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false });

        // Raw JSON shapes
        private sealed class FichaTecnicaRaw
        {
            [JsonPropertyName("nutricional")] public NutricionalRaw? Nutricional { get; set; }
            [JsonPropertyName("modo_preparo")] public string? ModoPreparo { get; set; }
            [JsonPropertyName("ingredientes")] public List<string>? Ingredientes { get; set; }
            [JsonPropertyName("alergenos")] public List<string>? Alergenos { get; set; }
            [JsonPropertyName("alergenos_outros")] public string? AlergenosOutros { get; set; }
        }

        private sealed class NutricionalRaw
        {
            [JsonPropertyName("porcao_g")] public decimal? PorcaoG { get; set; }
            [JsonPropertyName("kcal")] public decimal? Kcal { get; set; }
            [JsonPropertyName("carbs_g")] public decimal? CarbsG { get; set; }
            [JsonPropertyName("proteina_g")] public decimal? ProteinaG { get; set; }
            [JsonPropertyName("gordura_g")] public decimal? GorduraG { get; set; }
            [JsonPropertyName("gordura_saturada_g")] public decimal? GorduraSaturadaG { get; set; }
            [JsonPropertyName("fibras_g")] public decimal? FibrasG { get; set; }
            [JsonPropertyName("sodio_mg")] public decimal? SodioMg { get; set; }
        }
    }
}
