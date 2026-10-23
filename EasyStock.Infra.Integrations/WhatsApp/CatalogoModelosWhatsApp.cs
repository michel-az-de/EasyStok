using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Integration;

namespace EasyStock.Infra.Integrations.WhatsApp;

public sealed partial class CatalogoModelosWhatsApp(
    HttpClient http, IEmpresaRepository empresas, IIntegrationCredentialResolver credenciais) : ICatalogoModelosAtendimento
{
    public async Task<IReadOnlyList<ModeloAtendimento>> ListarAsync(Guid empresaId, CancellationToken ct = default)
    {
        var empresa = await empresas.GetByIdAsync(empresaId);
        var credencial = await credenciais.ObterAsync<CredencialWhatsAppMeta>(
            empresaId, CredencialWhatsAppMeta.ProviderKey, AmbienteIntegracao.Production, ct);
        if (empresaId == Guid.Empty || empresa?.Id != empresaId || string.IsNullOrWhiteSpace(empresa.WhatsAppPhoneNumberId)
            || string.IsNullOrWhiteSpace(credencial?.WabaId) || string.IsNullOrWhiteSpace(credencial.AccessToken)
            || credencial.PhoneNumberId != empresa.WhatsAppPhoneNumberId)
            throw CatalogoModelosException.SemConexao("Conecte o WhatsApp da empresa em Integrações para consultar seus modelos aprovados.");

        var modelos = new List<ModeloAtendimento>();
        var cursores = new HashSet<string>(StringComparer.Ordinal);
        string? depois = null;
        // O cursor vai para a mesma WABA. Nunca seguimos paging.next, que pode conter token ou outra origem.
        for (var pagina = 0; pagina < 100; pagina++)
        {
            var url = $"{Uri.EscapeDataString(credencial.WabaId)}/message_templates"
                + "?fields=name,language,status,category,components,parameter_format&limit=100"
                + (depois is null ? "" : $"&after={Uri.EscapeDataString(depois)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credencial.AccessToken);
            try
            {
                using var response = await http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                    throw new CatalogoModelosException("Não foi possível consultar os modelos na Meta. Confira a conexão e tente novamente.");
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (!json.RootElement.TryGetProperty("data", out var dados) || dados.ValueKind != JsonValueKind.Array)
                    throw new CatalogoModelosException("A Meta devolveu uma lista de modelos inválida.");
                foreach (var item in dados.EnumerateArray())
                    if (Texto(item, "status") == "APPROVED") modelos.Add(Ler(item));
                if (!json.RootElement.TryGetProperty("paging", out var paging) || Texto(paging, "next") is null)
                    return modelos.OrderBy(m => m.Nome, StringComparer.Ordinal).ThenBy(m => m.Idioma).ToList();
                depois = paging.TryGetProperty("cursors", out var cursors) ? Texto(cursors, "after") : null;
                if (depois is null || !cursores.Add(depois)) break;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested)
            {
                throw new CatalogoModelosException("A consulta dos modelos não foi confirmada. Tente novamente.");
            }
        }
        throw new CatalogoModelosException("A lista de modelos da Meta não pôde ser carregada por completo. Tente novamente.");
    }

    private static ModeloAtendimento Ler(JsonElement item)
    {
        string? corpo = null, cabecalho = null, rodape = null, indisponivel = null;
        var nome = Texto(item, "name") ?? "";
        var idioma = Texto(item, "language") ?? "";
        var categoria = Texto(item, "category") ?? "";
        if (categoria is not ("UTILITY" or "MARKETING") || Texto(item, "parameter_format") == "NAMED")
            indisponivel = "Este formato de modelo ainda não pode ser enviado pelo atendimento.";
        if (item.TryGetProperty("components", out var componentes) && componentes.ValueKind == JsonValueKind.Array)
            foreach (var componente in componentes.EnumerateArray())
                switch (Texto(componente, "type"))
                {
                    case "BODY": corpo = Texto(componente, "text"); break;
                    case "HEADER" when Texto(componente, "format") == "TEXT":
                        cabecalho = Texto(componente, "text");
                        if (cabecalho?.Contains("{{", StringComparison.Ordinal) == true)
                            indisponivel = "Parâmetros no cabeçalho ainda não são aceitos aqui.";
                        break;
                    case "FOOTER": rodape = Texto(componente, "text"); break;
                    default: indisponivel = "Modelos com mídia, botões ou outros componentes ainda não são aceitos aqui."; break;
                }
        var parametros = Parametro().Matches(corpo ?? "").Select(m => int.Parse(m.Groups[1].Value)).Distinct().Order().ToArray();
        if (string.IsNullOrWhiteSpace(nome) || string.IsNullOrWhiteSpace(idioma) || string.IsNullOrWhiteSpace(corpo)
            || !parametros.SequenceEqual(Enumerable.Range(1, parametros.Length))
            || Parametro().Replace(corpo ?? "", "").Contains("{{", StringComparison.Ordinal))
            indisponivel = "O conteúdo deste modelo não pode ser preenchido aqui.";
        return new ModeloAtendimento(nome, idioma, categoria, corpo ?? "", parametros.Length, cabecalho, rodape, indisponivel);
    }

    [GeneratedRegex(@"\{\{([1-9][0-9]?)\}\}")]
    private static partial Regex Parametro();

    private static string? Texto(JsonElement item, string campo) =>
        item.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

public sealed class CatalogoModelosIndisponivel : ICatalogoModelosAtendimento
{
    public Task<IReadOnlyList<ModeloAtendimento>> ListarAsync(Guid empresaId, CancellationToken ct = default) =>
        throw CatalogoModelosException.SemConexao("O envio real do WhatsApp não está conectado. Configure a conexão em Integrações.");
}
