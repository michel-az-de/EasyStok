using System.Diagnostics;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Enums.Ia;
using EasyStock.Domain.Integration;

namespace EasyStock.Application.Services.Ia;

/// <summary>
/// #3064 (S2b de #2948): escolhe, por loja, por onde a chamada de IA sai. Sem a flag <c>ia.provedor_por_loja</c> (ou sem
/// empresa), delega ao cliente do <c>.env</c> com o registro da S2a, sem mudar nada. Com a flag, segue a spec §5:
/// 0. a empresa da chamada tem de ser o tenant atual; 1. modelo pedido ligado, senão o padrão; 2. provedor ligado e gasto
/// do mês abaixo do orçamento (cota suave, D6); 3. chave do cofre; 4. grava <c>chamada_ia</c> com provedor, modelo e
/// custo pela faixa horária (D7). Scoped: flag e rota ficam em memória durante a requisição.
/// </summary>
public sealed class RoteadorIaLlmClient(
    IAgenteLlmClient doEnv,
    ITenantFeatureFlagRepository flags,
    ITenantAtual tenant,
    IRotasIa rotas,
    IIntegrationCredentialResolver cofre,
    IClienteProvedorIa provedores,
    IRegistroChamadaIa registro,
    TimeProvider relogio,
    ILogger<RoteadorIaLlmClient> logger) : IAgenteLlmClient
{
    /// <summary>Fração do orçamento a partir da qual cada chamada avisa no log.</summary>
    public const decimal FracaoAviso = 0.8m;

    private readonly Dictionary<Guid, bool> _flag = [];
    private readonly Dictionary<(Guid, string), RotaIa?> _rotas = [];

    public IAgenteLlmClient DoEnv => doEnv;

    /// <summary>Global, do <c>.env</c>; quem tem a empresa usa <see cref="DisponivelAsync"/>.</summary>
    public bool Disponivel => doEnv.Disponivel;

    public async Task<bool> DisponivelAsync(Guid empresaId, CancellationToken ct = default)
    {
        if (!await PorLojaAsync(empresaId, ct)) return await doEnv.DisponivelAsync(empresaId, ct);
        try
        {
            await AvaliarAsync(empresaId, null, FinalidadeIa.Outra, gravarCota: false, ct);
            return true;
        }
        catch (ProvedorIaIndisponivelException)
        {
            return false;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Banco ou cofre com falha: indisponível (a equipe atende), nunca 500 no portão.
            logger.LogError(ex, "IA da empresa {EmpresaId}: falha ao checar provedor, gasto ou chave; fica indisponível.", empresaId);
            return false;
        }
    }

    public async Task<RespostaLlm> EnviarAsync(RequisicaoLlm requisicao, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requisicao);
        if (!await PorLojaAsync(requisicao.EmpresaId, ct)) return await doEnv.EnviarAsync(requisicao, ct);

        var (rota, chave) = await AvaliarAsync(requisicao.EmpresaId, requisicao.Modelo, requisicao.Finalidade, gravarCota: true, ct);
        var destino = new DestinoIa(rota.ProvedorId, rota.BaseUrl, rota.Autenticacao, chave, rota.Modelo.IdExterno);
        var cronometro = Stopwatch.StartNew();
        RespostaLlm resposta;
        try
        {
            resposta = await provedores.EnviarAsync(destino, requisicao with { Modelo = rota.Modelo.IdExterno }, ct);
        }
        catch (Exception ex) when (GravacaoChamadaIa.NaoGrava(ex, ct))
        {
            throw;
        }
        catch (Exception ex)
        {
            await GravarAsync(requisicao, rota, null, GravacaoChamadaIa.Desfecho(ex), cronometro.ElapsedMilliseconds);
            throw;
        }
        await GravarAsync(requisicao, rota, resposta, DesfechoChamadaIa.Ok, cronometro.ElapsedMilliseconds);
        return resposta;
    }

    private async Task<bool> PorLojaAsync(Guid empresaId, CancellationToken ct)
    {
        if (empresaId == Guid.Empty) return false;
        if (_flag.TryGetValue(empresaId, out var ligada)) return ligada;
        try
        {
            ligada = (await flags.ListarAtivasAsync(empresaId, ct)).Contains(FeatureCatalogo.IaProvedorPorLoja);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Sem conseguir ler a flag, a loja segue no .env, como antes da S2b: a leitura nunca derruba o turno.
            logger.LogError(ex, "IA da empresa {EmpresaId}: falha ao ler a flag {Flag}; segue pelo .env.", empresaId,
                FeatureCatalogo.IaProvedorPorLoja);
            ligada = false;
        }
        _flag[empresaId] = ligada;
        return ligada;
    }

    /// <summary>Passos 0 a 3 da spec §5. Qualquer barreira lança <see cref="ProvedorIaIndisponivelException"/>.</summary>
    private async Task<(RotaIa Rota, string Chave)> AvaliarAsync(
        Guid empresaId, string? modeloPedido, FinalidadeIa finalidade, bool gravarCota, CancellationToken ct)
    {
        if (tenant.EmpresaId == Guid.Empty || tenant.EmpresaId != empresaId)
        {
            logger.LogError("IA recusada: a empresa {EmpresaId} da chamada não é o tenant atual {Tenant}.", empresaId, tenant.EmpresaId);
            throw new ProvedorIaIndisponivelException("A empresa da chamada de IA não é o tenant atual.");
        }

        var rota = await RotaAsync(empresaId, modeloPedido, ct);
        if (rota is null)
        {
            logger.LogWarning("IA da empresa {EmpresaId} sem provedor ligado ou modelo padrão: a equipe atende.", empresaId);
            throw new ProvedorIaIndisponivelException("A loja não tem provedor de IA ligado.");
        }

        if (rota.OrcamentoMensalUsd is { } orcamento)
        {
            if (rota.Modelo.PrecoEntrada is null || rota.Modelo.PrecoSaida is null)
            {
                // Custo nulo deixaria a cota muda: com orçamento, modelo sem preço não roda.
                logger.LogError("IA sem preço: o modelo {Modelo} da empresa {EmpresaId} não tem preço e o provedor {ProvedorId} tem orçamento.",
                    rota.Modelo.IdExterno, empresaId, rota.ProvedorId);
                throw new ProvedorIaIndisponivelException("O modelo de IA da loja não tem preço e o provedor tem orçamento.");
            }

            var gasto = await rotas.GastoDoMesAsync(empresaId, rota.ProvedorId, Agora(), ct);
            if (gasto >= orcamento)
            {
                logger.LogWarning("Cota de IA estourada: empresa {EmpresaId}, provedor {ProvedorId}, gasto US$ {Gasto} de US$ {Orcamento}.",
                    empresaId, rota.ProvedorId, gasto, orcamento);
                if (gravarCota)
                    await GravacaoChamadaIa.GravarAsync(registro, Chamada(empresaId, finalidade, rota, null,
                        DesfechoChamadaIa.CotaEstourada, 0), null, logger);
                throw new ProvedorIaIndisponivelException("O orçamento de IA do mês acabou.");
            }
            if (gasto >= orcamento * FracaoAviso)
                logger.LogWarning("Cota de IA perto do fim: empresa {EmpresaId}, provedor {ProvedorId}, gasto US$ {Gasto} de US$ {Orcamento}.",
                    empresaId, rota.ProvedorId, gasto, orcamento);
        }

        var credencial = await cofre.ObterAsync<ChaveProvedorIa>(empresaId, rota.ProviderKey, AmbienteIntegracao.Production, ct);
        if (string.IsNullOrWhiteSpace(credencial?.ApiKey))
        {
            logger.LogWarning("IA da empresa {EmpresaId} sem chave no cofre para o provedor {ProvedorId}.", empresaId, rota.ProvedorId);
            throw new ProvedorIaIndisponivelException("O provedor de IA da loja não tem chave.");
        }
        return (rota, credencial.ApiKey);
    }

    private async Task<RotaIa?> RotaAsync(Guid empresaId, string? modeloPedido, CancellationToken ct)
    {
        var chave = (empresaId, modeloPedido ?? "");
        if (_rotas.TryGetValue(chave, out var memo)) return memo;
        var rota = await rotas.ObterAsync(empresaId, modeloPedido, ct);
        if (rota is { CaiuNoPadrao: true } && !string.IsNullOrWhiteSpace(modeloPedido))
            logger.LogInformation("IA da empresa {EmpresaId}: modelo {Pedido} sem cadastro ligado, usando o padrão {Padrao}.",
                empresaId, modeloPedido, rota.Modelo.IdExterno);
        _rotas[chave] = rota;
        return rota;
    }

    private DateTime Agora() => relogio.GetUtcNow().UtcDateTime;

    private ChamadaIa Chamada(Guid empresaId, FinalidadeIa finalidade, RotaIa rota, RespostaLlm? resposta,
        DesfechoChamadaIa desfecho, long duracaoMs)
    {
        var agora = Agora();
        var custo = resposta is null
            ? null
            : rota.Modelo.Custo(resposta.TokensEntrada, resposta.TokensSaida, resposta.TokensCacheLido, FaixaHorariaIa.ForaDoPico(agora));
        return ChamadaIa.Registrar(empresaId, rota.ProvedorId, rota.Modelo.Id, resposta?.Modelo ?? rota.Modelo.IdExterno,
            finalidade, null, resposta?.TokensEntrada ?? 0, resposta?.TokensSaida ?? 0, resposta?.TokensCacheLido ?? 0,
            resposta?.TokensCacheEscrito ?? 0, custo, (int)Math.Min(duracaoMs, int.MaxValue), desfecho, agora);
    }

    private async Task GravarAsync(RequisicaoLlm requisicao, RotaIa rota, RespostaLlm? resposta, DesfechoChamadaIa desfecho, long duracaoMs)
    {
        ChamadaIa chamada;
        try
        {
            chamada = Chamada(requisicao.EmpresaId, requisicao.Finalidade, rota, resposta, desfecho, duracaoMs);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RegistroChamadaIa perdido: falha ao registrar chamada de IA da empresa {EmpresaId} ({Finalidade}).",
                requisicao.EmpresaId, requisicao.Finalidade);
            return;
        }
        await GravacaoChamadaIa.GravarAsync(registro, chamada, null, logger);
    }
}
