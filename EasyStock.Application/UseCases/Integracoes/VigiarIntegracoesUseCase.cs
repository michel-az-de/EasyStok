using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Integracoes;

public sealed record ResultadoVigiaIntegracoes(int Testadas, int Falhas, int LembretesCriados, int LembretesResolvidos);

/// <summary>
/// Vigia das integrações (F16, #1246), uma rodada a cada 15 min: testa a chave em uso de cada
/// integração das empresas acompanhadas e avisa a dona pelo lembrete (S43). Cross-tenant: o host
/// liga o bypass de RLS e segura o advisory lock.
/// <list type="bullet">
///   <item>Falhou e não há lembrete aberto do provider: cria <see cref="TipoLembrete.IntegracaoParada"/>,
///   no máximo um por dia (a referência leva a data), e o avaliador S43 dispara o aviso.</item>
///   <item>Passou: conclui o lembrete aberto de parada daquele provider.</item>
///   <item>Validade a menos de 7 dias: <see cref="TipoLembrete.IntegracaoVencendo"/>, um por validade.</item>
/// </list>
/// O estado (último teste) fica gravado pelo <see cref="ExecutorTesteIntegracao"/> e aparece no GET,
/// que acende a faixa vermelha do console.
/// </summary>
public sealed class VigiarIntegracoesUseCase(
    IAlvosVigiaIntegracoesQuery alvos,
    ExecutorTesteIntegracao executor,
    ILembreteRepository lembretes,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<VigiarIntegracoesUseCase> logger)
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(15);

    public async Task<ResultadoVigiaIntegracoes> ExecuteAsync(CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var abertos = (await lembretes.ListarAutomaticosAbertosAsync(ct))
            .Where(l => l.Tipo == TipoLembrete.IntegracaoParada)
            .ToList();
        int testadas = 0, falhas = 0, criados = 0, resolvidos = 0;

        foreach (var empresaId in await alvos.ListarEmpresasAsync(ct))
        {
            foreach (var definicao in CatalogoIntegracoes.Todas)
            {
                TesteIntegracaoRegistrado? teste;
                try
                {
                    teste = await executor.TestarAsync(empresaId, definicao.Provider, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Uma empresa com problema não para a rodada das outras.
                    logger.LogError(ex, "Vigia: teste de {Provider} da empresa {EmpresaId} falhou.", definicao.Provider, empresaId);
                    continue;
                }
                if (teste is null) continue;
                testadas++;

                var prefixo = definicao.Provider + ":";
                var abertoDoProvider = abertos
                    .Where(l => l.EmpresaId == empresaId && l.Referencia?.StartsWith(prefixo, StringComparison.Ordinal) == true)
                    .ToList();

                if (!teste.Ok)
                {
                    falhas++;
                    if (abertoDoProvider.Count == 0
                        && await CriarAsync(empresaId, TipoLembrete.IntegracaoParada, $"{prefixo}{agora:yyyyMMdd}",
                            $"Integração parada: {definicao.Nome}. {teste.Mensagem}", agora, ct))
                        criados++;
                }
                else
                {
                    foreach (var aberto in abertoDoProvider)
                    {
                        aberto.Concluir(agora);
                        resolvidos++;
                    }
                }

                if (teste.ValidoAte is { } validoAte && validoAte <= agora + RegrasIntegracao.JanelaVencimento
                    && await CriarAsync(empresaId, TipoLembrete.IntegracaoVencendo, $"{prefixo}vence:{validoAte:yyyyMMdd}",
                        $"A chave de {definicao.Nome} vence em {validoAte:dd/MM/yyyy}. Cadastre a nova em Gestão > Integrações.", agora, ct))
                    criados++;
            }
        }

        if (criados + resolvidos > 0)
            await unitOfWork.CommitAsync();

        return new ResultadoVigiaIntegracoes(testadas, falhas, criados, resolvidos);
    }

    private async Task<bool> CriarAsync(Guid empresaId, TipoLembrete tipo, string referencia, string texto, DateTime agora, CancellationToken ct)
    {
        if (await lembretes.ExisteAutomaticoAsync(empresaId, tipo, referencia, ct)) return false;
        var limpo = texto.Length > Lembrete.TextoTamanhoMaximo ? texto[..Lembrete.TextoTamanhoMaximo] : texto;
        await lembretes.AddAsync(Lembrete.Automatico(empresaId, tipo, referencia, limpo, agora), ct);
        return true;
    }
}
