using System;

namespace EasyStok.Domain.ValueObjects
{
 public sealed record CodigoLote
 {
 public string Value { get; }

 private CodigoLote(string value)
 {
 Value = value;
 }

 public static CodigoLote From(string value)
 {
 if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Código de lote é obrigatório.", nameof(value));
 var normalized = value.Trim();
 if (normalized.Length >100) throw new ArgumentException("Código de lote muito longo.", nameof(value));
 // Permitir letras, dígitos, '-' e '_' e '/'
 foreach (var ch in normalized)
 {
 if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '_' && ch != '/')
 throw new ArgumentException("Código de lote contém caracteres inválidos.", nameof(value));
 }
 return new CodigoLote(normalized.ToUpperInvariant());
 }

 public override string ToString() => Value;
 }
}
