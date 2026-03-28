global using System.Reactive.Linq;
global using System.Reactive.Subjects;
global using Bunit;
global using DotNetQuery.Core;
// Alias takes precedence over the Bunit namespace import, resolving the
// ambiguity between Bunit.TestContext and TUnit.Core.TestContext in
// TUnit's generated hook code.
global using TestContext = TUnit.Core.TestContext;

[assembly: Timeout(5_000)]
