#requires -Version 7
param([switch]$UmaVez)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot
$source = Join-Path $projectRoot 'recebidos/0003/f9516de2be105ca80eeffd6ac6c14e5397a0d543/codigo/bloco-01.cs'
$expected = 'c54956cb7932277da34f0863e4c4124a033081bd4572c1e3b1db543ceda1b06e'
$bytes = [IO.File]::ReadAllBytes($source)
if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)) -ne $expected) { throw "Fonte 0003 divergente: $source" }
$code = [Text.Encoding]::UTF8.GetString($bytes)
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'src/Soma.g.cs'),"internal static class Recebido {`n$code`n}")
& dotnet build (Join-Path $PSScriptRoot 'src/SomaListener.csproj') -c Release --nologo
if ($LASTEXITCODE) { throw 'Falha ao compilar a fonte recebida.' }
Write-Host "COMPILADO_SHA256=$expected"
$dll = Join-Path $PSScriptRoot 'src/bin/Release/net8.0/SomaListener.dll'
$opcoes = @()
if ($UmaVez) { $opcoes += '--once' }
& dotnet $dll $projectRoot @opcoes
if ($LASTEXITCODE) { throw "Listener encerrou com código $LASTEXITCODE." }
