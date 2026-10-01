using EasyStock.Application.Ports.Output.Integration.Conexao;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Domain.Integration;

namespace EasyStock.Application.UseCases.Integracoes;

/// <summary>Resultado do teste já gravado (na linha da chave da loja ou no estado da chave global).</summary>
public sealed record TesteIntegracaoRegistrado(
    string Provider,
    bool Ok,
    string Mensagem,
    DateTime TestadoEm,
    OrigemChaveIntegracao Origem,
    DateTime? ValidoAte);

/// <summary>
/// Núcleo do botão Testar e do vigia (F16, #1246): acha a chave em uso (a da loja primeiro; na
/// falta, a global da FMA), chama o testador do provider com teto de 5 s e grava o resultado.
/// Sem chave em uso devolve null: não configurada não é falha.
/// </summary>
public sealed class ExecutorTesteIntegracao(
    IEnumerable<ITestadorIntegracao> testadores,
    ICredencialIntegracaoRepository credenciais,
    IIntegrationCredentialResolver resolver,
    IChavesGlobaisIntegracao chavesGlobais,
    IEstadoTesteIntegracaoStore estadoGlobal,
    IEmpresaRepository empresas,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<ExecutorTesteIntegracao> logger)
{
    public static readonly TimeSpan Teto = TimeSpan.FromSeconds(5);

    public async Task<TesteIntegracaoRegistrado?> TestarAsync(Guid empresaId, string provider, CancellationToken ct = default)
    {
        var definicao = CatalogoIntegracoes.Obter(provider)
            ?? throw new UseCaseValidationException($"Integração desconhecida: {provider}.");
        var testador = testadores.FirstOrDefault(t => string.Equals(t.Provider, definicao.Provider, StringComparison.Ordinal));
        if (testador is null) return null;

        var linha = definicao.LojaGrava
            ? (await credenciais.ListarAtivasDoProviderAsync(empresaId, definicao.Provider, ct)).FirstOrDefault()
            : null;

        var chave = linha is not null
            ? await ChaveDaLojaAsync(empresaId, linha, ct)
            : await ChaveGlobalAsync(empresaId, definicao.Provider);
        if (chave is null) return null;

        var resultado = await ExecutarComTetoAsync(testador, chave, ct);
        var agora = relogio.GetUtcNow().UtcDateTime;

        if (linha is not null)
        {
            linha.RegistrarTeste(agora, resultado.Ok, resultado.Mensagem);
            await credenciais.UpdateAsync(linha, ct);
            await unitOfWork.CommitAsync();
        }
        else
        {
            estadoGlobal.Registrar(empresaId, definicao.Provider, new EstadoTesteIntegracao(agora, resultado.Ok, resultado.Mensagem));
        }

        return new TesteIntegracaoRegistrado(definicao.Provider, resultado.Ok, resultado.Mensagem, agora, chave.Origem, linha?.ValidoAte);
    }

    private async Task<ChaveParaTeste?> ChaveDaLojaAsync(Guid empresaId, CredencialIntegracao linha, CancellationToken ct)
    {
        Dictionary<string, string>? campos;
        try
        {
            campos = await resolver.ObterAsync<Dictionary<string, string>>(empresaId, linha.ProviderKey, linha.Ambiente, ct);
        }
        catch (Exception ex) when (ex is ChaveMestraAusenteException or System.Security.Cryptography.CryptographicException)
        {
            // KEK trocada ou ausente: a chave existe mas não abre. É falha a mostrar, não exceção.
            logger.LogWarning("Credencial {Provider} da empresa {EmpresaId} não decifrou: {Tipo}.",
                linha.ProviderKey, empresaId, ex.GetType().Name);
            campos = null;
        }

        return campos is null
            ? new ChaveParaTeste(linha.ProviderKey, linha.Ambiente, new Dictionary<string, string>(), OrigemChaveIntegracao.Loja)
            : new ChaveParaTeste(linha.ProviderKey, linha.Ambiente, campos, OrigemChaveIntegracao.Loja);
    }

    private async Task<ChaveParaTeste?> ChaveGlobalAsync(Guid empresaId, string provider)
    {
        var global = chavesGlobais.Obter(provider);
        if (global is null) return null;

        var campos = new Dictionary<string, string>(global);
        if (provider == CatalogoIntegracoes.WhatsApp)
        {
            // O token é da app da FMA; o número é o da loja, vinculado pela FMA.
            var numero = (await empresas.GetByIdAsync(empresaId))?.WhatsAppPhoneNumberId;
            if (string.IsNullOrWhiteSpace(numero)) return null;
            campos[CatalogoIntegracoes.CampoPhoneNumberId] = numero;
        }

        return new ChaveParaTeste(provider, AmbienteIntegracao.Production, campos, OrigemChaveIntegracao.Global);
    }

    private async Task<ResultadoTesteIntegracao> ExecutarComTetoAsync(ITestadorIntegracao testador, ChaveParaTeste chave, CancellationToken ct)
    {
        if (chave.Campos.Count == 0)
            return ResultadoTesteIntegracao.Falhou("A chave salva não abre com a chave-mestra atual. Cadastre de novo.");

        using var teto = new CancellationTokenSource(Teto, relogio);
        using var ligado = CancellationTokenSource.CreateLinkedTokenSource(ct, teto.Token);
        try
        {
            return await testador.TestarAsync(chave, ligado.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ResultadoTesteIntegracao.Falhou($"O provedor não respondeu em {Teto.TotalSeconds:0} s.");
        }
        catch (HttpRequestException)
        {
            return ResultadoTesteIntegracao.Falhou("Não foi possível falar com o provedor. Confira a internet do servidor.");
        }
    }
}
