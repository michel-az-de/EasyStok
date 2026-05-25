using System.Text.Json.Serialization;

namespace EasyStok.Mobile.Models;

// Envelope padrao da API (EasyStockControllerBase.DataOk).
public sealed record EnvelopeApi<T>(
    [property: JsonPropertyName("data")] T Data,
    [property: JsonPropertyName("meta")] object? Meta);

// POST /api/auth/login
public sealed record RequisicaoLogin(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("senha")] string Senha,
    [property: JsonPropertyName("empresaId")] Guid? EmpresaId);

public sealed record RespostaLogin(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("refreshToken")] string RefreshToken,
    [property: JsonPropertyName("expiresIn")] int ExpiresIn,
    [property: JsonPropertyName("usuario")] UsuarioInfo Usuario);

public sealed record UsuarioInfo(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("nome")] string Nome,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("nivel")] string Nivel);

// POST /api/auth/refresh
public sealed record RequisicaoRenovacao(
    [property: JsonPropertyName("refreshToken")] string RefreshToken);

public sealed record RespostaRenovacao(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("refreshToken")] string RefreshToken,
    [property: JsonPropertyName("expiresIn")] int ExpiresIn);

// POST /api/auth/logout
public sealed record RequisicaoSair(
    [property: JsonPropertyName("refreshToken")] string RefreshToken);

// Erros da API: { errors: [{ code, message, ... }] }
public sealed record ErroApi(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("details")] string? Details);

public sealed record EnvelopeErroApi(
    [property: JsonPropertyName("error")] ErroApi? Error,
    [property: JsonPropertyName("errors")] ErroApi[]? Errors);
