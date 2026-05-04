using CommunityToolkit.Mvvm.ComponentModel;

namespace EasyStok.Mobile.ViewModels;

/// <summary>
/// Base para todos os VMs. Expoe IsBusy + ErrorMessage + helper RunAsync
/// que centraliza try/catch + IsBusy toggle.
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
		}
		finally
		{
			IsBusy = false;
		}
	}
}
