#requires -Version 7
[CmdletBinding()]
param([string]$Runner, [switch]$Reenviar)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path (Split-Path $PSScriptRoot)
if (!$Runner) {
    $Runner = @('chess-cycle-csharp','source') | ForEach-Object {
        Join-Path $raiz "$_/ChatAutomation/ChatAutomation.csproj"
    } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$Runner) { throw 'Runner ausente. Informe -Runner seguido do caminho de ChatAutomation.csproj.' }
$rota = Get-Content (Join-Path $PSScriptRoot 'rota.local.json') -Raw | ConvertFrom-Json
& dotnet build $Runner -c Release --nologo
if ($LASTEXITCODE) { throw 'Build C# falhou.' }
function Invoke-Runner([string[]]$Argumentos) {
    $saida = & dotnet run --no-build -c Release --project $Runner -- @Argumentos
    if ($LASTEXITCODE) { throw ($saida -join [Environment]::NewLine) }
    ($saida -join [Environment]::NewLine) | ConvertFrom-Json
}
$lista = Invoke-Runner @('list-chats')
$alvo = @($lista.chats | Where-Object { $_.url -ceq $rota.url })
if ($alvo.Count -gt 1) { throw 'Mais de um cadastro para a mesma URL.' }
if ($alvo.Count) { $nome = $alvo[0].name } else {
    $nome = $rota.alias
    if ($lista.chats | Where-Object name -eq $nome) { throw 'Alias ocupado por outra conversa.' }
    $null = Invoke-Runner @('add-chat','--name',$nome,'--url',$rota.url)
}
$confirmado = (Invoke-Runner @('list-chats')).chats | Where-Object { $_.name -eq $nome -and $_.url -ceq $rota.url }
if (!$confirmado) { throw 'Cadastro sem confirmação de URL.' }
Write-Host "CHAT_REGISTRADO=$nome"
$dir = Join-Path $PSScriptRoot 'evidencias'
$null = New-Item -ItemType Directory -Force $dir
$pedido = Join-Path $dir '0003-prompt.txt'
$marca = Join-Path $dir '0003-tentativa.txt'
if (!(Test-Path $pedido)) {
    $nonce = [guid]::NewGuid().ToString('N')
    "No GitHub alexbonemberger/chat-automation-test_id_0000, main, crie responses/0003.md. Primeira linha CHAT_DESTINO_$nonce. Depois uma frase original e um bloco Markdown delimitado por três crases com linguagem csharp contendo um método estático Soma. Preserve o arquivo se já existir. Confirme o commit. Se faltar acesso de escrita ao GitHub, informe isso aqui." | Set-Content $pedido -Encoding utf8
}
try {
if (!(Test-Path $marca) -or $Reenviar) {
    try { $null = Invoke-Runner @('focus-chat','--chat',$nome) } catch {
        $null = Invoke-Runner @('open-chat','--chat',$nome)
        $null = Invoke-Runner @('focus-chat','--chat',$nome)
    }
    Get-Date -Format o | Set-Content $marca
    $resultado = Invoke-Runner @('send-chat-message','--chat',$nome,'--file',$pedido)
    $resultado | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $dir '0003-envio.json')
}
} catch { Write-Warning "Envio UIA: $_" }
& (Join-Path $PSScriptRoot 'Iniciar.ps1')
