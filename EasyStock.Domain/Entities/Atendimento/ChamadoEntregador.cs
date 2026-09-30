using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>Chamado de entregador (S44): pedido em texto livre por um entregador, com situação.</summary>
public class ChamadoEntregador
{
    public const int TextoTamanhoMaximo = 500;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid? ViagemId { get; private set; }
    public string Texto { get; private set; } = null!;
    public SituacaoChamadoEntregador Situacao { get; private set; }
    public DateTime AbertoEm { get; private set; }
    public DateTime? AtendidoEm { get; private set; }
    public DateTime? CanceladoEm { get; private set; }

    // EF Core ctor sem parâmetros
    private ChamadoEntregador() { }

    public static ChamadoEntregador Abrir(Guid empresaId, string texto, Guid? viagemId, DateTime agora)
    {
        if (empresaId == Guid.Empty) throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");
        if (string.IsNullOrWhiteSpace(texto)) throw new RegraDeDominioVioladaException("Texto do chamado é obrigatório.");
        var limpo = texto.Trim();
        if (limpo.Length > TextoTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"Texto do chamado acima de {TextoTamanhoMaximo} caracteres.");
        return new ChamadoEntregador
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ViagemId = viagemId == Guid.Empty ? null : viagemId,
            Texto = limpo,
            Situacao = SituacaoChamadoEntregador.Aberto,
            AbertoEm = Datas.Utc(agora),
        };
    }

    /// <summary>Idempotente.</summary>
    public void Atender(DateTime agora)
    {
        if (Situacao != SituacaoChamadoEntregador.Aberto) return;
        Situacao = SituacaoChamadoEntregador.Atendido;
        AtendidoEm = Datas.Utc(agora);
    }

    /// <summary>Idempotente.</summary>
    public void Cancelar(DateTime agora)
    {
        if (Situacao != SituacaoChamadoEntregador.Aberto) return;
        Situacao = SituacaoChamadoEntregador.Cancelado;
        CanceladoEm = Datas.Utc(agora);
    }
}
