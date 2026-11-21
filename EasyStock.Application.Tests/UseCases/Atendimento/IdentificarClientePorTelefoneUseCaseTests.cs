using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using Microsoft.Extensions.Logging.Abstractions;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

public class IdentificarClientePorTelefoneUseCaseTests
{
    private const string WaId = "5511997573992";
    private const string TelefoneE164 = "+5511997573992";

    private readonly IClienteRepository _clienteRepository = Substitute.For<IClienteRepository>();
    private readonly IClienteStorefrontRepository _clienteStorefrontRepository = Substitute.For<IClienteStorefrontRepository>();
    private readonly IVinculoClienteConversaRepository _vinculos = Substitute.For<IVinculoClienteConversaRepository>();
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IdentificarClientePorTelefoneUseCase _useCase;

    public IdentificarClientePorTelefoneUseCaseTests()
    {
        _useCase = new IdentificarClientePorTelefoneUseCase(
            _clienteRepository, _clienteStorefrontRepository,
            _vinculos, NullLogger<IdentificarClientePorTelefoneUseCase>.Instance);
    }

    [Fact]
    public async Task TelefoneCompartilhadoFicaPendenteSemEscolherPrimeiroNemAlterarHash()
    {
        var donoOtp = ClienteEntity.Criar(_empresaId, "Ana");
        donoOtp.TelefoneHash = ClienteOtp.CalcularTelefoneHash(TelefoneE164);
        _clienteRepository.TemTelefoneCompartilhadoAsync(_empresaId,
            Arg.Is<IReadOnlyList<string>>(t => t.Contains(TelefoneE164) && t.Contains("11997573992")),
            Arg.Is<IReadOnlyList<string>>(h => h.Contains(donoOtp.TelefoneHash)), Arg.Any<CancellationToken>())
            .Returns(true);
        _clienteStorefrontRepository.GetByTelefoneHashAsync(_empresaId, donoOtp.TelefoneHash, Arg.Any<CancellationToken>())
            .Returns(donoOtp);

        var resultado = await _useCase.ExecuteAsync(new(_empresaId, WaId, "Outra pessoa"));

        resultado.IdentidadePendente.Should().BeTrue();
        resultado.Cliente!.Should().BeNull();
        resultado.EhNovo.Should().BeFalse();
        await _clienteRepository.DidNotReceiveWithAnyArgs().AddAsync(default!);
        await _clienteRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
        await _clienteStorefrontRepository.DidNotReceiveWithAnyArgs().GetByTelefoneHashAsync(default, default!, default);
        donoOtp.TelefoneHash.Should().Be(ClienteOtp.CalcularTelefoneHash(TelefoneE164));
    }

