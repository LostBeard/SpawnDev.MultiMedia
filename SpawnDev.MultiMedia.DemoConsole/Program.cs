using Microsoft.Extensions.DependencyInjection;
using SpawnDev.MultiMedia.DemoConsole.UnitTests;
using SpawnDev.UnitTesting;

// Default: run unit tests
try
{
    var services = new ServiceCollection();
    services.AddSingleton<DesktopMultiMediaTests>();
    var sp = services.BuildServiceProvider();
    // When running a specific test, register the test type but skip full method discovery.
    // Full discovery via FindAllTests loads all method signatures via reflection,
    // which can trigger .NET 10 JIT crashes with heavy generic types (ILGPU).
    // ConsoleRunner.ResolveSingleTest resolves only the requested method by name.
    var runner = new UnitTestRunner(sp, false);
    if (args.Length == 0)
    {
        // Full discovery: list all tests
        runner.SetTestTypes(new[] { typeof(DesktopMultiMediaTests) });
    }
    else
    {
        // Single test: register type without method discovery to avoid .NET 10 JIT crash
        runner.RegisterTestTypes(new[] { typeof(DesktopMultiMediaTests) });
    }
    await ConsoleRunner.Run(args, runner);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
return 0;
