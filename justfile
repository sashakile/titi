# titi — monorepo CLI for C# projects
# https://just.systems

default:
    @just --list

# Build the project
build:
    dotnet build

# Run all tests
test:
    dotnet test

# Pack NuGet packages
pack:
    dotnet pack --configuration Release

# Clean build artifacts
clean:
    dotnet clean
    find src test -maxdepth 3 \( -name "bin" -o -name "obj" \) -print0 2>/dev/null | xargs -r0 rm -rf

# Restore dependencies
restore:
    dotnet restore

# Format Clojure sources (requires cljfmt on PATH)
fmt:
    cljfmt fix .

# Check formatting without modifying files
fmt-check:
    cljfmt check .

# Show project status
status:
    wai status

# Regenerate lock files (after swaps, version bumps, or new dependencies)
lock:
    dotnet restore --force-evaluate

# Run the same checks as CI
ci: restore build test
