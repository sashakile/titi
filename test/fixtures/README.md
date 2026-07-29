# Test Fixtures

## synthetic-monorepo

A synthetic .NET monorepo with 3 library projects and 2 test projects used for
testing titi's `titi tests list`, `titi tests record`, `titi test-manifest`,
and `titi testaruda-adapter` commands.

### Structure

```
libs/
  Orion.Core.Data/    — Core data types and parsing (Parser.cs, Foo.cs)
  Orion.Auth/         — Authentication service (AuthService.cs)
  Orion.Storage/      — Storage interface (Repository.cs)
tests/
  Orion.UnitTests/    — xUnit tests (BasicTests.cs, LibraryTests.cs)
  Orion.IntegrationTests/ — NUnit integration tests (Tests.cs)
```

### Maintenance

- Regenerate `.titi/graph.cache` when project structure changes (add/remove
  `.csproj` files, add/remove project references) by running `titi affected`
  or `titi open` in the fixture directory.
- Regenerate test-cache edges when test source files change by running
  `titi tests record` in the fixture directory.
- The fixture's `titi.config.json` defines tier mappings and project prefix.
- Source files are minimal — they compile and pass basic tests. Add new source
  files to test edge-building when `DG-01` or `DG-04` logic changes.
