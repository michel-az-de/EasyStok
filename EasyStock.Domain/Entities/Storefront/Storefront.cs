using System.Globalization;
using System.Text.RegularExpressions;

namespace EasyStock.Domain.Entities.Storefront;

/// <summary>
/// Loja virtual hospedada na plataforma — uma por <see cref="Empresa"/>.
/// Multi-tenant via <see cref="EmpresaId"/> (ver ADR-0002).
/// Slug é único globalmente (resolução pública via /api/storefront/{slug}/...).
///
/// <para>
/// <strong>Defaults safe-by-default:</strong> storefront é criado inativo,
/// <see cref="NfeAutomaticaHabilitada"/> = false e <see cref="ModeloFiscal"/> = "manual".
/// Ver ADR-0010 (NF-e no MVP) — emissão automática só funciona após contador validar
/// modelo fiscal (TASK-002/003) e admin explicitamente habilitar via
/// <see cref="HabilitarNfeAutomatica"/>.
/// </para>
///
/// <para>
/// <strong>Pré-requisitos para ativar (Application Layer):</strong> ao menos 1
/// <c>CardapioItem</c> visível, credencial MercadoPago cadastrada, ao menos 1
/// <c>FreteZona</c>. Esta entity NÃO valida pré-requisitos diretamente — apenas
/// expõe <see cref="Ativar"/>/<see cref="Desativar"/>. Use caso de ativação
/// (AtivarStorefrontUseCase) faz a validação cross-aggregate.
/// </para>
/// </summary>
public class Storefront
{
    /// <summary>
    /// Regex de slug: 3-40 chars, lowercase alphanum + hyphen, não inicia/termina com hyphen,
    /// sem hyphens consecutivos. PII na URL pública — restritivo é OK.
    /// </summary>
    private static readonly Regex SlugRegex = new(
        @"^[a-z0-9]([a-z0-9]|-(?!-)){1,38}[a-z0-9]$",
        RegexOptions.Compiled);

