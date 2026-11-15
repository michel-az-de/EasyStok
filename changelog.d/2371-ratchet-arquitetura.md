### Changed

- Arquitetura: a dívida de controllers com DbContext direto (13) e o tamanho do `Program.cs` da API (teto 245, alvo 200) viram ratchet que roda no CI. Controller novo com DbContext ou Program maior reprova; a lista e o teto só descem (#2371).
