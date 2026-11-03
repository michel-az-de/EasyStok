using System.Diagnostics;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.UseCases.Atendimento;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.Ia;

/// <summary>Claude Code em modo isolado e sem ferramentas. Autenticação é do próprio CLI; nenhuma credencial é lida aqui.</summary>
public sealed class ClaudeCliMontagemFichaClient : IMontagemFichaLlmClient
{
    private readonly MontagemFichaCliOptions _opcoes;
    private readonly ILogger<ClaudeCliMontagemFichaClient> _logger;
    private readonly SemaphoreSlim _unica = new(1, 1);
    private readonly ExecutorMontagemCli _executar;

    public ClaudeCliMontagemFichaClient(IOptions<MontagemFichaCliOptions> options, ILogger<ClaudeCliMontagemFichaClient> logger)
        : this(options, logger, ProcessoClaudeFicha.ExecutarAsync) { }

    internal ClaudeCliMontagemFichaClient(IOptions<MontagemFichaCliOptions> options, ILogger<ClaudeCliMontagemFichaClient> logger, ExecutorMontagemCli executar)
    { _opcoes = options.Value; _logger = logger; _executar = executar; }

    public bool Disponivel => _opcoes.Enabled && !string.IsNullOrWhiteSpace(_opcoes.ExecutablePath)
        && Path.IsPathFullyQualified(_opcoes.ExecutablePath) && File.Exists(_opcoes.ExecutablePath)
        && _opcoes.TimeoutSegundos is >= 1 and <= 120
        && _opcoes.MaxStdoutBytes is >= 1024 and <= 262144 && _opcoes.MaxStderrBytes is >= 256 and <= 32768;

