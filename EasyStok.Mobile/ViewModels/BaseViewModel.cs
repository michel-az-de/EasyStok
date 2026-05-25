using CommunityToolkit.Mvvm.ComponentModel;
using EasyStok.Mobile.Services;

namespace EasyStok.Mobile.ViewModels;

/// <summary>
/// Base para todos os VMs. IsBusy + ErrorMessage + helper RunAsync que
/// centraliza try/catch + IsBusy toggle + log em CrashLog + Snackbar
/// pra erros (assim falhas de comando nunca passam despercebidas mesmo
/// quando a Page nao tem campo de erro inline).
/// </summary>
public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    protected async Task RunAsync(Func<Task> work)
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            CrashLog.Write(GetType().Name + ".RunAsync", ex);
            await UiSafe.ShowSnackbarAsync(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
