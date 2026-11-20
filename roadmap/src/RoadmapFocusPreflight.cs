using System.Text.Json;

internal sealed record RoadmapUiReply(int ExitCode, string Status, string Reason, string Detail)
{
    public bool Ok => ExitCode == 0 && Status is not "BLOCKED" and not "INVALID_RESPONSE";

    public static RoadmapUiReply Parse(int exitCode, string stdout, string stderr)
    {
        try
        {
            using var json = JsonDocument.Parse(stdout);
            var root = json.RootElement;
            static string? Field(JsonElement obj, string key) =>
                obj.ValueKind == JsonValueKind.Object &&
                obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() : null;
            var nested = root.TryGetProperty("result", out var result)
                ? result : default;
            var status = Field(root, "status") ?? Field(nested, "status") ?? "INVALID_RESPONSE";
            var reason = Field(root, "reasonCode") ?? "NONE";
            var detail = Field(root, "detail") ?? Field(nested, "detail") ?? stderr;
            return new RoadmapUiReply(exitCode, status, reason, detail[..Math.Min(detail.Length, 600)]);
        }
        catch (JsonException)
        {
            return new RoadmapUiReply(exitCode, "INVALID_RESPONSE", "INVALID_JSON",
                ("stdout=" + stdout + " stderr=" + stderr)[..Math.Min(600, ("stdout=" + stdout + " stderr=" + stderr).Length)]);
        }
    }
}

internal static class RoadmapFocusPreflight
{
    internal sealed record Observation(int Attempt, string Operation, int ExitCode,
        string Status, string ReasonCode, string Detail);
    private static bool IsChanged(RoadmapUiReply r) =>
        r.Reason == "InvalidOperationException" &&
        (r.Detail.StartsWith("TARGET_TAB_CHANGED:", StringComparison.Ordinal) ||
         r.Detail.StartsWith("Guia escolhida mudou durante a validação.", StringComparison.Ordinal) ||
         r.Detail.StartsWith("URL ou seleção da guia mudou após o foco.", StringComparison.Ordinal));
    private static bool IsMissing(RoadmapUiReply r) =>
        r.Reason == "InvalidOperationException" &&
        (r.Detail.StartsWith("TARGET_WINDOW_CLOSED:", StringComparison.Ordinal) ||
         r.Detail.StartsWith("PINNED_TAB_NOT_FOUND:", StringComparison.Ordinal) ||
         r.Detail.StartsWith("URL exata não encontrada nas guias inspecionáveis", StringComparison.Ordinal));
    private static bool IsVerifiedFocus(RoadmapUiReply r) =>
        r.Ok && r.Status == "FOCUSED_EXACT_URL";
    private static bool IsVerifiedOpen(RoadmapUiReply r) =>
        r.Ok && r.Status == "OPENED_VERIFIED";

    public static async Task EnsureAsync(string chat,
        Func<string[], Task<RoadmapUiReply>> ui,
        Func<TimeSpan, Task> delay,
        Action<Observation> journal)
    {
        var opened = false;
        const int maximumFocusAttempts = 4;
        for (var attempt = 1; attempt <= maximumFocusAttempts; attempt++)
        {
            var focus = await ui(["focus-chat", "--chat", chat]);
            journal(new Observation(attempt, "focus-chat", focus.ExitCode,
                focus.Status, focus.Reason, focus.Detail));
            if (IsVerifiedFocus(focus)) return;

            if (IsMissing(focus) && !opened)
            {
                opened = true;
                // Nova janela permite recuperar HWND/RuntimeId efêmeros. Não
                // seleciona cópia antiga ou outra guia por URL.
                var open = await ui(["open-chat", "--chat", chat,
                    "--mode", "new-window"]);
                journal(new Observation(attempt, "open-chat", open.ExitCode,
                    open.Status, open.Reason, open.Detail));
                if (!IsVerifiedOpen(open))
                    throw new InvalidOperationException(
                        "FOCUS_OPEN_UNVERIFIED: nenhuma nova abertura ou envio autorizado.");
                await delay(TimeSpan.FromMilliseconds(850));
                continue;
            }
            if (IsChanged(focus) && attempt < maximumFocusAttempts)
            {
                // Reobserva somente o mesmo chat/pin; nunca faz open-chat,
                // rebind, escolha por indice ou envio nesta recuperação.
                await delay(TimeSpan.FromMilliseconds(650));
                continue;
            }

            var category = IsChanged(focus)
                ? "FOCUS_TARGET_TAB_UNSTABLE"
                : "FOCUS_PREFLIGHT_BLOCKED";
            throw new InvalidOperationException(
                category + ": " + focus.Reason + ": " + focus.Detail);
        }
        throw new InvalidOperationException(
            "FOCUS_PREFLIGHT_RETRY_EXHAUSTED: nenhum envio autorizado.");
    }