    [Fact]
    public async Task ConhecidoRetornaExistente()
    {
        var existente = ClienteEntity.Criar(_empresaId, "Maria Silva");
        existente.RegistrarPedido(DateTime.UtcNow);
        _clienteStorefrontRepository
            .GetByTelefoneHashAsync(_empresaId, ClienteOtp.CalcularTelefoneHash(TelefoneE164), Arg.Any<CancellationToken>())
            .Returns(existente);

        var resultado = await _useCase.ExecuteAsync(
            new IdentificarClientePorTelefoneInput(_empresaId, WaId, "Maria do Zap"));

        resultado.Cliente!.Id.Should().Be(existente.Id);
        resultado.EhNovo.Should().BeFalse();
        resultado.EhLead.Should().BeFalse();
        await _clienteRepository.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task ConhecidoPeloTelefoneDoCadastroRetornaExistente()
    {
        // Cadastro feito pelo ERP (sem TelefoneHash), com o número gravado sem o +55.
        var existente = ClienteEntity.Criar(_empresaId, "João");
        _clienteRepository.FindByTelefoneAsync(_empresaId, "11997573992").Returns(existente);

        var resultado = await _useCase.ExecuteAsync(
            new IdentificarClientePorTelefoneInput(_empresaId, WaId, null));

        resultado.Cliente!.Id.Should().Be(existente.Id);
        resultado.EhNovo.Should().BeFalse();
        resultado.EhLead.Should().BeTrue("OrderCount == 0 define lead");
    }

    [Fact]
    public async Task ConhecidoPeloTelefoneComDdiSemMaisRetornaExistente()
    {
        // #1290: o VO Telefone grava "55 11 99757-3992" digitado no ERP como "5511997573992".
        var existente = ClienteEntity.Criar(_empresaId, "Bia");
        _clienteRepository.FindByTelefoneAsync(_empresaId, "5511997573992").Returns(existente);

        var resultado = await _useCase.ExecuteAsync(
            new IdentificarClientePorTelefoneInput(_empresaId, WaId, null));

        resultado.Cliente!.Id.Should().Be(existente.Id);
        resultado.EhNovo.Should().BeFalse();
        await _clienteRepository.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task ConhecidoPeloTelefoneDoCadastroGanhaTelefoneHash()
    {
        // #1290: sem o hash, o OTP e o checkout guest do site (que buscam por TelefoneHash) criavam
        // outro cadastro para o mesmo cliente.
        var existente = ClienteEntity.Criar(_empresaId, "João");
        _clienteRepository.FindByTelefoneAsync(_empresaId, "11997573992").Returns(existente);

        await _useCase.ExecuteAsync(new IdentificarClientePorTelefoneInput(_empresaId, "551197573992", null));

        existente.TelefoneHash.Should().Be(ClienteOtp.CalcularTelefoneHash(TelefoneE164),
            "o hash é o do E.164 canônico, com o nono dígito, como o cliente digita no OTP");
    }

    [Fact]
    public async Task ConhecidoPeloTelefoneComHashProprioMantemHash()
    {
        var existente = ClienteEntity.Criar(_empresaId, "Carla");
        existente.TelefoneHash = "hash-do-otp";
        _clienteRepository.FindByTelefoneAsync(_empresaId, TelefoneE164).Returns(existente);

        await _useCase.ExecuteAsync(new IdentificarClientePorTelefoneInput(_empresaId, WaId, null));

        existente.TelefoneHash.Should().Be("hash-do-otp");
    }

    [Fact]
    public async Task WaIdSemNonoDigitoEncontraCadastroComNonoDigito()
    {
        // A Meta entrega wa_id de celular BR antigo sem o 9: 55 11 97573992.
        var existente = ClienteEntity.Criar(_empresaId, "Ana");
        _clienteRepository.FindByTelefoneAsync(_empresaId, TelefoneE164).Returns(existente);

        var resultado = await _useCase.ExecuteAsync(
            new IdentificarClientePorTelefoneInput(_empresaId, "551197573992", null));

        resultado.Cliente!.Id.Should().Be(existente.Id);
    }

    [Fact]
    public async Task DesconhecidoCriaLead()
    {
        ClienteEntity? adicionado = null;
        await _clienteRepository.AddAsync(Arg.Do<ClienteEntity>(c => adicionado = c));

        var resultado = await _useCase.ExecuteAsync(
            new IdentificarClientePorTelefoneInput(_empresaId, WaId, "  Fulano de Tal "));

        resultado.EhNovo.Should().BeTrue();
        resultado.EhLead.Should().BeTrue();
        adicionado.Should().NotBeNull();
        adicionado!.Should().BeSameAs(resultado.Cliente);
        adicionado.EmpresaId.Should().Be(_empresaId);
        adicionado.Nome.Should().Be("Fulano de Tal");
        adicionado.OrderCount.Should().Be(0);
        adicionado.Telefone.Should().Be(TelefoneE164);
        adicionado.TelefoneHash.Should().Be(ClienteOtp.CalcularTelefoneHash(TelefoneE164));
        adicionado.Telefones.Should().ContainSingle(t =>
            t.Numero == TelefoneE164 && t.Whatsapp && t.Principal && t.ClienteId == adicionado.Id);
    }

    [Fact]
    public async Task DesconhecidoSemNomeDePerfilUsaNomePadrao()
    {
        var resultado = await _useCase.ExecuteAsync(
            new IdentificarClientePorTelefoneInput(_empresaId, WaId, "   "));

        resultado.Cliente!.Nome.Should().Be(IdentificarClientePorTelefoneUseCase.NomePadraoLead);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Telefone_declarado_existente_exige_posse_sem_modificar_cadastro(bool temHash)
    {
        var existente = ClienteEntity.Criar(_empresaId, "Nome preservado");
        if (temHash)
        {
            existente.TelefoneHash = ClienteOtp.CalcularTelefoneHash(TelefoneE164);
            _clienteStorefrontRepository.GetByTelefoneHashAsync(_empresaId, existente.TelefoneHash, Arg.Any<CancellationToken>()).Returns(existente);
        }
        else _clienteRepository.FindByTelefoneAsync(_empresaId, "11997573992").Returns(existente);
        await _useCase.Invoking(s => s.ExecuteAsync(new(_empresaId, WaId, "Nome alegado", ReutilizarCadastro: false)))
            .Should().ThrowAsync<TelefoneExigeVerificacaoException>();
        existente.Nome.Should().Be("Nome preservado");
        if (!temHash) existente.TelefoneHash.Should().BeNull();
        await _clienteRepository.DidNotReceiveWithAnyArgs().AddAsync(default!);
        await _clienteRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
    }

    [Fact]
    public async Task Telefone_declarado_novo_continua_criando_cliente()
    {
        var resultado = await _useCase.ExecuteAsync(new(_empresaId, WaId, "Nova cliente", ReutilizarCadastro: false));
        resultado.EhNovo.Should().BeTrue();
        resultado.Cliente.Should().NotBeNull();
        await _clienteRepository.Received(1).AddAsync(resultado.Cliente!);
    }

    // #2492 -------------------------------------------------------------------------------------

    private DecisaoVinculoTelefone Decidir(Guid? recusado, Guid? escolhido)
    {
        var hash = ClienteOtp.CalcularTelefoneHash(TelefoneE164);
        var decisao = escolhido is { } z
            ? DecisaoVinculoTelefone.Escolher(_empresaId, hash, z, recusado, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow)
            : DecisaoVinculoTelefone.Recusar(_empresaId, hash, recusado!.Value, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        _vinculos.ObterDecisaoAtivaAsync(_empresaId, Arg.Is<IReadOnlyList<string>>(h => h.Contains(hash)), Arg.Any<CancellationToken>())
            .Returns(decisao);
        return decisao;
    }

    [Fact]
    public async Task Escolha_da_equipe_vence_o_telefone_compartilhado()
    {
        var z = ClienteEntity.Criar(_empresaId, "Pessoa certa");
        Decidir(recusado: Guid.NewGuid(), escolhido: z.Id);
        _clienteRepository.GetByIdAsync(_empresaId, z.Id).Returns(z);
        _clienteRepository.TemTelefoneCompartilhadoAsync(default, default!, default!, default).ReturnsForAnyArgs(true);

        var resultado = await _useCase.ExecuteAsync(new(_empresaId, WaId, "Contato", HonrarDecisaoDaEquipe: true));

        resultado.Cliente!.Id.Should().Be(z.Id);
        await _clienteRepository.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task Escolha_de_cliente_desativado_e_ignorada_e_a_recusa_continua()
    {
        var y = ClienteEntity.Criar(_empresaId, "Pessoa errada");
        var z = ClienteEntity.Criar(_empresaId, "Pessoa certa");
        z.Ativo = false;
        Decidir(recusado: y.Id, escolhido: z.Id);
        _clienteRepository.GetByIdAsync(_empresaId, z.Id).Returns(z);
        _clienteStorefrontRepository.GetByTelefoneHashAsync(_empresaId, ClienteOtp.CalcularTelefoneHash(TelefoneE164), Arg.Any<CancellationToken>())
            .Returns(y);

        var resultado = await _useCase.ExecuteAsync(new(_empresaId, WaId, "Contato", HonrarDecisaoDaEquipe: true));

        resultado.IdentidadePendente.Should().BeTrue();
        await _clienteRepository.DidNotReceiveWithAnyArgs().AddAsync(default!);
        y.TelefoneHash.Should().BeNull("o recusado não ganha o hash do telefone");
    }

    [Fact]
    public async Task Recusa_pula_o_recusado_e_acha_outro_cadastro_do_telefone()
    {
        var y = ClienteEntity.Criar(_empresaId, "Pessoa errada");
        var outro = ClienteEntity.Criar(_empresaId, "Outra pessoa");
        Decidir(recusado: y.Id, escolhido: null);
        _clienteStorefrontRepository.GetByTelefoneHashAsync(_empresaId, ClienteOtp.CalcularTelefoneHash(TelefoneE164), Arg.Any<CancellationToken>())
            .Returns(y);
        _clienteRepository.FindByTelefoneAsync(_empresaId, "11997573992").Returns(outro);

        var resultado = await _useCase.ExecuteAsync(new(_empresaId, WaId, "Contato", HonrarDecisaoDaEquipe: true));

        resultado.Cliente!.Id.Should().Be(outro.Id);
    }

    [Fact]
    public async Task Sem_a_flag_do_webhook_a_decisao_nao_e_lida()
    {
        var existente = ClienteEntity.Criar(_empresaId, "Dona do OTP");
        Decidir(recusado: existente.Id, escolhido: null);
        _clienteStorefrontRepository.GetByTelefoneHashAsync(_empresaId, ClienteOtp.CalcularTelefoneHash(TelefoneE164), Arg.Any<CancellationToken>())
            .Returns(existente);

        var resultado = await _useCase.ExecuteAsync(new(_empresaId, WaId, null));

        resultado.Cliente!.Id.Should().Be(existente.Id);
        await _vinculos.DidNotReceiveWithAnyArgs().ObterDecisaoAtivaAsync(default, default!, default);
    }
}
