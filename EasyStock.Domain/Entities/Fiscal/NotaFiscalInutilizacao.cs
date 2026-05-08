using EasyStock.Domain.Enums.Fiscal;

namespace EasyStock.Domain.Entities.Fiscal;

/// <summary>
/// Inutilização formal de uma faixa de numeração fiscal não utilizada
/// (buracos por falha pós-reserva, ADR-004). Obrigatório executar
/// mensalmente quando houver buracos detectados.
/// </summary>
public sealed class NotaFiscalInutilizacao
{
    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid LojaId { get; private set; }
    public ModeloDocumentoFiscal Modelo { get; private set; }
    public int Serie { get; private set; }
    public int NumeroInicial { get; private set; }
    public int NumeroFinal { get; private set; }
    public int Ano { get; private set; }
    public string Justificativa { get; private set; } = null!;
    public string? ProtocoloInutilizacao { get; private set; }
    public StatusInutilizacao Status { get; private set; }
    public string? XmlInutilizacao { get; private set; }
    public string? MotivoRejeicao { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AlteradoEm { get; private set; }

    private NotaFiscalInutilizacao() { }

    public static NotaFiscalInutilizacao Criar(
        Guid empresaId,
        Guid lojaId,
        ModeloDocumentoFiscal modelo,
        int serie,
        int numeroInicial,
        int numeroFinal,
        int ano,
        string justificativa)
    {
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId é obrigatório.", nameof(empresaId));
        if (lojaId == Guid.Empty)
            throw new ArgumentException("LojaId é obrigatório.", nameof(lojaId));
        if (serie <= 0)
            throw new ArgumentOutOfRangeException(nameof(serie));
        if (numeroInicial <= 0 || numeroFinal < numeroInicial)
            throw new ArgumentException("Faixa de números inválida.", nameof(numeroInicial));
        if (numeroFinal > 999_999_999)
            throw new ArgumentOutOfRangeException(nameof(numeroFinal));
        if (ano < 2000 || ano > 9999)
            throw new ArgumentOutOfRangeException(nameof(ano), "Ano (4 dígitos) inválido.");
        if (string.IsNullOrWhiteSpace(justificativa) || justificativa.Length is < 15 or > 255)
            throw new ArgumentException("Justificativa deve ter entre 15 e 255 caracteres.", nameof(justificativa));

        var agora = DateTime.UtcNow;
        return new NotaFiscalInutilizacao
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            LojaId = lojaId,
            Modelo = modelo,
            Serie = serie,
            NumeroInicial = numeroInicial,
            NumeroFinal = numeroFinal,
            Ano = ano,
            Justificativa = justificativa.Trim(),
            Status = StatusInutilizacao.EmAndamento,
            CriadoEm = agora,
            AlteradoEm = agora,
        };
    }

    public void MarcarAutorizada(string protocolo, string xmlEvento)
    {
        if (Status != StatusInutilizacao.EmAndamento)
            throw new InvalidOperationException("Inutilização não está em andamento.");
        if (string.IsNullOrWhiteSpace(protocolo))
            throw new ArgumentException("Protocolo é obrigatório.", nameof(protocolo));
        if (string.IsNullOrWhiteSpace(xmlEvento))
            throw new ArgumentException("XML é obrigatório.", nameof(xmlEvento));

        Status = StatusInutilizacao.Autorizada;
        ProtocoloInutilizacao = protocolo;
        XmlInutilizacao = xmlEvento;
        AlteradoEm = DateTime.UtcNow;
    }

    public void MarcarRejeitada(string motivo)
    {
        if (Status != StatusInutilizacao.EmAndamento)
            throw new InvalidOperationException("Inutilização não está em andamento.");

        Status = StatusInutilizacao.Rejeitada;
        MotivoRejeicao = motivo;
        AlteradoEm = DateTime.UtcNow;
    }
}
