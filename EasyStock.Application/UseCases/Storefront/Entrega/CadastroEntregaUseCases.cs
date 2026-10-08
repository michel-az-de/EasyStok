using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.UseCases.Storefront.Entrega;

// ── Contratos ──────────────────────────────────────────────────────────

public sealed record JanelaEntregaInput(int DiaDaSemana, TimeOnly HoraInicio, TimeOnly HoraFim, int CapacidadeMaxima, string Label);

public sealed record JanelaEntregaResult(
    Guid Id, int DiaDaSemana, TimeOnly HoraInicio, TimeOnly HoraFim, int CapacidadeMaxima, string Label, bool Ativa)
{
    internal static JanelaEntregaResult De(JanelaEntrega j) =>
        new(j.Id, j.DiaDaSemana, j.HoraInicio, j.HoraFim, j.CapacidadeMaxima, j.Label, j.Ativa);
}

/// <summary>Zona por faixa de CEP (<c>CepInicio</c> e <c>CepFim</c>) ou por bairros; nunca os dois.</summary>
public sealed record FreteZonaInput(
    string Label, decimal Valor, int TempoEstimadoMinutos, int Ordem,
    string? CepInicio, string? CepFim, IReadOnlyList<string>? Bairros);

public sealed record FreteZonaResult(
    Guid Id, string Label, decimal Valor, int TempoEstimadoMinutos, int Ordem, bool Ativa,
    string TipoCobertura, string? CepInicio, string? CepFim, IReadOnlyList<string> Bairros)
{
    internal static FreteZonaResult De(FreteZona z) => new(
        z.Id, z.Label, z.Valor, z.TempoEstimadoMinutos, z.Ordem, z.Ativa, z.TipoCobertura, z.CepInicio, z.CepFim,
        string.IsNullOrWhiteSpace(z.BairrosJson) ? [] : JsonSerializer.Deserialize<List<string>>(z.BairrosJson) ?? []);
}

public sealed record BloqueioEntregaInput(DateOnly Data, string Motivo, Guid? JanelaEspecificaId);

public sealed record BloqueioEntregaResult(Guid Id, DateOnly Data, string Motivo, Guid? JanelaEspecificaId)
{
    internal static BloqueioEntregaResult De(BloqueioEntrega b) => new(b.Id, b.Data, b.Motivo, b.JanelaEspecificaId);
}

/// <summary>A empresa do token ainda não tem vitrine: não há onde cadastrar.</summary>
public sealed class LojaSemVitrineException() : Exception("Sua vitrine ainda não foi criada.");

/// <summary>Janela, zona ou bloqueio inexistente ou de outra loja: 404, sem vazar existência.</summary>
public sealed class CadastroEntregaNaoEncontradoException(string oQue, Guid id) : Exception($"{oQue} {id} não encontrada.");

/// <summary>
/// A loja da empresa do token (S45, ADR-0031). As tabelas de entrega não têm <c>EmpresaId</c>: o escopo
/// é o <c>StorefrontId</c>, e todo registro lido por id é conferido contra ele.
/// </summary>
internal static class LojaDoTenant
{
    public static async Task<StorefrontEntity> ObterAsync(IStorefrontRepository lojas, Guid empresaId, CancellationToken ct) =>
        await lojas.GetByEmpresaAsync(empresaId, ct) ?? throw new LojaSemVitrineException();
}

// ── Janelas ────────────────────────────────────────────────────────────

