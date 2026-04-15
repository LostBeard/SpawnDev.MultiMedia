using Microsoft.Extensions.DependencyInjection;
using SpawnDev.MultiMedia.DemoConsole.UnitTests;
using SpawnDev.UnitTesting;

// Default: run unit tests
try
{
    var services = new ServiceCollection();
    services.AddSingleton<DesktopMultiMediaTests>();
    var sp = services.BuildServiceProvider();
    var runner = new UnitTestRunner(sp, true);
    await ConsoleRunner.Run(args, runner);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
return 0;
