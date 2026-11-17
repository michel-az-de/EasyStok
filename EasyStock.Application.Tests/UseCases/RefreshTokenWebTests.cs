using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.RefreshToken;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// #827: o refresh do navegador (sem sessão nativa) gira o token a cada uso. O antigo, se alguém o reapresenta
/// depois da rotação (vazou, foi copiado), é recusado; o novo segue valendo. As sessões nativas têm os próprios
/// testes (SessoesNativasTests); este cobre o caminho que o Web e o Admin usam.
/// </summary>
public class RefreshTokenWebTests
{
    private readonly Dictionary<string, RefreshToken> _tokens = [];
    private readonly IRefreshTokenRepository _repo = Substitute.For<IRefreshTokenRepository>();
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly Usuario _usuario = Usuario.Criar("Ana", "ana@casadababa.com", "hash");
    private readonly RefreshTokenUseCase _renovar;

    public RefreshTokenWebTests()
    {
        _usuario.Empresas.Add(new UsuarioEmpresa { UsuarioId = _usuario.Id, EmpresaId = Guid.NewGuid(), Ativo = true });
        _usuarios.GetByIdAsync(_usuario.Id).Returns(_usuario);
        _repo.GetByTokenHashAsync(Arg.Any<string>()).Returns(c => _tokens.GetValueOrDefault(c.Arg<string>()));
        _repo.AddAsync(Arg.Any<RefreshToken>()).Returns(c =>
        {
            var token = c.Arg<RefreshToken>();
            _tokens[token.TokenHash] = token;
            return Task.CompletedTask;
        });
        // Espelha o UPDATE condicional do repositório: só o primeiro consumo de um token válido afeta a linha.
        _repo.ConsumirAsync(Arg.Any<Guid>(), Arg.Any<DateTime>()).Returns(c =>
        {
            var token = _tokens.Values.SingleOrDefault(t => t.Id == c.Arg<Guid>());
            if (token is null || !token.EstaValido()) return false;
            token.Revogar();
            return true;
        });
        _renovar =new RefreshTokenUseCase(_repo, _usuarios, Substitute.For<IAuditLogRepository>(),
            Substitute.For<IJwtTokenService>(), Substitute.For<IUnitOfWork>(), NullLogger<RefreshTokenUseCase>.Instance);
    }

    [Fact]
    public async Task Renovar_gira_o_refresh_e_o_antigo_reapresentado_e_recusado()
    {
        Gravar("refresh-antigo", DateTime.UtcNow.AddDays(1));

        var renovado = await _renovar.ExecuteAsync(new RefreshTokenCommand("refresh-antigo"));

        renovado.RefreshToken.Should().NotBe("refresh-antigo");
        _tokens[Hash("refresh-antigo")].Revogado.Should().BeTrue("o antigo morre na rotação");
        var reuso = () => _renovar.ExecuteAsync(new RefreshTokenCommand("refresh-antigo"));
        await reuso.Should().ThrowAsync<CredenciaisInvalidasException>();
        var seguinte = await _renovar.ExecuteAsync(new RefreshTokenCommand(renovado.RefreshToken));
        seguinte.RefreshToken.Should().NotBe(renovado.RefreshToken, "o novo segue valendo e gira de novo");
    }

    [Fact]
    public async Task Refresh_vencido_ou_desconhecido_e_recusado_sem_emitir_outro()
    {
        Gravar("refresh-vencido", DateTime.UtcNow.AddMinutes(-1));

        var vencido = () => _renovar.ExecuteAsync(new RefreshTokenCommand("refresh-vencido"));
        var desconhecido = () => _renovar.ExecuteAsync(new RefreshTokenCommand("nunca-emitido"));

        await vencido.Should().ThrowAsync<CredenciaisInvalidasException>();
        await desconhecido.Should().ThrowAsync<CredenciaisInvalidasException>();
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task Renovacao_que_perde_o_consumo_atomico_e_recusada_sem_emitir_outro()
    {
        // #2745: outra renovação com o mesmo token consumiu a linha primeiro; esta não pode ganhar um par novo.
        var token = Gravar("refresh-disputado", DateTime.UtcNow.AddDays(1));
        _repo.ConsumirAsync(token.Id, Arg.Any<DateTime>()).Returns(false);

        var perdedora = () => _renovar.ExecuteAsync(new RefreshTokenCommand("refresh-disputado"));

        await perdedora.Should().ThrowAsync<CredenciaisInvalidasException>();
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    private RefreshToken Gravar(string valor, DateTime expiraEm) =>
        _tokens[Hash(valor)] = RefreshToken.Criar(_usuario.Id, Hash(valor), expiraEm, null, null);

    private static string Hash(string valor) => TokenHashHelper.ComputeSha256Hash(valor);
}