/// <summary>S45: janelas de entrega da loja. Desativar tira do checkout sem apagar o histórico de vagas.</summary>
public sealed class CadastroJanelasEntregaUseCase(
    IStorefrontRepository lojas, IJanelaEntregaRepository janelas, IUnitOfWork unitOfWork)
{
    public async Task<IReadOnlyList<JanelaEntregaResult>> ListarAsync(Guid empresaId, CancellationToken ct = default)
    {
        var loja = await LojaDoTenant.ObterAsync(lojas, empresaId, ct);
        return (await janelas.GetTodasDoStorefrontAsync(loja.Id, ct)).Select(JanelaEntregaResult.De).ToList();
    }

    public async Task<JanelaEntregaResult> CriarAsync(Guid empresaId, JanelaEntregaInput input, CancellationToken ct = default)
    {
        var loja = await LojaDoTenant.ObterAsync(lojas, empresaId, ct);
        var janela = JanelaEntrega.Criar(loja.Id, input.DiaDaSemana, input.HoraInicio, input.HoraFim, input.CapacidadeMaxima, input.Label);
        await janelas.AddAsync(janela, ct);
        await unitOfWork.CommitAsync();
        return JanelaEntregaResult.De(janela);
    }

    public Task<JanelaEntregaResult> AtualizarAsync(Guid empresaId, Guid id, JanelaEntregaInput input, CancellationToken ct = default) =>
        AlterarAsync(empresaId, id, j => j.Atualizar(input.DiaDaSemana, input.HoraInicio, input.HoraFim, input.CapacidadeMaxima, input.Label), ct);

    public Task<JanelaEntregaResult> DefinirAtivaAsync(Guid empresaId, Guid id, bool ativa, CancellationToken ct = default) =>
        AlterarAsync(empresaId, id, j => { if (ativa) j.Ativar(); else j.Desativar(); }, ct);

    /// <summary>
    /// #1440: apaga a janela criada por engano. Com vaga (mesmo liberada) ou bloqueio apontando para ela,
    /// recusa: o histórico não some e as FKs são RESTRICT. Nesse caso o caminho é pausar.
    /// </summary>
    public async Task ExcluirAsync(Guid empresaId, Guid id, CancellationToken ct = default)
    {
        var loja = await LojaDoTenant.ObterAsync(lojas, empresaId, ct);
        var janela = await ObterDaLojaAsync(janelas, loja.Id, id, ct);
        if (await janelas.TemUsoAsync(janela.Id, ct))
            throw new UseCaseValidationException("Esta janela já tem pedido ou bloqueio marcado. Pause em vez de excluir.");
        await janelas.RemoveAsync(janela, ct);
        await unitOfWork.CommitAsync();
    }

    private async Task<JanelaEntregaResult> AlterarAsync(Guid empresaId, Guid id, Action<JanelaEntrega> alteracao, CancellationToken ct)
    {
        var loja = await LojaDoTenant.ObterAsync(lojas, empresaId, ct);
        var janela = await ObterDaLojaAsync(janelas, loja.Id, id, ct);
        alteracao(janela);
        await janelas.UpdateAsync(janela, ct);
        await unitOfWork.CommitAsync();
        return JanelaEntregaResult.De(janela);
    }

    internal static async Task<JanelaEntrega> ObterDaLojaAsync(IJanelaEntregaRepository janelas, Guid lojaId, Guid id, CancellationToken ct)
    {
        var janela = await janelas.GetByIdAsync(id, ct);
        return janela is not null && janela.StorefrontId == lojaId
            ? janela
            : throw new CadastroEntregaNaoEncontradoException("Janela de entrega", id);
    }
}

// ── Zonas ──────────────────────────────────────────────────────────────

