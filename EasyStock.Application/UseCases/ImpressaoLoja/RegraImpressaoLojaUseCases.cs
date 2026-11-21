using System.Text.Json;
using EasyStock.Application.Validators;

namespace EasyStock.Application.UseCases.ImpressaoLoja;

/// <param name="Impressao">Texto canônico da regra, ou <c>null</c> se a loja nunca configurou (o cliente usa o padrão dele).</param>
public sealed record RegraImpressaoLojaResult(Guid LojaId, int Versao, JsonElement? Impressao, DateTime? AlteradoEm)
{
    internal static RegraImpressaoLojaResult De(Guid lojaId, RegraImpressaoSalva salva)
    {
        var regra = RegraImpressaoNormalizador.DoJson(salva.Json);
        return new RegraImpressaoLojaResult(
            lojaId,
            salva.Versao,
            regra is null ? null : Elemento(RegraImpressaoNormalizador.ParaJson(regra)),
            salva.AlteradoEm);
    }

    internal static JsonElement Elemento(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}

public sealed record ObterRegraImpressaoLojaQuery(Guid EmpresaId, Guid LojaId);

public class ObterRegraImpressaoLojaUseCase(IRegraImpressaoLojaRepository repositorio)
{
    /// <returns><c>null</c> quando a loja não existe ou não é da empresa.</returns>
    public async Task<RegraImpressaoLojaResult?> ExecuteAsync(ObterRegraImpressaoLojaQuery query, CancellationToken ct = default)
    {
        var salva = await repositorio.ObterAsync(query.EmpresaId, query.LojaId, ct);
        return salva is null ? null : RegraImpressaoLojaResult.De(query.LojaId, salva);
    }
}

/// <param name="VersaoEsperada">A versão que o cliente leu (ETag do GET). 0 quando a loja nunca configurou.</param>
public sealed record GravarRegraImpressaoCommand(
    Guid EmpresaId, Guid LojaId, int VersaoEsperada, RegraImpressaoLojaEntrada Regra);

public enum DesfechoGravacaoImpressao
{
    Gravada,
    /// <summary>O corpo já é o que está gravado: nada muda, nem a versão. Vale também para a retentativa com versão velha.</summary>
    SemMudanca,
    Conflito,
    LojaNaoEncontrada,
}

/// <param name="Atual">Estado depois da gravação; no conflito, o estado que venceu, para o cliente refazer em cima dele.</param>
public sealed record GravarRegraImpressaoResult(DesfechoGravacaoImpressao Desfecho, RegraImpressaoLojaResult? Atual);

/// <summary>
/// PUT idempotente com concorrência otimista (#2199; auditoria 3, seção 8.8): mesmo corpo responde sem mudar a versão,
/// corpo novo sobre versão velha é conflito, e a gravação só acontece se a versão do banco ainda for a esperada.
/// </summary>
public class GravarRegraImpressaoLojaUseCase(
    IRegraImpressaoLojaRepository repositorio,
    IEtiquetaTemplateRepository templates,
    TimeProvider relogio)
{
    private static readonly RegraImpressaoLojaValidator Validator = new();

    public async Task<GravarRegraImpressaoResult> ExecuteAsync(GravarRegraImpressaoCommand command, CancellationToken ct = default)
    {
        var salva = await repositorio.ObterAsync(command.EmpresaId, command.LojaId, ct);
        if (salva is null)
            return new GravarRegraImpressaoResult(DesfechoGravacaoImpressao.LojaNaoEncontrada, null);

        var validacao = Validator.Validate(command.Regra);
        if (!validacao.IsValid)
            throw Invalida(validacao.Errors.Select(e => (e.PropertyName, e.ErrorMessage)));

        var regra = RegraImpressaoNormalizador.Normalizar(command.Regra);
        await ValidarTemplatesAsync(command.EmpresaId, regra);
        var json = RegraImpressaoNormalizador.ParaJson(regra);

        var atual = RegraImpressaoLojaResult.De(command.LojaId, salva);
        if (atual.Impressao?.GetRawText() == json)
            return new GravarRegraImpressaoResult(DesfechoGravacaoImpressao.SemMudanca, atual);
        if (command.VersaoEsperada != salva.Versao)
            return new GravarRegraImpressaoResult(DesfechoGravacaoImpressao.Conflito, atual);

        var agora = relogio.GetUtcNow().UtcDateTime;
        var gravacao = await repositorio.GravarAsync(command.EmpresaId, command.LojaId, command.VersaoEsperada, json, agora, ct);
        if (gravacao.Gravada)
        {
            return new GravarRegraImpressaoResult(
                DesfechoGravacaoImpressao.Gravada,
                new RegraImpressaoLojaResult(command.LojaId, gravacao.Versao, RegraImpressaoLojaResult.Elemento(json), agora));
        }

        // Outra gravação chegou entre a leitura e o UPDATE: devolve o que venceu. Se venceu o mesmo corpo
        // (duplo clique, retry em voo), o pedido já está atendido e não é conflito.
        var vencedora = await repositorio.ObterAsync(command.EmpresaId, command.LojaId, ct);
        var resultadoVencedor = vencedora is null ? null : RegraImpressaoLojaResult.De(command.LojaId, vencedora);
        return new GravarRegraImpressaoResult(
            resultadoVencedor?.Impressao?.GetRawText() == json ? DesfechoGravacaoImpressao.SemMudanca : DesfechoGravacaoImpressao.Conflito,
            resultadoVencedor);
    }

    private async Task ValidarTemplatesAsync(Guid empresaId, RegraImpressaoLoja regra)
    {
        var erros = new List<(string Campo, string Mensagem)>();
        foreach (var doc in regra.Documentos.Where(d => d.Template is not null))
        {
            var template = doc.Template!;
            var existe = template.Origem == "Empresa"
                ? await templates.GetEmpresaByIdAsync(empresaId, template.Id) is not null
                : await templates.GetSistemaByIdAsync(template.Id) is not null;
            if (!existe)
                erros.Add(($"documentos.{doc.Documento}.template", $"Modelo {template.Origem} {template.Id} não encontrado."));
        }

        if (erros.Count > 0) throw Invalida(erros);
    }

    private static UseCaseValidationException Invalida(IEnumerable<(string Campo, string Mensagem)> erros)
    {
        var lista = erros.ToList();
        return new UseCaseValidationException(
            "IMPRESSAO_INVALIDA",
            "Regra de impressão inválida: " + string.Join("; ", lista.Select(e => $"{e.Campo}: {e.Mensagem}")),
            lista.Select(e => new { campo = e.Campo, mensagem = e.Mensagem }).ToList());
    }
}
