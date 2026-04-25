using ILGPU.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace SpawnDev.MultiMedia;

/// <summary>
/// DI registration helpers for SpawnDev.MultiMedia. Registers the GPU-backed
/// helpers (<see cref="GpuPixelFormatConverter"/>, <see cref="GpuMjpgDecoder"/>)
/// as singletons sharing a single ILGPU <see cref="Accelerator"/>.
///
/// Stateless types — <see cref="MediaDevices"/>, <see cref="PixelFormatConverter"/>,
/// <see cref="MjpgDecoder"/> — do NOT need DI; they're static / per-call helpers.
/// </summary>
public static class MultiMediaServiceCollectionExtensions
{
    /// <summary>
    /// Register the GPU-backed MultiMedia helpers using an existing
    /// <see cref="Accelerator"/> from the DI container. Use this when your app
    /// already manages an ILGPU Accelerator (typical for SpawnDev.ILGPU
    /// consumers — they register one, then everyone shares it).
    /// </summary>
    /// <example>
    /// <code>
    /// // Program.cs
    /// builder.Services.AddSingleton&lt;Context&gt;(sp =&gt; Context.CreateDefault());
    /// builder.Services.AddSingleton&lt;Accelerator&gt;(sp =&gt;
    ///     sp.GetRequiredService&lt;Context&gt;().GetPreferredDevice(false).CreateAccelerator(sp.GetRequiredService&lt;Context&gt;()));
    /// builder.Services.AddMultiMedia();
    /// </code>
    /// </example>
    public static IServiceCollection AddMultiMedia(this IServiceCollection services)
    {
        // Both helpers take an Accelerator and stay alive for the app lifetime
        // (kernels are JIT-compiled lazily on first use; subsequent calls reuse
        // the compiled kernels). Singleton matches their cost model.
        services.AddSingleton<GpuPixelFormatConverter>(sp =>
            new GpuPixelFormatConverter(sp.GetRequiredService<Accelerator>()));
        services.AddSingleton<GpuMjpgDecoder>(sp =>
            new GpuMjpgDecoder(sp.GetRequiredService<Accelerator>()));
        return services;
    }
}
