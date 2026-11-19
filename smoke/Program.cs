using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
switch(args[0]) {
case "list-chats": Console.WriteLine("""{"chats":[{"url":"fixture","name":"fixture"}]}"""); break;
case "is-busy": Console.WriteLine("""{"isBusy":false}"""); break;
case "check-max-limit": Console.WriteLine("""{"isAtMaxLimit":false}"""); break;
case "focus-chat": Console.WriteLine("{}"); break;
case "send-chat-message":
    var prompt=File.ReadAllText(args[^1]);
    var path=Regex.Match(prompt,@"roadmap-runs/[0-9a-f]{32}/(dobro|triplo)/[1-3]/Program\.cs").Value;
    if(path.Length==0) return 2;
    var code=path.Contains("/dobro/1/") ? "int x = ;" : path.Contains("/triplo/") ? "Console.WriteLine(int.Parse(args[0])*3);" : "Console.WriteLine(int.Parse(args[0])*2);";
    var body=JsonSerializer.Serialize(new{message="Prova CI de roadmap (chat simulado)",branch="main",content=Convert.ToBase64String(Encoding.UTF8.GetBytes(code))});
    var info=new ProcessStartInfo("gh"){RedirectStandardInput=true,RedirectStandardOutput=true,UseShellExecute=false};
    foreach(var s in new[]{"api","-X","PUT","repos/alexbonemberger/chat-automation-test_id_0000/contents/"+path,"--input","-"}) info.ArgumentList.Add(s);
    using(var p=Process.Start(info)) { p.StandardInput.Write(body);p.StandardInput.Close();p.StandardOutput.ReadToEnd();p.WaitForExit();if(p.ExitCode!=0)return 1; }
    Console.WriteLine("{}");break;
default:return 2;
}
return 0;
