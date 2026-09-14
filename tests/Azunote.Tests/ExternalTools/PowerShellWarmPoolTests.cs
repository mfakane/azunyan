using System.Diagnostics;
using Xunit;

namespace Azunote.Tests;

public sealed class PowerShellWarmPoolTests
{
    private static bool PowerShellAvailable =>
        OperatingSystem.IsWindows()
        && ExternalToolLaunchResolver.ResolvePowerShell() is not null;

    private static ExternalToolDefinition Pwsh(
        string script,
        string? stdin = null,
        ExternalToolInputMode inputMode = ExternalToolInputMode.None,
        string? per = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null) =>
        new(
            script,
            arguments: null,
            inputMode: inputMode,
            per: per,
            stdin: stdin,
            workingDirectory: workingDirectory,
            environment: environment,
            commandMode: ExternalToolCommandMode.Pwsh);

    private static ExternalToolContext Context(string document = "", string selection = "") =>
        new(null, document, selection);

    private static async Task<(ExternalToolResult Cold, ExternalToolResult Warm)> RunBothAsync(
        ExternalToolDefinition definition,
        ExternalToolContext context)
    {
        var cold = await ExternalToolRunner.RunAsync(definition, context, warmPool: null);

        using var pool = new PowerShellWarmPool();
        pool.EnsureWarm();
        await WaitForWarmAsync(pool);
        var warm = await ExternalToolRunner.RunAsync(definition, context, pool);

        Assert.True(pool.StartCount > 0, "No waiting process was started.");
        return (cold, warm);
    }