    /// <summary>Modelos fiscais permitidos. Ver ADR-0007/0010.</summary>
    private static readonly HashSet<string> ModelosFiscaisPermitidos =
        new(StringComparer.OrdinalIgnoreCase) { "manual", "nfe55", "nfce65" };

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }

    /// <summary>Loja padrão do ERP que recebe pedidos do storefront. Resolvido em outro caso de uso.</summary>
    public Guid? LojaPadraoId { get; private set; }

    public string Slug { get; private set; } = null!;

    /// <summary>Domínio custom (ex: "casadababa.com"). Null = só funciona via subdomain do app.</summary>
    public string? DominioCustom { get; private set; }

    public string TituloPublico { get; private set; } = null!;
    public string? SubtituloPublico { get; private set; }
    public string? LogoUrl { get; private set; }

    /// <summary>Cor primária em hex (#RRGGBB ou #RGB). Permite branding por tenant.</summary>
    public string? CorPrimaria { get; private set; }

    /// <summary>Telefone WhatsApp em formato E.164 (ex: +5511997573992).</summary>
    public string? WhatsappPedidos { get; private set; }

    /// <summary>Valor mínimo para entrega. 0 = sem mínimo.</summary>
    public decimal PedidoMinimoEntrega { get; private set; }

    /// <summary>Frete grátis acima de N reais. Null = nunca grátis.</summary>
    public decimal? FreteGratisAcima { get; private set; }

    /// <summary>
    /// Mensagem exibida quando cliente está fora da área de cobertura.
    /// Default: "Por enquanto a Babá entrega só em determinadas regiões."
    /// </summary>
    public string? MensagemForaArea { get; private set; }

    // ── Frete por raio (ADR-0017) — opcional; null = raio desligado, usa zona ──

    /// <summary>Latitude do ponto fixo da cozinha (origem do raio). Null = raio não configurado.</summary>
    public double? CozinhaLat { get; private set; }

    /// <summary>Longitude do ponto fixo da cozinha.</summary>
    public double? CozinhaLng { get; private set; }

    /// <summary>Fator de rota (distanciaRota = haversine × fator). Operacional ~1.4.</summary>
    public double? FreteFatorRota { get; private set; }

    /// <summary>Até esta distância de rota (metros) o frete é grátis.</summary>
    public int? FreteFaixaGratisMetros { get; private set; }

    /// <summary>Acima desta distância de rota (metros) = fora de cobertura (retirada).</summary>
    public int? FreteRaioMaxMetros { get; private set; }

    /// <summary>JSON array das faixas: <c>[{"id","ateMetros","valorCentavos"}]</c>. Null = sem faixas.</summary>
    public string? FreteFaixasJson { get; private set; }

    /// <summary>
    /// Feature flag — emissão automática de NF-e/NFC-e está habilitada.
    /// <strong>Default false</strong> (safe). Ver ADR-0010: só ativar após TASK-003 done
    /// (decisão contador) e <see cref="ModeloFiscal"/> definido.
    /// </summary>
    public bool NfeAutomaticaHabilitada { get; private set; }

    /// <summary>
    /// Modelo fiscal a usar para emissão automática.
    /// Valores permitidos: "manual" (default), "nfe55", "nfce65".
    /// Default "manual" até ADR-0007 ser Accepted (cenário A/B/C).
    /// </summary>
    public string ModeloFiscal { get; private set; } = "manual";

    public bool Ativo { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AlteradoEm { get; private set; }

    // EF Core ctor sem parâmetros
    private Storefront() { }

    /// <summary>
    /// Factory. Defaults: <see cref="Ativo"/> = false, <see cref="NfeAutomaticaHabilitada"/> = false,
    /// <see cref="ModeloFiscal"/> = "manual".
    /// </summary>
    public static Storefront Criar(
        Guid empresaId,
        string slug,
        string tituloPublico,
        decimal pedidoMinimoEntrega)
    {
        if (empresaId == Guid.Empty)
            throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");

        var slugNormalizado = NormalizarSlug(slug);
        ValidarSlug(slugNormalizado);
        ValidarTituloPublico(tituloPublico);
        ValidarPedidoMinimo(pedidoMinimoEntrega);

        var agora = DateTime.UtcNow;
        return new Storefront
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Slug = slugNormalizado,
            TituloPublico = tituloPublico.Trim(),
            PedidoMinimoEntrega = pedidoMinimoEntrega,
            Ativo = false,                            // safe default
            NfeAutomaticaHabilitada = false,          // safe default — ADR-0010
            ModeloFiscal = "manual",                  // safe default — ADR-0007
            CriadoEm = agora,
            AlteradoEm = agora,
        };
    }

    public void Ativar()
    {
        if (Ativo) return;
        Ativo = true;
        AlteradoEm = DateTime.UtcNow;
    }

    public void Desativar()
    {
        if (!Ativo) return;
        Ativo = false;
        AlteradoEm = DateTime.UtcNow;
    }

    public void AtualizarBranding(
        string? subtituloPublico = null,
        string? logoUrl = null,
        string? corPrimaria = null,
        string? whatsappPedidos = null,
        string? mensagemForaArea = null)
    {
        if (corPrimaria is not null)
            ValidarCorHex(corPrimaria);

        SubtituloPublico = subtituloPublico?.Trim();
        LogoUrl = logoUrl?.Trim();
        CorPrimaria = corPrimaria?.Trim();
        WhatsappPedidos = whatsappPedidos?.Trim();
        MensagemForaArea = mensagemForaArea?.Trim();
        AlteradoEm = DateTime.UtcNow;
    }

    public void AjustarPedidoMinimo(decimal valor)
    {
        ValidarPedidoMinimo(valor);
        PedidoMinimoEntrega = valor;
        AlteradoEm = DateTime.UtcNow;
    }

    public void DefinirFreteGratisAcima(decimal? valor)
    {
        if (valor is < 0)
            throw new RegraDeDominioVioladaException("Frete grátis acima de valor negativo é inválido.");
        FreteGratisAcima = valor;
        AlteradoEm = DateTime.UtcNow;
    }

    /// <summary>
    /// Configura o frete por raio (ADR-0017). Coordenada fora de faixa válida ou
    /// parâmetros não-positivos são rejeitados. Passar tudo null desliga o raio
    /// (o frete volta pra zona). As faixas vêm como JSON já validado pelo caller.
    /// </summary>
    public void ConfigurarFreteRaio(
        double? cozinhaLat,
        double? cozinhaLng,
        double? fatorRota,
        int? faixaGratisMetros,
        int? raioMaxMetros,
        string? faixasJson)
    {
        if (cozinhaLat is { } lat && (lat < -90 || lat > 90))
            throw new RegraDeDominioVioladaException($"Latitude inválida: {lat.ToString(CultureInfo.InvariantCulture)}.");
        if (cozinhaLng is { } lng && (lng < -180 || lng > 180))
            throw new RegraDeDominioVioladaException($"Longitude inválida: {lng.ToString(CultureInfo.InvariantCulture)}.");
        if (fatorRota is { } fr && fr <= 0)
            throw new RegraDeDominioVioladaException($"Fator de rota deve ser positivo: {fr.ToString(CultureInfo.InvariantCulture)}.");
        if (faixaGratisMetros is < 0)
            throw new RegraDeDominioVioladaException("Faixa grátis (metros) não pode ser negativa.");
        if (raioMaxMetros is { } rm && rm <= 0)
            throw new RegraDeDominioVioladaException("Raio máximo (metros) deve ser positivo.");

        CozinhaLat = cozinhaLat;
        CozinhaLng = cozinhaLng;
        FreteFatorRota = fatorRota;
        FreteFaixaGratisMetros = faixaGratisMetros;
        FreteRaioMaxMetros = raioMaxMetros;
        FreteFaixasJson = string.IsNullOrWhiteSpace(faixasJson) ? null : faixasJson.Trim();
        AlteradoEm = DateTime.UtcNow;
    }

    /// <summary>
    /// Define modelo fiscal. ADR-0007: aguarda decisão do contador.
    /// Valores permitidos: "manual" (não emite), "nfe55", "nfce65".
    /// </summary>
    public void DefinirModeloFiscal(string modelo)
    {
        if (string.IsNullOrWhiteSpace(modelo))
            throw new RegraDeDominioVioladaException("Modelo fiscal é obrigatório.");

        var normalizado = modelo.Trim().ToLowerInvariant();
        if (!ModelosFiscaisPermitidos.Contains(normalizado))
            throw new RegraDeDominioVioladaException(
                $"Modelo fiscal inválido: '{modelo}'. Permitidos: {string.Join(", ", ModelosFiscaisPermitidos)}.");

        ModeloFiscal = normalizado;
        AlteradoEm = DateTime.UtcNow;
    }

    /// <summary>
    /// Habilita emissão automática de NF-e/NFC-e. Exige <see cref="ModeloFiscal"/>
    /// diferente de "manual" (cenário decidido com contador — TASK-003).
    /// </summary>
    public void HabilitarNfeAutomatica()
    {
        if (ModeloFiscal == "manual")
            throw new RegraDeDominioVioladaException(
                "Não é possível habilitar NF-e automática sem definir modelo fiscal " +
                "(nfe55 ou nfce65). Ver ADR-0007/0010.");

        if (NfeAutomaticaHabilitada) return; // idempotente
        NfeAutomaticaHabilitada = true;
        AlteradoEm = DateTime.UtcNow;
    }

    public void DesabilitarNfeAutomatica()
    {
        if (!NfeAutomaticaHabilitada) return; // idempotente
        NfeAutomaticaHabilitada = false;
        AlteradoEm = DateTime.UtcNow;
    }

    public void DefinirDominioCustom(string? dominio)
    {
        if (dominio is not null && dominio.Length > 100)
            throw new RegraDeDominioVioladaException("Domínio custom não pode exceder 100 caracteres.");

        DominioCustom = dominio?.Trim().ToLowerInvariant();
        AlteradoEm = DateTime.UtcNow;
    }

    public void DefinirLojaPadrao(Guid? lojaId)
    {
        LojaPadraoId = lojaId == Guid.Empty ? null : lojaId;
        AlteradoEm = DateTime.UtcNow;
    }

    // ── Validações privadas ────────────────────────────────────────────

    private static string NormalizarSlug(string slug) => (slug ?? string.Empty).Trim().ToLowerInvariant();

    private static void ValidarSlug(string slugNormalizado)
    {
        if (string.IsNullOrWhiteSpace(slugNormalizado))
            throw new RegraDeDominioVioladaException("Slug é obrigatório.");

        if (slugNormalizado.Length < 3)
            throw new RegraDeDominioVioladaException(
                $"Slug deve ter no mínimo 3 caracteres (recebido: '{slugNormalizado}', {slugNormalizado.Length} char).");

        if (slugNormalizado.Length > 40)
            throw new RegraDeDominioVioladaException(
                $"Slug deve ter no máximo 40 caracteres (recebido: {slugNormalizado.Length} char).");

        if (!SlugRegex.IsMatch(slugNormalizado))
            throw new RegraDeDominioVioladaException(
                $"Slug '{slugNormalizado}' inválido. Use apenas letras minúsculas, números e hífen " +
                "(não pode iniciar/terminar com hífen, nem hífens consecutivos).");
    }

    private static void ValidarTituloPublico(string titulo)
    {
        if (string.IsNullOrWhiteSpace(titulo))
            throw new RegraDeDominioVioladaException("Título público é obrigatório.");
        if (titulo.Trim().Length > 120)
            throw new RegraDeDominioVioladaException("Título público não pode exceder 120 caracteres.");
    }

    private static void ValidarPedidoMinimo(decimal valor)
    {
        if (valor < 0m)
            throw new RegraDeDominioVioladaException(
                $"Pedido mínimo de entrega não pode ser negativo (recebido: {valor:C}).");
    }

    /// <summary>Aceita #RGB (4 chars) ou #RRGGBB (7 chars), case-insensitive.</summary>
    private static void ValidarCorHex(string cor)
    {
        if (string.IsNullOrWhiteSpace(cor))
            throw new RegraDeDominioVioladaException("Cor primária não pode ser vazia.");

        var corTrim = cor.Trim();
        var valido = corTrim.Length is 4 or 7
                     && corTrim[0] == '#'
                     && corTrim[1..].All(c => Uri.IsHexDigit(c));

        if (!valido)
            throw new RegraDeDominioVioladaException(
                $"Cor primária deve estar em formato hex (#RRGGBB ou #RGB). Recebido: '{cor}'.");
    }
}
