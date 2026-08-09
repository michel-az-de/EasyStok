using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AtualizarCliente;
using EasyStock.Application.UseCases.CriarCliente;
using Microsoft.Extensions.Logging;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Rede de segurança do cadastro de cliente PESSOA FÍSICA (#1018).
///
/// <para>
/// Escrita ANTES de estender a entidade para pessoa jurídica, porque até aqui
/// <c>CriarClienteUseCase</c> e <c>AtualizarClienteUseCase</c> não tinham teste algum — a
/// suíte não avisaria se o cadastro PF, que está em produção, quebrasse. Estes testes travam
/// o comportamento atual: normalização de documento e telefone, validação de dígito
/// verificador, e a regra deliberada de só validar o documento quando ele muda.
/// </para>
/// </summary>
public class ClienteCadastroUseCaseTests
{
    private readonly IClienteRepository _repo = Substitute.For<IClienteRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid Empresa = Guid.NewGuid();
    private const string CpfValido = "529.982.247-25";
    private const string CpfValidoDigitos = "52998224725";
    private const string CnpjValido = "11.222.333/0001-81";
    private const string CnpjValidoDigitos = "11222333000181";

    private CriarClienteUseCase Criar() =>
        new(_repo, _uow, Substitute.For<ILogger<CriarClienteUseCase>>());

    private AtualizarClienteUseCase Atualizar() =>
        new(_repo, _uow, Substitute.For<ILogger<AtualizarClienteUseCase>>());

    // ── criar ────────────────────────────────────────────────────────

    [Fact]
    public async Task Criar_pessoa_fisica_com_o_minimo_funciona()
    {
        var r = await Criar().ExecuteAsync(new CriarClienteCommand(Empresa, "Ana Beatriz Costa"));

        r.Nome.Should().Be("Ana Beatriz Costa");
        r.EmpresaId.Should().Be(Empresa);
        await _repo.Received(1).AddAsync(Arg.Any<ClienteEntity>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Criar_normaliza_cpf_para_so_digitos()
    {
        var r = await Criar().ExecuteAsync(
            new CriarClienteCommand(Empresa, "Ana", Documento: CpfValido));

        r.Documento.Should().Be(CpfValidoDigitos);
    }

    [Fact]
    public async Task Criar_aceita_cnpj_e_normaliza()
    {
        // CNPJ já era aceito antes do #1018 — o que faltava era o discriminador PF/PJ.
        var r = await Criar().ExecuteAsync(
            new CriarClienteCommand(Empresa, "Distribuidora Sul", Documento: CnpjValido));

        r.Documento.Should().Be(CnpjValidoDigitos);
    }

    [Fact]
    public async Task Criar_recusa_cpf_com_digito_verificador_errado()
    {
        var act = () => Criar().ExecuteAsync(
            new CriarClienteCommand(Empresa, "Ana", Documento: "111.111.111-11"));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _repo.DidNotReceive().AddAsync(Arg.Any<ClienteEntity>());
    }

    [Fact]
    public async Task Criar_recusa_cnpj_com_digito_verificador_errado()
    {
        var act = () => Criar().ExecuteAsync(
            new CriarClienteCommand(Empresa, "Distribuidora", Documento: "11.222.333/0001-99"));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task Criar_tolera_documento_de_comprimento_estranho()
    {
        // Estrangeiro/legado: DocumentoValidator só valida DV para 11 e 14 dígitos.
        var r = await Criar().ExecuteAsync(
            new CriarClienteCommand(Empresa, "John Smith", Documento: "AB-12345"));

        r.Documento.Should().Be("AB-12345");
    }

    [Fact]
    public async Task Criar_recusa_empresa_vazia()
    {
        var act = () => Criar().ExecuteAsync(new CriarClienteCommand(Guid.Empty, "Ana"));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task Criar_recusa_nome_com_tag_html()
    {
        var act = () => Criar().ExecuteAsync(
            new CriarClienteCommand(Empresa, "<script>alert(1)</script>"));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task Criar_recusa_email_invalido()
    {
        var act = () => Criar().ExecuteAsync(
            new CriarClienteCommand(Empresa, "Ana", Email: "nao-e-email"));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    // ── atualizar ────────────────────────────────────────────────────

    [Fact]
    public async Task Atualizar_cliente_inexistente_devolve_null()
    {
        _repo.GetByIdAsync(Empresa, Arg.Any<Guid>()).Returns((ClienteEntity?)null);

        var r = await Atualizar().ExecuteAsync(
            new AtualizarClienteCommand(Empresa, Guid.NewGuid(), "Ana"));

        r.Should().BeNull();
    }

    [Fact]
    public async Task Atualizar_registra_diff_no_audit_log()
    {
        var cliente = ClienteEntity.Criar(Empresa, "Ana");
        cliente.AtualizarCadastro("Ana", null, null, null, null, CpfValidoDigitos, null);
        _repo.GetByIdAsync(Empresa, cliente.Id).Returns(cliente);

        await Atualizar().ExecuteAsync(new AtualizarClienteCommand(
            Empresa, cliente.Id, "Ana Beatriz", Documento: CpfValidoDigitos));

        // Só o Nome mudou — documento igual não pode virar linha de auditoria.
        await _repo.Received(1).AddAlteracaoAsync(
            Arg.Is<Domain.Entities.ClienteAlteracao>(a => a.Campo == "Nome"));
        await _repo.DidNotReceive().AddAlteracaoAsync(
            Arg.Is<Domain.Entities.ClienteAlteracao>(a => a.Campo == "Documento"));
    }

    [Fact]
    public async Task Atualizar_valida_documento_quando_ele_muda()
    {
        var cliente = ClienteEntity.Criar(Empresa, "Ana");
        cliente.AtualizarCadastro("Ana", null, null, null, null, CpfValidoDigitos, null);
        _repo.GetByIdAsync(Empresa, cliente.Id).Returns(cliente);

        var act = () => Atualizar().ExecuteAsync(new AtualizarClienteCommand(
            Empresa, cliente.Id, "Ana", Documento: "111.111.111-11"));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task Cliente_legado_com_documento_invalido_continua_editavel()
    {
        // Comportamento DELIBERADO (CLI-01 on-change): o documento só é validado quando muda,
        // senão um cadastro antigo com documento ruim ficaria impossível de corrigir em
        // qualquer outro campo. Esta é a regra mais fácil de quebrar sem querer.
        var cliente = ClienteEntity.Criar(Empresa, "Legado");
        cliente.AtualizarCadastro("Legado", null, null, null, null, "11111111111", null);
        _repo.GetByIdAsync(Empresa, cliente.Id).Returns(cliente);

        var r = await Atualizar().ExecuteAsync(new AtualizarClienteCommand(
            Empresa, cliente.Id, "Legado Corrigido", Documento: "11111111111"));

        r.Should().NotBeNull();
        r!.Nome.Should().Be("Legado Corrigido");
    }

    [Fact]
    public async Task Atualizar_recusa_empresa_vazia()
    {
        var act = () => Atualizar().ExecuteAsync(
            new AtualizarClienteCommand(Guid.Empty, Guid.NewGuid(), "Ana"));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    // ── pessoa jurídica (ADR-0048, #1018) ────────────────────────────

    [Fact]
    public async Task Criar_sem_informar_tipo_nasce_pessoa_fisica()
    {
        // Regressão do contrato: PWA, mobile e qualquer cliente antigo não mandam o campo.
        var r = await Criar().ExecuteAsync(new CriarClienteCommand(Empresa, "Ana"));

        r.TipoPessoa.Should().Be("fisica");
        r.NomeFantasia.Should().BeNull();
        r.InscricaoEstadual.Should().BeNull();
    }

    [Fact]
    public async Task Criar_pessoa_juridica_guarda_razao_social_no_nome()
    {
        var r = await Criar().ExecuteAsync(new CriarClienteCommand(
            Empresa, "Distribuidora Sul Ltda", Documento: CnpjValido,
            PessoaJuridica: true, NomeFantasia: "Sul Bebidas", InscricaoEstadual: "123.456.789.111"));

        r.TipoPessoa.Should().Be("juridica");
        r.Nome.Should().Be("Distribuidora Sul Ltda", because: "no vocabulário do repo, Nome É a razão social");
        r.NomeFantasia.Should().Be("Sul Bebidas");
        r.InscricaoEstadual.Should().Be("123.456.789.111");
        r.Documento.Should().Be(CnpjValidoDigitos);
    }

    [Fact]
    public async Task Inscricao_estadual_aceita_isento()
    {
        // Formato varia por UF e o literal "ISENTO" é válido — mesmo tratamento que a IE do
        // emitente fiscal já recebe.
        var r = await Criar().ExecuteAsync(new CriarClienteCommand(
            Empresa, "Micro Empresa ME", PessoaJuridica: true, InscricaoEstadual: "ISENTO"));

        r.InscricaoEstadual.Should().Be("ISENTO");
    }

    [Fact]
    public async Task Campos_de_pj_sao_ignorados_quando_o_cadastro_e_pessoa_fisica()
    {
        // Payload inconsistente não deve deixar inscrição estadual pendurada numa PF.
        var r = await Criar().ExecuteAsync(new CriarClienteCommand(
            Empresa, "Ana", PessoaJuridica: false,
            NomeFantasia: "não deveria colar", InscricaoEstadual: "123"));

        r.TipoPessoa.Should().Be("fisica");
        r.NomeFantasia.Should().BeNull();
        r.InscricaoEstadual.Should().BeNull();
    }

    [Fact]
    public async Task Converter_pj_para_pf_limpa_os_campos_de_pj()
    {
        var cliente = ClienteEntity.Criar(Empresa, "Distribuidora Sul Ltda");
        cliente.AtualizarPessoaJuridica(true, "Sul Bebidas", "123456");
        _repo.GetByIdAsync(Empresa, cliente.Id).Returns(cliente);

        var r = await Atualizar().ExecuteAsync(new AtualizarClienteCommand(
            Empresa, cliente.Id, "Joao da Silva", PessoaJuridica: false));

        r!.TipoPessoa.Should().Be("fisica");
        r.NomeFantasia.Should().BeNull(because: "corrigir para PF não pode deixar dado de PJ para trás");
        r.InscricaoEstadual.Should().BeNull();
    }

    [Fact]
    public async Task Virar_pj_deixa_rastro_no_audit_log()
    {
        var cliente = ClienteEntity.Criar(Empresa, "Distribuidora Sul");
        _repo.GetByIdAsync(Empresa, cliente.Id).Returns(cliente);

        await Atualizar().ExecuteAsync(new AtualizarClienteCommand(
            Empresa, cliente.Id, "Distribuidora Sul", PessoaJuridica: true, InscricaoEstadual: "999"));

        await _repo.Received(1).AddAlteracaoAsync(
            Arg.Is<Domain.Entities.ClienteAlteracao>(a => a.Campo == "TipoPessoa" && a.ValorNovo == "juridica"));
        await _repo.Received(1).AddAlteracaoAsync(
            Arg.Is<Domain.Entities.ClienteAlteracao>(a => a.Campo == "InscricaoEstadual" && a.ValorNovo == "999"));
    }

    [Fact]
    public async Task Editar_pessoa_fisica_nao_gera_rastro_de_pj()
    {
        var cliente = ClienteEntity.Criar(Empresa, "Ana");
        _repo.GetByIdAsync(Empresa, cliente.Id).Returns(cliente);

        await Atualizar().ExecuteAsync(new AtualizarClienteCommand(Empresa, cliente.Id, "Ana Beatriz"));

        await _repo.DidNotReceive().AddAlteracaoAsync(
            Arg.Is<Domain.Entities.ClienteAlteracao>(a => a.Campo == "TipoPessoa"));
    }

    [Fact]
    public void Vocabulario_de_tipo_pessoa_recusa_valor_fora_da_lista()
    {
        TipoPessoaCliente.EhValido("fisica").Should().BeTrue();
        TipoPessoaCliente.EhValido("juridica").Should().BeTrue();
        TipoPessoaCliente.EhValido("PJ").Should().BeFalse();
        TipoPessoaCliente.EhValido(null).Should().BeFalse();
    }
}
