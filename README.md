# titi

[![tracked with wai](https://img.shields.io/badge/tracked%20with-wai-blue)](https://github.com/charly-vibes/wai)

Small Mono Repo tool for C# Projects.

## Status: Design & Planning (Tracer Bullet Phase)

titi is currently in the **spec-first** design phase. The core architecture is defined in `openspec/`, and the implementation of the "tracer bullet" (a basic end-to-end `titi open` command) is the next milestone.

## Purpose

titi is a ClojureCLR-based CLI tool that provides an orchestration layer for .NET monorepos. It resolves the tension between treating internal modules as independent NuGet packages (binary mode) vs. local project references (source mode).

## Core Capabilities

- **Reference swapping**: Toggle `PackageReference` ↔ `ProjectReference` via MSBuild conditional logic.
- **Dynamic solution generation**: Create transient `.slnx` files for a specific project's dependency closure.
- **Cascading version bumps**: Determine version increments based on API surface changes using ApiCompat.
- **Test Impact Analysis**: Generate `Microsoft.Build.Traversal` projects for affected tests.

## Tech Stack

- **ClojureCLR / ClojureCLR.Next** (Implementation Language)
- **.NET 10 SDK** (Runtime)
- **MSBuild / Microsoft.Build.Graph** (Dependency Graph Analysis)
- **Nerdbank.GitVersioning (NBGV)** (Versioning)

## Project Structure

- `openspec/`: Detailed requirements and specifications for all capabilities.
- `.wai/`: Why decisions were made (research, designs, and plans).
- `src/titi/`: ClojureCLR source code (pending implementation).
- `test/titi/`: Project tests (pending implementation).

## Getting Started

Check `justfile` for available commands. Run `wai status` to see the current project phase and next steps.
