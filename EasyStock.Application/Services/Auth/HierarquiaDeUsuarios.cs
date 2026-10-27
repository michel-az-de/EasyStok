using EasyStock.Domain.Services;

namespace EasyStock.Application.Services.Auth;

/// <summary>Uma escrita de hierarquia (#2070): a empresa do pedido, o alvo (nulo ao criar) e a ação.</summary>
/// <param name="NivelNovo">O nível do perfil novo ao trocar perfil ou criar.</param>
/// <param name="DonaNova">Criar já com a marca de dona.</param>
/// <param name="Motivo">A justificativa do suporte, exigida pela política em algumas ações sobre a dona.</param>
public sealed record PedidoDeHierarquia(
    Guid EmpresaId,
    Guid? AlvoId,
    AcaoSobreUsuario Acao,
    NivelAcesso? NivelNovo = null,
    bool DonaNova = false,
    string? Motivo = null);

/// <summary>Ator e alvo relidos depois da trava, a decisão e o motivo já normalizado.</summary>
/// <param name="Alvo">O alvo relido do banco depois da trava; nulo ao criar.</param>
public sealed record ContextoDaHierarquia(
    Usuario? Alvo,
    ParticipanteDaHierarquia Ator,
    ParticipanteDaHierarquia ParticipanteAlvo,
    DecisaoDaHierarquia Decisao,
    int DonasAtivas,
    string? Motivo)
{
    /// <summary>O suporte agiu sobre a dona: a ação vai também para a auditoria de plataforma, com o motivo.</summary>
    public bool SuporteSobreDona => Ator.EhSuporteGlobal && ParticipanteAlvo.EhDona;
}

