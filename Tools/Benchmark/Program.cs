using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

internal static class Program
{
    private const int ProducerCount = 10000;
    private const int ResourceCount = 100;
    private const int Warmups = 3;
    private const int Repetitions = 7;
    private const double HorizonSeconds = 30 * 24 * 60 * 60;

    private static int Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Console.WriteLine("Idle Hands analytic fast-forward benchmark");
            Console.WriteLine("UTC: " + DateTime.UtcNow.ToString("O"));
            Console.WriteLine("Runtime: " + RuntimeInformation.FrameworkDescription);
            Console.WriteLine("OS: " + RuntimeInformation.OSDescription);
            Console.WriteLine("Architecture: " + RuntimeInformation.ProcessArchitecture);
            Console.WriteLine("CPU: " + CpuModel());
            Console.WriteLine("Available logical processors: " + Environment.ProcessorCount);
            Console.WriteLine("Stopwatch frequency: " + Stopwatch.Frequency + " ticks/sec; high resolution: " + Stopwatch.IsHighResolution);
            Console.WriteLine("Workload: exactly {0} owned producers, {1} resources, {2:F0} simulated seconds (30 days)",
                ProducerCount, ResourceCount, HorizonSeconds);
            Console.WriteLine("Warmups: {0}; measured repetitions: {1}; fixture/content setup excluded from advance timings",
                Warmups, Repetitions);

            var contentClock = Stopwatch.StartNew();
            var content = CreateContent();
            contentClock.Stop();
            Console.WriteLine("Content compilation: {0:F6} ms", contentClock.Elapsed.TotalMilliseconds);
            for (int i = 0; i < Warmups; i++)
            {
                var simulation = CreateSimulation(content);
                Verify(simulation, simulation.AdvanceTo(HorizonSeconds));
            }

            var samples = new double[Repetitions];
            double total = 0;
            for (int i = 0; i < Repetitions; i++)
            {
                var setupClock = Stopwatch.StartNew();
                var simulation = CreateSimulation(content);
                setupClock.Stop();
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                var result = simulation.AdvanceTo(HorizonSeconds);
                long elapsedTicks = Stopwatch.GetTimestamp() - start;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                samples[i] = elapsedTicks * 1000.0 / Stopwatch.Frequency;
                total += samples[i];
                Verify(simulation, result);
                Console.WriteLine("Run {0}: advance={1:F6} ms ({2} ticks), setup={3:F6} ms, allocated={4} bytes, processed events={5}",
                    i + 1, samples[i], elapsedTicks, setupClock.Elapsed.TotalMilliseconds, allocated, result.ProcessedEvents);
            }
            Array.Sort(samples);
            Console.WriteLine("Advance min/median/mean/max: {0:F6}/{1:F6}/{2:F6}/{3:F6} ms",
                samples[0], samples[Repetitions / 2], total / Repetitions, samples[Repetitions - 1]);
            Console.WriteLine("Verified all 100 balances: 259200000 each; all 10000 producer counts: 1; no per-second events.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static ContentDatabase CreateContent()
    {
        var resources = new ResourceDefinition[ResourceCount];
        for (int i = 0; i < resources.Length; i++) resources[i] = new ResourceDefinition("resource-" + i);
        var producers = new ProducerDefinition[ProducerCount];
        for (int i = 0; i < producers.Length; i++)
            producers[i] = new ProducerDefinition("producer-" + i, null,
                new[] { new ResourceAmount("resource-" + (i % ResourceCount), 1) }, 1);
        return new ContentDatabase(resources, producers);
    }

    private static Simulation CreateSimulation(ContentDatabase content)
    {
        var simulation = new Simulation(content);
        for (int i = 0; i < ProducerCount; i++)
        {
            var result = simulation.Execute(new BuyProducerCommand(content.Producers[i].Id));
            if (!result.Success) throw new InvalidOperationException("Benchmark setup purchase failed: " + result.Error);
        }
        return simulation;
    }

    private static void Verify(Simulation simulation, AdvanceResult result)
    {
        if (!result.Completed || result.ProcessedEvents != 0 || simulation.Time != HorizonSeconds ||
            simulation.Resources.Count != ResourceCount || simulation.Producers.Count != ProducerCount)
            throw new InvalidOperationException("Incorrect benchmark workload or incomplete analytic advance.");
        double expected = (ProducerCount / ResourceCount) * HorizonSeconds;
        foreach (var resource in simulation.Resources)
            if (Math.Abs(resource.Balance.ToDouble() - expected) > 1e-6 || resource.Rate.ToDouble() != ProducerCount / ResourceCount)
                throw new InvalidOperationException("Incorrect analytic balance/rate for " + resource.Id);
        foreach (var producer in simulation.Producers)
            if (producer.Owned != 1 || !producer.Enabled)
                throw new InvalidOperationException("Incorrect active producer workload.");
    }

    private static string CpuModel()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            foreach (string line in File.ReadLines("/proc/cpuinfo"))
                if (line.StartsWith("model name", StringComparison.Ordinal))
                    return line.Substring(line.IndexOf(':') + 1).Trim();
        }
        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "not exposed by operating system";
    }
}
