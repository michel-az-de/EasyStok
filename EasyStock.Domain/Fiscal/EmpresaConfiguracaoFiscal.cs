using EasyStock.Domain.Integration;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Fiscal;

/// <summary>
/// Configuracao fiscal por empresa (1:1 com <see cref="Empresa"/>). Mantem dados
/// imutaveis durante longas janelas (regime tributario, IE/IM, endereco do
/// emitente) separados do agregado <see cref="Empresa"/> para evitar locks em
/// queries quentes (estoque, pedidos) quando admin atualiza dados fiscais.
///
/// <para>
/// <b>Habilitacao:</b> emissao real so e liberada quando <see cref="Habilitada"/>
/// = true. Chamar <see cref="Habilitar"/> exige IE, endereco, provedor e
/// (em producao) certificado digital configurados — falhar fast evita disparar
/// emissao com config incompleta.
/// </para>
///
/// <para>
/// <b>Numeracao:</b> <see cref="ProximoNumeroNfce"/> e incrementado por
/// <see cref="ReservarProximoNumero"/> sob lock otimista — concorrencia entre
/// dois pedidos simultaneos e detectada via xmin no commit.
/// </para>
/// </summary>
public class EmpresaConfiguracaoFiscal
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }

    public RegimeTributario RegimeTributario { get; set; }
    public string? InscricaoEstadual { get; set; }
    public string? InscricaoMunicipal { get; set; }

    /// <summary>Endereco completo do emitente (snapshot para envio a SEFAZ).</summary>
    public Endereco? Endereco { get; set; }

    public AmbienteIntegracao Ambiente { get; set; } = AmbienteIntegracao.Sandbox;

    /// <summary>
    /// Provedor SEFAZ escolhido. Lowercase: "focus", "enotas", "mock". Mock e
    /// default ate decisao Focus vs eNotas ser tomada — emissao com mock NUNCA
    /// vai a SEFAZ real (apenas devolve resposta sintetica para desenvolvimento).
    /// </summary>
    public string ProvedorPreferido { get; set; } = "mock";

    /// <summary>Serie da NFC-e (default 1). Trocada apenas em casos excepcionais (multi-loja, contingencia).</summary>
    public short SerieNfce { get; set; } = 1;

    /// <summary>Proximo numero a ser reservado por <see cref="ReservarProximoNumero"/>. Inicia em 1.</summary>
    public long ProximoNumeroNfce { get; set; } = 1;

    /// <summary>Identificador sequencial do CSC ativo na SEFAZ (normalmente "1" ou "2").</summary>
    public string? CscId { get; set; }

    /// <summary>Token CSC (Codigo de Seguranca do Contribuinte) fornecido pela SEFAZ. Usado na geracao do QR Code da NFC-e.</summary>
    public string? CscToken { get; set; }

    /// <summary>FK opcional a <see cref="CredencialIntegracao"/> com o certificado digital A1/A3 cifrado (KEK rotacionavel).</summary>
    public Guid? CertificadoCredencialId { get; set; }

    /// <summary>Quando true, emissao real esta liberada. Default false ate config completa + Habilitar() chamado.</summary>
    public bool Habilitada { get; set; }

    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }

    /// <summary>RowVersion (xmin) para optimistic concurrency em ReservarProximoNumero.</summary>
    public uint Versao { get; set; }

    public static EmpresaConfiguracaoFiscal Criar(Guid empresaId, RegimeTributario regime)
    {
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId obrigatorio.", nameof(empresaId));

        var agora = DateTime.UtcNow;
        return new EmpresaConfiguracaoFiscal
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            RegimeTributario = regime,
            Ambiente = AmbienteIntegracao.Sandbox,
            ProvedorPreferido = "mock",
            SerieNfce = 1,
            ProximoNumeroNfce = 1,
            Habilitada = false,
            CriadoEm = agora,
            AlteradoEm = agora,
        };
    }

    public void AtualizarDadosEmitente(string? inscricaoEstadual, string? inscricaoMunicipal, Endereco? endereco)
    {
        InscricaoEstadual = string.IsNullOrWhiteSpace(inscricaoEstadual) ? null : inscricaoEstadual.Trim();
        InscricaoMunicipal = string.IsNullOrWhiteSpace(inscricaoMunicipal) ? null : inscricaoMunicipal.Trim();
        Endereco = endereco;
        AlteradoEm = DateTime.UtcNow;
    }

    public void AlterarAmbiente(AmbienteIntegracao ambiente)
    {
        if (Ambiente == ambiente) return;
        Ambiente = ambiente;
        AlteradoEm = DateTime.UtcNow;
    }

    public void EscolherProvedor(string provedor)
    {
        if (string.IsNullOrWhiteSpace(provedor))
            throw new ArgumentException("Provedor obrigatorio.", nameof(provedor));

        var normalizado = provedor.Trim().ToLowerInvariant();
        if (normalizado is not ("focus" or "enotas" or "mock"))
            throw new RegraDeDominioVioladaException(
                $"Provedor desconhecido: '{provedor}'. Suportados: focus, enotas, mock.");

        if (ProvedorPreferido == normalizado) return;
        ProvedorPreferido = normalizado;
        AlteradoEm = DateTime.UtcNow;
    }

    public void VincularCertificado(Guid? credencialId)
    {
        if (CertificadoCredencialId == credencialId) return;
        CertificadoCredencialId = credencialId;
        AlteradoEm = DateTime.UtcNow;
    }

    /// <summary>
    /// Liga emissao real apos validar invariantes minimos. Producao exige
    /// certificado digital vinculado; sandbox aceita sem (provider mock).
    /// </summary>
    public void Habilitar()
    {
        if (string.IsNullOrWhiteSpace(InscricaoEstadual))
            throw new RegraDeDominioVioladaException("Inscricao Estadual obrigatoria para habilitar emissao.");
        if (Endereco is null || string.IsNullOrWhiteSpace(Endereco.Cep) || string.IsNullOrWhiteSpace(Endereco.Uf))
            throw new RegraDeDominioVioladaException("Endereco completo (CEP+UF) obrigatorio para habilitar emissao.");
        if (Ambiente == AmbienteIntegracao.Production && CertificadoCredencialId is null)
            throw new RegraDeDominioVioladaException("Certificado digital obrigatorio para producao.");
        if (Ambiente == AmbienteIntegracao.Production && ProvedorPreferido == "mock")
            throw new RegraDeDominioVioladaException("Provedor mock nao pode ser usado em producao.");

        if (Habilitada) return;
        Habilitada = true;
        AlteradoEm = DateTime.UtcNow;
    }

    public void Desabilitar()
    {
        if (!Habilitada) return;
        Habilitada = false;
        AlteradoEm = DateTime.UtcNow;
    }

    public void ConfigurarCsc(string cscId, string cscToken)
    {
        if (string.IsNullOrWhiteSpace(cscId))
            throw new ArgumentException("CSC ID obrigatorio.", nameof(cscId));
        if (string.IsNullOrWhiteSpace(cscToken))
            throw new ArgumentException("CSC Token obrigatorio.", nameof(cscToken));

        CscId = cscId.Trim();
        CscToken = cscToken.Trim();
        AlteradoEm = DateTime.UtcNow;
    }

    public void AlterarSerieNfce(short serie)
    {
        if (serie <= 0)
            throw new ArgumentException("Serie deve ser positiva.", nameof(serie));

        if (SerieNfce == serie) return;
        SerieNfce = serie;
        AlteradoEm = DateTime.UtcNow;
    }

    /// <summary>
    /// Reserva e retorna o proximo numero a ser usado em uma NFC-e. Incrementa
    /// o contador. Concorrencia controlada via <see cref="Versao"/> (xmin) —
    /// dois callers simultaneos: o segundo recebe DbUpdateConcurrencyException.
    /// </summary>
    public long ReservarProximoNumero()
    {
        var numero = ProximoNumeroNfce;
        ProximoNumeroNfce = numero + 1;
        AlteradoEm = DateTime.UtcNow;
        return numero;
    }
}
