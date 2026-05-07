using EasyStock.Domain.Enums;

namespace EasyStock.Domain.Entities;

public class CobrancaAssinatura
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid AssinaturaId { get; set; }
    public string Txid { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public string PixCopiaCola { get; set; } = string.Empty;
    public string QrCodeBase64 { get; set; } = string.Empty;
    public StatusCobranca Status { get; set; } = StatusCobranca.Pendente;
    public DateTime CriadoEm { get; set; }
    public DateTime ExpiracaoEm { get; set; }
    public DateTime? PagoEm { get; set; }

    public string? MetodoPagamento { get; set; } // "Pix" | "Boleto" | "Cartao"
    public string? BoletoUrl { get; set; }
    public string? BoletoCodigo { get; set; }
    public int TentativasLembrete { get; set; }
    public DateTime? UltimoLembreteEm { get; set; }

    /// <summary>
    /// Link opcional para a <see cref="Fatura"/> agnostica criada junto com
    /// esta cobranca (a partir da convivencia introduzida em F5). Null em
    /// cobrancas legadas anteriores ao backfill.
    /// </summary>
    public Guid? FaturaId { get; set; }

    public Empresa? Empresa { get; set; }
    public AssinaturaEmpresa? Assinatura { get; set; }
    public Fatura? Fatura { get; set; }

    public static CobrancaAssinatura Criar(
        Guid empresaId,
        Guid assinaturaId,
        string txid,
        decimal valor,
        string pixCopiaCola,
        string qrCodeBase64,
        DateTime expiracaoEm)
    {
        var agora = DateTime.UtcNow;
        return new CobrancaAssinatura
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            AssinaturaId = assinaturaId,
            Txid = txid,
            Valor = valor,
            PixCopiaCola = pixCopiaCola,
            QrCodeBase64 = qrCodeBase64,
            Status = StatusCobranca.Pendente,
            CriadoEm = agora,
            ExpiracaoEm = expiracaoEm
        };
    }

    public void MarcarComoPaga()
    {
        Status = StatusCobranca.Paga;
        PagoEm = DateTime.UtcNow;
    }

    public void MarcarComoFalhada() => Status = StatusCobranca.Falhada;

    public void AtualizarDadosPix(string pixCopiaCola, string qrCodeBase64, DateTime expiracaoEm)
    {
        PixCopiaCola = pixCopiaCola;
        QrCodeBase64 = qrCodeBase64;
        ExpiracaoEm = expiracaoEm;
    }

    public void Expirar() => Status = StatusCobranca.Expirada;

    public void RegistrarLembrete()
    {
        TentativasLembrete++;
        UltimoLembreteEm = DateTime.UtcNow;
    }

    public void AtualizarDadosBoleto(string boletoCodigo, string? boletoUrl)
    {
        MetodoPagamento = "Boleto";
        BoletoCodigo = boletoCodigo;
        BoletoUrl = boletoUrl;
    }
}
