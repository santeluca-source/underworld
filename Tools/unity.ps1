<#
.SYNOPSIS
  Lancia Unity in batchmode sul progetto UNDERWORLD e riassume il risultato.

.EXAMPLE
  powershell -File Tools/unity.ps1 -Action compile
  powershell -File Tools/unity.ps1 -Action setup
  powershell -File Tools/unity.ps1 -Action build
  powershell -File Tools/unity.ps1 -Action editmode
  powershell -File Tools/unity.ps1 -Action method -Method Underworld.EditorTools.Qualcosa.Esegui
  powershell -File Tools/unity.ps1 -Project C:\altra\copia\UnityProject -Action compile

.NOTES
  Un solo Unity alla volta per ogni copia del progetto (Unity blocca la cartella).
  Codice di uscita: 0 = ok, diverso da 0 = errore. Il log completo è in UnityProject/Logs/<azione>.log.
  Percorso di Unity: variabile UNDERWORLD_UNITY oppure il percorso standard sotto.
#>
param(
    [string]$Project = (Join-Path $PSScriptRoot "..\UnityProject"),
    [ValidateSet("compile", "setup", "build", "editmode", "method")]
    [string]$Action = "compile",
    [string]$Method,
    [string]$LogName,
    [int]$TimeoutSec = 1500
)

$ErrorActionPreference = "Stop"
$unity = $env:UNDERWORLD_UNITY
if (-not $unity) { $unity = "C:\Users\sante\Documents\Codex\UnityEditors\6000.3.29f1\Editor\Unity.exe" }
if (-not (Test-Path $unity)) { Write-Host "Unity non trovato: $unity"; exit 2 }

$Project = (Resolve-Path $Project).Path
$logs = Join-Path $Project "Logs"
New-Item -ItemType Directory -Force $logs | Out-Null
if (-not $LogName) { $LogName = $Action }
$log = Join-Path $logs "$LogName.log"
if (Test-Path $log) { Remove-Item $log -Force }

function Quote([string]$s) { if ($s -match '\s') { return '"' + $s + '"' } return $s }

$argList = @("-batchmode", "-nographics", "-projectPath", (Quote $Project), "-logFile", (Quote $log))
$results = $null
switch ($Action) {
    "compile"  { $argList += "-quit" }
    "setup"    { $argList += @("-executeMethod", "Underworld.EditorTools.UnderworldSetup.Run") }
    "build"    { $argList += @("-executeMethod", "Underworld.EditorTools.UnderworldSetup.BuildWindows") }
    "method"   {
        if (-not $Method) { Write-Host "Serve -Method"; exit 2 }
        $argList += @("-executeMethod", $Method, "-quit")
    }
    "editmode" {
        $results = Join-Path $logs "editmode-results.xml"
        if (Test-Path $results) { Remove-Item $results -Force }
        $argList += @("-runTests", "-testPlatform", "EditMode", "-testResults", (Quote $results))
    }
}

$sw = [Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process $unity -ArgumentList $argList -PassThru -NoNewWindow
$null = $proc.Handle  # senza questa riga PowerShell perde il codice di uscita
if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
    try { $proc.Kill() } catch {}
    Write-Host "TIMEOUT dopo $TimeoutSec s. Log: $log"
    exit 3
}
$code = $proc.ExitCode
$sw.Stop()

Write-Host ("Unity {0}: exit={1} tempo={2:n0}s log={3}" -f $Action, $code, $sw.Elapsed.TotalSeconds, $log)

if (Test-Path $log) {
    $problems = Select-String -Path $log -Pattern "error CS\d+|Scripts have compiler errors|\[UNDERWORLD\].*(fallito|Failed)|^\s*\w*Exception:|Error building Player" |
        Select-Object -First 40
    foreach ($p in $problems) { Write-Host ("  ! " + $p.Line.Trim()) }
    $info = Select-String -Path $log -Pattern "\[UNDERWORLD\]" | Select-Object -Last 15
    foreach ($i in $info) { Write-Host ("  > " + $i.Line.Trim()) }
}

if ($Action -eq "editmode") {
    if (-not (Test-Path $results)) { Write-Host "Nessun file di risultati dei test."; if ($code -eq 0) { $code = 4 } }
    else {
        [xml]$x = Get-Content $results
        $run = $x.'test-run'
        Write-Host ("Test EditMode: totale={0} ok={1} falliti={2} saltati={3}" -f $run.total, $run.passed, $run.failed, $run.skipped)
        $failed = $x.SelectNodes("//test-case[@result='Failed']")
        foreach ($f in $failed) {
            Write-Host ("  FALLITO " + $f.fullname)
            $msg = $f.failure.message.'#cdata-section'
            if ($msg) { Write-Host ("    " + ($msg -split "`n" | Select-Object -First 3) -join " | ") }
        }
    }
}

exit $code
