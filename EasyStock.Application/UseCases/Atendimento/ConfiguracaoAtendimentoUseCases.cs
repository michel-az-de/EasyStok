using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.UseCases.Atendimento;

public sealed record ConfiguracaoAtendimentoResult(
    Guid EmpresaId,
    string Tom,
    NivelSugestaoAtendimento NivelSugestao,
    string SaudacaoPrimeiroContato,
    string SaudacaoRetorno,
    string FraseEspera,
    string MensagemForaArea,
    int RespiroMinutos,
    int TempoPreparoPadraoMinutos,
    DateTime? WebhookVerificadoEm,
    DateTime? UltimaMensagemRecebidaEm,
    bool Ativo,
    string? ModeloRetomadaNome = null,
    string ModeloRetomadaIdioma = ConfiguracaoAtendimento.IdiomaModeloPadrao,
    int SlaRespostaMinutos = ConfiguracaoAtendimento.SlaRespostaPadraoMinutos,
    string MensagemEscalada = ConfiguracaoAtendimento.MensagemEscaladaPadrao,
    string MensagemEncaminhamento = ConfiguracaoAtendimento.MensagemEncaminhamentoPadrao,
    string? NomeDona = null,
    string? ModeloAgente = null,
    long? CotaTokensMes = null,
    IReadOnlyList<EtapaRoteiroAtendimento>? Roteiro = null,
    bool RoteiroPersonalizado = false,
    string? ObservacoesLoja = null);

public sealed record ObterConfiguracaoAtendimentoQuery(Guid EmpresaId);

public sealed class ObterConfiguracaoAtendimentoUseCase(IConfiguracaoAtendimentoRepository repository)
{
    public async Task<ConfiguracaoAtendimentoResult> ExecuteAsync(ObterConfiguracaoAtendimentoQuery query)
    {
        var config = await repository.GetOrDefaultAsync(query.EmpresaId);
        ValidarEmpresa(config, query.EmpresaId);
        return ToResult(config);
    }

    internal static void ValidarEmpresa(ConfiguracaoAtendimento? config, Guid empresaId)
    {
        if (empresaId == Guid.Empty || config is not null && config.EmpresaId != empresaId)
            throw new UseCaseValidationException("Configuração de atendimento inválida para a empresa.");
    }

    internal static ConfiguracaoAtendimentoResult ToResult(ConfiguracaoAtendimento c) => new(
        c.EmpresaId, c.Tom, c.NivelSugestao, c.SaudacaoPrimeiroContato, c.SaudacaoRetorno,
        c.FraseEspera, c.MensagemForaArea, c.RespiroMinutos, c.TempoPreparoPadraoMinutos,
        c.WebhookVerificadoEm, c.UltimaMensagemRecebidaEm, c.Ativo, c.ModeloRetomadaNome, c.ModeloRetomadaIdioma,
        c.SlaRespostaMinutos, c.MensagemEscalada, c.MensagemEncaminhamento,
        // S68 (#2597): o roteiro sai sempre efetivo (o da loja ou o padrão), com a marca de qual dos dois.
        c.NomeDona, c.ModeloAgente, c.CotaTokensMes, RoteiroAtendimento.DaLoja(c.RoteiroJson),
        !ReferenceEquals(RoteiroAtendimento.DaLoja(c.RoteiroJson), RoteiroAtendimento.Padrao), c.ObservacoesLoja);
}

public sealed record AtualizarConfiguracaoAtendimentoCommand(
    Guid EmpresaId,
    string? Tom,
    NivelSugestaoAtendimento? NivelSugestao,
    string? SaudacaoPrimeiroContato,
    string? SaudacaoRetorno,
    string? FraseEspera,
    string? MensagemForaArea,
    int? RespiroMinutos,
    int? TempoPreparoPadraoMinutos,
    bool? Ativo,
    int? SlaRespostaMinutos = null,
    string? MensagemEscalada = null,
    string? MensagemEncaminhamento = null,
    string? NomeDona = null,
    string? ModeloAgente = null,
    long? CotaTokensMes = null,
    IReadOnlyList<EtapaRoteiroAtendimento>? Roteiro = null,
    string? ObservacoesLoja = null);

