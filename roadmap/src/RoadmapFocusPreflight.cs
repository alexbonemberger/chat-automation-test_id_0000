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

    private static bool MissingVisible(RoadmapUiReply r) =>
        r.Reason == "InvalidOperationException" &&
        r.Detail.StartsWith("RECOVER_VISIBLE_TAB_MISSING:", StringComparison.Ordinal);
    private static bool Recovered(RoadmapUiReply r) =>
        r.Ok && r.Status == "RECOVERED_EXISTING_TAB";

    public static async Task EnsureAsync(string chat,
        Func<string[], Task<RoadmapUiReply>> ui,
        Func<TimeSpan, Task> delay,
        Action<Observation> journal)
    {
        var opened = false;
        const int maximumFocusAttempts = 5;

        async Task<RoadmapUiReply> Observe(string operation, int attempt, params string[] args)
        {
            var reply = await ui(args);
            journal(new Observation(attempt, operation, reply.ExitCode,
                reply.Status, reply.Reason, reply.Detail));
            return reply;
        }

        // Reutiliza a janela ja aberta, inclusive quando open-chat abriu o Edge
        // mas o ACK perdeu a confirmacao. Nao abre, seleciona ou envia nada.
        async Task<RoadmapUiReply> RecoverExisting(int attempt)
        {
            RoadmapUiReply last = new(1, "BLOCKED", "UNKNOWN", "no attempt");
            for (var probe = 0; probe < 3; probe++)
            {
                last = await Observe("recover-existing-chat", attempt,
                    "recover-existing-chat", "--chat", chat);
                if (Recovered(last) || !MissingVisible(last)) return last;
                if (probe < 2) await delay(TimeSpan.FromMilliseconds(500));
            }
            return last;
        }

        for (var attempt = 1; attempt <= maximumFocusAttempts; attempt++)
        {
            var focus = await Observe("focus-chat", attempt,
                "focus-chat", "--chat", chat);
            if (IsVerifiedFocus(focus)) return;

            if (IsMissing(focus))
            {
                var existing = await RecoverExisting(attempt);
                if (Recovered(existing))
                {
                    await delay(TimeSpan.FromMilliseconds(350));
                    continue; // Ainda exige confirmar novamente focus-chat.
                }
                if (!MissingVisible(existing))
                    throw new InvalidOperationException(
                        "FOCUS_EXISTING_UNVERIFIED: " + existing.Reason +
                        ": " + existing.Detail);

                if (opened)
                    throw new InvalidOperationException(
                        "FOCUS_REOPEN_FAILED: abertura anterior nao produziu " +
                        "uma guia unica comprovada; nenhuma segunda janela sera aberta.");

                opened = true;
                var open = await Observe("open-chat", attempt,
                    "open-chat", "--chat", chat, "--mode", "new-window");
                if (IsVerifiedOpen(open))
                {
                    await delay(TimeSpan.FromMilliseconds(600));
                    continue;
                }
                // Se a UIA abriu a janela mas perdeu o ACK, somente reconcilia
                // uma guia ativa unica e comprovada. NUNCA abre de novo.
                var postOpen = await RecoverExisting(attempt);
                if (Recovered(postOpen))
                {
                    await delay(TimeSpan.FromMilliseconds(350));
                    continue;
                }
                throw new InvalidOperationException(
                    "FOCUS_OPEN_UNVERIFIED: open=" + open.Status + "/" +
                    open.Reason + ": " + open.Detail +
                    "; recover=" + postOpen.Status + "/" +
                    postOpen.Reason + ": " + postOpen.Detail);
            }

            if (IsChanged(focus) && attempt < maximumFocusAttempts)
            {
                await delay(TimeSpan.FromMilliseconds(500));
                continue;
            }
            throw new InvalidOperationException(
                (IsChanged(focus) ? "FOCUS_TARGET_TAB_UNSTABLE" : "FOCUS_PREFLIGHT_BLOCKED")
                + ": " + focus.Reason + ": " + focus.Detail);
        }
        throw new InvalidOperationException(
            "FOCUS_PREFLIGHT_RETRY_EXHAUSTED: nao autorizado enviar mensagem.");
    }

    public static async Task SelfTestAsync()
    {
        static RoadmapUiReply FocusOk() => new(0,"FOCUSED_EXACT_URL","NONE","");
        static RoadmapUiReply Closed() => new(2,"BLOCKED","InvalidOperationException",
            "TARGET_WINDOW_CLOSED: janela fechada");
        static RoadmapUiReply Expired() => new(2,"BLOCKED","InvalidOperationException",
            "PINNED_TAB_NOT_FOUND: guia original ausente");
        static RoadmapUiReply Changed() => new(2,"BLOCKED","InvalidOperationException",
            "TARGET_TAB_CHANGED: guia instavel");
        static RoadmapUiReply Missing() => new(2,"BLOCKED","InvalidOperationException",
            "RECOVER_VISIBLE_TAB_MISSING: nenhuma guia ativa");
        static RoadmapUiReply Ambiguous() => new(2,"BLOCKED","InvalidOperationException",
            "RECOVER_VISIBLE_TAB_AMBIGUOUS: multiplas janelas");
        static RoadmapUiReply RecoverOk() => new(0,"RECOVERED_EXISTING_TAB","NONE","");
        static RoadmapUiReply OpenOk() => new(0,"OPENED_VERIFIED","NONE","");
        static RoadmapUiReply OpenPartial() => new(2,"BLOCKED","InvalidOperationException",
            "OPEN_COMPOSER_NOT_READY: janela abriu, editor nao confirmou");
        async Task<(bool pass,int focus,int recover,int open,List<Observation> log)> Scenario(
            RoadmapUiReply[] focuses,RoadmapUiReply[] recoveries,RoadmapUiReply[] opens)
        {
            int fc=0, rc=0,oc=0;
            var events = new List<Observation>();
            bool pass=true;
            try
            {
                await EnsureAsync("TEST_ID_0000",cmd=>
                {
                    RoadmapUiReply answer;
                    if(cmd[0]=="focus-chat")
                        answer=focuses[Math.Min(fc++,focuses.Length-1)];
                    else if(cmd[0]=="recover-existing-chat")
                    {
                        Check(cmd.SequenceEqual(["recover-existing-chat","--chat","TEST_ID_0000"]));
                        answer=recoveries[Math.Min(rc++,recoveries.Length-1)];
                    }
                    else if(cmd[0]=="open-chat")
                    {
                        Check(cmd.SequenceEqual(["open-chat","--chat","TEST_ID_0000",
                            "--mode","new-window"]));
                        answer=opens[Math.Min(oc++,opens.Length-1)];
                    }
                    else throw new InvalidDataException("UNEXPECTED_UIA_COMMAND");
                    return Task.FromResult(answer);
                },_=>Task.CompletedTask,events.Add);
            }
            catch(InvalidOperationException){pass=false;}
            return(pass,fc,rc,oc,events);
        }
        var ready=await Scenario([FocusOk()],[],[]);
        Check(ready.pass && ready.focus==1 && ready.open==0 && ready.recover==0);
        // Principal regressao do caso real: Edge ja aberto apos falha de ACK.
        var existing=await Scenario([Expired(),FocusOk()],[RecoverOk()],[]);
        Check(existing.pass && existing.focus==2 && existing.recover==1 && existing.open==0);
        var noWindow=await Scenario([Expired(),FocusOk()],[Missing()],[OpenOk()]);
        Check(noWindow.pass && noWindow.open==1 && noWindow.focus==2 && noWindow.recover==3);
        // Janela abriu, open-chat retornou erro, a mesma guia ficou disponivel.
        var partial=await Scenario([Closed(),FocusOk()],
            [Missing(),Missing(),Missing(),RecoverOk()],[OpenPartial()]);
        Check(partial.pass && partial.open==1 && partial.recover==4 && partial.focus==2);
        var ambiguous=await Scenario([Expired()],[Ambiguous()],[]);
        Check(!ambiguous.pass && ambiguous.open==0 && ambiguous.recover==1);
        var noACK=await Scenario([Expired()],
            [Missing(),Missing(),Missing(),Missing()],[OpenPartial()]);
        Check(!noACK.pass && noACK.open==1);
        var transient=await Scenario([Changed(),FocusOk()],[],[]);
        Check(transient.pass && transient.focus==2 && transient.open==0);
        var persistent=await Scenario([Changed()],[],[]);
        Check(!persistent.pass && persistent.focus==5 && persistent.open==0);
        var unexpected=await Scenario([new(2,"BLOCKED","InvalidOperationException","OTHER_ERROR")],[],[]);
        Check(!unexpected.pass && unexpected.open==0);
        var validNested=RoadmapUiReply.Parse(0,
            """{"chat":"TEST_ID_0000","result":{"status":"OPENED_VERIFIED"}}""","");
        Check(IsVerifiedOpen(validNested));
        var invalid=RoadmapUiReply.Parse(2,"not json","err");
        Check(!invalid.Ok && invalid.Status=="INVALID_RESPONSE");
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidDataException("FOCUS_PREFLIGHT_SELFTEST_FAIL");
    }
}
