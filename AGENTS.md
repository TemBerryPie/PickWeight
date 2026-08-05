# Repository Guidelines

## Project Structure & Module Organization

- `src/PickWeight.Core/` contains platform-independent models, abstractions, and draw/persistence services. Keep selection rules and reusable business logic here.
- `src/PickWeight.App/` is the WinUI 3 application. Pages live in `Pages/`, presentation logic in `ViewModels/`, app-specific integrations in `Services/`, and images/icons in `Assets/`.
- `src/PickWeight.Tray/` contains the Windows Forms-based system tray host.
- `tests/PickWeight.Core.Tests/` contains xUnit tests for core behavior.
- `dist/` contains published output and should not be edited manually.
- `implicit_config.sample.json` documents the optional implicit-weight configuration format.

## Build, Test, and Development Commands

Run commands from the repository root:

```powershell
dotnet restore PickWeight.slnx
dotnet build src/PickWeight.App/PickWeight.App.csproj -p:Platform=x64
dotnet test tests/PickWeight.Core.Tests/PickWeight.Core.Tests.csproj
dotnet test tests/PickWeight.Core.Tests/PickWeight.Core.Tests.csproj --collect:"XPlat Code Coverage"
```

`restore` downloads dependencies, `build` validates the WinUI application for x64, and `test` runs the platform-independent test suite. For interactive development, open `PickWeight.slnx` in Visual Studio and deploy `PickWeight.App`.

## Coding Style & Naming Conventions

Use C# with four-space indentation, nullable reference types enabled, and implicit usings. Follow existing .NET conventions: `PascalCase` for public types and members, `_camelCase` for private fields, and `camelCase` for parameters and locals. Interfaces use the `I` prefix, such as `IDrawService`.

Keep ViewModels focused on presentation state and commands. Put reusable logic behind abstractions in `PickWeight.Core`; avoid coupling core code to WinUI or Windows APIs. Prefer small, descriptive methods and async methods suffixed with `Async`.

## Testing Guidelines

Tests use xUnit and cover core draw rules, persistence, validation, and import/export behavior. Name test classes after the subject, for example `DrawServiceTests`, and use descriptive method names that state the scenario and expected result. Add or update tests whenever changing selection probabilities, state serialization, or member/group rules.

## Commit & Pull Request Guidelines

This repository has no established Git history yet. Use short, imperative commit subjects, such as `Add ClassIsland notification bridge`, and keep unrelated changes in separate commits.

Pull requests should explain the user-visible behavior, implementation approach, and verification performed. Link related issues when available. Include screenshots for WinUI changes and note any configuration or published-output changes. Do not commit secrets or machine-specific state files.

## Configuration Tips

Runtime state is stored under `%LOCALAPPDATA%\PickWeight\state.json`. Treat `implicit_config.sample.json` as documentation; use a local `implicit_config.json` for development and avoid committing private class lists or weighting data.