    public async Task<RespostaLlm> EnviarAsync(RequisicaoLlm requisicao, CancellationToken ct = default)
    {
        if (!Disponivel) throw new InvalidOperationException("O provedor CLI da ficha não está disponível.");
        if (requisicao.Ferramentas.Count != 1 || requisicao.Ferramentas[0].Nome != BlocosMontagemFicha.NomeFerramenta)
            throw new InvalidOperationException("O CLI aceita apenas a composição tipada da ficha.");
        if (requisicao.Mensagens.Count != 1 || requisicao.Mensagens[0].Papel != MensagemLlm.Usuario
            || requisicao.Mensagens[0].Conteudo.Count != 1 || requisicao.Mensagens[0].Conteudo[0] is not BlocoTextoLlm)
            throw new InvalidOperationException("A composição exige um único contexto textual.");
        if (!await _unica.WaitAsync(0, ct)) throw new InvalidOperationException("Já existe uma organização de ficha em andamento.");
        string? pasta = null;
        var tempo = Stopwatch.StartNew();
        try
        {
            // BlocoLlm é abstrato e não tem contrato polimórfico de JSON: projeta os textos, sem perder o contexto.
            var entrada = JsonSerializer.Serialize(requisicao.Mensagens.Select(m => new
            { papel = m.Papel, textos = m.Conteudo.OfType<BlocoTextoLlm>().Select(b => b.Texto).ToArray() }));
            if (Encoding.UTF8.GetByteCount(entrada) > 131072) throw new InvalidDataException("Contexto da ficha excede o limite.");
            pasta = Directory.CreateTempSubdirectory("easystok-ficha-cli-").FullName;
            var inicio = CriarInicio(requisicao, pasta);
            var resultado = await _executar(inicio, entrada, _opcoes, ct);
            if (resultado.ExitCode != 0) throw new InvalidOperationException("O CLI não conseguiu organizar a ficha.");
            var resposta = Traduzir(resultado.Stdout);
            _logger.LogInformation("Montagem da ficha por Claude CLI: {LatenciaMs} ms, {Entrada}+{Saida} tokens, execução isolada",
                tempo.ElapsedMilliseconds, resposta.TokensEntrada, resposta.TokensSaida);
            return resposta;
        }
        finally
        {
            // Só remove a pasta criada por esta chamada e apenas se continuar vazia.
            if (pasta is not null) { try { Directory.Delete(pasta); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            _unica.Release();
        }
    }

    internal ProcessStartInfo CriarInicio(RequisicaoLlm requisicao, string pasta)
    {
        var inicio = new ProcessStartInfo(_opcoes.ExecutablePath!)
        {
            WorkingDirectory = pasta, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argumento in new[] { "-p", "--safe-mode", "--restricted", "--tools", "", "--disable-slash-commands",
            "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}", "--setting-sources", "", "--no-session-persistence",
            "--system-prompt-snapshot", "off", "--output-format", "json", "--json-schema", requisicao.Ferramentas[0].SchemaJson,
            "--effort", "low", "--system-prompt", requisicao.System }) inicio.ArgumentList.Add(argumento);
        // Primeiro provedor/OAuth do CLI; não herda endpoints, API keys ou provedores alternativos do shell.
        // HEADROOM_* e arquivos de configuração/autenticação nunca são alterados ou lidos.
        LimparAmbiente(inicio);
        return inicio;
    }

    internal static void LimparAmbiente(ProcessStartInfo inicio)
    {
        foreach (var nome in inicio.Environment.Keys.Where(n => n.StartsWith("ANTHROPIC_", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("CLAUDE_CODE_USE_", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("OTEL_", StringComparison.OrdinalIgnoreCase)).ToArray()) inicio.Environment.Remove(nome);
        inicio.Environment.Remove("CLAUDECODE");
        inicio.Environment["CLAUDE_CODE_ENABLE_TELEMETRY"] = "0";
        inicio.Environment["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1";
        inicio.Environment["DISABLE_TELEMETRY"] = "1";
        inicio.Environment["DO_NOT_TRACK"] = "1";
    }

    internal static RespostaLlm Traduzir(string stdout)
    {
        using var doc = JsonDocument.Parse(stdout);
        var raiz = doc.RootElement;
        if (raiz.ValueKind != JsonValueKind.Object || !raiz.TryGetProperty("subtype", out var subtipo) || subtipo.GetString() != "success"
            || !raiz.TryGetProperty("is_error", out var erro) || erro.ValueKind != JsonValueKind.False
            || !raiz.TryGetProperty("structured_output", out var estrutura) || estrutura.ValueKind != JsonValueKind.Object
            || (raiz.TryGetProperty("permission_denials", out var negacoes) && (negacoes.ValueKind != JsonValueKind.Array || negacoes.GetArrayLength() != 0)))
            throw new InvalidDataException("O CLI não devolveu uma composição estruturada confirmada.");
        var entrada = 0; var saida = 0;
        if (raiz.TryGetProperty("usage", out var uso) && uso.ValueKind == JsonValueKind.Object)
        {
            long soma = 0;
            foreach (var campo in new[] { "input_tokens", "cache_creation_input_tokens", "cache_read_input_tokens" })
                if (uso.TryGetProperty(campo, out var valor) && valor.TryGetInt32(out var tokens) && tokens > 0) soma += tokens;
            entrada = (int)Math.Min(int.MaxValue, soma);
            if (uso.TryGetProperty("output_tokens", out var valorSaida) && valorSaida.TryGetInt32(out var n) && n > 0) saida = n;
        }
        var resposta = new RespostaLlm(RespostaLlm.StopToolUse,
            [new BlocoUsoFerramentaLlm("cli-ficha", BlocosMontagemFicha.NomeFerramenta, estrutura.Clone())], entrada, saida);
        _ = BlocosMontagemFicha.Traduzir(resposta);
        return resposta;
    }
}

internal sealed record ResultadoMontagemCli(int ExitCode, string Stdout);
internal delegate Task<ResultadoMontagemCli> ExecutorMontagemCli(ProcessStartInfo inicio, string entrada, MontagemFichaCliOptions opcoes, CancellationToken ct);

internal static class ProcessoClaudeFicha
{
    internal static async Task<ResultadoMontagemCli> ExecutarAsync(ProcessStartInfo inicio, string entrada, MontagemFichaCliOptions opcoes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(opcoes.TimeoutSegundos));
        using var processo = new Process { StartInfo = inicio };
        if (!processo.Start()) throw new InvalidOperationException("Não foi possível iniciar o CLI.");
        void Encerrar() { try { if (!processo.HasExited) processo.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
        var stdout = LerLimitadoAsync(processo.StandardOutput.BaseStream, opcoes.MaxStdoutBytes, Encerrar, limite.Token);
        var stderr = LerLimitadoAsync(processo.StandardError.BaseStream, opcoes.MaxStderrBytes, Encerrar, limite.Token);
        try
        {
            await processo.StandardInput.WriteAsync(entrada.AsMemory(), limite.Token);
            processo.StandardInput.Close();
            await Task.WhenAll(processo.WaitForExitAsync(limite.Token), stdout, stderr);
            return new(processo.ExitCode, await stdout);
        }
        catch
        {
            Encerrar();
            // Observa os leitores cancelados sem deixar tarefas de I/O órfãs.
            limite.Cancel();
            try { await Task.WhenAll(stdout, stderr); } catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException) { }
            throw;
        }
    }

    internal static async Task<string> LerLimitadoAsync(Stream stream, int maximo, Action encerrar, CancellationToken ct)
    {
        using var memoria = new MemoryStream();
        var buffer = new byte[4096];
        int lidos;
        while ((lidos = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (memoria.Length + lidos > maximo) { encerrar(); throw new InvalidDataException("Saída do CLI excede o limite."); }
            memoria.Write(buffer, 0, lidos);
        }
        return Encoding.UTF8.GetString(memoria.GetBuffer(), 0, (int)memoria.Length);
    }
}
