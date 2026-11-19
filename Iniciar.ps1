#requires -Version 7.0
param([switch]$UmaVez)
$ErrorActionPreference = 'Stop'
$null = Get-Command git -CommandType Application -ErrorAction Stop
$scriptPath = Join-Path $PSScriptRoot 'listener.py'
$opcoes = @()
if ($UmaVez) { $opcoes += '--once' }
if (Get-Command py -CommandType Application -ErrorAction SilentlyContinue) {
    & py -3 $scriptPath @opcoes
} else {
    & python $scriptPath @opcoes
}
if ($LASTEXITCODE -ne 0) { throw "O listener terminou com código $LASTEXITCODE." }
