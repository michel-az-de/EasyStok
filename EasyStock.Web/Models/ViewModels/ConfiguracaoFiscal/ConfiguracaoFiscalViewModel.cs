namespace EasyStock.Web.Models.ViewModels.ConfiguracaoFiscal;

/// <summary>
/// Espelha o response do GET /api/configuracao-fiscal. Usado pela tela
/// /configuracao-fiscal para renderizar status + formularios inline.
/// </summary>
public class ConfiguracaoFiscalViewModel
{
    public bool Configurado { get; set; }
    public bool Habilitada { get; set; }
    public string? Ambiente { get; set; }
    public string? RegimeTributario { get; set; }
    public string? Provedor { get; set; }
    public short SerieNfce { get; set; }
    public long ProximoNumeroNfce { get; set; }

    public string? InscricaoEstadual { get; set; }
    public string? InscricaoMunicipal { get; set; }
    public EnderecoFiscalDto? Endereco { get; set; }

    public bool TemCsc { get; set; }
    public string? CscId { get; set; }

    public CertificadoDto? Certificado { get; set; }

    public bool IsMock => string.Equals(Provedor, "mock", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Indica se a config tem o minimo necessario para chamar Habilitar() sem erro
    /// (IE + Endereco com CEP + UF). Cert e cobrado apenas em Production.
    /// </summary>
    public bool PodeHabilitar =>
        !string.IsNullOrWhiteSpace(InscricaoEstadual)
        && Endereco is not null
        && !string.IsNullOrWhiteSpace(Endereco.Cep)
        && !string.IsNullOrWhiteSpace(Endereco.Uf);
}

public class EnderecoFiscalDto
{
    public string? Logradouro { get; set; }
    public string? Numero { get; set; }
    public string? Complemento { get; set; }
    public string? Bairro { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    public string? Cep { get; set; }
}

public class CertificadoDto
{
    public Guid CredencialId { get; set; }
    public bool Ativo { get; set; }
    public DateTime? ValidoAte { get; set; }
    public int? DiasParaExpirar { get; set; }
}
