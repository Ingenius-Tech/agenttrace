# Third-party components

AgentTrace Studio uses the following components. Each one keeps its own license. The exact versions are in
`Directory.Packages.props`.

| Component | Use | License |
|---|---|---|
| [Avalonia](https://avaloniaui.net/) (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`) | Desktop UI | MIT |
| [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) | File viewer with syntax highlighting | MIT |
| [DiffPlex](https://github.com/mmanela/diffplex) | Diff computation | Apache 2.0 |
| [codicons](https://github.com/microsoft/vscode-codicons) (`src/AgentTrace.Desktop/Assets/codicon.ttf`) | Icon font | CC BY 4.0 (icons) and MIT (code) |
| [xUnit.net v3](https://xunit.net/), `Avalonia.Headless.XUnit`, `Microsoft.NET.Test.Sdk` | Tests only, not distributed | Apache 2.0 / MIT |

Transitive dependencies of Avalonia (checked in their NuGet packages): SkiaSharp, HarfBuzzSharp and their native binaries,
MicroCom.Runtime, Tmds.DBus.Protocol and Avalonia.BuildServices (all MIT), and `Avalonia.Angle.Windows.Natives` (ANGLE, with the
license included in its package). Publishing an executable (`dotnet publish`) also bundles the .NET libraries (MIT).

## Attribution required by CC BY 4.0

The icons in the `codicon.ttf` font are (c) Microsoft Corporation and are distributed under the
[Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/) license.