    public static async Task SelfTestAsync()
    {
        static RoadmapUiReply FocusOk() =>
            new(0, "FOCUSED_EXACT_URL", "NONE", "");
        static RoadmapUiReply Changed() =>
            new(2, "BLOCKED", "InvalidOperationException",
                "TARGET_TAB_CHANGED: a URL/identidade da guia não confirmou após o foco.");
        static RoadmapUiReply Closed() =>
            new(2, "BLOCKED", "InvalidOperationException",
                "TARGET_WINDOW_CLOSED: janela original não existe mais.");
        static RoadmapUiReply PinExpired() =>
            new(2, "BLOCKED", "InvalidOperationException",
                "PINNED_TAB_NOT_FOUND: guia original ausente. Outra cópia não será escolhida.");
        static RoadmapUiReply OpenOk() =>
            new(0, "OPENED_VERIFIED", "NONE", "");
        async Task<(int focuses, int opens, List<Observation> events, bool failed)> RunScenario(
            RoadmapUiReply[] focusReplies, RoadmapUiReply[] openReplies)
        {
            var focuses = 0;
            var opens = 0;
            var observations = new List<Observation>();
            var failed = false;
            try
            {
                await EnsureAsync("TEST_ID_0000", cmd =>
                {
                    RoadmapUiReply reply;
                    if (cmd[0] == "focus-chat")
                    {
                        reply = focusReplies[Math.Min(focuses, focusReplies.Length - 1)];
                        focuses++;
                    }
                    else if (cmd[0] == "open-chat")
                    {
                        Check(cmd.SequenceEqual(new[]{
                            "open-chat","--chat","TEST_ID_0000","--mode","new-window"
                        }));
                        reply = openReplies[Math.Min(opens, openReplies.Length - 1)];
                        opens++;
                    }
                    else throw new InvalidOperationException("UNEXPECTED_COMMAND");
                    return Task.FromResult(reply);
                }, _ => Task.CompletedTask, observations.Add);
            }
            catch (InvalidOperationException) { failed = true; }
            return (focuses, opens, observations, failed);
        }

        var success = await RunScenario([FocusOk()], []);
        Check(!success.failed && success.focuses == 1 && success.opens == 0);

        var transient = await RunScenario([Changed(), FocusOk()], []);
        Check(!transient.failed && transient.focuses == 2 && transient.opens == 0);

        var persistent = await RunScenario([Changed()], []);
        Check(persistent.failed && persistent.focuses == 4 && persistent.opens == 0);
        Check(persistent.events.Count == 4);

        var closed = await RunScenario([Closed(), FocusOk()], [OpenOk()]);
        Check(!closed.failed && closed.focuses == 2 && closed.opens == 1);
        var expired = await RunScenario([PinExpired(), FocusOk()], [OpenOk()]);
        Check(!expired.failed && expired.focuses == 2 && expired.opens == 1);
        Check(expired.events[1].Operation=="open-chat");
        var stillExpired = await RunScenario([PinExpired()], [OpenOk()]);
        Check(stillExpired.failed && stillExpired.focuses == 2 && stillExpired.opens == 1);

        var unsafeOpen = await RunScenario([Closed()], [
            new(0, "OPEN_UNVERIFIED", "NONE", "")]);
        Check(unsafeOpen.failed && unsafeOpen.focuses == 1 && unsafeOpen.opens == 1);

        var unknown = await RunScenario([
            new(2, "BLOCKED", "InvalidOperationException", "OTHER_ERROR")], []);
        Check(unknown.failed && unknown.focuses == 1 && unknown.opens == 0);

        var invalid = RoadmapUiReply.Parse(2,
            """{"status":"BLOCKED","reasonCode":"InvalidOperationException","detail":"TARGET_TAB_CHANGED: teste"}""", "");
        Check(invalid.Reason == "InvalidOperationException" && IsChanged(invalid));

        var validNested = RoadmapUiReply.Parse(0,
            """{"chat":"TEST_ID_0000","result":{"status":"OPENED_VERIFIED"}}""", "");
        Check(IsVerifiedOpen(validNested));

        var malformed = RoadmapUiReply.Parse(2, "not json", "test");
        Check(!malformed.Ok && malformed.Status == "INVALID_RESPONSE");
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidDataException("FOCUS_PREFLIGHT_SELFTEST_FAIL");
    }
}
