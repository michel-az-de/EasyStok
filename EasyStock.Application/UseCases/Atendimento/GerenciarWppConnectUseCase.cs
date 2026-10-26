using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Integration;

namespace EasyStock.Application.UseCases.Atendimento;

public sealed class GerenciarWppConnectUseCase(
    IWppConnectSessaoClient cliente, IIntegrationCredentialResolver credenciais,
    ITenantFeatureFlagRepository flags, IEmpresaRepository empresas)
{
    public async Task<EstadoWppConnect> ObterAsync(Guid empresaId, CancellationToken ct = default)
    {
        await ConferirAsync(empresaId, ct);
        if (!cliente.Habilitado) return new(false, false, false, "DESABILITADO");
        var sessao = await ObterCredencialAsync(empresaId, ct);
        if (sessao is null) return new(true, false, false, "NAO_VINCULADO");
        try { return await cliente.ObterEstadoAsync(sessao, ct); }
        catch (Exception ex) when (ex is WhatsAppCloudException or HttpRequestException
            || ex is OperationCanceledException && !ct.IsCancellationRequested)
        { return new(true, true, false, "INDISPONIVEL"); }
    }

    public async Task<EstadoWppConnect> ConectarAsync(Guid empresaId, Guid usuarioId, CancellationToken ct = default)
    {
        await ConferirAsync(empresaId, ct);
        if (!cliente.Habilitado) throw new UseCaseValidationException("A conexão provisória não está configurada.");
        var sessao = await ObterCredencialAsync(empresaId, ct);
        if (sessao is null)
        {
            var nome = CredencialWppConnect.NomeSessao(empresaId);
            var token = await cliente.GerarTokenAsync(nome, ct);
            sessao = new(nome, token);
        }
        if (sessao.InicioDaCaptura == default)
        {
            sessao = sessao with { InicioDaCaptura = DateTimeOffset.UtcNow };
            // Persistir cifrado antes de iniciar: o primeiro webhook já encontra o vínculo.
            await credenciais.SalvarAsync(empresaId, CategoriaIntegracao.Mensageria,
                CredencialWppConnect.ProviderKey, AmbienteIntegracao.Production, sessao, usuarioId, ct: ct);
        }
        await cliente.IniciarAsync(sessao, ct);
        return await cliente.ObterEstadoAsync(sessao, ct);
    }

    public async Task<EstadoWppConnect> ReconectarAsync(Guid empresaId, Guid usuarioId, CancellationToken ct = default)
    {
        await ConferirAsync(empresaId, ct);
        var sessao = await ObterCredencialAsync(empresaId, ct);
        if (sessao is not null) await cliente.FecharAsync(sessao, sair: false, ct);
        return await ConectarAsync(empresaId, usuarioId, ct);
    }

    public async Task DesconectarAsync(Guid empresaId, CancellationToken ct = default)
    {
        await ConferirAsync(empresaId, ct);
        var sessao = await ObterCredencialAsync(empresaId, ct);
        if (sessao is not null) await cliente.FecharAsync(sessao, sair: true, ct);
        await credenciais.DesativarAsync(empresaId, CredencialWppConnect.ProviderKey, ct);
    }

    private async Task<CredencialWppConnect?> ObterCredencialAsync(Guid empresaId, CancellationToken ct)
    {
        var sessao = await credenciais.ObterAsync<CredencialWppConnect>(empresaId,
            CredencialWppConnect.ProviderKey, AmbienteIntegracao.Production, ct);
        if (sessao is not null && sessao.Sessao != CredencialWppConnect.NomeSessao(empresaId))
            throw new UseCaseValidationException("A sessão não pertence a esta empresa.");
        return sessao;
    }

    private async Task ConferirAsync(Guid empresaId, CancellationToken ct)
    {
        if (empresaId == Guid.Empty || await empresas.GetByIdAsync(empresaId) is null)
            throw new UseCaseValidationException("Empresa não encontrada.");
        var ativas = await flags.ListarAtivasAsync(empresaId, ct);
        if (!ativas.Contains(FeatureCatalogo.ModuloAtendimento, StringComparer.OrdinalIgnoreCase))
            throw new UseCaseValidationException("O módulo de atendimento não está ativo nesta empresa.");
    }
}
