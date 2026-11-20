#requires -Version 7
[CmdletBinding()]
param(
    [string]$Root = 'C:\alexbonemberger\on-my-own\chat-automation\projects\chat-automation-test_id_0000',
    [string]$RoadmapRevision,
    [string]$RunnerRevision = 'aad5c8b8a6dc726003b3e190b160c5e3a3e73675',
    [string]$RunnerBranch = 'fix/roadmap-recover-existing-window-20261119',
    [string]$Plano = 'fundamentos-csharp-v2'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$RoadmapRepo = 'alexbonemberger/chat-automation-test_id_0000'
$RunnerRepo = 'alexbonemberger/chat-automation'
if ($RunnerRevision -notmatch '^[0-9a-f]{40}$') { throw 'RunnerRevision SHA invalido.' }
if ($RoadmapRevision -notmatch '^[0-9a-f]{40}$') { throw 'RoadmapRevision SHA invalido.' }
& gh auth status --hostname github.com
if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI precisa estar autenticado.' }

# Repositorio privado clonado em pasta isolada FORA do projeto publico e
# fora dos repositorios de desenvolvimento do usuario.
$PrivateBase = Join-Path $env:LOCALAPPDATA 'ChatAutomation\roadmap-private-runners'
[IO.Directory]::CreateDirectory($PrivateBase) | Out-Null
$Checkout = Join-Path $PrivateBase $RunnerRevision

if (-not (Test-Path -LiteralPath (Join-Path $Checkout '.git'))) {
    if (Test-Path -LiteralPath $Checkout) {
        throw "Checkout privado parcial; nao sobrescrevi: $Checkout"
    }
    & gh repo clone $RunnerRepo $Checkout -- --branch $RunnerBranch --single-branch --depth 1
    if ($LASTEXITCODE -ne 0) {
        throw 'Nao foi possivel clonar o runner privado no checkout isolado.'
    }
}
$Head = (& git -C $Checkout rev-parse HEAD) -join ''
if ($LASTEXITCODE -ne 0 -or $Head.Trim() -ne $RunnerRevision) {
    throw "Runner SHA nao corresponde ao checkout isolado: $Head"
}
$Project = Join-Path $Checkout 'ChatAutomation/ChatAutomation.csproj'
& dotnet build $Project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Compilacao do runner privado falhou.' }
$RunnerDll = Join-Path $Checkout 'ChatAutomation/bin/Release/net8.0-windows/chat-automation.dll'
if (-not (Test-Path -LiteralPath $RunnerDll)) {
    throw "DLL nao encontrada: $RunnerDll"
}
& dotnet $RunnerDll self-test
if ($LASTEXITCODE -ne 0) { throw 'Autoteste do runner privado falhou.' }
Write-Host "RUNNER_PRIVADO_ISOLADO=PASS SHA=$RunnerRevision"
Write-Host "RUNNER_DLL=$RunnerDll"

# Instala apenas o recuperador da revisao exata; seu proprio contrato
# verifica os SHA256 dos arquivos do roadmap, executa o focus-probe real
# e reusa a conversa existente sem criar outra janela.
$Path = 'roadmap/tools/Recuperar-Envio-Pendente.ps1'
$Endpoint = "repos/$RoadmapRepo/contents/$Path" + "?ref=$RoadmapRevision"
$Raw = @(& gh api $Endpoint)
if ($LASTEXITCODE -ne 0) { throw "Nao foi possivel baixar $Path" }
$Doc = ($Raw -join [Environment]::NewLine) | ConvertFrom-Json
if ($Doc.encoding -ne 'base64') { throw 'Codificacao GitHub inesperada.' }
$Bytes = [Convert]::FromBase64String(($Doc.content -replace '\s',''))
$Installer = Join-Path $env:TEMP 'ChatAutomation-Recuperar-Janela-Existente.ps1'
[IO.File]::WriteAllBytes($Installer,$Bytes)
Write-Host 'MODO=REUTILIZAR_JANELA_EXISTENTE_SEM_NOVA_ABERTURA'
Write-Host 'RUNNER_ISOLADO_SHA=' $RunnerRevision
& $Installer -Revision $RoadmapRevision -Root $Root -Runner $RunnerDll -Plano $Plano
