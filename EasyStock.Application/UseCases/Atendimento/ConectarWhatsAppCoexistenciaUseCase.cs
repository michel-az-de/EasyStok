using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.UseCases.Admin.VincularWhatsAppTenant;
using EasyStock.Domain.Integration;

namespace EasyStock.Application.UseCases.Atendimento;

/// <summary>O que o console manda depois do Embedded Signup v4 (#1417): o <c>code</c> do FB.login e os ids do evento.</summary>
public sealed record ConectarWhatsAppCoexistenciaCommand(
    Guid EmpresaId, Guid UsuarioId, string Code, string WabaId, string PhoneNumberId);

public enum StatusConexaoWhatsApp
{
    Conectado,
    EmpresaNaoEncontrada,
    NumeroEmUsoPorOutraEmpresa,
    NumeroReservadoDaPlataforma,
}

/// <summary>Pedido de sincronização: <see cref="Erro"/> preenchido quando a Meta recusou (a conexão segue de pé).</summary>
public sealed record SincronizacaoWhatsAppResultado(bool Solicitada, string? RequestId, string? Erro);

public sealed record ConexaoWhatsAppResultado(
    StatusConexaoWhatsApp Status,
    string? DisplayPhoneNumber = null,
    string? VerifiedName = null,
    bool? IsOnBizApp = null,
    string? PlatformType = null,
    SincronizacaoWhatsAppResultado? SincronizacaoEstadoApp = null,
    SincronizacaoWhatsAppResultado? SincronizacaoHistorico = null)
{
    /// <summary>Conectou, mas a Meta não diz que o número está no app Business: a coexistência pode não estar ativa.</summary>
    public bool ForaDoAppBusiness => Status == StatusConexaoWhatsApp.Conectado && IsOnBizApp != true;
}

/// <summary>Em que passo a Meta recusou; o controller decide o status HTTP por ele.</summary>
public enum EtapaConexaoWhatsApp
{
    TrocaDoCode,
    InscricaoNaWaba,
    ConsultaDoNumero,
}

public sealed class ConexaoWhatsAppRecusadaException(EtapaConexaoWhatsApp etapa, string mensagem, Exception inner)
    : Exception(mensagem, inner)
{
    public EtapaConexaoWhatsApp Etapa { get; } = etapa;
}

