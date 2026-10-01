namespace EasyStock.Domain.Integration;

/// <summary>
/// Credencial cifrada de integração externa por tenant. Cada empresa pode
/// configurar suas próprias chaves (Mercado Pago access token, certificado
/// A1 NFe, OAuth Mercado Livre, etc.) com isolamento entre tenants.
///
/// <para>
/// Payload cifrado em repouso via AES-256-GCM. KEK identificado por
/// <see cref="KekId"/> permite rotação sem migration de dados — basta
/// re-cifrar com a nova KEK e atualizar o id. Nonce/IV e auth tag GCM
/// armazenados separados pra simplificar rotação de algoritmo.
/// </para>
///
/// <para>
/// Constraint única recomendada em DB: <c>(empresa_id, provider_key,
/// ambiente)</c> — garante que cada empresa tenha no máximo uma credencial
/// ativa por provider e ambiente. Trocar credencial = nova versão (deactivate
/// antiga, ativar nova).
/// </para>
/// </summary>
public sealed class CredencialIntegracao
{
    /// <summary>Teto da mensagem do último teste de conexão (F16, #1246).</summary>
    public const int MensagemTesteTamanhoMaximo = 300;

    /// <summary>A máscara mostra só os últimos caracteres do segredo, nunca mais que isso.</summary>
    public const int MascaraTamanhoMaximo = 4;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public CategoriaIntegracao Categoria { get; private set; }

    /// <summary>
    /// Identificador do provider dentro da categoria.
    /// Ex: "mercadopago", "picpay", "efi" (Payments); "mercadolivre", "ifood"
    /// (Marketplace); "99entrega" (Logistics); "sefaz" (Fiscal).
    /// </summary>
    public string ProviderKey { get; private set; } = null!;

    public AmbienteIntegracao Ambiente { get; private set; }

    /// <summary>
    /// Payload JSON da credencial cifrado com AES-256-GCM. Estrutura interna
    /// específica por provider — apenas o resolver/adapter conhece o schema.
    /// </summary>
    public byte[] PayloadCifrado { get; private set; } = Array.Empty<byte>();

    /// <summary>
    /// Identificador da Key Encryption Key (KEK) usada na cifragem. Permite
    /// rotação: ao introduzir nova KEK, recifrar payloads e atualizar
    /// <see cref="KekId"/> sem mudar o algoritmo.
    /// </summary>
    public string KekId { get; private set; } = null!;

    /// <summary>Nonce/IV do AES-GCM (12 bytes recomendados).</summary>
    public byte[] Iv { get; private set; } = Array.Empty<byte>();

    /// <summary>Auth tag do AES-GCM (16 bytes).</summary>
    public byte[] Tag { get; private set; } = Array.Empty<byte>();

    public DateTime ValidoDe { get; private set; }
    public DateTime? ValidoAte { get; private set; }
    public bool Ativo { get; private set; } = true;

    /// <summary>
    /// Telemetria pra detectar credenciais não-usadas (candidatas a remoção).
    /// Atualizada pelo resolver a cada acesso bem-sucedido.
    /// </summary>
    public DateTime? UltimoUsoEm { get; private set; }

    /// <summary>
    /// Últimos caracteres do segredo, para a tela reconhecer qual chave está salva sem que o
    /// <c>GET</c> precise decifrar nada (F16, #1246). Nulo quando a credencial não tem segredo
    /// com sentido de máscara.
    /// </summary>
    public string? Mascara { get; private set; }

    /// <summary>Último teste de conexão (botão Testar ou vigia). Nulo = nunca testada.</summary>
    public DateTime? UltimoTesteEm { get; private set; }
    public bool? UltimoTesteOk { get; private set; }

    /// <summary>Mensagem do provedor em português no último teste, sem segredo.</summary>
    public string? UltimoTesteMensagem { get; private set; }

