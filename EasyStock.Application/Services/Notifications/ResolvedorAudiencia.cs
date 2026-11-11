using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// Uma pessoa da audiência e o contato de cada canal que ela pode receber (N4). <see cref="Email"/> nulo: a pessoa não
/// recebe e-mail nesta rotina. <see cref="Telefone"/> nulo: não recebe WhatsApp (sem verificação, sem opt-in explícito ou
/// sem WhatsApp de plataforma). <see cref="Consentimentos"/> é o estado atual dela, para a resolução de canais.
/// </summary>
public sealed record DestinatarioAudiencia(
    Guid UsuarioId,
    string Nome,
    string? Email,
    string? Telefone,
    IReadOnlyList<ConsentimentoNotificacao> Consentimentos);

/// <summary>Quem recebe o evento da rotina (N4).</summary>
public interface IResolvedorAudiencia
{
    /// <summary>
    /// A lista de destinatários (possivelmente vazia) quando a rotina declara audiência e ela vale; <c>null</c> quando a
    /// rotina não declara, a audiência está desligada, é <c>usuario</c> sem <c>usuarioId</c>, ou é <c>superadmins</c> numa
    /// rotina da empresa (ignorada com aviso). <c>null</c> manda o motor de volta ao destinatário pelas chaves do payload.
    /// </summary>
    /// <param name="rotina">A rotina escolhida para o evento.</param>
    /// <param name="empresaId">A empresa do evento.</param>
    /// <param name="usuarioIdPayload">O <c>usuarioId</c> explícito do payload: vence a audiência da rotina.</param>
    /// <param name="ct">Cancelamento.</param>
    Task<IReadOnlyList<DestinatarioAudiencia>?> ResolverAsync(
        RotinaNotificacao rotina, Guid empresaId, Guid? usuarioIdPayload, CancellationToken ct = default);
}

