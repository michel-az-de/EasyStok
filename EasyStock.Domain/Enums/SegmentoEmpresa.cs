namespace EasyStock.Domain.Enums
{
    public static class SegmentoEmpresa
    {
        public const string Restaurante = "restaurante";
        public const string Varejo = "varejo";
        public const string Distribuidora = "distribuidora";
        public const string Outro = "outro";

        public static readonly string[] Validos = { Restaurante, Varejo, Distribuidora, Outro };

        public static bool EhValido(string? valor) =>
            !string.IsNullOrWhiteSpace(valor) && Validos.Contains(valor.Trim().ToLowerInvariant());
    }
}
