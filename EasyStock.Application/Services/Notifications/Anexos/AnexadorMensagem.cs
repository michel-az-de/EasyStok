using System.Text.Json;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Domain.Entities.Notifications;

namespace EasyStock.Application.Services.Notifications.Anexos;

/// <inheritdoc />
public sealed class AnexadorMensagem(
    IEnumerable<IModeloAnexoNotificacao> modelos,
    IFileStorage armazenamento,
    ILogger<AnexadorMensagem> logger) : IAnexadorMensagem
{
    /// <summary>Metadado do template com o código do modelo de anexo.</summary>
    public const string ChaveAnexo = "anexo";

    /// <summary>Metadado do template com o formato (<c>png</c> ou <c>pdf</c>); sem ele, PNG.</summary>
    public const string ChaveFormato = "anexoFormato";

    public const string ChaveImagem = "imagem";
    public const string ChaveDocumento = "documento";

    private readonly IReadOnlyDictionary<string, IModeloAnexoNotificacao> _modelos =
        modelos.ToDictionary(m => m.Codigo, StringComparer.OrdinalIgnoreCase);

    // Uma geração por evento, modelo e formato: a audiência com várias pessoas reaproveita a mesma URL.
    private readonly Dictionary<(Guid, string, FormatoAnexo), string?> _gerados = new();

    public async Task<string?> AnexarAsync(
        EventoNotificacao evento, string? metadadosJson, IDictionary<string, object?> variaveis, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(metadadosJson)) return metadadosJson;

        var metadados = JsonSerializer.Deserialize<Dictionary<string, string>>(metadadosJson) ?? new();
        if (!metadados.TryGetValue(ChaveAnexo, out var codigo) || string.IsNullOrWhiteSpace(codigo)) return metadadosJson;

        var formato = LerFormato(metadados.GetValueOrDefault(ChaveFormato));
        metadados.Remove(ChaveAnexo);
        metadados.Remove(ChaveFormato);

        var url = await GerarUrlAsync(evento, codigo.Trim(), formato, variaveis, ct);
        if (url is not null) metadados[formato == FormatoAnexo.Pdf ? ChaveDocumento : ChaveImagem] = url;

        return JsonSerializer.Serialize(metadados);
    }

    private async Task<string?> GerarUrlAsync(
        EventoNotificacao evento, string codigo, FormatoAnexo formato, IDictionary<string, object?> variaveis, CancellationToken ct)
    {
        var chave = (evento.Id, codigo.ToLowerInvariant(), formato);
        if (_gerados.TryGetValue(chave, out var pronta)) return pronta;

        string? url = null;
        if (!_modelos.TryGetValue(codigo, out var modelo))
        {
            logger.LogWarning("Modelo de anexo {Modelo} não registrado: o aviso do evento {EventoId} sai sem anexo",
                codigo, evento.Id);
        }
        else
        {
            try
            {
                var anexo = await modelo.GerarAsync(evento.EmpresaId, variaveis, formato, ct);
                if (anexo is not null)
                {
                    // Nome aleatório: a URL é pública porque o provider de WhatsApp baixa a mídia por ela.
                    var salvo = await armazenamento.UploadAsync(new FileUploadRequest(
                        $"notificacoes/anexos/{evento.EmpresaId:N}",
                        $"{Guid.NewGuid():N}.{anexo.Extensao}",
                        anexo.ContentType,
                        anexo.Conteudo), ct);
                    url = salvo.Url;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Anexo {Modelo} do evento {EventoId} falhou: o aviso sai sem anexo",
                    codigo, evento.Id);
            }
        }

        _gerados[chave] = url;
        return url;
    }

    private static FormatoAnexo LerFormato(string? valor) =>
        string.Equals(valor?.Trim(), "pdf", StringComparison.OrdinalIgnoreCase) ? FormatoAnexo.Pdf : FormatoAnexo.Png;
}
