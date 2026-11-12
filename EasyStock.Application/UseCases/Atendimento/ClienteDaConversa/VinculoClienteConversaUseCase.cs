using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.ClienteDaConversa;

public sealed record CorrigirVinculoClienteCommand(Guid EmpresaId, Guid ConversaId, Guid RequestId,
    long VinculoRevisao, Guid? ClienteId, string Acao, Guid? DestinoClienteId, string Motivo);
public sealed record EstadoVinculoCliente(Guid? ClienteId, string? ClienteNome, Guid? ClienteAnteriorId, string? ClienteAnteriorNome,
    long VinculoRevisao, bool PodeCorrigir, string? MotivoBloqueio, IReadOnlyList<PedidoDoVinculo> Pedidos,
    IReadOnlyList<AlteracaoVinculoConversa> Historico);
public sealed class VinculoClienteConflitoException(string message) : Exception(message);

public sealed class VinculoClienteConversaUseCase(IConversaRepository conversas, IClienteRepository clientes,
    IVinculoClienteConversaRepository vinculos,
    IUnitOfWork uow, ICurrentUserAccessor usuario, TimeProvider relogio)
{
    public static bool PodeCorrigir(ICurrentUserAccessor usuario) => usuario.IsAuthenticated
        && usuario.Nivel is NivelAcesso.Admin or NivelAcesso.Gerente or NivelAcesso.SuperAdmin
        && usuario.TemPermissao(Permissao.AtenderConversas) && usuario.TemPermissao(Permissao.AcessarModuloAtendimento);

    public async Task<EstadoVinculoCliente> ObterAsync(Guid empresaId, Guid conversaId, CancellationToken ct = default)
    {
        ValidarEmpresa(empresaId);
        return await EstadoAsync(await ConversaAsync(empresaId, conversaId, false, ct), ct);
    }

    public async Task<IReadOnlyList<CandidatoClienteConversa>> CandidatosAsync(Guid empresaId, Guid conversaId,
        string telefone, CancellationToken ct = default)
    {
        ValidarEmpresa(empresaId);
        await ConversaAsync(empresaId, conversaId, false, ct);
        var normalizado = NormalizadorTelefone.NormalizarE164Br(telefone);
        var candidatos = NormalizadorTelefone.CandidatosDeWaId(normalizado.TrimStart('+'));
        var grafias = candidatos.SelectMany(t => new[] { t, t.TrimStart('+'), t.StartsWith("+55") ? t[3..] : t }).Distinct().ToArray();
        return await vinculos.ListarCandidatosAsync(empresaId, grafias, candidatos.Select(ClienteOtp.CalcularTelefoneHash).ToArray(), ct);
    }

    public async Task<EstadoVinculoCliente> ExecuteAsync(CorrigirVinculoClienteCommand command, CancellationToken ct = default)
    {
        ValidarEmpresa(command.EmpresaId);
        if (!PodeCorrigir(usuario)) throw new UnauthorizedAccessException("Somente a dona ou gerente pode corrigir a identidade.");
        var motivo = command.Motivo?.Trim() ?? "";
        if (command.RequestId == Guid.Empty || command.VinculoRevisao < 0 || motivo.Length is < 3 or > 500
            || command.Acao is not ("retirar" or "restaurar" or "vincular")
            || command.Acao != "vincular" && command.DestinoClienteId is not null || usuario.UsuarioId == Guid.Empty)
            throw new UseCaseValidationException("Informe a ação, a revisão e um motivo entre 3 e 500 caracteres.");
        return await uow.ExecuteInTransactionSemRetryAsync(async t =>
        {
            var conversa = await ConversaAsync(command.EmpresaId, command.ConversaId, true, t);
            var repetida = await vinculos.ObterTentativaAsync(command.EmpresaId, command.ConversaId, command.RequestId, t);
            if (repetida is not null)
            {
                if (repetida.UsuarioId != usuario.UsuarioId || repetida.Acao != command.Acao || repetida.Motivo != motivo
                    || repetida.ClienteAnteriorId != command.ClienteId || repetida.RevisaoAnterior != command.VinculoRevisao
                    || (command.Acao == "vincular" && repetida.ClienteNovoId != command.DestinoClienteId))
                    throw new VinculoClienteConflitoException("Esta tentativa já foi usada para outra correção.");
                return await EstadoAsync(conversa, t);
            }
            var pedidos = await vinculos.ListarPedidosAsync(command.EmpresaId, command.ConversaId, t);
            var bloqueio = Bloqueio(conversa, pedidos);
            if (bloqueio is not null) throw new VinculoClienteConflitoException(bloqueio);
            if (conversa.VinculoRevisao != command.VinculoRevisao || conversa.ClienteId != command.ClienteId)
                throw new VinculoClienteConflitoException("A identidade mudou. Atualize a conversa e confira novamente.");
            Guid? destino = command.Acao switch
            {
                "retirar" => null,
                "restaurar" => conversa.ClienteAnteriorId,
                _ => command.DestinoClienteId
            };
            if (command.Acao != "retirar" && (destino is null || destino == Guid.Empty)
                || command.Acao == "retirar" && conversa.ClienteId is null)
                throw new UseCaseValidationException("Escolha a pessoa que será vinculada à conversa.");
            if (destino is { } id)
            {
                var cliente = await clientes.GetByIdAsync(command.EmpresaId, id);
                if (cliente is null || cliente.EmpresaId != command.EmpresaId)
                    throw new UseCaseValidationException("Cliente não encontrado nesta empresa.");
                if (cliente.Bloqueado || !cliente.Ativo) throw new VinculoClienteConflitoException("O cadastro está bloqueado ou inativo.");
            }
            var agora = relogio.GetUtcNow().UtcDateTime;
            conversa.CorrigirVinculoCliente(destino, command.VinculoRevisao);
            await vinculos.ExpirarLinksAsync(command.EmpresaId, command.ConversaId, agora, t);
            await vinculos.EncerrarSessoesChatAsync(command.EmpresaId, command.ConversaId, agora, t);
            await vinculos.AddAsync(new AlteracaoVinculoConversa
            {
                EmpresaId = command.EmpresaId, ConversaId = command.ConversaId, RequestId = command.RequestId,
                UsuarioId = usuario.UsuarioId, ClienteAnteriorId = command.ClienteId, ClienteNovoId = destino,
                RevisaoAnterior = command.VinculoRevisao, Revisao = conversa.VinculoRevisao,
                Acao = command.Acao, Motivo = motivo, CriadaEm = agora
            }, t);
            await uow.CommitAsync();
            return await EstadoAsync(conversa, t);
        }, ct);
    }

    private void ValidarEmpresa(Guid empresaId)
    {
        if (!usuario.IsAuthenticated || !usuario.TemPermissao(Permissao.AcessarModuloAtendimento))
            throw new UnauthorizedAccessException();
        if (empresaId == Guid.Empty || empresaId != usuario.EmpresaId)
            throw new UseCaseValidationException("Empresa diferente da sessão.");
    }

    private async Task<Conversa> ConversaAsync(Guid empresaId, Guid conversaId, bool travar, CancellationToken ct)
    {
        var conversa = travar ? await conversas.ObterPorIdComLockAsync(empresaId, conversaId, ct)
            : await conversas.ObterPorIdAsync(empresaId, conversaId, ct);
        if (conversa is null || conversa.EmpresaId != empresaId) throw new ConversaNaoEncontradaException(conversaId);
        return conversa;
    }

    private async Task<EstadoVinculoCliente> EstadoAsync(Conversa conversa, CancellationToken ct)
    {
        var pedidos = await vinculos.ListarPedidosAsync(conversa.EmpresaId, conversa.Id, ct);
        var bloqueio = Bloqueio(conversa, pedidos);
        var cliente = conversa.ClienteId is { } id ? await clientes.GetByIdAsync(conversa.EmpresaId, id) : null;
        var anterior = conversa.ClienteAnteriorId is { } anteriorId ? await clientes.GetByIdAsync(conversa.EmpresaId, anteriorId) : null;
        return new(conversa.ClienteId, cliente?.EmpresaId == conversa.EmpresaId ? cliente.Nome : null,
            conversa.ClienteAnteriorId, anterior?.EmpresaId == conversa.EmpresaId ? anterior.Nome : null,
            conversa.VinculoRevisao, PodeCorrigir(usuario) && bloqueio is null, bloqueio,
            pedidos, await vinculos.ListarHistoricoAsync(conversa.EmpresaId, conversa.Id, ct));
    }

    private static string? Bloqueio(Conversa conversa, IReadOnlyList<PedidoDoVinculo> pedidos) =>
        pedidos.Count > 0 || conversa.PedidosHistoricoIds.Length > 0 || conversa.PedidoEmAndamentoId is not null
            ? "Esta conversa já possui pedido. Confira o histórico antes de continuar."
            : !conversa.HistoricoPedidosVerificado ? "O histórico antigo desta conversa não permite confirmar a ausência de pedidos."
            : !conversa.EstaAberta ? "Reabra a conversa antes de corrigir a identidade." : null;
}