/// <summary>
/// Resolve a audiência de uma rotina de plataforma (N4) sobre usuários internos. E-mail: usuário <c>Ativo</c>; nas
/// audiências coletivas também <c>EmailConfirmado</c>. WhatsApp: telefone verificado, opt-in explícito na categoria da
/// rotina e WhatsApp de plataforma configurado. Preferência desligada pula a pessoa, menos em <c>Seguranca</c>.
/// </summary>
public sealed class ResolvedorAudiencia(
    IAudienciaUsuarios usuarios,
    ISuperAdminsDaPlataforma superAdmins,
    IConsentimentoRepository consentimentos,
    IPreferenciaNotificacaoRepository preferencias,
    IConfiguration configuration,
    ILogger<ResolvedorAudiencia> logger) : IResolvedorAudiencia
{
    /// <summary>Interruptor da audiência (padrão ligado): <c>false</c> volta ao destinatário só pelo payload, sem deploy.</summary>
    public const string ChaveHabilitada = "Notifications:Audiencia:Habilitada";

    /// <summary>WhatsApp de plataforma (N6). Sem ele, a audiência nunca entrega WhatsApp.</summary>
    public const string ChaveWhatsAppPlataforma = "Notifications:WhatsApp:Plataforma:PhoneNumberId";

    private static readonly NivelAcesso[] TodosOsNiveisDaLoja =
        [NivelAcesso.Admin, NivelAcesso.Gerente, NivelAcesso.Operador, NivelAcesso.Visualizador];

    public async Task<IReadOnlyList<DestinatarioAudiencia>?> ResolverAsync(
        RotinaNotificacao rotina, Guid empresaId, Guid? usuarioIdPayload, CancellationToken ct = default)
    {
        if (bool.TryParse(configuration[ChaveHabilitada], out var habilitada) && !habilitada) return null;

        var audiencia = AudienciaDaRotina.Ler(rotina.ParametrosJson);
        if (audiencia is null) return null;

        if (audiencia == AudienciaNotificacao.Superadmins && rotina.EmpresaId is not null)
        {
            logger.LogWarning(
                "Rotina {Rotina} da empresa {EmpresaId} declara audiencia superadmins, que so vale em rotina global: ignorada",
                rotina.Codigo, rotina.EmpresaId);
            return null;
        }

        IReadOnlyList<UsuarioParaAudiencia> candidatos;
        bool coletiva;
        if (usuarioIdPayload is { } explicito)
        {
            var usuario = await usuarios.ObterAsync(explicito, ct);
            candidatos = usuario is null ? [] : [usuario];
            coletiva = false;
        }
        else
        {
            coletiva = true;
            switch (audiencia)
            {
                case AudienciaNotificacao.Usuario:
                case AudienciaNotificacao.Convidado:
                    // Sem usuarioId a audiência cai nas chaves do payload (cliente final, quem entrega o contato).
                    return null;
                case AudienciaNotificacao.Admins:
                    candidatos = await usuarios.ListarDaEmpresaAsync(empresaId, [NivelAcesso.Admin], ct);
                    break;
                case AudienciaNotificacao.Gestores:
                    candidatos = await usuarios.ListarDaEmpresaAsync(empresaId, [NivelAcesso.Admin, NivelAcesso.Gerente], ct);
                    break;
                case AudienciaNotificacao.Equipe:
                    // Quem atende conversas, pela mesma regra do JWT: a cozinha (Operador sem atender) fica de fora.
                    candidatos = await usuarios.ListarComPermissaoAsync(empresaId, Permissao.AtenderConversas, ct);
                    break;
                case AudienciaNotificacao.Inscritos:
                    candidatos = await usuarios.ListarDaEmpresaAsync(empresaId, TodosOsNiveisDaLoja, ct);
                    break;
                default:
                    candidatos = await superAdmins.ListarAsync(ct);
                    break;
            }
        }

        var ativos = candidatos.Where(u => u.Ativo).DistinctBy(u => u.Id).ToList();
        if (ativos.Count == 0) return [];

        var ids = ativos.Select(u => u.Id).ToList();
        var consentimentosPorUsuario = (await consentimentos.ListarPorUsuariosAsync(ids, ct))
            .GroupBy(c => c.UsuarioId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ConsentimentoNotificacao>)g.ToList());
        var inscritos = audiencia == AudienciaNotificacao.Inscritos;
        var preferenciasDaRotina = rotina.Categoria == CategoriaConteudoNotificacao.Seguranca && !inscritos
            ? []
            : await preferencias.ListarDaRotinaAsync(empresaId, rotina.Codigo, ids, ct);
        var desligadas = preferenciasDaRotina.Where(p => !p.Habilitada).Select(p => p.UsuarioId).ToHashSet();
        // #2211: na audiência inscritos a preferência é opt-in: sem linha ligada a pessoa não recebe.
        var ligadas = preferenciasDaRotina.Where(p => p.Habilitada).Select(p => p.UsuarioId).ToHashSet();
        // #2211: aviso a inscrito com remetente loja (N6) sai pelo número do atendimento, sem o WhatsApp de plataforma.
        // As demais audiências seguem exigindo o 2º número, sem mudar o que já está em produção.
        var pelaLoja = inscritos && RemetentePorTipoEvento.De(rotina.TipoEvento) == OrigemRemetente.Loja;
        var whatsAppDisponivel = pelaLoja || !string.IsNullOrWhiteSpace(configuration[ChaveWhatsAppPlataforma]);

        var destinatarios = new List<DestinatarioAudiencia>(ativos.Count);
        foreach (var u in ativos)
        {
            if (desligadas.Contains(u.Id)) continue;
            if (inscritos && !ligadas.Contains(u.Id)) continue;

            var consentimentosDele = consentimentosPorUsuario.GetValueOrDefault(u.Id) ?? [];
            var email = !string.IsNullOrWhiteSpace(u.Email) && (!coletiva || u.EmailConfirmado) ? u.Email : null;
            // N9: o convidado ainda nao verificou o telefone (o aceite do convite e que verifica) e o opt-in vem do atestado
            // da dona. A relaxacao vale so para a audiencia convidado e so enquanto o convite esta pendente.
            var telefoneVerificadoOuConvidado = u.TelefoneVerificadoEm is not null
                                                || (audiencia == AudienciaNotificacao.Convidado && u.ConvitePendente);
            var consentimentoWhatsApp = consentimentosDele
                .Where(c => c.Canal == CanalNotificacao.WhatsApp && c.Categoria == rotina.Categoria)
                .ToList();
            // Inscrito: a marcação da dona vale como opt-in, mas o opt-out explícito dele vence.
            var aceitaWhatsApp = inscritos
                ? !consentimentoWhatsApp.Any(c => !c.OptIn)
                : consentimentoWhatsApp.Any(c => c.OptIn);
            var whatsApp = whatsAppDisponivel
                           && !string.IsNullOrWhiteSpace(u.Telefone)
                           && telefoneVerificadoOuConvidado
                           && aceitaWhatsApp
                ? u.Telefone
                : null;

            destinatarios.Add(new DestinatarioAudiencia(u.Id, u.Nome, email, whatsApp, consentimentosDele));
        }

        return destinatarios;
    }
}
