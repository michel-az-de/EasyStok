namespace EasyStock.Domain.Entities.Fiscal;

/// <summary>
/// Certificado Digital A1 (.pfx + senha) cifrado por empresa. Usado pelo
/// gateway fiscal pra assinar XMLs (ADR-005). Cifragem AES-256-GCM com
/// KEK gerenciada por Azure Key Vault. Apenas um certificado por empresa
/// pode estar ativo de cada vez.
/// </summary>
public sealed class NotaFiscalCertificadoA1
{
    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public byte[] PfxCifrado { get; private set; } = Array.Empty<byte>();
    public byte[] SenhaCifrada { get; private set; } = Array.Empty<byte>();
    public byte[] Iv { get; private set; } = Array.Empty<byte>();
    public byte[] Tag { get; private set; } = Array.Empty<byte>();
    public string KekId { get; private set; } = null!;
    public string NomeTitular { get; private set; } = null!;
    public string DocumentoTitular { get; private set; } = null!;
    public DateTime ValidoDe { get; private set; }
    public DateTime ValidoAte { get; private set; }
    public bool Ativo { get; private set; }
    public Guid CriadoPorUsuarioId { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AlteradoEm { get; private set; }

    private NotaFiscalCertificadoA1() { }

    public static NotaFiscalCertificadoA1 Criar(
        Guid empresaId,
        byte[] pfxCifrado,
        byte[] senhaCifrada,
        byte[] iv,
        byte[] tag,
        string kekId,
        string nomeTitular,
        string documentoTitular,
        DateTime validoDe,
        DateTime validoAte,
        Guid criadoPorUsuarioId)
    {
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId é obrigatório.", nameof(empresaId));
        if (pfxCifrado is null || pfxCifrado.Length == 0)
            throw new ArgumentException("PFX cifrado é obrigatório.", nameof(pfxCifrado));
        if (senhaCifrada is null || senhaCifrada.Length == 0)
            throw new ArgumentException("Senha cifrada é obrigatória.", nameof(senhaCifrada));
        if (iv is null || iv.Length == 0)
            throw new ArgumentException("IV é obrigatório.", nameof(iv));
        if (tag is null || tag.Length == 0)
            throw new ArgumentException("Tag GCM é obrigatória.", nameof(tag));
        if (string.IsNullOrWhiteSpace(kekId))
            throw new ArgumentException("KekId é obrigatório.", nameof(kekId));
        if (string.IsNullOrWhiteSpace(nomeTitular))
            throw new ArgumentException("Nome do titular é obrigatório.", nameof(nomeTitular));
        if (string.IsNullOrWhiteSpace(documentoTitular))
            throw new ArgumentException("Documento do titular é obrigatório.", nameof(documentoTitular));
        if (validoAte <= validoDe)
            throw new ArgumentException("ValidoAte deve ser maior que ValidoDe.", nameof(validoAte));

        var agora = DateTime.UtcNow;
        return new NotaFiscalCertificadoA1
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            PfxCifrado = pfxCifrado,
            SenhaCifrada = senhaCifrada,
            Iv = iv,
            Tag = tag,
            KekId = kekId,
            NomeTitular = nomeTitular.Trim(),
            DocumentoTitular = new string(documentoTitular.Trim()),
            ValidoDe = validoDe,
            ValidoAte = validoAte,
            Ativo = true,
            CriadoPorUsuarioId = criadoPorUsuarioId,
            CriadoEm = agora,
            AlteradoEm = agora,
        };
    }

    public bool ExpiradoOu30Dias(DateTime now)
        => ValidoAte <= now || ValidoAte <= now.AddDays(30);

    public bool Expirado(DateTime now) => ValidoAte <= now;

    public void Desativar()
    {
        if (!Ativo) return;
        Ativo = false;
        AlteradoEm = DateTime.UtcNow;
    }

    public void RotacionarKek(byte[] novoPfx, byte[] novaSenha, string novoKekId, byte[] novoIv, byte[] novaTag)
    {
        if (novoPfx is null || novoPfx.Length == 0)
            throw new ArgumentException("PFX cifrado é obrigatório.", nameof(novoPfx));
        if (novaSenha is null || novaSenha.Length == 0)
            throw new ArgumentException("Senha cifrada é obrigatória.", nameof(novaSenha));
        if (string.IsNullOrWhiteSpace(novoKekId))
            throw new ArgumentException("KekId é obrigatório.", nameof(novoKekId));
        if (novoIv is null || novoIv.Length == 0)
            throw new ArgumentException("IV é obrigatório.", nameof(novoIv));
        if (novaTag is null || novaTag.Length == 0)
            throw new ArgumentException("Tag GCM é obrigatória.", nameof(novaTag));

        PfxCifrado = novoPfx;
        SenhaCifrada = novaSenha;
        KekId = novoKekId;
        Iv = novoIv;
        Tag = novaTag;
        AlteradoEm = DateTime.UtcNow;
    }
}
