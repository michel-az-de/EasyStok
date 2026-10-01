using EasyStock.Application.Ports.Output.Integration.Conexao;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Domain.Integration;

namespace EasyStock.Application.UseCases.Integracoes;

/// <summary>
/// Uma integração como a tela vê (F16, #1246). Nunca carrega segredo: só se há chave, a máscara
/// dos últimos 4, de onde veio e o último teste.
/// </summary>
/// <param name="Origem"><c>loja</c>, <c>global</c> (chave da FMA) ou null (sem chave).</param>
/// <param name="Alerta"><c>parada</c> (último teste falhou), <c>vencendo</c> (vale menos de 7 dias) ou null.</param>
/// <param name="Numero">WhatsApp: o <c>phone_number_id</c> vinculado pela FMA.</param>
public sealed record IntegracaoResult(
    string Provider,
    string Nome,
    string Categoria,
    string Ambiente,
    bool Ativo,
    bool TemCredencial,
    string? Origem,
    string? Mascara,
    DateTime? ValidoAte,
    DateTime? UltimoTesteEm,
    bool? UltimoTesteOk,
    string? UltimoTesteMensagem,
    string? Alerta,
    bool LojaGrava,
    string? GeridoPelaFma,
    string? Numero);

public sealed record SalvarChaveIntegracaoCommand(
    Guid EmpresaId,
    Guid UsuarioId,
    string Provider,
    IReadOnlyDictionary<string, string?> Campos,
    string? Ambiente,
    DateTime? ValidoAte);

public sealed record ResultadoTesteIntegracaoResult(string Provider, bool Ok, string Mensagem, DateTime TestadoEm, string Origem);

public sealed class IntegracaoNaoEncontradaException(string provider) : Exception($"Integração desconhecida: {provider}.");

/// <summary>Regras comuns: o catálogo, a janela de "vencendo" e o alerta da faixa vermelha.</summary>
public static class RegrasIntegracao
{
    public static readonly TimeSpan JanelaVencimento = TimeSpan.FromDays(7);

    public static DefinicaoIntegracao Definicao(string provider) =>
        CatalogoIntegracoes.Obter(provider) ?? throw new IntegracaoNaoEncontradaException(provider);

    public static string? Alerta(bool? ultimoTesteOk, DateTime? validoAte, DateTime agora) =>
        ultimoTesteOk == false ? "parada"
        : validoAte.HasValue && validoAte.Value <= agora + JanelaVencimento ? "vencendo"
        : null;
}

/// <summary>GET da tela: uma linha por integração do catálogo, na ordem dele.</summary>
public sealed class ListarIntegracoesUseCase(
    ICredencialIntegracaoRepository credenciais,
    IChavesGlobaisIntegracao chavesGlobais,
    IEstadoTesteIntegracaoStore estadoGlobal,
    IEmpresaRepository empresas,
    TimeProvider relogio)
{
    public async Task<IReadOnlyList<IntegracaoResult>> ExecuteAsync(Guid empresaId, CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var ativas = await credenciais.ListarAtivasDaEmpresaAsync(empresaId, ct);
        var numero = (await empresas.GetByIdAsync(empresaId))?.WhatsAppPhoneNumberId;

        return CatalogoIntegracoes.Todas.Select(d =>
        {
            var linha = ativas.FirstOrDefault(c => c.ProviderKey == d.Provider);
            if (linha is not null)
                return new IntegracaoResult(d.Provider, d.Nome, d.Categoria.ToString(), CatalogoIntegracoes.NomeAmbiente(linha.Ambiente),
                    Ativo: true, TemCredencial: true, "loja", linha.Mascara, linha.ValidoAte,
                    linha.UltimoTesteEm, linha.UltimoTesteOk, linha.UltimoTesteMensagem,
                    RegrasIntegracao.Alerta(linha.UltimoTesteOk, linha.ValidoAte, agora), d.LojaGrava, d.GeridoPelaFma, null);

            var temGlobal = chavesGlobais.Obter(d.Provider) is not null
                && (d.Provider != CatalogoIntegracoes.WhatsApp || !string.IsNullOrWhiteSpace(numero));
            var estado = temGlobal ? estadoGlobal.Obter(empresaId, d.Provider) : null;
            return new IntegracaoResult(d.Provider, d.Nome, d.Categoria.ToString(), "producao",
                Ativo: temGlobal, TemCredencial: temGlobal, temGlobal ? "global" : null, Mascara: null, ValidoAte: null,
                estado?.TestadoEm, estado?.Ok, estado?.Mensagem, RegrasIntegracao.Alerta(estado?.Ok, null, agora),
                d.LojaGrava, d.GeridoPelaFma, d.Provider == CatalogoIntegracoes.WhatsApp ? numero : null);
        }).ToList();
    }
}

