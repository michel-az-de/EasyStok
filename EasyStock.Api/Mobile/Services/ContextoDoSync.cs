namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// #1543 — o vínculo do sync com o ERP roda passo a passo e engole a falha de um passo para não
/// derrubar os outros. Só que o passo que falhou pode ter deixado entidades alteradas no contexto, e
/// o <c>SaveChanges</c> do passo seguinte gravava essa metade. Quem engole a falha descarta antes o que
/// ficou pendente.
/// </summary>
internal static class ContextoDoSync
{
    /// <summary>
    /// Desfaz no contexto tudo o que ainda não foi gravado: o que foi adicionado sai, o que foi
    /// alterado ou marcado para apagar volta ao valor lido do banco. Use logo depois de um
    /// <c>SaveChanges</c> bem-sucedido ter deixado o contexto limpo, para descartar só o passo que falhou.
    /// </summary>
    public static void DescartarMudancasPendentes(this DbContext db)
    {
        foreach (var entrada in db.ChangeTracker.Entries().ToList())
        {
            switch (entrada.State)
            {
                case EntityState.Added:
                    entrada.State = EntityState.Detached;
                    break;
                case EntityState.Modified:
                    entrada.CurrentValues.SetValues(entrada.OriginalValues);
                    entrada.State = EntityState.Unchanged;
                    break;
                case EntityState.Deleted:
                    entrada.State = EntityState.Unchanged;
                    break;
            }
        }
    }
}
