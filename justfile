# titi — monorepo CLI for C# projects
# https://just.systems

project := justfile_directory()
fixture := project + "/test/fixtures/sample-monorepo"

# Build the CLI project
build:
    cd {{project}} && dotnet build src/titi/titi.csproj

# Build in Release
build-release:
    cd {{project}} && dotnet build src/titi/titi.csproj --configuration Release

# Run unit + integration tests (excludes slow synthetic-fixture builds)
test:
    cd {{project}} && dotnet test test/titi/titi.Tests.csproj --filter "FullyQualifiedName!~Synthetic"

# Run all tests including slow synthetic-fixture builds
test-full:
    cd {{project}} && dotnet test test/titi/titi.Tests.csproj

# Run tests with coverage
test-coverage:
    cd {{project}} && dotnet test test/titi/titi.Tests.csproj --collect:"XPlat Code Coverage"

# Publish the CLI (self-contained, Linux x64)
publish:
    cd {{project}} && dotnet publish src/titi/titi.csproj --configuration Release --self-contained -r linux-x64 -o dist/

# Run `titi open` against the sample-monorepo fixture (smoke test)
titi-open:
    cd {{fixture}} && dotnet run --project "{{project}}/src/titi/titi.csproj" -- open Orion.Core.Data

# Run `titi affected` against the sample-monorepo fixture
titi-affected:
    cd {{fixture}} && dotnet run --project "{{project}}/src/titi/titi.csproj" -- affected

# Run `titi clean` in the fixture
titi-clean:
    cd {{fixture}} && dotnet run --project "{{project}}/src/titi/titi.csproj" -- clean

# Run the full smoke test sequence
smoke: titi-clean titi-open titi-affected

# ── Benchmark ───────────────────────────────────────────────────

# Benchmark CLR cold-start time for adapter timeout guidance.
# Benchmark CLR cold-start time for adapter timeout guidance.
# Delegates to scripts/benchmark-adapter-coldstart.sh
benchmark-adapter-coldstart:
    scripts/benchmark-adapter-coldstart.sh

# Pack NuGet packages
pack:
    cd {{project}} && dotnet pack src/titi/titi.csproj --configuration Release

# Clean build artifacts
clean:
    cd {{project}} && dotnet clean src/titi/titi.csproj
    cd {{project}} && dotnet clean test/titi/titi.Tests.csproj
    cd {{project}} && find src test -maxdepth 5 \( -name "bin" -o -name "obj" \) -print0 2>/dev/null | xargs -r0 rm -rf

# Restore dependencies
restore:
    cd {{project}} && dotnet restore src/titi/titi.csproj
    cd {{project}} && dotnet restore test/titi/titi.Tests.csproj

# Format Clojure sources (requires cljfmt on PATH)
fmt:
    cljfmt fix .

# Check formatting without modifying files
fmt-check:
    cljfmt check .

# Show project status
status:
    wai status

# Run the same checks as CI
ci: restore build-release test dont-check

# Check all dont claims are grounded (verified)
dont-check:
    dont check
