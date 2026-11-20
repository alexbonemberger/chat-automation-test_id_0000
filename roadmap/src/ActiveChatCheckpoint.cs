using System.Text;

internal static class ActiveChatCheckpoint
{
    // Identidade de chat e explicitamente persistida. Nunca escolher outro chat
    // porque uma guia mudou de foco ou a rota original permaneceu no bootstrap.
    public static string LoadOrCreate(string path, string initial,
        IReadOnlyCollection<string> registered)
    {
        RequireKnown(initial, registered);
        if (!File.Exists(path))
            AtomicWrite(path, initial);
        var current = File.ReadAllText(path, Encoding.UTF8).Trim();
        RequireKnown(current, registered);
        return current;
    }

    public static void CommitSwitch(string path, string origin, string target,
        IReadOnlyCollection<string> registered)
    {
        RequireKnown(origin, registered);
        RequireKnown(target, registered);
        if (!File.Exists(path))
            throw new InvalidDataException("CHAT_PIN_MISSING");
        var current = File.ReadAllText(path, Encoding.UTF8).Trim();
        if (string.Equals(current, target, StringComparison.Ordinal))
            return; // repeticao idempotente apos queda
        if (!string.Equals(current, origin, StringComparison.Ordinal))
            throw new InvalidDataException("CHAT_PIN_CHANGED: nenhuma substituicao arbitraria.");
        AtomicWrite(path, target);
    }

    private static void RequireKnown(string name, IReadOnlyCollection<string> registered)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            !registered.Contains(name, StringComparer.Ordinal))
            throw new InvalidDataException("CHAT_ATIVO_NAO_CADASTRADO: " + name);
    }

    private static void AtomicWrite(string path, string data)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, data, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    public static void SelfTest()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "roadmap-pin-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "chat.ativo");
            string[] known = ["ORIGEM", "SEGUINTE"];
            Check(LoadOrCreate(path, "ORIGEM", known) == "ORIGEM");
            Check(LoadOrCreate(path, "ORIGEM", known) == "ORIGEM");
            CommitSwitch(path, "ORIGEM", "SEGUINTE", known);
            CommitSwitch(path, "ORIGEM", "SEGUINTE", known);
            Check(LoadOrCreate(path, "ORIGEM", known) == "SEGUINTE");
            Check(Throws(() => CommitSwitch(path, "ORIGEM", "TERCEIRO", known)));
            Check(Throws(() => CommitSwitch(path, "OUTRO", "ORIGEM", known)));
            File.WriteAllText(path, "CORROMPIDO");
            Check(Throws(() => LoadOrCreate(path, "ORIGEM", known)));
            Check(Throws(() => CommitSwitch(path, "ORIGEM", "SEGUINTE", known)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
    }

    private static void Check(bool value)
    {
        if (!value) throw new InvalidDataException("ROADMAP_PIN_SELFTEST_FAIL");
    }
}