    private static async Task WaitForWarmAsync(PowerShellWarmPool pool)
    {
        // The pool warms in the background; give it a bounded chance to finish
        // so that the test exercises the warm path and not the fallback.
        for (var attempt = 0; attempt < 100 && !pool.HasWarmProcess; attempt++)
        {
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task Warm_run_matches_a_cold_run_for_standard_input()
    {
        if (!PowerShellAvailable)
        {
            return;
        }

        var definition = Pwsh(
            "$text = [Console]::In.ReadToEnd(); Write-Output ($text.ToUpperInvariant())",
            stdin: "${input}",
            inputMode: ExternalToolInputMode.Document);
        var (cold, warm) = await RunBothAsync(definition, Context(document: "hello warm start"));

        Assert.True(warm.Succeeded, warm.StandardError);
        Assert.Equal(cold.ExitCode, warm.ExitCode);
        Assert.Equal(cold.StandardOutput, warm.StandardOutput);
        Assert.Equal(cold.StandardError, warm.StandardError);
        Assert.Equal("HELLO WARM START", warm.StandardOutput.Trim());
    }

    [Fact]
    public async Task Warm_run_matches_a_cold_run_for_an_explicit_exit_code()
    {
        if (!PowerShellAvailable)
        {
            return;
        }

        var (cold, warm) = await RunBothAsync(
            Pwsh("Write-Output 'before'; exit 3"),
            Context());

        Assert.Equal(3, warm.ExitCode);
        Assert.Equal(cold.ExitCode, warm.ExitCode);
        Assert.Equal(cold.StandardOutput, warm.StandardOutput);
    }

    [Fact]
    public async Task Warm_run_matches_a_cold_run_for_a_thrown_error()
    {
        if (!PowerShellAvailable)
        {
            return;
        }

        var (cold, warm) = await RunBothAsync(Pwsh("throw 'boom'"), Context());

        Assert.False(warm.Succeeded);
        Assert.Equal(cold.ExitCode, warm.ExitCode);
        Assert.Contains("boom", warm.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Warm_run_applies_configured_environment_variables()
    {
        if (!PowerShellAvailable)
        {
            return;
        }

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AZUNOTE_WARM_TEST"] = "warm value"
        };
        var (cold, warm) = await RunBothAsync(
            Pwsh("Write-Output $env:AZUNOTE_WARM_TEST", environment: environment),
            Context());

        Assert.Equal("warm value", warm.StandardOutput.Trim());
        Assert.Equal(cold.StandardOutput, warm.StandardOutput);
    }

    [Fact]
    public async Task Warm_run_applies_the_working_directory()
    {
        if (!PowerShellAvailable)
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"azunote warm {Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var (cold, warm) = await RunBothAsync(
                Pwsh("Write-Output $PWD.Path", workingDirectory: root),
                Context());

            Assert.Equal(
                Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
                warm.StandardOutput.Trim().TrimEnd(Path.DirectorySeparatorChar));
            Assert.Equal(cold.StandardOutput, warm.StandardOutput);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Warm_run_round_trips_non_ascii_text()
    {
        if (!PowerShellAvailable)
        {
            return;
        }

        var (cold, warm) = await RunBothAsync(
            Pwsh("Write-Output 'あずにゃん'"),
            Context());

        Assert.Equal("あずにゃん", warm.StandardOutput.Trim());
        Assert.Equal(cold.StandardOutput, warm.StandardOutput);
    }

    [Fact]
    public async Task Warm_run_matches_a_cold_run_for_each_input_line()
    {
        if (!PowerShellAvailable)
        {
            return;
        }

        var definition = Pwsh(
            "Write-Output ('[' + [Console]::In.ReadToEnd() + ']')",
            stdin: "${input}",
            inputMode: ExternalToolInputMode.Selection,
            per: "line");
        var (cold, warm) = await RunBothAsync(definition, Context(selection: "one\ntwo\nthree"));

        Assert.Equal(3, warm.InvocationCount);
        Assert.Equal(cold.StandardOutput, warm.StandardOutput);
        Assert.Contains("[two]", warm.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_dead_waiting_process_falls_back_to_a_cold_launch()
    {
        if (!PowerShellAvailable)
        {
            return;
        }

        using var pool = new PowerShellWarmPool();
        pool.EnsureWarm();
        await WaitForWarmAsync(pool);
        pool.KillWarmProcessesForTest();

        var result = await ExternalToolRunner.RunAsync(
            Pwsh("Write-Output 'fallback'"),
            Context(),
            pool);

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal("fallback", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task A_pool_without_an_interpreter_falls_back_to_a_cold_launch()
    {
        if (!PowerShellAvailable)
        {
            return;
        }

        using var pool = new PowerShellWarmPool(resolveLauncher: () => null);
        pool.EnsureWarm();
        await Task.Delay(200);

        var result = await ExternalToolRunner.RunAsync(
            Pwsh("Write-Output 'fallback'"),
            Context(),
            pool);

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal("fallback", result.StandardOutput.Trim());
        Assert.Equal(0, pool.StartCount);
    }

    [Fact]
    public async Task The_pool_keeps_one_process_and_refills_after_a_run()
    {
        if (!PowerShellAvailable)
        {
            return;
        }

        using var pool = new PowerShellWarmPool();
        pool.EnsureWarm();
        pool.EnsureWarm();
        await WaitForWarmAsync(pool);
        Assert.Equal(1, pool.StartCount);

        var result = await ExternalToolRunner.RunAsync(
            Pwsh("Write-Output 'taken'"),
            Context(),
            pool);
        Assert.True(result.Succeeded, result.StandardError);

        await WaitForWarmAsync(pool);
        Assert.Equal(2, pool.StartCount);
        Assert.True(pool.HasWarmProcess);
    }

    [Fact]
    public async Task Cooling_and_disposing_leave_no_waiting_process()
    {
        if (!PowerShellAvailable)
        {
            return;
        }

        var pool = new PowerShellWarmPool();
        pool.EnsureWarm();
        await WaitForWarmAsync(pool);
        var processId = pool.WarmProcessIdForTest;
        Assert.True(processId.HasValue);

        pool.Cool();
        Assert.False(pool.HasWarmProcess);

        pool.Dispose();
        pool.EnsureWarm();
        await Task.Delay(200);
        Assert.False(pool.HasWarmProcess);
        Assert.True(HasExited(processId!.Value));
    }

    [Fact]
    public async Task An_idle_waiting_process_is_discarded()
    {
        if (!PowerShellAvailable)
        {
            return;
        }

        using var pool = new PowerShellWarmPool(idleTimeout: TimeSpan.FromMilliseconds(200));
        pool.EnsureWarm();
        await WaitForWarmAsync(pool);

        for (var attempt = 0; attempt < 50 && pool.HasWarmProcess; attempt++)
        {
            await Task.Delay(100);
        }

        Assert.False(pool.HasWarmProcess);
    }

    [Fact]
    public async Task The_resting_count_follows_the_configured_idle_processes()
    {
        if (!PowerShellAvailable)
        {
            return;
        }

        using var pool = new PowerShellWarmPool();
        pool.Configure(maxProcesses: 4, idleProcesses: 2);
        pool.EnsureWarm();
        await WaitForCountAsync(pool, 2);

        Assert.Equal(2, pool.TargetProcessCount);
        Assert.Equal(2, pool.WarmProcessCount);
        Assert.Equal(2, pool.StartCount);
    }

    [Fact]
    public void A_reservation_raises_the_target_up_to_the_maximum_and_then_restores_it()
    {
        using var pool = new PowerShellWarmPool(resolveLauncher: () => null);
        pool.Configure(maxProcesses: 4, idleProcesses: 2);
        pool.EnsureWarm();
        Assert.Equal(2, pool.TargetProcessCount);

        using (pool.Reserve(3))
        {
            Assert.Equal(3, pool.TargetProcessCount);
            using (pool.Reserve(50))
            {
                Assert.Equal(4, pool.TargetProcessCount);
            }

            Assert.Equal(3, pool.TargetProcessCount);
        }

        Assert.Equal(2, pool.TargetProcessCount);
    }

    [Fact]
    public void A_reservation_below_the_resting_count_does_not_lower_it()
    {
        using var pool = new PowerShellWarmPool(resolveLauncher: () => null);
        pool.Configure(maxProcesses: 4, idleProcesses: 3);
        pool.EnsureWarm();

        using (pool.Reserve(2))
        {
            Assert.Equal(3, pool.TargetProcessCount);
        }

        Assert.Equal(3, pool.TargetProcessCount);
    }

    [Fact]
    public void No_pwsh_tool_keeps_the_target_at_zero()
    {
        using var pool = new PowerShellWarmPool(resolveLauncher: () => null);
        pool.Configure(maxProcesses: 4, idleProcesses: 2);
        Assert.Equal(0, pool.TargetProcessCount);

        pool.EnsureWarm();
        Assert.Equal(2, pool.TargetProcessCount);

        pool.Cool();
        Assert.Equal(0, pool.TargetProcessCount);
        using (pool.Reserve(4))
        {
            Assert.Equal(0, pool.TargetProcessCount);
        }
    }

    [Fact]
    public async Task A_per_run_reserves_and_then_releases_the_extra_processes()
    {
        if (!PowerShellAvailable)
        {
            return;
        }

        using var pool = new PowerShellWarmPool();
        pool.Configure(maxProcesses: 4, idleProcesses: 1);
        pool.EnsureWarm();
        await WaitForCountAsync(pool, 1);

        var definition = Pwsh(
            "Write-Output ([Console]::In.ReadToEnd())",
            stdin: "${input}",
            inputMode: ExternalToolInputMode.Selection,
            per: "line");
        var result = await ExternalToolRunner.RunAsync(
            definition,
            Context(selection: string.Join('\n', "one", "two", "three", "four", "five")),
            pool);

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal(5, result.InvocationCount);
        // The reservation is released with the run, so the target is the
        // resting count again.
        Assert.Equal(1, pool.TargetProcessCount);
        await WaitForCountAsync(pool, 1);
        Assert.Equal(1, pool.WarmProcessCount);
    }

    [Fact]
    public async Task A_single_part_run_does_not_reserve_extra_processes()
    {
        if (!PowerShellAvailable)
        {
            return;
        }

        using var pool = new PowerShellWarmPool();
        pool.Configure(maxProcesses: 4, idleProcesses: 1);
        pool.EnsureWarm();
        await WaitForCountAsync(pool, 1);

        var result = await ExternalToolRunner.RunAsync(
            Pwsh("Write-Output 'single'"),
            Context(),
            pool);

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal(1, result.InvocationCount);
        // One taken and one replacement: the run never asked for more.
        Assert.Equal(2, pool.StartCount);
    }

    private static async Task WaitForCountAsync(PowerShellWarmPool pool, int count)
    {
        for (var attempt = 0; attempt < 200 && pool.WarmProcessCount < count; attempt++)
        {
            await Task.Delay(50);
        }
    }

    private static bool HasExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
