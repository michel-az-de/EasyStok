using EasyStock.Application.Ports.Output.Security;

namespace EasyStock.Application.UseCases.RegistrarEmpresa
{
    public sealed record RegistrarEmpresaCommand(
        string NomeEmpresa,
        string? Documento,
        string NomeAdmin,
        string EmailAdmin,
        string SenhaAdmin);

    public sealed record RegistrarEmpresaResult(
        Guid EmpresaId,
        Guid UsuarioId,
        string NomeEmpresa,
        string NomeAdmin,
        string Email);

    public class RegistrarEmpresaUseCase(
        IUsuarioRepository usuarioRepository,
        IPlanoRepository planoRepository,
        IPerfilRepository perfilRepository,
        IAssinaturaEmpresaRepository assinaturaRepository,
        IEmpresaRepository empresaRepository,
        IUsuarioEmpresaRepository usuarioEmpresaRepository,
        IUsuarioPerfilRepository usuarioPerfilRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IRowLevelSecurityBypass rlsBypass,
        ILogger<RegistrarEmpresaUseCase> logger)
    {
        public async Task<RegistrarEmpresaResult> ExecuteAsync(RegistrarEmpresaCommand command)
        {
            // issue 1024: registrar empresa e cross-tenant POR DEFINICAO — cria o tenant. A
            // requisicao e anonima, entao o SetTenantOnConnectionInterceptor emite
            // app.empresa_id = '' e a policy tenant_isolation (WITH CHECK, ADR-0010) recusa os
            // INSERTs em assinaturas_empresa, perfis, usuarios_empresas e usuarios_perfis:
            // 42501 new row violates row-level security policy.
            //
            // O escopo cobre o metodo INTEIRO, nao so os INSERTs, porque as LEITURAS tambem
            // sofrem: perfis tem EmpresaId, logo tem RLS, e sem bypass GetPadroesAsync volta
            // vazio — o use case criaria um perfil Admin por empresa em vez de reusar o padrao,
            // mudando a forma do dado sem erro nenhum. (empresas e planos nao tem EmpresaId,
            // entao ficam fora da RLS e nao dependem disto; medido em 2026-08-12.)
            //
            // O bypass e aplicado pelo interceptor na PROXIMA abertura de conexao — por isso o
            // using precisa envolver tambem o CommitAsync la embaixo, que e onde os INSERTs saem.
            using var _rls = rlsBypass.Begin();

            logger.LogInformation("Registrando nova empresa: {NomeEmpresa}", command.NomeEmpresa);

            var emailExistente = await usuarioRepository.GetByEmailAsync(command.EmailAdmin);
            if (emailExistente is not null)
                throw new UseCaseValidationException("Email ja cadastrado.");

            // Bloqueio anti-abuso: mesmo CNPJ/CPF não pode esticar trial
            // criando empresas novas com emails diferentes.
            if (!string.IsNullOrWhiteSpace(command.Documento))
            {
                var docExistente = await empresaRepository.GetByDocumentoAsync(command.Documento.Trim());
                if (docExistente is not null)
                    throw new UseCaseValidationException("CNPJ/CPF já está em uso por outra empresa.");
            }

            var planos = await planoRepository.GetAtivosAsync();
            var planoLista = planos.ToList();

            var planoStarter = planoLista.FirstOrDefault(p => p.Nome == "Starter")
                ?? planoLista.FirstOrDefault()
                ?? throw new UseCaseValidationException("Nenhum plano ativo encontrado.");

            var agora = DateTime.UtcNow;

            var empresa = Empresa.Criar(command.NomeEmpresa, command.Documento);

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

            assinatura.AtivarTrial(14);
            await assinaturaRepository.AddAsync(assinatura);

            var senhaHash = passwordHasher.Hash(command.SenhaAdmin);
            var usuario = Usuario.Criar(command.NomeAdmin.Trim(), command.EmailAdmin.Trim(), senhaHash);

            await usuarioRepository.AddAsync(usuario);

            var usuarioEmpresa = new UsuarioEmpresa
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuario.Id,
                EmpresaId = empresa.Id,
                Ativo = true,
                CriadoEm = agora
            };

            await usuarioEmpresaRepository.AddAsync(usuarioEmpresa);

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

            var usuarioPerfil = new UsuarioPerfil
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuario.Id,
                EmpresaId = empresa.Id,
                PerfilId = perfilAdmin.Id,
                AtribuidoEm = agora
            };

            await usuarioPerfilRepository.AddAsync(usuarioPerfil);

            await unitOfWork.CommitAsync();

            logger.LogInformation("Empresa registrada com sucesso. EmpresaId: {EmpresaId}, UsuarioId: {UsuarioId}", empresa.Id, usuario.Id);

            return new RegistrarEmpresaResult(
                EmpresaId: empresa.Id,
                UsuarioId: usuario.Id,
                NomeEmpresa: empresa.Nome,
                NomeAdmin: usuario.Nome,
                Email: usuario.Email);
        }
    }
}
