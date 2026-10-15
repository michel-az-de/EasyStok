using EasyStock.Application.Ports.Output.Atendimento.Email;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Services.Atendimento.Email;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Services.Atendimento;

/// <summary>
/// Caixa de suporte e fio do e-mail da empresa do tenant corrente (#1432), lidos como em
/// <see cref="RemetenteWhatsAppDoTenant"/>: o tenant é o <see cref="EasyStockDbContext.CurrentTenantId"/> (JWT no
/// console, override nos jobs) e a caixa sai decifrada do resolver de credenciais.
/// </summary>
public sealed class CaixaEmailDoTenant(EasyStockDbContext dbContext, IIntegrationCredentialResolver credenciais)
    : ICaixaEmailDoTenant
{
    public async Task<CaixaEmailAtendimento?> ObterCaixaAsync(CancellationToken ct = default)
    {
        var tenant = dbContext.CurrentTenantId;
        if (tenant == Guid.Empty)
            return null;

        return await credenciais.ObterAsync<CaixaEmailAtendimento>(
            tenant, CaixaEmailAtendimento.ProviderKey, AmbienteIntegracao.Production, ct);
    }

    public async Task<FioEmail?> ObterFioAsync(string contatoEmail, CancellationToken ct = default)
    {
        var tenant = dbContext.CurrentTenantId;
        if (tenant == Guid.Empty || string.IsNullOrWhiteSpace(contatoEmail))
            return null;

        var contato = contatoEmail.Trim().ToLowerInvariant();
        var conversa = await dbContext.Set<Conversa>().AsNoTracking()
            .Where(c => c.EmpresaId == tenant && c.Canal == CanalConversa.Email
                        && c.ContatoIdExterno == contato && c.Situacao != SituacaoConversa.Encerrada)
            .OrderByDescending(c => c.UltimaMensagemEm)
            .Select(c => new { c.Id, c.Assunto })
            .FirstOrDefaultAsync(ct);
        if (conversa is null)
            return null;

        // Mais recentes primeiro; o primeiro Message-ID guardado como veio é o que encadeia (anexo e id
        // sintético têm prefixo e são pulados). Dez bastam: cada e-mail grava a principal e os anexos juntos.
        var ids = await dbContext.Set<Mensagem>().AsNoTracking()
            .Where(m => m.EmpresaId == tenant && m.ConversaId == conversa.Id
                        && m.Direcao == DirecaoMensagem.Entrada && m.ExternoId != null)
            .OrderByDescending(m => m.EnviadaEm)
            .Select(m => m.ExternoId)
            .Take(10)
            .ToListAsync(ct);

        return new FioEmail(conversa.Assunto, ids.FirstOrDefault(IdExternoEmail.ServeParaEncadear));
    }
}
