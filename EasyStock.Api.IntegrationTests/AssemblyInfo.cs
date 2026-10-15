// Os testes de integracao sobem a API (WebApplicationFactory) + Postgres (Testcontainers) e
// configuram a app via env vars PROCESS-WIDE — precedencia host-builder > appsettings; ver
// PostgresApiIntegrationTests.CriarFactory / BannerE2ETests.CriarFactory. Se duas CLASSES
// rodarem em paralelo (default do xUnit = 1 collection por classe), uma sobrescreve a
// ConnectionString__DefaultConnection da outra e a app conecta no banco errado -> flake.
// Serializar as collections elimina essa corrida. E o pre-requisito pra este projeto entrar
// no CI (EasyStok.CI.slnf) sem os 89 flakes do incidente do #824. Custo: integracao roda
// sequencial — aceitavel (poucos testes, ja pesados por WebApplicationFactory).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