/// <summary>
/// PUT: grava a chave da loja cifrada (AES-256-GCM). Substitui a anterior do mesmo provider em
/// qualquer ambiente (uma ativa por provider). Sem KEK, <see cref="ChaveMestraAusenteException"/>.
/// </summary>
public sealed class SalvarChaveIntegracaoUseCase(
    ICredencialIntegracaoRepository credenciais,
    IIntegrationCredentialResolver resolver)
{
    public async Task ExecuteAsync(SalvarChaveIntegracaoCommand command, CancellationToken ct = default)
    {
        var definicao = RegrasIntegracao.Definicao(command.Provider);
        if (!definicao.LojaGrava)
            throw new UseCaseValidationException($"{definicao.Nome} é gerido pela FMA: a loja não grava chave.");

        var campos = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var nome in definicao.CamposObrigatorios)
        {
            var valor = command.Campos.TryGetValue(nome, out var v) ? v?.Trim() : null;
            if (string.IsNullOrEmpty(valor))
                throw new UseCaseValidationException($"Preencha o campo {nome}.");
            if (valor.Length > 2048)
                throw new UseCaseValidationException($"O campo {nome} passou de 2048 caracteres.");
            campos[nome] = valor;
        }

        var ambiente = definicao.EscolheAmbiente
            ? CatalogoIntegracoes.LerAmbiente(command.Ambiente)
              ?? throw new UseCaseValidationException("Escolha o ambiente: sandbox ou producao.")
            : AmbienteIntegracao.Production;

        // Uma ativa por provider: a do outro ambiente sai junto, no mesmo commit do SalvarAsync.
        foreach (var outra in (await credenciais.ListarAtivasDoProviderAsync(command.EmpresaId, definicao.Provider, ct))
                     .Where(c => c.Ambiente != ambiente))
        {
            outra.Desativar();
            await credenciais.UpdateAsync(outra, ct);
        }

        try
        {
            await resolver.SalvarAsync(command.EmpresaId, definicao.Categoria, definicao.Provider, ambiente, campos,
                command.UsuarioId, command.ValidoAte,
                CatalogoIntegracoes.Mascara(definicao.CampoMascara is null ? null : campos[definicao.CampoMascara]), ct);
        }
        catch (ArgumentException ex) when (ex.ParamName == "validoAte")
        {
            throw new UseCaseValidationException("A validade precisa ser uma data futura.");
        }
    }
}

/// <summary>POST testar: o botão da tela. Sem chave em uso, 400 com o motivo.</summary>
public sealed class TestarIntegracaoUseCase(ExecutorTesteIntegracao executor)
{
    public async Task<ResultadoTesteIntegracaoResult> ExecuteAsync(Guid empresaId, string provider, CancellationToken ct = default)
    {
        var definicao = RegrasIntegracao.Definicao(provider);
        var registrado = await executor.TestarAsync(empresaId, definicao.Provider, ct)
            ?? throw new UseCaseValidationException($"{definicao.Nome} não tem chave para testar.");
        return new ResultadoTesteIntegracaoResult(registrado.Provider, registrado.Ok, registrado.Mensagem, registrado.TestadoEm,
            registrado.Origem == OrigemChaveIntegracao.Loja ? "loja" : "global");
    }
}

/// <summary>POST desativar: desliga a chave da loja. Com chave global, a integração volta a usá-la.</summary>
public sealed class DesativarIntegracaoUseCase(IIntegrationCredentialResolver resolver)
{
    public async Task<int> ExecuteAsync(Guid empresaId, string provider, CancellationToken ct = default)
    {
        var definicao = RegrasIntegracao.Definicao(provider);
        if (!definicao.LojaGrava)
            throw new UseCaseValidationException($"{definicao.Nome} é gerido pela FMA: a loja não desliga.");
        return await resolver.DesativarAsync(empresaId, definicao.Provider, ct);
    }
}
