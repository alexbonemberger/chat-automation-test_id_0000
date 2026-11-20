using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class RoadmapVerifier
{
    private sealed record Execution(int ExitCode, string Stdout, string Stderr);
    private sealed record CaseReceipt(string input, string expected, string actual, bool pass, int exitCode);
    private sealed record Receipt(bool pass, int buildExit, CaseReceipt[] cases, string sourceSha256);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private const int MaxCapturedCharacters = 65536;

    private static async Task<string> DrainAsync(StreamReader reader)
    {
        var chars = new char[4096];
        var text = new StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(chars.AsMemory())) > 0)
        {
            var remaining = MaxCapturedCharacters - text.Length;
            if (remaining > 0) text.Append(chars, 0, Math.Min(count, remaining));
        }
        return text.ToString();
    }

    private static async Task<Execution> ExecuteAsync(string executable, IEnumerable<string> args,
        TimeSpan timeout)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, RedirectStandardInput = true,
            CreateNoWindow = true
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ??
            throw new IOException("PROCESS_START_FAILED: " + executable);
        process.StandardInput.Close();
        var stdout = DrainAsync(process.StandardOutput);
        var stderr = DrainAsync(process.StandardError);
        using var cancel = new CancellationTokenSource(timeout);
        var timedOut = false;
        try { await process.WaitForExitAsync(cancel.Token); }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync();
        }
        return new Execution(timedOut ? 124 : process.ExitCode,
            await stdout, (await stderr) + (timedOut ? "\nPROCESS_TIMEOUT" : ""));
    }

    public static async Task VerifyAsync(string source, string planPath, string stepId,
        string destination, bool compileOnly = false)
    {
        source = Path.GetFullPath(source);
        planPath = Path.GetFullPath(planPath);
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(destination);
        using var plan = JsonDocument.Parse(await File.ReadAllTextAsync(planPath));
        var steps = plan.RootElement.GetProperty("steps").EnumerateArray()
            .Where(s => s.GetProperty("id").GetString() == stepId).ToArray();
        if (steps.Length != 1)
            throw new InvalidDataException("VERIFIER_STEP_NOT_UNIQUE: " + stepId);
        var testCases = steps[0].GetProperty("cases").EnumerateArray()
            .Select(c => (input: c.GetProperty("input").GetString()!,
                expected: c.GetProperty("output").GetString()!)).ToArray();
        if (testCases.Length == 0)
            throw new InvalidDataException("VERIFIER_CASES_MISSING");

        var work = Path.Combine(Path.GetTempPath(), "roadmap-csharp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            File.Copy(source, Path.Combine(work, "Program.cs"), overwrite: false);
            var projectPath = Path.Combine(work, "Codigo.csproj");
            await File.WriteAllTextAsync(projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
                "<OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>" +
                "<ImplicitUsings>enable</ImplicitUsings><EnableNETAnalyzers>false</EnableNETAnalyzers>" +
                "</PropertyGroup></Project>", new UTF8Encoding(false));
            var build = await ExecuteAsync("dotnet",
                ["build", projectPath, "-c", "Release", "--nologo"],
                TimeSpan.FromMinutes(3));
            var buildLog = (build.Stdout + build.Stderr).Replace(work, "<build>",
                StringComparison.OrdinalIgnoreCase).Replace(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "<usuario>", StringComparison.OrdinalIgnoreCase);
            await File.WriteAllTextAsync(Path.Combine(destination, "build.log"), buildLog,
                new UTF8Encoding(false));

            var passed = build.ExitCode == 0;
            var checks = new List<CaseReceipt>();
            if (passed && !compileOnly)
            {
                var dll = Path.Combine(work, "bin", "Release", "net8.0", "Codigo.dll");
                foreach (var test in testCases)
                {
                    var execution = await ExecuteAsync("dotnet", [dll, test.input],
                        TimeSpan.FromSeconds(20));
                    var actual = (execution.Stdout + execution.Stderr).TrimEnd();
                    var ok = execution.ExitCode == 0 &&
                        string.Equals(actual, test.expected, StringComparison.Ordinal);
                    checks.Add(new CaseReceipt(test.input, test.expected,
                        actual[..Math.Min(actual.Length, 2000)], ok, execution.ExitCode));
                    if (!ok) passed = false;
                }
            }

            var digest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source)));
            var receipt = new Receipt(passed, build.ExitCode, checks.ToArray(), digest);
            await File.WriteAllTextAsync(Path.Combine(destination, "resultado.json"),
                JsonSerializer.Serialize(receipt, Json), new UTF8Encoding(false));
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    public static async Task SelfTestAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(),
            "roadmap-verifier-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var plan = Path.Combine(folder, "plan.json");
            var src = Path.Combine(folder, "Program.cs");
            var json = JsonSerializer.Serialize(new
            {
                steps = new[] { new { id = "dobro", cases = new[]
                    { new { input = "4", output = "8" },
                      new { input = "-7", output = "-14" } } } }
            });
            await File.WriteAllTextAsync(plan, json);

            await File.WriteAllTextAsync(src,
                "Console.WriteLine(checked(int.Parse(args[0]) * 2));");
            var good = Path.Combine(folder, "good");
            await VerifyAsync(src, plan, "dobro", good);
            Check(good, expectedPass: true, expectedBuildExit: 0, expectedCases: 2);

            var compileOnly = Path.Combine(folder, "compile-only");
            await VerifyAsync(src, plan, "dobro", compileOnly, compileOnly: true);
            Check(compileOnly, expectedPass: true, expectedBuildExit: 0, expectedCases: 0);

            await File.WriteAllTextAsync(src, "Console.WriteLine(0);");
            var wrong = Path.Combine(folder, "wrong");
            await VerifyAsync(src, plan, "dobro", wrong);
            Check(wrong, expectedPass: false, expectedBuildExit: 0, expectedCases: 2);

            await File.WriteAllTextAsync(src, "int x = ;");
            var invalid = Path.Combine(folder, "invalid");
            await VerifyAsync(src, plan, "dobro", invalid);
            Check(invalid, expectedPass: false, expectedBuildExit: 1, expectedCases: 0);
            if (!File.ReadAllText(Path.Combine(invalid, "build.log")).Contains("CS1525"))
                throw new InvalidDataException("VERIFIER_SELFTEST_DIAGNOSTIC_MISSING");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void Check(string folder, bool expectedPass, int expectedBuildExit,
        int expectedCases)
    {
        using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "resultado.json")));
        var value = result.RootElement;
        if (value.GetProperty("pass").GetBoolean() != expectedPass ||
            (expectedBuildExit == 0
                ? value.GetProperty("buildExit").GetInt32() != 0
                : value.GetProperty("buildExit").GetInt32() == 0) ||
            value.GetProperty("cases").GetArrayLength() != expectedCases ||
            value.GetProperty("sourceSha256").GetString()?.Length != 64)
            throw new InvalidDataException("VERIFIER_SELFTEST_RESULT_MISMATCH");
    }
}
