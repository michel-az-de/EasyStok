using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Services;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

/// <summary>
/// B1 (#2070): o suporte é o perfil global (sem empresa) de nível SuperAdmin. Um perfil de empresa com nível SuperAdmin é
/// dado inválido: vale Administração dentro da empresa e nunca abre a plataforma.
/// </summary>
public class PerfilSuporteGlobalTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();

    [Fact]
    public void Perfil_global_de_nivel_SuperAdmin_e_o_suporte()
    {
        var perfil = new Perfil { Nivel = NivelAcesso.SuperAdmin, EmpresaId = null };

        perfil.EhSuporteGlobal.Should().BeTrue();
        perfil.NivelNaEmpresa.Should().Be(NivelAcesso.SuperAdmin);
    }

    [Fact]
    public void Perfil_de_empresa_com_nivel_SuperAdmin_nao_e_suporte_e_vale_Administracao()
    {
        var perfil = new Perfil { Nivel = NivelAcesso.SuperAdmin, EmpresaId = EmpresaId };

        perfil.EhSuporteGlobal.Should().BeFalse();
        perfil.NivelNaEmpresa.Should().Be(NivelAcesso.Admin);
    }

    [Fact]
    public void A_expressao_do_EF_e_a_mesma_regra_da_propriedade()
    {
        var predicado = Perfil.SuporteGlobal.Compile();

        predicado(new Perfil { Nivel = NivelAcesso.SuperAdmin, EmpresaId = null }).Should().BeTrue();
        predicado(new Perfil { Nivel = NivelAcesso.SuperAdmin, EmpresaId = EmpresaId }).Should().BeFalse();
        predicado(new Perfil { Nivel = NivelAcesso.Admin, EmpresaId = null }).Should().BeFalse();
    }

    [Fact]
    public void A_expressao_de_nivel_Admin_na_empresa_concorda_com_NivelNaEmpresa()
    {
        var predicado = Perfil.DaNivelAdminNaEmpresa.Compile();
        foreach (var nivel in Enum.GetValues<NivelAcesso>())
        foreach (var empresa in new Guid?[] { null, EmpresaId })
        {
            var perfil = new Perfil { Nivel = nivel, EmpresaId = empresa };
            predicado(perfil).Should().Be(perfil.NivelNaEmpresa == NivelAcesso.Admin, $"{nivel} com empresa {empresa}");
        }
    }

    [Fact]
    public void Usuario_com_perfil_de_empresa_de_nivel_SuperAdmin_nao_e_superadmin()
    {
        var usuario = Usuario.Criar("Ana", "ana@exemplo.test", "hash");
        usuario.Perfis.Add(new UsuarioPerfil
        {
            UsuarioId = usuario.Id, EmpresaId = EmpresaId,
            Perfil = new Perfil { Nivel = NivelAcesso.SuperAdmin, EmpresaId = EmpresaId },
        });

        usuario.EhSuperAdmin().Should().BeFalse();
    }

    [Theory]
    [InlineData(NivelAcesso.SuperAdmin, false, false)]
    [InlineData(NivelAcesso.SuperAdmin, true, false)]
    [InlineData(NivelAcesso.Admin, true, true)]
    [InlineData(NivelAcesso.Operador, false, true)]
    public void Atribuivel_na_empresa(NivelAcesso nivel, bool daEmpresa, bool esperado) =>
        new Perfil { Nivel = nivel, EmpresaId = daEmpresa ? EmpresaId : null }
            .AtribuivelNaEmpresa(EmpresaId).Should().Be(esperado);

    [Fact]
    public void Perfil_de_outra_empresa_nao_e_atribuivel() =>
        new Perfil { Nivel = NivelAcesso.Operador, EmpresaId = Guid.NewGuid() }.AtribuivelNaEmpresa(EmpresaId).Should().BeFalse();

    [Fact]
    public void Participante_sem_perfil_e_Visualizador_e_perfil_de_outra_empresa_nao_conta()
    {
        var usuario = Usuario.Criar("Ana", "ana@exemplo.test", "hash");
        usuario.Empresas.Add(new UsuarioEmpresa { UsuarioId = usuario.Id, EmpresaId = EmpresaId, Ativo = true });
        usuario.Perfis.Add(new UsuarioPerfil
        {
            UsuarioId = usuario.Id, EmpresaId = Guid.NewGuid(), Perfil = new Perfil { Nivel = NivelAcesso.Admin },
        });

        var participante = ParticipanteDaHierarquia.De(usuario, EmpresaId, ehSuporteGlobal: false);

        participante.Nivel.Should().Be(NivelAcesso.Visualizador);
        participante.VinculoAtivo.Should().BeTrue();
        participante.EhDona.Should().BeFalse();
    }

    [Fact]
    public void Participante_le_a_marca_e_o_perfil_de_mais_poder_da_empresa()
    {
        var usuario = Usuario.Criar("Baba", "baba@exemplo.test", "hash");
        usuario.EmailConfirmado = true;
        usuario.Empresas.Add(new UsuarioEmpresa { UsuarioId = usuario.Id, EmpresaId = EmpresaId, Ativo = true, EhDona = true });
        usuario.Perfis.Add(new UsuarioPerfil { UsuarioId = usuario.Id, EmpresaId = EmpresaId, Perfil = new Perfil { Nivel = NivelAcesso.Operador } });
        usuario.Perfis.Add(new UsuarioPerfil { UsuarioId = usuario.Id, EmpresaId = EmpresaId, Perfil = new Perfil { Nivel = NivelAcesso.Admin } });

        var participante = ParticipanteDaHierarquia.De(usuario, EmpresaId, ehSuporteGlobal: false);

        participante.Nivel.Should().Be(NivelAcesso.Admin);
        participante.EhDonaAtiva.Should().BeTrue();
        participante.ContaProvada.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, null, false)]
    [InlineData(false, "+55•••1234", false)]
    [InlineData(false, ViaDoConvite.Email, true)]
    [InlineData(false, ViaDoConvite.Google, true)]
    [InlineData(true, null, true)]
    public void Conta_provada_nunca_so_pelo_WhatsApp(bool emailConfirmado, string? via, bool provada)
    {
        var usuario = Usuario.Criar("Ana", "ana@exemplo.test", "hash");
        usuario.EmailConfirmado = emailConfirmado;
        usuario.ConviteAceitoVia = via;

        usuario.ContaProvada.Should().Be(provada);
    }
}
