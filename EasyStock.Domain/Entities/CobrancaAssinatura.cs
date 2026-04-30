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

    public Empresa? Empresa { get; set; }
    public AssinaturaEmpresa? Assinatura { get; set; }

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

    public void Expirar() => Status = StatusCobranca.Expirada;
}
