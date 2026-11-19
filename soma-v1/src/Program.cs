using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

const string remote = "https://github.com/alexbonemberger/chat-automation-test_id_0000.git";
const string sourceHash = "c54956cb7932277da34f0863e4c4124a033081bd4572c1e3b1db543ceda1b06e";
if (args.Length == 0) throw new ArgumentException("Informe a pasta do projeto.");
var root = Path.Combine(Path.GetFullPath(args[0]), "evidencias", "soma-v1");
Directory.CreateDirectory(root);
using var single = new FileStream(Path.Combine(root, "listener.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
var cache = Path.Combine(root, "cache.git");
Directory.CreateDirectory(cache);
Git("init", "--bare", "--quiet");
bool once = args.Skip(1).Contains("--once");
do
{
    try
    {
        Git("fetch", "--quiet", "--depth=1", remote, "refs/heads/main");
        var commit = Git("rev-parse", "FETCH_HEAD").Trim();
        var paths = Git("ls-tree", "-r", "--name-only", commit, "--", "signals").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var path in paths.Where(p => Regex.IsMatch(p, @"^signals/[0-9]{4}\.json$")))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            var receipt = Path.Combine(root, id + ".json");
            var blob = Git("rev-parse", commit + ":" + path).Trim();
            if (File.Exists(receipt))
            {
                using var old = JsonDocument.Parse(File.ReadAllBytes(receipt));
                if (old.RootElement.GetProperty("blob").GetString() != blob)
                    Console.Error.WriteLine($"CONFLITO id={id}: sinal alterado após o recibo.");
                continue;
            }
            try
            {
                using var json = JsonDocument.Parse(Git("show", commit + ":" + path));
                var signal = json.RootElement;
                if (signal.GetProperty("op").GetString() != "soma-v1")
                    throw new FormatException("Operação desconhecida.");
                var a = signal.GetProperty("a").GetInt32();
                var b = signal.GetProperty("b").GetInt32();
                var resultado = Recebido.Soma(a, b);
                var data = new { id, op = "soma-v1", a, b, resultado, commit, blob, fonteSha256 = sourceHash, utc = DateTimeOffset.UtcNow };
                File.WriteAllText(receipt + ".tmp", JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(receipt + ".tmp", receipt);
                Console.WriteLine($"EXECUTADO id={id} Soma({a}, {b}) = {resultado} RECIBO={receipt}");
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or OverflowException or InvalidOperationException)
            {
                Console.Error.WriteLine($"SINAL_INVALIDO id={id}: {ex.Message}");
                if (once) throw;
            }
        }
    }
    catch (IOException ex) when (!once)
    {
        Console.Error.WriteLine($"Falha de leitura ou gravação: {ex.Message}");
    }
    if (!once) Thread.Sleep(8000);
} while (!once);

string Git(params string[] arguments)
{
    var info = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = cache };
    info.Environment["GIT_TERMINAL_PROMPT"] = "0";
    foreach (var argument in arguments) info.ArgumentList.Add(argument);
    using var process = Process.Start(info) ?? throw new IOException("Git indisponível.");
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(45000))
    {
        process.Kill(true);
        throw new IOException("Git excedeu 45 segundos.");
    }
    if (process.ExitCode != 0) throw new IOException(stderr.GetAwaiter().GetResult());
    return stdout.GetAwaiter().GetResult();
}
