using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Infra.Postgre.Data.Configurations.Storefront;

public class ExpedienteLojaConfiguration : IEntityTypeConfiguration<ExpedienteLoja>
{
    public void Configure(EntityTypeBuilder<ExpedienteLoja> builder)
    {
        builder.ToTable("expedientes_loja");
        builder.HasKey(x => x.EmpresaId);

        builder.Ignore(x => x.Horarios);
        builder.Property(x => x.HorariosJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ControleManual).HasConversion<int>().IsRequired();
        builder.Property(x => x.MensagemForaDoHorario).IsRequired().HasMaxLength(ExpedienteLoja.MensagemTamanhoMaximo);
        builder.Property(x => x.MensagemLojaFechada).IsRequired().HasMaxLength(ExpedienteLoja.MensagemTamanhoMaximo);
        builder.Property(x => x.CriadoEm).IsRequired();
        builder.Property(x => x.AlteradoEm).IsRequired();

        builder.HasOne<Domain.Entities.Empresa>()
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