/// <summary>S45: zonas de frete da loja, por faixa de CEP ou por bairros.</summary>
public sealed class CadastroZonasFreteUseCase(
    IStorefrontRepository lojas, IFreteZonaRepository zonas, IUnitOfWork unitOfWork)
{
    public async Task<IReadOnlyList<FreteZonaResult>> ListarAsync(Guid empresaId, CancellationToken ct = default)
    {
        var loja = await LojaDoTenant.ObterAsync(lojas, empresaId, ct);
        return (await zonas.GetTodasDoStorefrontAsync(loja.Id, ct)).Select(FreteZonaResult.De).ToList();
    }

    public async Task<FreteZonaResult> CriarAsync(Guid empresaId, FreteZonaInput input, CancellationToken ct = default)
    {
        var porCep = PorCep(input);
        var loja = await LojaDoTenant.ObterAsync(lojas, empresaId, ct);
        var zona = porCep
            ? FreteZona.CriarPorCep(loja.Id, input.Label, input.CepInicio!, input.CepFim!, input.Valor, input.TempoEstimadoMinutos, input.Ordem)
            : FreteZona.CriarPorBairros(loja.Id, input.Label, [.. input.Bairros!], input.Valor, input.TempoEstimadoMinutos, input.Ordem);
        await zonas.AddAsync(zona, ct);
        await unitOfWork.CommitAsync();
        return FreteZonaResult.De(zona);
    }

    public Task<FreteZonaResult> AtualizarAsync(Guid empresaId, Guid id, FreteZonaInput input, CancellationToken ct = default)
    {
        var porCep = PorCep(input);
        return AlterarAsync(empresaId, id, z =>
        {
            // Cobertura primeiro: se for inválida, os dados também não mudam.
            if (porCep) z.DefinirCoberturaPorCep(input.CepInicio!, input.CepFim!);
            else z.DefinirCoberturaPorBairros([.. input.Bairros!]);
            z.AtualizarDados(input.Label, input.Valor, input.TempoEstimadoMinutos, input.Ordem);
        }, ct);
    }

    public Task<FreteZonaResult> DefinirAtivaAsync(Guid empresaId, Guid id, bool ativa, CancellationToken ct = default) =>
        AlterarAsync(empresaId, id, z => { if (ativa) z.Ativar(); else z.Desativar(); }, ct);

    private async Task<FreteZonaResult> AlterarAsync(Guid empresaId, Guid id, Action<FreteZona> alteracao, CancellationToken ct)
    {
        var loja = await LojaDoTenant.ObterAsync(lojas, empresaId, ct);
        var zona = await zonas.GetByIdAsync(id, ct);
        if (zona is null || zona.StorefrontId != loja.Id)
            throw new CadastroEntregaNaoEncontradoException("Zona de frete", id);
        alteracao(zona);
        await zonas.UpdateAsync(zona, ct);
        await unitOfWork.CommitAsync();
        return FreteZonaResult.De(zona);
    }

    /// <summary>Exatamente um tipo de cobertura: faixa de CEP completa ou lista de bairros.</summary>
    private static bool PorCep(FreteZonaInput input)
    {
        var temCep = !string.IsNullOrWhiteSpace(input.CepInicio) || !string.IsNullOrWhiteSpace(input.CepFim);
        var temBairros = input.Bairros is { Count: > 0 };
        if (temCep == temBairros)
            throw new UseCaseValidationException("Informe a faixa de CEP (início e fim) ou a lista de bairros, e só um dos dois.");
        return temCep;
    }
}

// ── Bloqueios ──────────────────────────────────────────────────────────

/// <summary>S45: bloqueios pontuais (feriado, férias) do dia inteiro ou de uma janela da loja.</summary>
public sealed class CadastroBloqueiosEntregaUseCase(
    IStorefrontRepository lojas, IBloqueioEntregaRepository bloqueios, IJanelaEntregaRepository janelas, IUnitOfWork unitOfWork)
{
    public const int PeriodoMaximoDias = 366;

    public async Task<IReadOnlyList<BloqueioEntregaResult>> ListarAsync(
        Guid empresaId, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        if (ate < de)
            throw new UseCaseValidationException("O fim do período vem antes do início.");
        if (ate.DayNumber - de.DayNumber > PeriodoMaximoDias)
            throw new UseCaseValidationException($"Período máximo de {PeriodoMaximoDias} dias.");

        var loja = await LojaDoTenant.ObterAsync(lojas, empresaId, ct);
        return (await bloqueios.GetByStorefrontPeriodoAsync(loja.Id, de, ate, ct)).Select(BloqueioEntregaResult.De).ToList();
    }

    public async Task<BloqueioEntregaResult> CriarAsync(Guid empresaId, BloqueioEntregaInput input, CancellationToken ct = default)
    {
        var loja = await LojaDoTenant.ObterAsync(lojas, empresaId, ct);
        if (input.JanelaEspecificaId is { } janelaId && janelaId != Guid.Empty)
            await CadastroJanelasEntregaUseCase.ObterDaLojaAsync(janelas, loja.Id, janelaId, ct);

        var bloqueio = BloqueioEntrega.Criar(loja.Id, input.Data, input.Motivo, input.JanelaEspecificaId);
        await bloqueios.AddAsync(bloqueio, ct);
        await unitOfWork.CommitAsync();
        return BloqueioEntregaResult.De(bloqueio);
    }

    public async Task RemoverAsync(Guid empresaId, Guid id, CancellationToken ct = default)
    {
        var loja = await LojaDoTenant.ObterAsync(lojas, empresaId, ct);
        var bloqueio = await bloqueios.GetByIdAsync(id, ct);
        if (bloqueio is null || bloqueio.StorefrontId != loja.Id)
            throw new CadastroEntregaNaoEncontradoException("Bloqueio de entrega", id);
        await bloqueios.RemoveAsync(bloqueio, ct);
        await unitOfWork.CommitAsync();
    }
}
