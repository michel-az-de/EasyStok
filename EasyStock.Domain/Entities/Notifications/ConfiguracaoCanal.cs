using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Domain.Entities.Notifications;

public class ConfiguracaoCanal
{
    public Guid Id { get; set; }
    public Guid? EmpresaId { get; set; }
    public CanalNotificacao Canal { get; set; }
    public string ProviderAtivo { get; set; } = "stub";
    public byte[]? CredenciaisCifradas { get; set; }
    public int? LimiteDiarioPorUsuario { get; set; }
    public TimeOnly? JanelaPermitidaInicio { get; set; }
    public TimeOnly? JanelaPermitidaFim { get; set; }
    public bool AtivoNoTenant { get; set; } = true;
    public DateTime AtualizadoEm { get; set; }
    public string AtualizadoPor { get; set; } = "system";

    public Empresa? Empresa { get; set; }

    public static ConfiguracaoCanal Criar(
        CanalNotificacao canal,
        string providerAtivo,
        Guid? empresaId = null,
        byte[]? credenciaisCifradas = null,
        bool ativoNoTenant = true) => new()
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Canal = canal,
            ProviderAtivo = providerAtivo,
            CredenciaisCifradas = credenciaisCifradas,
            AtivoNoTenant = ativoNoTenant,
            AtualizadoEm = DateTime.UtcNow
        };

    public void TrocarProvider(string novoProvider, byte[]? novasCredenciais, string atualizadoPor)
    {
        ProviderAtivo = novoProvider;
        CredenciaisCifradas = novasCredenciais;
        AtualizadoPor = atualizadoPor;
        AtualizadoEm = DateTime.UtcNow;
    }

    public void Ativar(string atualizadoPor)
    {
        AtivoNoTenant = true;
        AtualizadoPor = atualizadoPor;
        AtualizadoEm = DateTime.UtcNow;
    }

    public void Desativar(string atualizadoPor)
    {
        AtivoNoTenant = false;
        AtualizadoPor = atualizadoPor;
        AtualizadoEm = DateTime.UtcNow;
    }
}
