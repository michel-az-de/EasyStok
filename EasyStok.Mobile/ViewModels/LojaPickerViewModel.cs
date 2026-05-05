using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyStok.Mobile.Models;
using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;
using System.Collections.ObjectModel;

namespace EasyStok.Mobile.ViewModels;

public sealed partial class LojaPickerViewModel : BaseViewModel
{
	private readonly IAutenticacaoService _auth;
	private readonly IEmpresaService _company;
	private readonly ISecureStore _store;

	public ObservableCollection<Loja> Lojas { get; } = new();

	[ObservableProperty]
	private bool _semLojas;

	[ObservableProperty]
	private string _empresaLabel = string.Empty;

	public LojaPickerViewModel(IAutenticacaoService auth, IEmpresaService company, ISecureStore store)
	{
		_auth = auth;
		_company = company;
		_store = store;
	}

	public async Task InitializeAsync()
	{
		await RunAsync(async () =>
		{
			var empresaId = await _store.GetEmpresaIdAsync()
				?? await _auth.GetEmpresaIdFromTokenAsync();

			if (empresaId is null)
			{
				ErrorMessage = "Empresa nao definida na sessao.";
				await Shell.Current.GoToAsync("//tenant-picker");
				return;
			}

			await _store.SetEmpresaIdAsync(empresaId.Value);
			EmpresaLabel = $"Empresa: {empresaId.Value.ToString()[..8]}…";

			var lojas = await _company.ListLojasAsync(empresaId.Value);
			Lojas.Clear();
			foreach (var l in lojas.Where(x => x.Ativa))
				Lojas.Add(l);

			SemLojas = Lojas.Count == 0;

			// Auto-escolha se ha exatamente uma loja ativa.
			if (Lojas.Count == 1)
			{
				await SelectAsync(Lojas[0]);
			}
		});
	}

	[RelayCommand]
	private Task SelectAsync(Loja loja) => RunAsync(async () =>
	{
		await _store.SetLojaIdAsync(loja.Id);
		await Shell.Current.GoToAsync("//home");
	});

	[RelayCommand]
	private Task SairAsync() => RunAsync(async () =>
	{
		await _auth.SairAsync();
		await Shell.Current.GoToAsync("//login");
	});
}
