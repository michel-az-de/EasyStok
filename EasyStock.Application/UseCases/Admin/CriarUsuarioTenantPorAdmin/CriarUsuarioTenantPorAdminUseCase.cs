using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace EasyStock.Application.UseCases.Admin.CriarUsuarioTenantPorAdmin;

public sealed record CriarUsuarioTenantPorAdminCommand(
    Guid TenantId,
    string Nome,
    string Email,
    NivelAcesso Nivel,
    bool EnviarEmail);

public sealed record CriarUsuarioTenantPorAdminResult(
    Guid UsuarioId,
    Guid TenantId,
    string Nome,
    string Email,
    NivelAcesso Nivel,
    string SenhaTemporaria,
    bool EmailEnviado,
    string? EmailErro);

/// <summary>
/// Cria um usuário em um tenant existente — operação de back-office quando o cliente
/// perdeu acesso ao único admin ou precisa de mais um operador rapidamente. Senha é
/// gerada server-side e retornada 1x (banner do admin) + opcionalmente enviada por email.
/// </summary>
public sealed class CriarUsuarioTenantPorAdminUseCase(
    IUsuarioRepository usuarioRepository,
    IEmpresaRepository empresaRepository,
    IPerfilRepository perfilRepository,
    IUsuarioEmpresaRepository usuarioEmpresaRepository,
    IUsuarioPerfilRepository usuarioPerfilRepository,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IEmailService? emailService,
    ILogger<CriarUsuarioTenantPorAdminUseCase> logger)
{
    private const int SenhaTempLength = 16;

    public async Task<CriarUsuarioTenantPorAdminResult> ExecuteAsync(CriarUsuarioTenantPorAdminCommand command)
    {
        if (command.TenantId == Guid.Empty)
            throw new UseCaseValidationException("Cliente inválido.");
        if (string.IsNullOrWhiteSpace(command.Nome) || command.Nome.Trim().Length is < 2 or > 120)
            throw new UseCaseValidationException("Nome deve ter entre 2 e 120 caracteres.");
        if (string.IsNullOrWhiteSpace(command.Email))
            throw new UseCaseValidationException("E-mail é obrigatório.");

        var emailNorm = command.Email.Trim().ToLowerInvariant();
        if (emailNorm.Length > 160 || !System.Text.RegularExpressions.Regex.IsMatch(emailNorm, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            throw new UseCaseValidationException("E-mail inválido.");

        var empresa = await empresaRepository.GetByIdAsync(command.TenantId)
            ?? throw new UseCaseValidationException("Cliente não encontrado.");

        var emailExistente = await usuarioRepository.GetByEmailAsync(emailNorm);
        if (emailExistente is not null)
            throw new UseCaseValidationException("Já existe um usuário com esse e-mail no sistema.");

        if (command.Nivel == NivelAcesso.SuperAdmin)
            throw new UseCaseValidationException("Não é permitido criar usuário SuperAdmin em um tenant.");

        // Resolve perfil correspondente ao nível pedido. Se não existir um perfil padrão
        // pra esse nível, cria um sob a empresa — mantém o tenant auto-suficiente.
        var perfis = (await perfilRepository.GetPadroesAsync()).ToList();
        var perfil = perfis.FirstOrDefault(p => p.Nivel == command.Nivel);
        var agora = DateTime.UtcNow;
        if (perfil is null)
        {
            perfil = new Perfil
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresa.Id,
                Nome = command.Nivel.ToString(),
                Descricao = NomeAmigavelPerfil(command.Nivel),
                Nivel = command.Nivel,
                CriadoEm = agora
            };
            await perfilRepository.AddAsync(perfil);
        }

        var senhaTemp = GerarSenhaAleatoria(SenhaTempLength);
        var senhaHash = passwordHasher.Hash(senhaTemp);
        var usuario = Usuario.Criar(command.Nome.Trim(), emailNorm, senhaHash);
        await usuarioRepository.AddAsync(usuario);

        await usuarioEmpresaRepository.AddAsync(new UsuarioEmpresa
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            EmpresaId = empresa.Id,
            Ativo = true,
            CriadoEm = agora
        });

        await usuarioPerfilRepository.AddAsync(new UsuarioPerfil
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            EmpresaId = empresa.Id,
            PerfilId = perfil.Id,
            AtribuidoEm = agora
        });

        await unitOfWork.CommitAsync();

        logger.LogInformation(
            "Admin criou usuário em tenant. TenantId={TenantId}, UsuarioId={UsuarioId}, Nivel={Nivel}",
            empresa.Id, usuario.Id, command.Nivel);

        var (emailEnviado, emailErro) = command.EnviarEmail
            ? await TentarEnviarEmailAsync(usuario, senhaTemp, empresa.Nome)
            : (false, null);

        return new CriarUsuarioTenantPorAdminResult(
            UsuarioId: usuario.Id,
            TenantId: empresa.Id,
            Nome: usuario.Nome,
            Email: usuario.Email,
            Nivel: command.Nivel,
            SenhaTemporaria: senhaTemp,
            EmailEnviado: emailEnviado,
            EmailErro: emailErro);
    }

    private async Task<(bool Enviado, string? Erro)> TentarEnviarEmailAsync(Usuario usuario, string senhaTemp, string nomeEmpresa)
    {
        if (emailService is null) return (false, "EmailService não configurado.");
        try
        {
            var nomeSafe = System.Net.WebUtility.HtmlEncode(usuario.Nome);
            var senhaSafe = System.Net.WebUtility.HtmlEncode(senhaTemp);
            var empresaSafe = System.Net.WebUtility.HtmlEncode(nomeEmpresa);
            var body = $@"<p>Olá {nomeSafe},</p>
<p>Foi criada uma conta para você no EasyStock — empresa <strong>{empresaSafe}</strong>.</p>
<p><strong>Senha temporária:</strong> <code>{senhaSafe}</code></p>
<p>Recomendamos trocá-la após o primeiro login.</p>";
            await emailService.SendAsync(usuario.Email, "EasyStock — Sua conta foi criada", body, isHtml: true);
            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao enviar email de boas-vindas para {UsuarioId}", usuario.Id);
            return (false, ex.Message);
        }
    }

    private static string NomeAmigavelPerfil(NivelAcesso nivel) => nivel switch
    {
        NivelAcesso.Admin => "Administrador com acesso total",
        NivelAcesso.Gerente => "Gerente com acesso a relatórios e gestão",
        NivelAcesso.Operador => "Operador de caixa e estoque",
        NivelAcesso.Visualizador => "Acesso somente leitura",
        _ => nivel.ToString()
    };

    private static string GerarSenhaAleatoria(int length)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$";
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
            sb.Append(chars[RandomNumberGenerator.GetInt32(chars.Length)]);
        return sb.ToString();
    }
}
