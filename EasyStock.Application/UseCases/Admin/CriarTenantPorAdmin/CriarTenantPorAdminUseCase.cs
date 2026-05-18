using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace EasyStock.Application.UseCases.Admin.CriarTenantPorAdmin;

public sealed record CriarTenantPorAdminCommand(
    string NomeEmpresa,
    string? Documento,
    string NomeAdmin,
    string EmailAdmin,
    bool EnviarEmail);

public sealed record CriarTenantPorAdminResult(
    Guid TenantId,
    Guid UsuarioId,
    string NomeEmpresa,
    string NomeAdmin,
    string EmailAdmin,
    string SenhaTemporaria,
    bool EmailEnviado,
    string? EmailErro,
    DateTime TrialFim);

/// <summary>
/// Cadastro de tenant pelo back-office (operador SuperAdmin). Difere do
/// <see cref="RegistrarEmpresa.RegistrarEmpresaUseCase"/> (signup público) em três pontos:
/// (1) senha é gerada server-side — operador nunca digita senha de cliente;
/// (2) senha temporária retorna no result pra ser exibida 1x no banner do admin;
/// (3) email opcional com a senha — redundância para ditar por telefone se a caixa estiver fora.
///
/// Trial e plano default seguem o mesmo padrão do signup público (Starter + 14 dias).
/// </summary>
public class CriarTenantPorAdminUseCase(
    IUsuarioRepository usuarioRepository,
    IPlanoRepository planoRepository,
    IPerfilRepository perfilRepository,
    IAssinaturaEmpresaRepository assinaturaRepository,
    IEmpresaRepository empresaRepository,
    IUsuarioEmpresaRepository usuarioEmpresaRepository,
    IUsuarioPerfilRepository usuarioPerfilRepository,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IEmailService? emailService,
    ILogger<CriarTenantPorAdminUseCase> logger)
{
    private const int TrialDiasPadrao = 14;
    private const int SenhaTempLength = 16;

    public async Task<CriarTenantPorAdminResult> ExecuteAsync(CriarTenantPorAdminCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.NomeEmpresa))
            throw new UseCaseValidationException("Razão social ou nome é obrigatório.");
        if (string.IsNullOrWhiteSpace(command.NomeAdmin))
            throw new UseCaseValidationException("Nome do responsável é obrigatório.");
        if (string.IsNullOrWhiteSpace(command.EmailAdmin))
            throw new UseCaseValidationException("E-mail do responsável é obrigatório.");

        var emailNorm = command.EmailAdmin.Trim().ToLowerInvariant();
        var emailExistente = await usuarioRepository.GetByEmailAsync(emailNorm);
        if (emailExistente is not null)
            throw new UseCaseValidationException("Já existe um usuário com esse e-mail. Use outro ou edite o cadastro existente.");

        if (!string.IsNullOrWhiteSpace(command.Documento))
        {
            var docExistente = await empresaRepository.GetByDocumentoAsync(command.Documento.Trim());
            if (docExistente is not null)
                throw new UseCaseValidationException($"Esse CNPJ/CPF já pertence a {docExistente.Nome}.");
        }

        var planoStarter = (await planoRepository.GetAtivosAsync())
            .ToList()
            .FirstOrDefault(p => p.Nome == "Starter")
            ?? throw new UseCaseValidationException("Nenhum plano Starter ativo encontrado.");

        var agora = DateTime.UtcNow;
        var empresa = Empresa.Criar(command.NomeEmpresa.Trim(), command.Documento?.Trim());
        await empresaRepository.AddAsync(empresa);

        var assinatura = new AssinaturaEmpresa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresa.Id,
            PlanoId = planoStarter.Id,
            DataInicio = agora,
            Status = StatusAssinatura.Ativa,
            CriadoEm = agora,
            AlteradoEm = agora
        };
        assinatura.AtivarTrial(TrialDiasPadrao);
        await assinaturaRepository.AddAsync(assinatura);

        var senhaTemp = GerarSenhaAleatoria(SenhaTempLength);
        var senhaHash = passwordHasher.Hash(senhaTemp);
        var usuario = Usuario.Criar(command.NomeAdmin.Trim(), emailNorm, senhaHash);
        await usuarioRepository.AddAsync(usuario);

        await usuarioEmpresaRepository.AddAsync(new UsuarioEmpresa
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            EmpresaId = empresa.Id,
            Ativo = true,
            CriadoEm = agora
        });

        var perfispadrao = await perfilRepository.GetPadroesAsync();
        var perfilAdmin = perfispadrao.FirstOrDefault(p => p.Nome == "Admin");
        if (perfilAdmin is null)
        {
            perfilAdmin = new Perfil
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresa.Id,
                Nome = "Admin",
                Descricao = "Administrador com acesso total",
                Nivel = NivelAcesso.Admin,
                CriadoEm = agora
            };
            await perfilRepository.AddAsync(perfilAdmin);
        }

        await usuarioPerfilRepository.AddAsync(new UsuarioPerfil
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            EmpresaId = empresa.Id,
            PerfilId = perfilAdmin.Id,
            AtribuidoEm = agora
        });

        await unitOfWork.CommitAsync();

        logger.LogInformation(
            "Admin cadastrou tenant manualmente. EmpresaId={EmpresaId}, UsuarioId={UsuarioId}",
            empresa.Id, usuario.Id);

        var (emailEnviado, emailErro) = command.EnviarEmail
            ? await TentarEnviarEmailAsync(usuario, senhaTemp)
            : (false, null);

        return new CriarTenantPorAdminResult(
            TenantId: empresa.Id,
            UsuarioId: usuario.Id,
            NomeEmpresa: empresa.Nome,
            NomeAdmin: usuario.Nome,
            EmailAdmin: usuario.Email,
            SenhaTemporaria: senhaTemp,
            EmailEnviado: emailEnviado,
            EmailErro: emailErro,
            TrialFim: assinatura.TrialFim ?? agora);
    }

    private async Task<(bool Enviado, string? Erro)> TentarEnviarEmailAsync(Usuario usuario, string senhaTemp)
    {
        if (emailService is null) return (false, "EmailService não configurado.");
        try
        {
            var nomeSafe = System.Net.WebUtility.HtmlEncode(usuario.Nome);
            var senhaSafe = System.Net.WebUtility.HtmlEncode(senhaTemp);
            var body = $@"<p>Olá {nomeSafe},</p>
<p>Sua conta no EasyStock foi criada pelo time de suporte.</p>
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

    private static string GerarSenhaAleatoria(int length)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$";
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
            sb.Append(chars[RandomNumberGenerator.GetInt32(chars.Length)]);
        return sb.ToString();
    }
}