/// <summary>
/// Toda escrita de hierarquia passa aqui (#2070, ADR-0038): transação sem retry, trava da empresa
/// (<see cref="IUsuarioEmpresaRepository.TravarHierarquiaAsync"/>), releitura do ator e do alvo depois da trava, contagem
/// das donas ativas e <see cref="PoliticaDeHierarquia"/>. Recusa grava <c>hierarquia-recusada</c> e só então lança; a
/// escrita roda dentro da mesma transação, com o contexto relido. Quem chama faz antes a guarda de tenant (o mesmo
/// "Usuario nao encontrado"), que nunca depende da política.
/// <para>
/// O perfil de suporte é global (sem empresa) e a RLS o esconde dentro da transação, que corre no tenant da empresa. Por
/// isso a marca de suporte do ator e do alvo é lida antes da trava, onde o repositório abre a conexão com o bypass. Ela
/// não muda por nenhuma escrita de hierarquia.
/// </para>
/// </summary>
public sealed class HierarquiaDeUsuarios(
    IUsuarioRepository usuarios,
    IUsuarioEmpresaRepository vinculos,
    ICurrentUserAccessor atual,
    IAuditLogRepository auditoria,
    IUnitOfWork unitOfWork,
    ILogger<HierarquiaDeUsuarios> logger)
{
    /// <summary>Tamanho mínimo da justificativa do suporte (o mesmo da verificação administrativa do telefone).</summary>
    public const int MotivoMinimo = 10;

    public async Task<T> ExecutarAsync<T>(
        PedidoDeHierarquia pedido, Func<ContextoDaHierarquia, CancellationToken, Task<T>> escrita, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        ArgumentNullException.ThrowIfNull(escrita);
        var atorSuporte = await EhSuporteGlobalAsync(atual.UsuarioId);
        var alvoSuporte = pedido.AlvoId is { } id && await EhSuporteGlobalAsync(id);
        var motivo = string.IsNullOrWhiteSpace(pedido.Motivo) ? null : pedido.Motivo.Trim();

        var (contexto, valor) = await unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
        {
            await vinculos.TravarHierarquiaAsync(pedido.EmpresaId, token);
            var ator = await ParticipanteAsync(atual.UsuarioId, pedido.EmpresaId, atorSuporte);
            var alvo = pedido.AlvoId is { } alvoId ? await usuarios.GetByIdAsync(alvoId) ?? throw NaoEncontrado() : null;
            var participanteAlvo = alvo is null
                ? ParticipanteDaHierarquia.Novo(pedido.NivelNovo, pedido.DonaNova)
                : ParticipanteDaHierarquia.De(alvo, pedido.EmpresaId, alvoSuporte);
            var donas = await vinculos.ContarDonasAtivasAsync(pedido.EmpresaId, token);
            var decisao = PoliticaDeHierarquia.Avaliar(ator, participanteAlvo, pedido.Acao, donas, pedido.NivelNovo);
            var lido = new ContextoDaHierarquia(alvo, ator, participanteAlvo, decisao, donas, motivo);

            if (!decisao.Permitido)
            {
                await RegistrarRecusaAsync(pedido.EmpresaId, alvo?.Id, Descrever(pedido.Acao), decisao.Codigo!);
                return (lido, default(T)!);
            }

            if (decisao.ExigeMotivo && (motivo?.Length ?? 0) < MotivoMinimo)
                throw new UseCaseValidationException(
                    "MOTIVO_OBRIGATORIO", $"Escreva o motivo (pelo menos {MotivoMinimo} caracteres): fica na auditoria.");

            return (lido, await escrita(lido, token));
        }, ct);

        if (!contexto.Decisao.Permitido) throw Recusa(contexto.Decisao.Codigo!);
        return valor;
    }

    /// <summary>
    /// Antes de anonimizar a própria conta (LGPD): em cada empresa em que é dona, com a trava dela, a pessoa sai da
    /// contagem e perde a marca; a última dona ativa é recusada (<c>ULTIMA_DONA</c>). Uma transação por empresa, em ordem
    /// de Id; a anonimização em si vem depois, sem trava, porque já não mexe em dona nenhuma.
    /// </summary>
    public async Task SairComoDonaAsync(Usuario propria, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(propria);
        var empresas = propria.Empresas.Where(e => e.EhDona).Select(e => e.EmpresaId).Distinct().OrderBy(e => e).ToList();
        var suporte = propria.EhSuperAdmin();

        foreach (var empresaId in empresas)
        {
            var decisao = await unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
            {
                await vinculos.TravarHierarquiaAsync(empresaId, token);
                var participante = await ParticipanteAsync(propria.Id, empresaId, suporte);
                var donas = await vinculos.ContarDonasAtivasAsync(empresaId, token);
                var resultado = PoliticaDeHierarquia.AvaliarAnonimizacao(participante, donas);
                if (!resultado.Permitido)
                {
                    await RegistrarRecusaAsync(empresaId, propria.Id, "anonimizar a conta", resultado.Codigo!);
                    return resultado;
                }

                if (await vinculos.DefinirDonaAsync(propria.Id, empresaId, dona: false, token) > 0)
                {
                    await auditoria.AddAsync(RastroDeUsuarios.Linha(propria.Id, RastroDeUsuarios.DonaDesmarcada,
                        "Marca de dona retirada: a própria conta foi anonimizada", atual, empresaId));
                    await unitOfWork.CommitAsync();
                }
                return resultado;
            }, ct);

            if (!decisao.Permitido) throw Recusa(decisao.Codigo!);
        }
    }

    /// <summary>Quem está pedindo, no recorte da empresa, fora da trava: serve às leituras (as <c>acoes</c> de cada linha).</summary>
    public async Task<ParticipanteDaHierarquia> AtorAsync(Guid empresaId)
    {
        var usuario = atual.UsuarioId == Guid.Empty ? null : await usuarios.GetByIdAsync(atual.UsuarioId);
        return usuario is null
            ? ParticipanteDaHierarquia.Ninguem
            : ParticipanteDaHierarquia.De(usuario, empresaId, usuario.EhSuperAdmin());
    }

    public Task<int> DonasAtivasAsync(Guid empresaId, CancellationToken ct = default) =>
        vinculos.ContarDonasAtivasAsync(empresaId, ct);

    /// <summary>Recusa da política na forma que a API conhece: 400 para os erros do próprio pedido, senão a de hierarquia.</summary>
    public static Exception Recusa(string codigo) =>
        codigo is CodigosDaHierarquia.UsuarioProprio or CodigosDaHierarquia.DonaSuporte
            ? new UseCaseValidationException(codigo, Mensagem(codigo))
            : new HierarquiaRecusadaException(codigo, Mensagem(codigo));

    public static string Mensagem(string codigo) => codigo switch
    {
        CodigosDaHierarquia.Hierarquia => "Você não pode mudar este acesso: fale com a dona ou com o suporte.",
        CodigosDaHierarquia.UsuarioProprio => "Você não pode mudar o seu próprio acesso por aqui. Peça a outra pessoa da administração.",
        CodigosDaHierarquia.DonaProtegida => "A dona não pode ser rebaixada nem desativada por aqui: tire a marca de dona antes ou fale com o suporte.",
        CodigosDaHierarquia.UltimaDona => "A empresa precisa de pelo menos uma dona ativa: marque outra dona antes.",
        CodigosDaHierarquia.DonaNaoAdmin => "Só quem tem acesso ativo de Administração pode ser dona.",
        CodigosDaHierarquia.DonaContaNaoProvada => "A pessoa precisa entrar uma vez pelo link do e-mail ou pela conta Google antes de virar dona.",
        CodigosDaHierarquia.DonaSuporte => "O suporte não recebe a marca de dona.",
        CodigosDaHierarquia.DonaEmailExistente => "Este e-mail já tem cadastro. A dona nova precisa de um e-mail que ainda não esteja no EasyStok.",
        _ => "Ação recusada pela hierarquia de acesso.",
    };

    private static string Descrever(AcaoSobreUsuario acao) => acao switch
    {
        AcaoSobreUsuario.Criar => "criar o acesso",
        AcaoSobreUsuario.EditarNome => "editar o nome",
        AcaoSobreUsuario.TrocarEmail => "trocar o e-mail",
        AcaoSobreUsuario.TrocarPerfil => "trocar o perfil",
        AcaoSobreUsuario.Desativar => "desativar o acesso",
        AcaoSobreUsuario.Reativar => "reativar o acesso",
        AcaoSobreUsuario.ReenviarConvite => "reenviar o convite",
        AcaoSobreUsuario.MarcarDona => "marcar como dona",
        AcaoSobreUsuario.DesmarcarDona => "tirar a marca de dona",
        _ => acao.ToString(),
    };

    private async Task<bool> EhSuporteGlobalAsync(Guid usuarioId) =>
        usuarioId != Guid.Empty && (await usuarios.GetByIdAsync(usuarioId))?.EhSuperAdmin() == true;

    private async Task<ParticipanteDaHierarquia> ParticipanteAsync(Guid usuarioId, Guid empresaId, bool ehSuporteGlobal)
    {
        var usuario = usuarioId == Guid.Empty ? null : await usuarios.GetByIdAsync(usuarioId);
        return usuario is null
            ? ParticipanteDaHierarquia.Ninguem
            : ParticipanteDaHierarquia.De(usuario, empresaId, ehSuporteGlobal);
    }

    /// <summary>A recusa fica gravada (a transação comita sem escrita nenhuma) e o log leva só os identificadores.</summary>
    private async Task RegistrarRecusaAsync(Guid empresaId, Guid? alvoId, string acao, string codigo)
    {
        var autor = await RastroDeUsuarios.AutorAsync(usuarios, atual);
        await auditoria.AddAsync(RastroDeUsuarios.Linha(
            alvoId ?? atual.UsuarioId, RastroDeUsuarios.HierarquiaRecusada,
            $"Pedido para {acao} recusado ({codigo}); feito por {autor}", atual, empresaId, sucesso: false));
        await unitOfWork.CommitAsync();
        logger.LogWarning(
            "Hierarquia recusou {Codigo} na empresa {EmpresaId}: ator {AtorId}, alvo {AlvoId}",
            codigo, empresaId, atual.UsuarioId, alvoId);
    }

    private static UseCaseValidationException NaoEncontrado() => new("Usuario nao encontrado.");
}
