using SpawnDev.MultiMedia;

var devices = await MediaDevices.EnumerateDevices();
Console.WriteLine($"Found {devices.Length} media device(s) on this PC:");
Console.WriteLine();
foreach (var d in devices)
{
    Console.WriteLine($"  [{d.Kind}] {d.Label}");
    Console.WriteLine($"    ID: {d.DeviceId}");
    Console.WriteLine();
}

if (devices.Length == 0)
    Console.WriteLine("  (none found)");
