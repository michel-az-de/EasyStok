using System.Globalization;

namespace EasyStok.Mobile.Converters;

public sealed class InvertedBoolConverter : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is bool b ? !b : true;

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is bool b ? !b : false;
}

public sealed class StringNotNullOrEmptyConverter : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is string s && !string.IsNullOrEmpty(s);

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotImplementedException();
}
