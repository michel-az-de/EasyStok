using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.CadastrarUsuario;

public sealed record CadastrarUsuarioCommand(string Nome, string Email, string Senha, string BaseUrl = "") : ICommand;

public sealed record CadastrarUsuarioResult(Guid UsuarioId);