# net

The protocol's C# side, on .NET 10: `Online.Protocol`, the messages of the three links and the helpers the launcher,
the server and the simulated players share, and its tests. Warnings are errors.

| Folder or file | What it is |
|---|---|
| `src/Online.Protocol/` | The messages of the three links, and nothing that acts on them: what the server, the bots and the launcher share |
| `tests/Protocol.Tests/` | The tests of `Online.Protocol` alone: the relay's packets, the stat packs |
| `tests/TestTimeLimit.cs` | A time limit for every test, compiled into each test project |
| `net.slnx` | The solution: every project above |
| `Directory.Build.props` | The settings every project shares (the framework, warnings as errors, the version) |
| `Directory.Packages.props` | Every package's version, in one place |
| `.editorconfig`, `tests/.editorconfig` | Code style and analyzer severities; the code metrics CA1502 (method complexity) and CA1506 (class coupling) are on, CA1506 not in the tests |
| `CodeMetricsConfig.txt`, `CodeMetrics.globalconfig` | The code metrics' limits (25; 95 a type, 40 a member), and CA1509, which checks that file |
| `global.json` | The .NET SDK's version and the test runner |
| `switches.tsv` | The switch registry's rows for the shared test files (the projects' own are in their folders) |

    cd net
    dotnet test --project tests/Protocol.Tests

From `net/` (its `global.json` runs the tests on Microsoft.Testing.Platform).
