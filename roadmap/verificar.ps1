#requires -Version 7
param([string]$Fonte,[string]$Plano,[string]$Etapa,[string]$Destino,[switch]$SoCompilar)
$ErrorActionPreference='Stop'
$casos = @(((Get-Content -LiteralPath $Plano -Raw | ConvertFrom-Json).steps | Where-Object id -ceq $Etapa).cases)
if (!$casos.Count -or $null -eq $casos[0]) { throw "Etapa sem casos: $Etapa" }
$Destino = (New-Item -Force -ItemType Directory $Destino).FullName
$build = New-Item -ItemType Directory (Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N')))
try {
    Copy-Item -LiteralPath $Fonte -Destination (Join-Path $build 'Program.cs')
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup></Project>' | Set-Content (Join-Path $build 'Codigo.csproj')
    $lines = & dotnet build (Join-Path $build 'Codigo.csproj') -c Release --nologo 2>&1
    $buildCode = $LASTEXITCODE
    $lines | ForEach-Object { "$_".Replace($build.FullName,'<build>') } | Set-Content (Join-Path $Destino 'build.log')
    $pass = $buildCode -eq 0
    $checks = @()
    if ($pass -and !$SoCompilar) {
        foreach ($case in $casos) {
            $salida = & dotnet (Join-Path $build 'bin/Release/net8.0/Codigo.dll') ([string]$case.input) 2>&1
            $code = $LASTEXITCODE
            $actual = ($salida -join "`n").TrimEnd()
            $ok = $code -eq 0 -and $actual -ceq [string]$case.output
            $checks += @{input=$case.input; expected=$case.output;actual=$actual.Substring(0,[Math]::Min(2000,$actual.Length));pass=$ok;exitCode=$code}
            if (!$ok) { $pass=$false }
        }
    }
    @{pass=$pass;buildExit=$buildCode;cases=$checks;sourceSha256=(Get-FileHash -LiteralPath $Fonte).Hash} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $Destino 'resultado.json')
} finally { Remove-Item -LiteralPath $build.FullName -Recurse -Force }
