namespace EasyStock.Application.UseCases.Fiscal.EmitirNfce;

/// <summary>
/// Solicitacao de emissao de NFC-e. Caller (controller) recebe esse comando
/// ja com itens e pagamentos mapeados — esta camada nao busca Pedido. Isolar
/// a transformacao Pedido -> EmitirNfceCommand fica no controller ou na
/// camada que rege o fluxo de checkout.
///
/// <para>
/// <b>Idempotencia:</b> <see cref="IdempotencyKey"/> obrigatorio. Use case
/// faz <c>FindByIdempotencyKeyAsync</c> antes de tudo — re-tentativa com mesma
/// chave retorna o NfeDocumento ja criado (qualquer status, inclusive Rascunho/EnviadaAguardandoRetorno).
/// </para>
/// </summary>
public sealed record EmitirNfceCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid PedidoId,
    [property: Required][property: MinLength(8)][property: MaxLength(80)] string IdempotencyKey,
    decimal TotalNota,
    [property: Required] DadosEmitenteInput Emitente,
    DadosDestinatarioInput? Destinatario,
    [property: Required][property: MinLength(1)] List<EmitirNfceItemInput> Itens,
    Guid? UsuarioId = null,
    [property: MaxLength(120)] string? UsuarioNome = null,
    [property: MaxLength(20)] string? Origem = "pwa-caixa") : ICommand;

/// <summary>Dados do emitente (snapshot enviado ao SEFAZ). Caller monta a partir da Empresa.</summary>
public sealed record DadosEmitenteInput(
    [property: Required][property: MaxLength(14)] string Cnpj,
    [property: Required][property: MaxLength(120)] string RazaoSocial,
    string? NomeFantasia,
    string? InscricaoEstadual,
    string? InscricaoMunicipal);

/// <summary>Dados do destinatario (consumidor identificado). Null = "consumidor nao identificado".</summary>
public sealed record DadosDestinatarioInput(
    [property: MaxLength(14)] string? CpfCnpj,
    [property: MaxLength(120)] string? Nome,
    [property: MaxLength(120)] string? Email);

/// <summary>Item da NFC-e ja mapeado (Produto -> snapshot fiscal).</summary>
public sealed record EmitirNfceItemInput(
    [property: Required][property: MaxLength(120)] string NomeSnapshot,
    decimal Quantidade,
    decimal PrecoUnitario,
    [property: Required][property: MaxLength(6)] string Unidade,
    [property: MaxLength(8)] string? Ncm,
    [property: MaxLength(4)] string? Cfop,
    Guid? ProdutoIdSnapshot = null,
    byte OrigemMercadoria = 0,
    [property: MaxLength(10)] string? CstOuCsosn = null);