    public Guid CriadoPorUsuarioId { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AlteradoEm { get; private set; }

    public Empresa? Empresa { get; private set; }

    // EF Core ctor sem parâmetros.
    private CredencialIntegracao() { }

    public static CredencialIntegracao Criar(
        Guid empresaId,
        CategoriaIntegracao categoria,
        string providerKey,
        AmbienteIntegracao ambiente,
        byte[] payloadCifrado,
        string kekId,
        byte[] iv,
        byte[] tag,
        Guid criadoPorUsuarioId,
        DateTime? validoAte = null,
        string? mascara = null)
    {
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId é obrigatório.", nameof(empresaId));
        if (string.IsNullOrWhiteSpace(providerKey))
            throw new ArgumentException("ProviderKey é obrigatório.", nameof(providerKey));
        if (payloadCifrado is null || payloadCifrado.Length == 0)
            throw new ArgumentException("PayloadCifrado é obrigatório.", nameof(payloadCifrado));
        if (string.IsNullOrWhiteSpace(kekId))
            throw new ArgumentException("KekId é obrigatório.", nameof(kekId));
        if (iv is null || iv.Length == 0)
            throw new ArgumentException("Iv é obrigatório.", nameof(iv));
        if (tag is null || tag.Length == 0)
            throw new ArgumentException("Tag é obrigatório.", nameof(tag));
        if (criadoPorUsuarioId == Guid.Empty)
            throw new ArgumentException("CriadoPorUsuarioId é obrigatório.", nameof(criadoPorUsuarioId));

        if (mascara is not null && mascara.Length > MascaraTamanhoMaximo)
            throw new ArgumentException($"Máscara acima de {MascaraTamanhoMaximo} caracteres.", nameof(mascara));

        var agora = DateTime.UtcNow;
        if (validoAte.HasValue && validoAte.Value <= agora)
            throw new ArgumentException("ValidoAte precisa ser futuro.", nameof(validoAte));

        return new CredencialIntegracao
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Categoria = categoria,
            ProviderKey = providerKey.Trim().ToLowerInvariant(),
            Ambiente = ambiente,
            PayloadCifrado = payloadCifrado,
            KekId = kekId.Trim(),
            Iv = iv,
            Tag = tag,
            ValidoDe = agora,
            ValidoAte = validoAte,
            Mascara = string.IsNullOrEmpty(mascara) ? null : mascara,
            Ativo = true,
            CriadoPorUsuarioId = criadoPorUsuarioId,
            CriadoEm = agora,
            AlteradoEm = agora,
        };
    }

    /// <summary>
    /// Atualiza <see cref="UltimoUsoEm"/>. Chame após resolver e usar a credencial
    /// com sucesso (ex: chamada à API do provider retornou sem erro de auth).
    /// </summary>
    public void RegistrarUso()
    {
        UltimoUsoEm = DateTime.UtcNow;
    }

    /// <summary>
    /// Grava o resultado do teste de conexão (F16). A mensagem é cortada no teto: quem chama
    /// já a traduziu e tirou qualquer segredo.
    /// </summary>
    public void RegistrarTeste(DateTime em, bool ok, string? mensagem)
    {
        UltimoTesteEm = DateTime.SpecifyKind(em, DateTimeKind.Utc);
        UltimoTesteOk = ok;
        var limpa = string.IsNullOrWhiteSpace(mensagem) ? null : mensagem.Trim();
        UltimoTesteMensagem = limpa is { Length: > MensagemTesteTamanhoMaximo }
            ? limpa[..MensagemTesteTamanhoMaximo]
            : limpa;
    }

    /// <summary>
    /// A validade cai até <paramref name="limite"/>? Usado pelo vigia para avisar a dona antes de
    /// a chave vencer. Sem <see cref="ValidoAte"/>, nunca vence.
    /// </summary>
    public bool VenceAte(DateTime limite) => ValidoAte.HasValue && ValidoAte.Value <= limite;

    /// <summary>
    /// Desativa a credencial. Resolvers devem ignorar credenciais inativas
    /// e cair no stub/erro em vez de chamar provider externo.
    /// </summary>
    public void Desativar()
    {
        if (!Ativo) return;
        Ativo = false;
        AlteradoEm = DateTime.UtcNow;
    }

    public void Reativar()
    {
        if (Ativo) return;
        if (ValidoAte.HasValue && ValidoAte.Value <= DateTime.UtcNow)
            throw new InvalidOperationException("Credencial expirada não pode ser reativada — crie nova.");
        Ativo = true;
        AlteradoEm = DateTime.UtcNow;
    }

    /// <summary>
    /// Re-cifra o payload com nova KEK + novo IV/Tag (rotação de chave-mestra).
    /// </summary>
    public void RotacionarKek(byte[] novoPayloadCifrado, string novaKekId, byte[] novoIv, byte[] novaTag)
    {
        if (novoPayloadCifrado is null || novoPayloadCifrado.Length == 0)
            throw new ArgumentException("Novo payload é obrigatório.", nameof(novoPayloadCifrado));
        if (string.IsNullOrWhiteSpace(novaKekId))
            throw new ArgumentException("Nova KekId é obrigatória.", nameof(novaKekId));

        PayloadCifrado = novoPayloadCifrado;
        KekId = novaKekId.Trim();
        Iv = novoIv;
        Tag = novaTag;
        AlteradoEm = DateTime.UtcNow;
    }

    /// <summary>
    /// Indica se a credencial está utilizável agora (ativa e dentro da janela
    /// de validade). Resolvers devem verificar antes de decifrar e usar.
    /// </summary>
    public bool EstaUtilizavel()
    {
        if (!Ativo) return false;
        var agora = DateTime.UtcNow;
        if (agora < ValidoDe) return false;
        if (ValidoAte.HasValue && agora > ValidoAte.Value) return false;
        return true;
    }
}
