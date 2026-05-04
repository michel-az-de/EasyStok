using System.Text.Json.Serialization;

namespace EasyStok.Mobile.Models;

// Envelope padrao da API (EasyStockControllerBase.DataOk).
public sealed record ApiEnvelope<T>(
	[property: JsonPropertyName("data")] T Data,
	[property: JsonPropertyName("meta")] object? Meta);

// POST /api/auth/login
public sealed record LoginRequest(
	[property: JsonPropertyName("email")] string Email,
	[property: JsonPropertyName("senha")] string Senha,
	[property: JsonPropertyName("empresaId")] Guid? EmpresaId);

public sealed record LoginResponse(
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
public sealed record RefreshRequest(
	[property: JsonPropertyName("refreshToken")] string RefreshToken);

public sealed record RefreshResponse(
	[property: JsonPropertyName("accessToken")] string AccessToken,
	[property: JsonPropertyName("refreshToken")] string RefreshToken,
	[property: JsonPropertyName("expiresIn")] int ExpiresIn);

// POST /api/auth/logout
public sealed record LogoutRequest(
	[property: JsonPropertyName("refreshToken")] string RefreshToken);

// Erros da API: { errors: [{ code, message, ... }] }
public sealed record ApiError(
	[property: JsonPropertyName("code")] string? Code,
	[property: JsonPropertyName("message")] string? Message,
	[property: JsonPropertyName("details")] string? Details);

public sealed record ApiErrorEnvelope(
	[property: JsonPropertyName("error")] ApiError? Error,
	[property: JsonPropertyName("errors")] ApiError[]? Errors);
