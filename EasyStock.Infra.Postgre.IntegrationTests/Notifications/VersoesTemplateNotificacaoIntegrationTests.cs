using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.UseCases.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

public class VersoesTemplateNotificacaoIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private readonly MotorNotificacoesSuporte _s = new(fixture);

    private void ExigirBanco() => Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");

    private static AsyncServiceScope Escopo(ServiceProvider provider, Guid empresa)
    {
        var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(empresa);
        return scope;
    }

    private static async Task<TemplateResult> Criar(IServiceProvider services, Guid empresa, string codigo) =>
        await services.GetRequiredService<CriarTemplateUseCase>().ExecuteAsync(new(codigo, "Modelo de teste",
            CanalNotificacao.Email, TipoEventoNotificacao.TarefaPendente, "Pedido", "Olá {{ nome }}",
            "pt-BR", "teste", empresa));

    private static Task<TemplateResult> Editar(IServiceProvider services, Guid empresa, Guid id) =>
        services.GetRequiredService<AtualizarTemplateUseCase>().ExecuteAsync(new(id, "Pedido novo", "Oi {{ nome }}", "teste", empresa));

    private static Task<AprovarTemplateResult> Aprovar(IServiceProvider services, Guid empresa, Guid id) =>
        services.GetRequiredService<AprovarTemplateUseCase>().ExecuteAsync(new(id, "teste", empresa));

    [SkippableFact]
    public async Task Editar_aprovar_e_resolver_preserva_publicada_ate_aprovacao_sem_envio()
    {
        ExigirBanco();
        var empresa = await _s.SemearEmpresaAsync();
        var outraEmpresa = await _s.SemearEmpresaAsync();
        var codigo = $"r4-modelo-{Guid.NewGuid():N}";
        await using var provider = _s.ConstruirProviderDaApi(true, empresa);
        await using var scope = Escopo(provider, empresa);
        var services = scope.ServiceProvider;
        var repo = services.GetRequiredService<ITemplateRepository>();
        var original = await Criar(services, empresa, codigo);
        await Aprovar(services, empresa, original.Id);
        var nova = await Editar(services, empresa, original.Id);
        (await repo.GetAtivoAsync(codigo, CanalNotificacao.Email, empresa))!.Id.Should().Be(original.Id);

        await Aprovar(services, empresa, nova.Id);
        var resolvido = await repo.GetAtivoAsync(codigo, CanalNotificacao.Email, empresa);
        resolvido!.Id.Should().Be(nova.Id);
        resolvido.Versao.Should().Be(2);
        var texto = await services.GetRequiredService<IRendererTemplate>().RenderizarAsync(resolvido.CorpoTemplate,
            new Dictionary<string, object?> { ["nome"] = "Maria" });
        texto.Should().Be("Oi Maria");
        var (ativas, total) = await repo.ListarAsync(empresa, ativo: true);
        total.Should().Be(1);
        ativas.Should().ContainSingle().Which.Id.Should().Be(nova.Id);

        Func<Task> editarAntiga = () => Editar(services, empresa, original.Id);
        Func<Task> aprovarAntiga = () => Aprovar(services, empresa, original.Id);
        await editarAntiga.Should().ThrowAsync<InvalidOperationException>();
        await aprovarAntiga.Should().ThrowAsync<InvalidOperationException>();

        await using var outroProvider = _s.ConstruirProviderDaApi(true, outraEmpresa);
        await using var outroScope = Escopo(outroProvider, outraEmpresa);
        Func<Task> invadir = () => Aprovar(outroScope.ServiceProvider, outraEmpresa, nova.Id);
        await invadir.Should().ThrowAsync<ConfiguracaoNotificacaoNaoEncontradaException>();
    }

    [SkippableFact]
    public async Task Apenas_atuais_filtra_antes_de_contar_paginar_e_aplicar_estado()
    {
        ExigirBanco();
        var empresa = await _s.SemearEmpresaAsync();
        await using var provider = _s.ConstruirProviderDaApi(true, empresa);
        await using var scope = Escopo(provider, empresa);
        var services = scope.ServiceProvider;
        var a = await Criar(services, empresa, $"r4-a-{Guid.NewGuid():N}");
        await Aprovar(services, empresa, a.Id);
        var b = await Criar(services, empresa, $"r4-b-{Guid.NewGuid():N}");
        var atualA = await Editar(services, empresa, a.Id);
        atualA = await Editar(services, empresa, atualA.Id);
        var repo = services.GetRequiredService<ITemplateRepository>();
        var (pagina1, total1) = await repo.ListarAsync(empresa, page: 1, pageSize: 1, apenasAtuais: true);
        var (pagina2, total2) = await repo.ListarAsync(empresa, page: 2, pageSize: 1, apenasAtuais: true);
        total1.Should().Be(2);
        total2.Should().Be(2);
        pagina1.Concat(pagina2).Select(t => t.Id).Should().BeEquivalentTo([atualA.Id, b.Id]);
        var (ativasAtuais, totalAtivas) = await repo.ListarAsync(empresa, ativo: true, apenasAtuais: true);
        totalAtivas.Should().Be(0, "a versão publicada antiga não substitui o rascunho atual no filtro");
        ativasAtuais.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Edicoes_concorrentes_na_mesma_base_nao_duplicam_versao_e_aprovacao_nao_se_perde()
    {
        ExigirBanco();
        var empresa = await _s.SemearEmpresaAsync();
        await using var provider = _s.ConstruirProviderDaApi(true, empresa);
        TemplateResult original;
        await using (var seed = Escopo(provider, empresa))
        {
            original = await Criar(seed.ServiceProvider, empresa, $"r4-corrida-{Guid.NewGuid():N}");
            await Aprovar(seed.ServiceProvider, empresa, original.Id);
        }
        await using var a = Escopo(provider, empresa);
        await using var b = Escopo(provider, empresa);
        // Os dois escopos rastreiam a base antes de disputar o lock da família.
        await a.ServiceProvider.GetRequiredService<ITemplateRepository>().GetByIdAsync(original.Id);
        await b.ServiceProvider.GetRequiredService<ITemplateRepository>().GetByIdAsync(original.Id);
        async Task<TemplateResult?> TentarEditar(IServiceProvider services)
        {
            try { return await Editar(services, empresa, original.Id); }
            catch (InvalidOperationException) { return null; }
        }
        var resultados = await Task.WhenAll(TentarEditar(a.ServiceProvider), TentarEditar(b.ServiceProvider));
        var nova = resultados.Where(r => r is not null).Should().ContainSingle().Which!;

        await Task.WhenAll(Aprovar(a.ServiceProvider, empresa, nova.Id), Aprovar(b.ServiceProvider, empresa, nova.Id));
        await using var leitura = Escopo(provider, empresa);
        var repo = leitura.ServiceProvider.GetRequiredService<ITemplateRepository>();
        var (versoes, total) = await repo.ListarAsync(empresa);
        total.Should().Be(2);
        versoes.Select(t => t.Versao).Should().BeEquivalentTo([1, 2]);
        versoes.Where(t => t.Ativo).Should().ContainSingle().Which.Id.Should().Be(nova.Id);
        (await repo.GetAtivoAsync(original.Codigo, CanalNotificacao.Email, empresa))!.Id.Should().Be(nova.Id);
    }
}
