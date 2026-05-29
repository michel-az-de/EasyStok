using System.Linq.Expressions;

namespace EasyStock.Infra.Postgre.Data.Extensions;

internal static class AuditFieldsModelBuilderExtensions
{
    public static EntityTypeBuilder<T> ConfigureCriadoEm<T>(
        this EntityTypeBuilder<T> builder,
        Expression<Func<T, DateTime>> selector) where T : class
    {
        builder.Property(selector).IsRequired();
        return builder;
    }

    public static EntityTypeBuilder<T> ConfigureAtualizadoEm<T>(
        this EntityTypeBuilder<T> builder,
        Expression<Func<T, DateTime>> selector) where T : class
    {
        builder.Property(selector).IsRequired();
        return builder;
    }
}
