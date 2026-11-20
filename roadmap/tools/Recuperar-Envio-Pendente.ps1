#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Revision,
    [string]$Root = 'C:\alexbonemberger\on-my-own\chat-automation\projects\chat-automation-test_id_0000',
    [string]$Plano = 'fundamentos-csharp-v2',
    [string]$Runner
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$Repo = 'alexbonemberger/chat-automation-test_id_0000'
if ($Revision -notmatch '^[0-9a-f]{40}$') { throw 'Revision precisa ser SHA-1 de commit completo.' }
if ($Plano -notmatch '^[a-z0-9-]{1,64}$') { throw 'Plano invalido.' }

& gh auth status --hostname github.com
if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI nao autenticado.' }

$Files = @(
    'roadmap/src/Program.cs',
    'roadmap/src/RoadmapVerifier.cs',
    'roadmap/src/RoadmapFocusPreflight.cs',
    'roadmap/plans/fundamentos-csharp-v2.json',
    'roadmap/tools/Inspecionar-Janelas-Agendadas.ps1'
)

foreach ($Rel in $Files) {
    $Endpoint = "repos/$Repo/contents/$Rel" + "?ref=$Revision"
    $Raw = @(& gh api $Endpoint)
    if ($LASTEXITCODE -ne 0) { throw "Falha buscando arquivo $Rel do commit fixo." }
    $Item = ($Raw -join [Environment]::NewLine) | ConvertFrom-Json
    if ($Item.encoding -ne 'base64') { throw "Encoding inesperado: $Rel" }
    $Bytes = [Convert]::FromBase64String(($Item.content -replace '\s',''))
    $Expected = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($Bytes))
    $Target = Join-Path $Root $Rel
    [IO.Directory]::CreateDirectory((Split-Path -Parent $Target)) | Out-Null
    if (Test-Path -LiteralPath $Target) {
        $Current = (Get-FileHash -LiteralPath $Target -Algorithm SHA256).Hash
        if ($Current -ne $Expected) {
            $Backup = "$Target.pre-safe-resume.bak"
            if (-not (Test-Path -LiteralPath $Backup)) {
                Copy-Item -LiteralPath $Target -Destination $Backup
            }
        }
    }
    $Tmp = "$Target.tmp"
    [IO.File]::WriteAllBytes($Tmp,$Bytes)
    [IO.File]::Move($Tmp,$Target,$true)
    if ((Get-FileHash -LiteralPath $Target -Algorithm SHA256).Hash -ne $Expected) {
        throw "SHA256 divergente: $Rel"
    }
    Write-Host "SINCRONIZADO=$Rel SHA256=$Expected"
}

$Project = Join-Path $Root 'roadmap/src/Roadmap.csproj'
& dotnet build $Project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Falha de compilacao do controlador.' }
$Dll = Join-Path $Root 'roadmap/src/bin/Release/net8.0/Roadmap.dll'

& dotnet $Dll focus-self-test
if ($LASTEXITCODE -ne 0) { throw 'Falha do autoteste focus-self-test.' }

$Audit = Join-Path $Root 'roadmap/tools/Inspecionar-Janelas-Agendadas.ps1'
& $Audit -Root $Root -Minutes 30

$RoadmapState = Join-Path $Root "evidencias/roadmap/$Plano"
$RunPath = Join-Path $RoadmapState 'execucao'
$PlanPath = Join-Path $Root "roadmap/plans/$Plano.json"
if (-not (Test-Path -LiteralPath $PlanPath)) { throw 'Plano local nao encontrado.' }
$Plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
if ($Plan.steps.Count -lt 1) { throw 'Plano sem etapas.' }
$FirstStep = [string]$Plan.steps[0].id
$Folder = Join-Path $RoadmapState "$FirstStep/1"
[IO.Directory]::CreateDirectory($Folder) | Out-Null

$RootBase = Split-Path -Parent (Split-Path -Parent $Root)
$RunnerOptions = @(
    (Join-Path $RootBase 'chess-cycle-csharp/ChatAutomation/bin/Release/net8.0-windows/chat-automation.dll'),
    (Join-Path $RootBase 'source/ChatAutomation/bin/Release/net8.0-windows/chat-automation.dll')
)
if ([string]::IsNullOrWhiteSpace($Runner)) {
    $Runner = $RunnerOptions | Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
} else {
    $Runner = [IO.Path]::GetFullPath($Runner)
}
if (-not $Runner) {
    throw 'Runner ChatAutomation nao encontrado; verificar caminho antes de enviar.'
}

# Prova real do mecanismo de foco, sem envio de mensagem.
& dotnet $Dll focus-probe $Runner 'TEST_ID_0000' $Folder
$FocusCode = $LASTEXITCODE
if ($FocusCode -ne 0) {
    Write-Host 'ESTADO=FOCUS_PROBE_BLOCKED_NENHUM_ENVIO'
    Write-Host "LOG=$(Join-Path $Folder 'focus-probe.jsonl')"
    return
}

if (-not (Test-Path -LiteralPath $RunPath)) {
    Write-Host 'ESTADO=EXECUCAO_NAO_INICIADA'
    & (Join-Path $Root 'roadmap/Iniciar.ps1') -Plano $Plano -Runner $Runner
    return
}