/// <summary>
/// Conecta o número da loja por coexistência (#1417, Embedded Signup v4): troca o <c>code</c> pelo business token,
/// inscreve o app na WABA, confere o número, vincula o <c>phone_number_id</c> à empresa (mesmas regras do back-office:
/// número de outra empresa ou da plataforma é recusado), grava o token cifrado e pede as duas sincronizações.
/// O vínculo vem antes do token: número recusado não deixa token trocado na empresa. Sincronização que falha não
/// derruba a conexão, só aparece no resultado (a Meta aceita refazer em até 24 h).
/// </summary>
public sealed class ConectarWhatsAppCoexistenciaUseCase(
    IMetaEmbeddedSignupClient meta,
    IIntegrationCredentialResolver credenciais,
    VincularWhatsAppDoTenantUseCase vincularWhatsApp,
    ILogger<ConectarWhatsAppCoexistenciaUseCase> logger)
{
    /// <summary>O <c>code</c> da Meta tem poucas centenas de caracteres; o teto só barra lixo.</summary>
    public const int TamanhoMaximoCode = 4096;

    /// <summary>Ids numéricos da Meta; mesmo teto da coluna do <c>phone_number_id</c>.</summary>
    public const int TamanhoMaximoId = VincularWhatsAppDoTenantUseCase.TamanhoMaximo;

    public async Task<ConexaoWhatsAppResultado> ExecuteAsync(ConectarWhatsAppCoexistenciaCommand cmd, CancellationToken ct = default)
    {
        var (code, wabaId, phoneNumberId) = Validar(cmd);

        var token = await Etapa(EtapaConexaoWhatsApp.TrocaDoCode,
            () => meta.TrocarCodigoPorTokenAsync(code, ct));
        await Etapa(EtapaConexaoWhatsApp.InscricaoNaWaba,
            async () => { await meta.InscreverAppNaWabaAsync(wabaId, token, ct); return true; });
        var numero = await Etapa(EtapaConexaoWhatsApp.ConsultaDoNumero,
            () => meta.ConsultarNumeroAsync(phoneNumberId, token, ct));

        var vinculo = await vincularWhatsApp.ExecuteAsync(new VincularWhatsAppDoTenantCommand(cmd.EmpresaId, phoneNumberId), ct);
        var recusa = vinculo.Status switch
        {
            StatusVinculoWhatsApp.EmpresaNaoEncontrada => StatusConexaoWhatsApp.EmpresaNaoEncontrada,
            StatusVinculoWhatsApp.NumeroEmUsoPorOutraEmpresa => StatusConexaoWhatsApp.NumeroEmUsoPorOutraEmpresa,
            StatusVinculoWhatsApp.NumeroReservadoDaPlataforma => StatusConexaoWhatsApp.NumeroReservadoDaPlataforma,
            _ => (StatusConexaoWhatsApp?)null,
        };
        if (recusa is { } status)
            return new ConexaoWhatsAppResultado(status);

        await credenciais.SalvarAsync(cmd.EmpresaId, CategoriaIntegracao.Mensageria, CredencialWhatsAppMeta.ProviderKey,
            AmbienteIntegracao.Production, new CredencialWhatsAppMeta(token, wabaId, phoneNumberId, DateTime.UtcNow),
            cmd.UsuarioId, ct: ct);

        if (numero.IsOnBizApp != true)
            logger.LogWarning("Coexistência: número {PhoneNumberId} conectado, mas a Meta não o marca no app Business (is_on_biz_app={NoApp}).",
                phoneNumberId, numero.IsOnBizApp);

        var estado = await SincronizarAsync(phoneNumberId, token, TipoSincronizacaoWhatsApp.EstadoDoApp, ct);
        var historico = await SincronizarAsync(phoneNumberId, token, TipoSincronizacaoWhatsApp.Historico, ct);

        logger.LogInformation("Coexistência: WhatsApp {PhoneNumberId} conectado à empresa {EmpresaId}.", phoneNumberId, cmd.EmpresaId);
        return new ConexaoWhatsAppResultado(StatusConexaoWhatsApp.Conectado,
            numero.DisplayPhoneNumber, numero.VerifiedName, numero.IsOnBizApp, numero.PlatformType, estado, historico);
    }

    private async Task<SincronizacaoWhatsAppResultado> SincronizarAsync(
        string phoneNumberId, string token, TipoSincronizacaoWhatsApp tipo, CancellationToken ct)
    {
        try
        {
            var requestId = await meta.SolicitarSincronizacaoAsync(phoneNumberId, token, tipo, ct);
            return new SincronizacaoWhatsAppResultado(true, requestId, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Coexistência: a sincronização {Tipo} do número {PhoneNumberId} falhou; a conexão segue.", tipo, phoneNumberId);
            return new SincronizacaoWhatsAppResultado(false, null, ex.Message);
        }
    }

    private static async Task<T> Etapa<T>(EtapaConexaoWhatsApp etapa, Func<Task<T>> chamada)
    {
        try
        {
            return await chamada();
        }
        catch (WhatsAppCloudException ex)
        {
            throw new ConexaoWhatsAppRecusadaException(etapa, MensagemDa(etapa, ex), ex);
        }
    }

    private static string MensagemDa(EtapaConexaoWhatsApp etapa, WhatsAppCloudException ex) => etapa switch
    {
        EtapaConexaoWhatsApp.TrocaDoCode =>
            $"A Meta recusou o código da conexão (expirado ou já usado). Abra o fluxo de novo. Detalhe: {ex.Message}",
        EtapaConexaoWhatsApp.InscricaoNaWaba =>
            $"A Meta não deixou o EasyStok receber as mensagens desta conta do WhatsApp Business. Detalhe: {ex.Message}",
        _ => $"A Meta não confirmou o número escolhido. Detalhe: {ex.Message}",
    };

    private static (string Code, string WabaId, string PhoneNumberId) Validar(ConectarWhatsAppCoexistenciaCommand cmd)
    {
        if (cmd.EmpresaId == Guid.Empty)
            throw new UseCaseValidationException("Empresa não identificada.");

        var code = cmd.Code?.Trim() ?? "";
        if (code.Length == 0 || code.Length > TamanhoMaximoCode)
            throw new UseCaseValidationException("Código da Meta ausente ou inválido: abra o fluxo de conexão de novo.");

        return (code, IdDaMeta(cmd.WabaId, "waba_id"), IdDaMeta(cmd.PhoneNumberId, "phone_number_id"));
    }

    private static string IdDaMeta(string? bruto, string nome)
    {
        var id = bruto?.Trim() ?? "";
        if (id.Length == 0 || id.Length > TamanhoMaximoId || !id.All(char.IsAsciiDigit))
            throw new UseCaseValidationException($"{nome} inválido: deve ser o id numérico que a Meta devolveu no fluxo.");
        return id;
    }
}