/// <remarks>
/// S68 (#2597): nulo mantém cada campo do Assistente; vazio volta ao padrão (roteiro vazio, modelo vazio, cota 0). O
/// modelo só entra se estiver na lista aprovada (ADR-0066); sem a lista, nenhum entra.
/// </remarks>
public sealed class AtualizarConfiguracaoAtendimentoUseCase(
    IConfiguracaoAtendimentoRepository repository, IUnitOfWork unitOfWork, ICacheService? cacheService = null,
    ILogger<AtualizarConfiguracaoAtendimentoUseCase>? logger = null,
    IOptions<ModelosAprovadosAgenteOptions>? modelos = null)
{
    public async Task<ConfiguracaoAtendimentoResult> ExecuteAsync(AtualizarConfiguracaoAtendimentoCommand command)
    {
        var configuracao = await repository.GetByEmpresaIdAsync(command.EmpresaId);
        ObterConfiguracaoAtendimentoUseCase.ValidarEmpresa(configuracao, command.EmpresaId);
        var nova = configuracao is null;
        configuracao ??= ConfiguracaoAtendimento.CriarPadrao(command.EmpresaId);
        // A recusa do modelo ou do roteiro vem antes de qualquer mudança na entidade rastreada.
        var modeloAgente = ModeloValidado(command.ModeloAgente, configuracao.ModeloAgente);
        var roteiroJson = RoteiroValidado(command.Roteiro);

        try
        {
            configuracao.Atualizar(
                command.Tom,
                command.NivelSugestao,
                command.SaudacaoPrimeiroContato,
                command.SaudacaoRetorno,
                command.FraseEspera,
                command.MensagemForaArea,
                command.RespiroMinutos,
                command.TempoPreparoPadraoMinutos,
                command.Ativo,
                command.SlaRespostaMinutos);
            configuracao.DefinirMoldes(command.MensagemEscalada, command.MensagemEncaminhamento);
            configuracao.DefinirAssistente(command.NomeDona, modeloAgente, command.CotaTokensMes, roteiroJson,
                command.ObservacoesLoja);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or RegraDeDominioVioladaException)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        if (nova) await repository.AddAsync(configuracao);
        else await repository.UpdateAsync(configuracao);

        await unitOfWork.CommitAsync();

        // #1566: a inbox guarda o SLA em cache; sem isto o prazo novo só apareceria depois do TTL. Melhor esforço: a
        // configuração já foi persistida, então a falha do cache vira aviso e o TTL cobre.
        if (cacheService is not null)
        {
            try { await cacheService.RemoveAsync(CacheKeys.AtendimentoSlaResposta(command.EmpresaId)); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger?.LogWarning(ex, "Configuracao do atendimento salva, mas o cache do SLA nao foi invalidado (empresa {EmpresaId})", command.EmpresaId);
            }
        }

        return ObterConfiguracaoAtendimentoUseCase.ToResult(configuracao);
    }

    private string? ModeloValidado(string? modelo, string? gravado)
    {
        if (string.IsNullOrWhiteSpace(modelo)) return modelo;
        var id = modelo.Trim();
        // O já gravado que saiu da lista volta igual sem travar o resto; o turno já usa o padrão no lugar dele.
        if (string.Equals(id, gravado, StringComparison.Ordinal)) return id;
        if (modelos?.Value.Aprovado(id) != true)
            throw new UseCaseValidationException("Modelo fora da lista aprovada do Assistente.");
        return id;
    }

    private static string? RoteiroValidado(IReadOnlyList<EtapaRoteiroAtendimento>? roteiro)
    {
        if (roteiro is null) return null;
        if (roteiro.Count == 0) return string.Empty;
        if (RoteiroAtendimento.Erro(roteiro) is { } erro) throw new UseCaseValidationException(erro);
        return RoteiroAtendimento.Serializar(roteiro.Select(e => e with { Nome = e.Nome.Trim(), Instrucao = e.Instrucao.Trim() }));
    }
}

/// <summary>
/// S58 (#1391): define (ou limpa, com nome vazio) o modelo aprovado que reabre a conversa fora da janela de 24 h.
/// O nome tem de existir aprovado na Meta, com uma variável no corpo (o primeiro nome do cliente).
/// </summary>
public sealed class DefinirModeloRetomadaUseCase(IConfiguracaoAtendimentoRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<ConfiguracaoAtendimentoResult> ExecuteAsync(Guid empresaId, string? nome, string? idioma)
    {
        var configuracao = await repository.GetByEmpresaIdAsync(empresaId);
        ObterConfiguracaoAtendimentoUseCase.ValidarEmpresa(configuracao, empresaId);
        var nova = configuracao is null;
        configuracao ??= ConfiguracaoAtendimento.CriarPadrao(empresaId);

        try
        {
            configuracao.DefinirModeloRetomada(nome, idioma);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        if (nova) await repository.AddAsync(configuracao);
        else await repository.UpdateAsync(configuracao);
        await unitOfWork.CommitAsync();

        return ObterConfiguracaoAtendimentoUseCase.ToResult(configuracao);
    }
}
