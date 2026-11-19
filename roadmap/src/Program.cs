using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class Program
{
    const string Repo = "alexbonemberger/chat-automation-test_id_0000";
    static string Api => "repos/" + Repo;
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    record Case(string Input, string Output);
    record Step(string Id, string Task, Case[] Cases);
    record Plan(string Id, Step[] Steps);
    record Result(int Code, string Out, string Error);
    static bool Id(string s) => Regex.IsMatch(s, "^[a-z0-9-]{1,64}$");
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(12);

    static async Task<Result> Run(string exe, IEnumerable<string> args, string? input = null, bool required = true)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var proc = Process.Start(info) ?? throw new IOException(exe);
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        if (input != null) await proc.StandardInput.WriteAsync(input);
        proc.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try { await proc.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { proc.Kill(true); throw new TimeoutException(exe); }
        var r = new Result(proc.ExitCode, await stdout, await stderr);
        if (required && r.Code != 0) throw new IOException($"{exe}: {r.Error} {r.Out}");
        return r;
    }

    static async Task<JsonElement> Get(string path) =>
        JsonDocument.Parse((await Run("gh", ["api",path])).Out).RootElement.Clone();

    static async Task<byte[]?> Read(string path,string reference)
    {
        var r = await Run("gh", ["api", $"{Api}/contents/{path}?ref={reference}"], required:false);
        if (r.Code != 0 && r.Error.Contains("HTTP 404")) return null;
        if (r.Code != 0) throw new IOException(r.Error);
        return Convert.FromBase64String(JsonDocument.Parse(r.Out).RootElement.GetProperty("content").GetString()!);
    }

    static void Save(string path,string value)
    {
        File.WriteAllText(path+".tmp",value);
        File.Move(path+".tmp",path,true);
    }

    static async Task Publish(string path,string data)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        var old = await Read(path,"main");
        if (old != null && old.SequenceEqual(bytes)) return;
        if (old != null) throw new IOException($"Recibo já existe com outro conteúdo: {path}");
        var body = JsonSerializer.Serialize(new { message="Registra " + path, branch="main",content=Convert.ToBase64String(bytes) });
        await Run("gh",["api","-X","PUT",Api+"/contents/"+path,"--input","-"],body);
    }

    static async Task<int> Main(string[] args)
    {
        try { await Cycle(args); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
    }

    static async Task Cycle(string[] args)
    {
        var root=Path.GetFullPath(args[0]);
        var dll=Path.GetFullPath(args[1]);
        var name=args[2];
        if (!Id(name)) throw new ArgumentException("Nome de plano inválido");
        var planPath=Path.Combine(root,"roadmap","plans",name+".json");
        var planBytes=File.ReadAllBytes(planPath);
        var plan=JsonSerializer.Deserialize<Plan>(planBytes,Json)!;
        if (!Id(plan.Id) || plan.Steps.Length==0 || plan.Steps.Select(s=>s.Id).Distinct().Count()!=plan.Steps.Length ||
            plan.Steps.Any(s=>!Id(s.Id)||s.Cases.Length==0)) throw new InvalidDataException("Plano inválido");
        var route=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"rota.local.json"))).RootElement;
        async Task<JsonElement> Ui(params string[] cmd) => JsonDocument.Parse((await Run("dotnet",new[]{dll}.Concat(cmd))).Out).RootElement.Clone();
        var list=(await Ui("list-chats")).GetProperty("chats");
        var chat=list.EnumerateArray().Single(x=>x.GetProperty("url").GetString()==route.GetProperty("url").GetString()).GetProperty("name").GetString()!;
        var publishedPlan=await Read("roadmap/plans/"+name+".json","main");
        if(publishedPlan==null || !publishedPlan.SequenceEqual(planBytes)) throw new IOException("Publique a mesma versão do plano na main antes de iniciar.");
        var state=Path.Combine(root,"evidencias","roadmap",name);
        Directory.CreateDirectory(state);
        using var lease=new FileStream(Path.Combine(state,"cycle.lock"),FileMode.OpenOrCreate,FileAccess.Write,FileShare.None);
        var runFile=Path.Combine(state,"execucao");
        if (!File.Exists(runFile)) Save(runFile,Guid.NewGuid().ToString("N"));
        var run=File.ReadAllText(runFile);
        var pin=Path.Combine(state,"plano.sha256");
        var digest=Convert.ToHexString(SHA256.HashData(planBytes));
        if (!File.Exists(pin)) Save(pin,digest);
        if (File.ReadAllText(pin)!=digest) throw new IOException("Plano alterado durante a execução.");
        Console.WriteLine($"ROADMAP={name} EXECUCAO={run} CHAT={chat}");
        string previous="";
        foreach (var step in plan.Steps)
        {
            bool passed=false;
            string feedback="";
            for (int attempt=1;attempt<=3;attempt++)
            {
                var dest=$"roadmap-runs/{run}/{step.Id}/{attempt}";
                var folder=Path.Combine(state,step.Id,attempt.ToString());
                Directory.CreateDirectory(folder);
                var path=dest+"/Program.cs";
                var report=Path.Combine(folder,"recibo.json");
                if (!File.Exists(report))
                {
                    if (await Read(path,"main")==null)
                    {
                        var sent=Path.Combine(folder,"envio.txt");
                        if (!File.Exists(sent))
                        {
                            var prompt=$"No GitHub {Repo}, main, crie {path} com o programa de console C# .NET 8 completo. Preserve se já existir. Etapa {step.Id}: {step.Task} Casos: {JsonSerializer.Serialize(step.Cases)}. Leia a versão anterior se indicada: {previous}. Diagnóstico anterior: {feedback}. Grave somente o arquivo .cs e confirme o commit.";
                            var promptFile=Path.Combine(folder,"prompt.txt"); Save(promptFile,prompt);
                            try { await Ui("focus-chat","--chat",chat); }
                            catch (IOException) { await Ui("open-chat","--chat",chat); await Ui("focus-chat","--chat",chat); }
                            var clock=Stopwatch.StartNew();
                            while ((await Ui("is-busy","--chat",chat)).GetProperty("isBusy").GetBoolean())
                            {
                                if(clock.Elapsed>Limit) throw new TimeoutException("Chat ocupado.");
                                await Task.Delay(8000);
                            }
                            if ((await Ui("check-max-limit","--chat",chat)).GetProperty("isAtMaxLimit").GetBoolean()) throw new IOException("Conversa atingiu o limite antes do envio.");
                            Save(sent,DateTimeOffset.UtcNow.ToString("O"));
                            var reply=await Ui("send-chat-message","--chat",chat,"--file",promptFile);
                            Save(Path.Combine(folder,"envio.json"),reply.GetRawText());
                            Console.WriteLine("PEDIDO="+path);
                        }
                        var wait=Stopwatch.StartNew();
                        while(await Read(path,"main")==null)
                        {
                            if(wait.Elapsed>Limit) throw new TimeoutException("Sem fonte no GitHub. Pedido preservado em "+folder);
                            await Task.Delay(8000);
                        }
                    }
                    var commits=await Get($"{Api}/commits?sha=main&path={path}&per_page=1");
                    var sha=commits[0].GetProperty("sha").GetString()!;
                    var src=await Read(path,sha) ?? throw new IOException(path);
                    var remotePlan=await Read("roadmap/plans/"+name+".json",sha);
                    if(remotePlan==null || !remotePlan.SequenceEqual(planBytes)) throw new IOException("O plano local difere do commit do candidato.");
                    var source=Path.Combine(folder,"Program.cs"); File.WriteAllBytes(source,src);
                    await Run("pwsh",["-NoProfile","-File",Path.Combine(root,"roadmap","verificar.ps1"),source,planPath,step.Id,folder,"-SoCompilar"]);
                    var local=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"resultado.json"))).RootElement;
                    var log=File.ReadAllText(Path.Combine(folder,"build.log"));
                    log=log.Replace(root,"<projeto>",StringComparison.OrdinalIgnoreCase).Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<usuario>",StringComparison.OrdinalIgnoreCase);
                    var ok=local.GetProperty("pass").GetBoolean();
                    string? job=null;
                    string tests="";
                    if(ok)
                    {
                        var ticket=$"{run}-{step.Id}-{attempt}";
                        var field=new[]{"workflow","run","roadmap.yml","--repo",Repo,"--ref","main","-f","ticket="+ticket,"-f","commit="+sha,"-f","plano="+name,"-f","etapa="+step.Id,"-f","fonte="+path};
                        async Task<JsonElement?> Find()
                        {
                            var list=await Get($"{Api}/actions/workflows/roadmap.yml/runs?branch=main&event=workflow_dispatch&per_page=100");
                            foreach(var item in list.GetProperty("workflow_runs").EnumerateArray())
                                if(item.GetProperty("display_title").GetString()=="roadmap-"+ticket) return item.Clone();
                            return null;
                        }
                        var action=await Find();
                        if(action==null && !File.Exists(Path.Combine(folder,"despachado")))
                        {
                            await Run("gh",field);
                            Save(Path.Combine(folder,"despachado"),ticket);
                        }
                        var watch=Stopwatch.StartNew();
                        while(action==null || action.Value.GetProperty("status").GetString()!="completed")
                        {
                            if(watch.Elapsed>Limit) throw new TimeoutException("Actions pendente: "+ticket);
                            await Task.Delay(8000); action=await Find();
                        }
                        var id=action.Value.GetProperty("id").GetInt64().ToString();
                        job=action.Value.GetProperty("html_url").GetString();
                        if(action.Value.GetProperty("conclusion").GetString()!="success") {
                            tests="Actions: "+action.Value.GetProperty("conclusion").GetString()+" "+job;
                            ok=false;
                        } else {
                            var artifact=Path.Combine(folder,"actions"); Directory.CreateDirectory(artifact);
                            await Run("gh",["run","download",id,"--repo",Repo,"-n","resultado","-D",artifact]);
                            tests=File.ReadAllText(Path.Combine(artifact,"resultado.json"));
                            var result=JsonDocument.Parse(tests).RootElement;
                            if(result.GetProperty("sourceSha256").GetString()!=Convert.ToHexString(SHA256.HashData(src))) throw new IOException("Hash do candidato divergente");
                            ok=result.GetProperty("pass").GetBoolean();
                        }
                    }
                    Save(report,JsonSerializer.Serialize(new {pass=ok,step=step.Id,attempt,source=path,commit=sha,sha256=Convert.ToHexString(SHA256.HashData(src)),buildLog=log,tests,actions=job},Json));
                }
                var summary=File.ReadAllText(report);
                await Publish(dest+"/recibo.json",summary);
                var receipt=JsonDocument.Parse(summary).RootElement;
                passed=receipt.GetProperty("pass").GetBoolean();
                previous=path;
                Console.WriteLine($"{step.Id} tentativa={attempt} {(passed?"PASS":"FAIL")} {dest}/recibo.json");
                if(passed) break;
                feedback="Leia "+dest+"/recibo.json para o log de compilação e os resultados dos testes.";
            }
            if(!passed) throw new IOException("Três tentativas concluídas na etapa "+step.Id);
        }
        Console.WriteLine("ROADMAP=PASS");
    }
}
