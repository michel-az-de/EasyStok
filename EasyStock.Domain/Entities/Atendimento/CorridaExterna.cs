using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Corrida feita por terceiro (99, Lalamove, Uber) para uma viagem (I9.1, ADR-0061 D5). É o lugar único
/// do custo de corrida: <see cref="Viagem"/>, <see cref="ParadaViagem"/> e <see cref="Entregador"/> não
/// guardam custo. Uma viagem pode ter mais de uma (corrida refeita). O instante vem por parâmetro (UTC).
/// </summary>
public class CorridaExterna
{
    public const int MotoristaTamanhoMaximo = 120;

    /// <summary>R$ 5.000,00: acima disso é erro de digitação, não corrida.</summary>
    public const int CustoMaximoCentavos = 500_000;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid ViagemId { get; private set; }
    public EmpresaEntregador Provedor { get; private set; }
    public OrigemCorridaExterna Origem { get; private set; }
    public int CustoCentavos { get; private set; }
    public string? Motorista { get; private set; }
    public Guid RegistradoPorUsuarioId { get; private set; }
    public DateTime RegistradoEm { get; private set; }
    public Guid? CorrigidoPorUsuarioId { get; private set; }
    public DateTime? CorrigidoEm { get; private set; }

    // EF Core ctor sem parâmetros
    private CorridaExterna() { }

    /// <summary>Registro manual. Aceita viagem montando, em rota ou concluída; desfeita não tem corrida.</summary>
    public static CorridaExterna RegistrarManual(
        Viagem viagem, EmpresaEntregador provedor, int custoCentavos, string? motorista, Guid usuarioId, DateTime agora)
    {
        ArgumentNullException.ThrowIfNull(viagem);
        GarantirViagemComCorrida(viagem);
        if (!Enum.IsDefined(provedor)) throw new RegraDeDominioVioladaException("Provedor da corrida inválido.");
        if (provedor == EmpresaEntregador.Propria)
            throw new RegraDeDominioVioladaException("Entregador próprio não é corrida externa: escolha o provedor que fez a corrida.");
        if (usuarioId == Guid.Empty) throw new RegraDeDominioVioladaException("Quem registrou a corrida é obrigatório.");

        return new CorridaExterna
        {
            Id = Guid.NewGuid(),
            EmpresaId = viagem.EmpresaId,
            ViagemId = viagem.Id,
            Provedor = provedor,
            Origem = OrigemCorridaExterna.Manual,
            CustoCentavos = CustoValido(custoCentavos),
            Motorista = MotoristaValido(motorista),
            RegistradoPorUsuarioId = usuarioId,
            RegistradoEm = Datas.Utc(agora),
        };
    }

    /// <summary>Corrige custo e motorista e guarda quem corrigiu. Quem registrou não muda.</summary>
    public void Corrigir(Viagem viagem, int custoCentavos, string? motorista, Guid usuarioId, DateTime agora)
    {
        ArgumentNullException.ThrowIfNull(viagem);
        if (viagem.Id != ViagemId) throw new RegraDeDominioVioladaException("Corrida de outra viagem.");
        GarantirViagemComCorrida(viagem);
        if (usuarioId == Guid.Empty) throw new RegraDeDominioVioladaException("Quem corrigiu a corrida é obrigatório.");

        var custo = CustoValido(custoCentavos);
        var nome = MotoristaValido(motorista);
        CustoCentavos = custo;
        Motorista = nome;
        CorrigidoPorUsuarioId = usuarioId;
        CorrigidoEm = Datas.Utc(agora);
    }

    private static void GarantirViagemComCorrida(Viagem viagem)
    {
        if (viagem.Situacao == SituacaoViagem.Desfeita)
            throw new RegraDeDominioVioladaException("Viagem desfeita não tem corrida.");
    }

    private static int CustoValido(int custoCentavos)
    {
        if (custoCentavos < 0) throw new RegraDeDominioVioladaException("O custo da corrida não pode ser negativo.");
        if (custoCentavos > CustoMaximoCentavos) throw new RegraDeDominioVioladaException("Custo da corrida acima de R$ 5.000,00. Confira o valor.");
        return custoCentavos;
    }

    private static string? MotoristaValido(string? motorista)
    {
        if (string.IsNullOrWhiteSpace(motorista)) return null;
        var limpo = motorista.Trim();
        if (limpo.Length > MotoristaTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"Motorista acima de {MotoristaTamanhoMaximo} caracteres.");
        return limpo;
    }
}