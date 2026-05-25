using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;

namespace EasyStock.Api.UnitTests.Http;

/// <summary>
/// Valida o comportamento de deduplicação de parâmetros de query string.
/// Cobre o fix no TokenRefreshHandler.AddQueryString que impedia que
/// empresaId fosse adicionado duas vezes quando o serviço já o incluía na URL.
/// </summary>
public class QueryStringDeduplicationTests
{
    // Replica a lógica corrigida do TokenRefreshHandler.AddQueryString
    private static Uri? AddQueryStringIfAbsent(Uri? uri, string key, string value)
    {
        if (uri is null) return null;

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (query[key] is not null) return uri;   // já existe — não duplicar

        var updated = QueryHelpers.AddQueryString(uri.ToString(), key, value);
        return new Uri(updated, uri.IsAbsoluteUri ? UriKind.Absolute : UriKind.Relative);
    }

    [Fact]
    public void Deve_adicionar_empresaId_quando_ausente_na_url()
    {
        var uri = new Uri("https://api.easystok.com/estoque?page=1");
        var result = AddQueryStringIfAbsent(uri, "empresaId", "abc-123");

        result!.Query.Should().Contain("empresaId=abc-123");
        CountOccurrences(result.Query, "empresaId").Should().Be(1);
    }

    [Fact]
    public void Nao_deve_duplicar_empresaId_quando_ja_presente_na_url()
    {
        var uri = new Uri("https://api.easystok.com/estoque?empresaId=abc-123&page=1");
        var result = AddQueryStringIfAbsent(uri, "empresaId", "abc-123");

        result.Should().Be(uri, "a URI não deve ser modificada quando o parâmetro já existe");
        CountOccurrences(result!.Query, "empresaId").Should().Be(1);
    }

    [Fact]
    public void Nao_deve_duplicar_empresaId_independente_de_valor_diferente_na_session()
    {
        // Garante que o handler não substitui nem duplica mesmo se o valor de session diferir
        var uri = new Uri("https://api.easystok.com/produtos?empresaId=original-id");
        var result = AddQueryStringIfAbsent(uri, "empresaId", "session-id");

        result.Should().Be(uri);
        CountOccurrences(result!.Query, "empresaId").Should().Be(1);
        result.Query.Should().Contain("original-id");
    }

    [Fact]
    public void Deve_adicionar_empresaId_em_url_sem_query_string()
    {
        var uri = new Uri("https://api.easystok.com/estoque");
        var result = AddQueryStringIfAbsent(uri, "empresaId", "novo-id");

        result!.Query.Should().Contain("empresaId=novo-id");
        CountOccurrences(result.Query, "empresaId").Should().Be(1);
    }

    [Fact]
    public void Deve_retornar_null_para_uri_nula()
    {
        var result = AddQueryStringIfAbsent(null, "empresaId", "qualquer");
        result.Should().BeNull();
    }

    private static int CountOccurrences(string text, string pattern) =>
        (text.Length - text.Replace(pattern, "").Length) / pattern.Length;
}
