#requires -Version 7
param([string]$Plano='inicial',[string]$Runner)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$base=Split-Path (Split-Path $root)
if (!$Runner) {
    $Runner=@('chess-cycle-csharp','source') | ForEach-Object { Join-Path $base "$_/ChatAutomation/bin/Release/net8.0-windows/chat-automation.dll" } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$Runner) { throw 'Informe -Runner com o caminho do chat-automation.dll já compilado.' }
& gh auth status --hostname github.com
if ($LASTEXITCODE) { throw 'Autentique uma vez com gh auth login e execute de novo.' }
& dotnet build (Join-Path $PSScriptRoot 'src/Roadmap.csproj') -c Release --nologo
if ($LASTEXITCODE) { throw 'Build do controlador falhou.' }
& dotnet (Join-Path $PSScriptRoot 'src/bin/Release/net8.0/Roadmap.dll') $root $Runner $Plano
if ($LASTEXITCODE) { throw "Ciclo interrompido com código $LASTEXITCODE. Estado preservado." }
