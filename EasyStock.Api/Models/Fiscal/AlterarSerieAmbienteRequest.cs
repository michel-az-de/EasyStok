using EasyStock.Domain.Integration;

namespace EasyStock.Api.Models.Fiscal;

public sealed class AlterarSerieAmbienteRequest
{
    public AmbienteIntegracao? Ambiente { get; set; }
    public short? SerieNfce { get; set; }
}