$RunId = (Get-Content -LiteralPath $RunPath -Raw).Trim()
if ($RunId -notmatch '^[0-9a-f]{32}$') { throw 'ID de execucao invalido.' }
$Remote = "roadmap-runs/$RunId/$FirstStep/1/Program.cs"
$Sent = Join-Path $Folder 'envio.txt'
$SentReply = Join-Path $Folder 'envio.json'
$Receipt = Join-Path $Folder 'recibo.json'

$Res = @(& gh api "repos/$Repo/contents/$Remote" --jq '.sha' 2>&1)
$Exit = $LASTEXITCODE
$Exists = $false
if ($Exit -eq 0) {
    $Exists = $true
} elseif (($Res -join ' ') -notmatch 'HTTP 404|Not Found') {
    throw 'Consulta remota inconclusiva: sem autorizar reenvio. ' + ($Res -join ' ')
}

Write-Host "EXECUCAO=$RunId"
Write-Host "FONTE_REMOTA_PRESENTE=$Exists"
Write-Host "ENVIO_MARCADOR_PRESENTE=$(Test-Path -LiteralPath $Sent)"
Write-Host "ENVIO_RECIBO_PRESENTE=$(Test-Path -LiteralPath $SentReply)"
Write-Host "RECIBO_FINAL_PRESENTE=$(Test-Path -LiteralPath $Receipt)"

# Uma repeticao de legado ja registrada nunca autoriza um segundo envio.
# O marcador da primeira tentativa permanece como evidencia.
$Replay = Join-Path $Folder 'reenvio-unico.json'
$Preserved = Join-Path $Folder 'envio.txt.pre-replay-once'
if ((Test-Path -LiteralPath $Replay) -and -not $Exists -and
    -not (Test-Path -LiteralPath $Receipt) -and
    -not (Test-Path -LiteralPath $Sent)) {
    Write-Host 'ESTADO=REPLAY_ANTERIOR_SEM_MARCADOR_DE_SEGUNDO_ENVIO'
    Write-Host 'DECISAO=NAO_REPETIR_AUTOMATICAMENTE'
    return
}

if ((Test-Path -LiteralPath $Sent) -and -not $Exists -and
    -not (Test-Path -LiteralPath $Receipt)) {
    # O marcador legacy e gravado ANTES do comando de envio, e nao prova
    # entrega. A ausencia de codigo e de envio.json tambem nao prova falha
    # do envio. Permitir no maximo UMA repeticao idempotente autorizada para
    # este mesmo destino; preservar o marcador antigo como evidencia.
    $Replay = Join-Path $Folder 'reenvio-unico.json'
    $Preserved = Join-Path $Folder 'envio.txt.pre-replay-once'
    if (Test-Path -LiteralPath $SentReply) {
        Write-Host 'ESTADO=ENVIO_CONFIRMADO_NA_CLI_SEM_FONTE_REMOTA'
        Write-Host 'DECISAO=AGUARDAR_CODIGO_SEM_REENVIO'
        return
    }
    if ((Test-Path -LiteralPath $Replay) -or
        (Test-Path -LiteralPath $Preserved)) {
        Write-Host 'ESTADO=REPETICAO_JA_AUTORIZADA_SEM_FONTE_REMOTA'
        Write-Host 'DECISAO=NAO_REENVIAR_SEGUNDA_VEZ'
        return
    }
    $Age = ((Get-Date).ToUniversalTime() -
        (Get-Item -LiteralPath $Sent).LastWriteTimeUtc).TotalMinutes
    if ($Age -lt 3) {
        Write-Host "ESTADO=ENVIO_RECENTE_SEM_COMPROVACAO IDADE_MIN=$([math]::Round($Age,2))"
        Write-Host 'DECISAO=NAO_REENVIAR_AGORA'
        return
    }
    $Record = [pscustomobject]@{
        schema='chat-automation.legacy-intent-replay/1'
        execution=$RunId
        source=$Remote
        marker='envio.txt.pre-replay-once'
        previousSendStatus='UNKNOWN'
        githubSourcePresent=$false
        replyReceiptPresent=$false
        observedUtc=[DateTimeOffset]::UtcNow.ToString('O')
        maximumAdditionalSendAttempts=1
        note='O envio antigo pode ter ocorrido. Repeticao unica da mesma tarefa GitHub idempotente.'
    }
    $ReplayTemp = "$Replay.tmp"
    $Record | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $ReplayTemp -Encoding utf8
    [IO.File]::Move($ReplayTemp,$Replay)
    Move-Item -LiteralPath $Sent -Destination $Preserved -ErrorAction Stop
    Write-Host 'ESTADO=REPLAY_UNICO_REGISTRADO'
    Write-Host 'RISCO=O_ENVIO_ANTERIOR_PODE_TER_OCORRIDO'
    Write-Host 'PROTECAO=APENAS_UM_ENVIO_ADICIONAL_MESMO_DESTINO'
}

Write-Host 'ESTADO=RETOMADA_PERMITIDA_PELO_CONTROLADOR'
& (Join-Path $Root 'roadmap/Iniciar.ps1') -Plano $Plano -Runner $Runner
