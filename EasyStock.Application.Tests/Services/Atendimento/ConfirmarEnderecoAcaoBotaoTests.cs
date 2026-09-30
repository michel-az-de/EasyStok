using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.AcoesBotao;
using EasyStock.Application.UseCases.AdicionarClienteEndereco;
using EasyStock.Application.UseCases.Atendimento.Endereco;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>Monta o <see cref="ConfirmarEnderecoPendente"/> real sobre substitutos.</summary>
internal static class ConfirmacaoEnderecoFake
{
    public static ConfirmarEnderecoPendente Nova(IClienteRepository? clientes = null, IUnitOfWork? uow = null)
    {
        clientes ??= Substitute.For<IClienteRepository>();
        uow ??= Substitute.For<IUnitOfWork>();
        return new ConfirmarEnderecoPendente(new ConfirmarEnderecoClienteUseCase(clientes, uow,
            new AdicionarClienteEnderecoUseCase(clientes, uow, NullLogger<AdicionarClienteEnderecoUseCase>.Instance)));
    }
}

public class ConfirmarEnderecoAcaoBotaoTests
{
    private static readonly DateTime Agora = new(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IEscaladorConversa _escalador = Substitute.For<IEscaladorConversa>();

    [Fact]
    public async Task GravaEnderecoPendenteSemEscalar()
    {
        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = "Maria" };
        _clientes.GetByIdWithDetailsAsync(_empresaId, cliente.Id).Returns(cliente);
        _clientes.GetByIdAsync(_empresaId, cliente.Id).Returns(cliente);
        var conversa = Conversa.Abrir(_empresaId, "5511999998888", Agora, "Maria");
        conversa.VincularCliente(cliente.Id);
        ContextoConversaJson.Gravar(conversa, ContextoConversaJson.EnderecoPendente,
            new EnderecoNormalizado("05500000", "Rua Alvarenga", "120", null, "Butantã", "São Paulo", "SP", null));
        var handler = new ConfirmarEnderecoAcaoBotao(_escalador, ConfirmacaoEnderecoFake.Nova(_clientes), _conversas);

        await handler.ExecutarAsync(_empresaId, conversa, "x", Agora);

        await _clientes.Received(1).AddEnderecoAsync(Arg.Is<ClienteEndereco>(e => e.Padrao && e.Cep == "05500000"));
        await _escalador.DidNotReceiveWithAnyArgs().EscalarAsync(default, default!, default!, default);
        await _conversas.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.Autor == AutorMensagem.Sistema && m.Texto!.Contains("Rua Alvarenga")), Arg.Any<CancellationToken>());
        ContextoConversaJson.Ler<EnderecoNormalizado>(conversa, ContextoConversaJson.EnderecoPendente).Should().BeNull();
    }
}
