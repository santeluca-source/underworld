<#
.SYNOPSIS
  Scenari multiplayer automatici di UNDERWORLD: server e client veri (contratto, sezione 12).

.DESCRIPTION
  Per ogni scenario legge Tools/scenarios/<nome>.json, avvia i server, aspetta che siano pronti,
  avvia i client con -autotest, aspetta la fine e raccoglie i risultati JSON.
  Lo scenario FALLISCE se un processo fallisce, va oltre il tempo massimo, non si chiude,
  oppure se un log contiene un'eccezione (lista di eccezioni permesse: $ExceptionWhitelist).
  Alla fine chiude tutti i processi e stampa una tabella PASS/FAIL.
  Compatibile con Windows PowerShell 5.1.

  Formato del file di scenario:
  {
    "timeoutSec": 120,
    "servers": [ { "area": "TestGround", "autotest": "<nome>", "args": ["-maxplayers", "2"] } ],
    "clients": [ { "name": "Alfa", "autotest": "<nome>", "graphics": false,
                   "server": 0,                       (indice del server; -1 = nessun -connect)
                   "startAfterInGame": ["Uno"],       (parte quando questi client sono in gioco)
                   "holdUntilOthersDone": true,       (riceve -autotest-release: finisce dopo gli altri)
                   "delaySec": 0, "args": [] } ],
    "checkChildExit": { "client": "Locale", "withinSec": 10 },   (pid in data.childPid del risultato)
    "extraLogs": ["{tmp}/localserver/server.log"]
  }
  Segnaposto negli argomenti: {tmp} cartella temporanea dello scenario, {port:N} = porta base + N,
  {server:N} = 127.0.0.1:<porta del server N>.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/test.ps1 -Scenario all
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/test.ps1 -Scenario connect_two_clients -SkipBuild
#>
param(
    [string]$Scenario = "all",
    [int]$PortBase = 7700,
    [switch]$SkipBuild,
    [int]$TimeoutSec = 0,
    [string]$Exe
)

$ErrorActionPreference = "Stop"
$toolsDir = $PSScriptRoot
$repoRoot = Split-Path $toolsDir -Parent
if (-not $Exe) { $Exe = Join-Path $repoRoot "UnityProject\Builds\Windows\UNDERWORLD.exe" }
$scenarioDir = Join-Path $toolsDir "scenarios"

# Eccezioni permesse nei log (lista minima: aggiungi solo con un motivo scritto).
$ExceptionWhitelist = @(
)

# ---------------- Aiuti ----------------

function Read-Shared([string]$path) {
    if (-not (Test-Path $path)) { return "" }
    try {
        $fs = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
        try {
            $sr = New-Object IO.StreamReader($fs)
            return $sr.ReadToEnd()
        } finally { $fs.Dispose() }
    } catch { return "" }
}

function Find-Exceptions([string]$path) {
    $found = @()
    $text = Read-Shared $path
    if (-not $text) { return $found }
    foreach ($line in ($text -split "`r?`n")) {
        if ($line -match '\b\w*Exception\b') {
            $ok = $false
            foreach ($w in $ExceptionWhitelist) { if ($line -match $w) { $ok = $true } }
            if (-not $ok) { $found += $line.Trim() }
        }
    }
    return $found
}

function Quote-Arg([string]$s) {
    if ($s -eq "") { return '""' }
    if ($s -match '[\s"]') { return '"' + ($s -replace '"', '\"') + '"' }
    return $s
}

$script:AllProcs = New-Object System.Collections.ArrayList

function Start-Game([string[]]$gameArgs) {
    $argString = ($gameArgs | ForEach-Object { Quote-Arg $_ }) -join " "
    $p = Start-Process -FilePath $Exe -ArgumentList $argString -PassThru
    $null = $p.Handle  # senza questa riga PowerShell perde il codice di uscita
    [void]$script:AllProcs.Add($p)
    return $p
}

function Stop-Proc($p) {
    if ($p -and -not $p.HasExited) { try { $p.Kill(); $null = $p.WaitForExit(5000) } catch {} }
}

function Stop-Pid([int]$procId) {
    if ($procId -le 0) { return }
    try { $x = Get-Process -Id $procId -ErrorAction Stop; $x.Kill() } catch {}
}

function Wait-Until([scriptblock]$cond, [double]$seconds) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $seconds) {
        if (& $cond) { return $true }
        Start-Sleep -Milliseconds 200
    }
    return (& $cond)
}

# La virgola impedisce a PowerShell di "srotolare" un array di un solo elemento.
function As-Array($x) { if ($null -eq $x) { return ,@() } return ,@($x) }

# ---------------- Uno scenario ----------------

function Invoke-Scenario([string]$file, [int]$index, [string]$runDir) {
    $name = [IO.Path]::GetFileNameWithoutExtension($file)
    $spec = (Get-Content $file -Raw -Encoding UTF8) | ConvertFrom-Json
    $tmp = Join-Path $runDir $name
    New-Item -ItemType Directory -Force $tmp | Out-Null
    $base = $PortBase + $index * 10
    if ($base + 9 -gt 7799) { throw "Porte fuori dall'intervallo 7700-7799 (scenario $name, base $base)." }
    $timeout = if ($TimeoutSec -gt 0) { $TimeoutSec } elseif ($spec.timeoutSec) { [int]$spec.timeoutSec } else { 120 }
    $procTimeout = [Math]::Max(20, $timeout - 10)

    $expand = {
        param([string]$s)
        $s = $s.Replace("{tmp}", $tmp)
        $s = [regex]::Replace($s, '\{port:(\d+)\}', { param($m) [string]($base + [int]$m.Groups[1].Value) })
        $s = [regex]::Replace($s, '\{server:(\d+)\}', { param($m) "127.0.0.1:" + [string]($base + [int]$m.Groups[1].Value) })
        return $s
    }.GetNewClosure()

    $rows = New-Object System.Collections.ArrayList
    $procs = @()   # { Kind, Label, Proc, Log, Out, DataDir, Hold, Release }
    $notes = New-Object System.Collections.ArrayList
    $failed = $false
    $childPid = 0
    Write-Host ""
    Write-Host "=== Scenario $name (porte $base-$($base + 9), tempo massimo $timeout s)"
    Write-Host "    cartella: $tmp"

    try {
        # ---- Server ----
        $servers = As-Array $spec.servers
        for ($i = 0; $i -lt $servers.Count; $i++) {
            $s = $servers[$i]
            $port = $base + $i
            $dataDir = Join-Path $tmp "server$i"
            New-Item -ItemType Directory -Force $dataDir | Out-Null
            $log = Join-Path $tmp "server$i.log"
            $a = @("-server", "-area", $s.area, "-port", [string]$port, "-listen", "127.0.0.1", "-datadir", $dataDir,
                   "-batchmode", "-nographics", "-logFile", $log)
            $out = $null
            if ($s.autotest) {
                $out = Join-Path $tmp "server$i.result.json"
                $a += @("-autotest", $s.autotest, "-autotest-out", $out, "-autotest-timeout", [string]$procTimeout)
            }
            foreach ($x in (As-Array $s.args)) { $a += (& $expand ([string]$x)) }
            $p = Start-Game $a
            Write-Host "    server$i avviato (pid $($p.Id), porta $port, area $($s.area))"
            $procs += [pscustomobject]@{ Kind = "server"; Label = "server$i"; Proc = $p; Log = $log; Out = $out; DataDir = $dataDir; Hold = $false; Release = $null; Name = "server$i" }
        }
        foreach ($sp in ($procs | Where-Object { $_.Kind -eq "server" })) {
            $ready = Join-Path $sp.DataDir "server.ready"
            $proc = $sp.Proc
            $ok = Wait-Until { (Test-Path $ready) -or $proc.HasExited } 90
            if (-not (Test-Path $ready)) {
                $failed = $true
                [void]$notes.Add("$($sp.Label) non è diventato pronto")
                throw "server non pronto"
            }
            Write-Host "    $($sp.Label) pronto"
        }

        # ---- Client ----
        $clients = As-Array $spec.clients
        foreach ($c in $clients) {
            foreach ($dep in (As-Array $c.startAfterInGame)) {
                $depProc = $procs | Where-Object { $_.Kind -eq "client" -and $_.Name -eq $dep } | Select-Object -First 1
                if (-not $depProc) { throw "startAfterInGame: client '$dep' sconosciuto o non ancora avviato" }
                $ing = $depProc.Out + ".ingame"
                $dp = $depProc.Proc
                $ok = Wait-Until { (Test-Path $ing) -or $dp.HasExited } 90
                if (-not (Test-Path $ing)) { [void]$notes.Add("$dep non è arrivato in gioco"); $failed = $true }
            }
            if ($c.delaySec) { Start-Sleep -Seconds ([double]$c.delaySec) }
            $cname = [string]$c.name
            $safe = ($cname -replace '[^A-Za-z0-9_-]', '_')
            if ($safe -eq "") { $safe = "client" }
            $log = Join-Path $tmp "client_$safe.log"
            $out = Join-Path $tmp "client_$safe.result.json"
            $a = @("-name", $cname, "-profile", (Join-Path $tmp "profile_$safe"), "-logFile", $log,
                   "-autotest", $c.autotest, "-autotest-out", $out, "-autotest-timeout", [string]$procTimeout)
            if ($c.graphics) { $a += @("-screen-width", "960", "-screen-height", "540", "-screen-fullscreen", "0") }
            else { $a += @("-batchmode", "-nographics") }
            $serverIndex = if ($null -ne $c.server) { [int]$c.server } else { 0 }
            if ($serverIndex -ge 0) { $a += @("-connect", ("127.0.0.1:" + [string]($base + $serverIndex))) }
            $release = $null
            if ($c.holdUntilOthersDone) {
                $release = Join-Path $tmp "release_$safe"
                $a += @("-autotest-release", $release)
            }
            foreach ($x in (As-Array $c.args)) { $a += (& $expand ([string]$x)) }
            $p = Start-Game $a
            Write-Host "    client $cname avviato (pid $($p.Id), $(if ($c.graphics) { 'con grafica' } else { 'senza grafica' }), scenario $($c.autotest))"
            $procs += [pscustomobject]@{ Kind = "client"; Label = "client $cname"; Proc = $p; Log = $log; Out = $out; DataDir = $null; Hold = [bool]$c.holdUntilOthersDone; Release = $release; Name = $cname }
        }

        # ---- Attesa dei client ----
        $deadline = (Get-Date).AddSeconds($timeout)
        $released = $false
        while ($true) {
            $clientProcs = @($procs | Where-Object { $_.Kind -eq "client" })
            $others = @($clientProcs | Where-Object { -not $_.Hold })
            if (-not $released -and @($others | Where-Object { -not $_.Proc.HasExited }).Count -eq 0) {
                foreach ($h in ($clientProcs | Where-Object { $_.Hold })) { Set-Content -Path $h.Release -Value "release" }
                $released = $true
            }
            if (@($clientProcs | Where-Object { -not $_.Proc.HasExited }).Count -eq 0) { break }
            if ((Get-Date) -gt $deadline) {
                $failed = $true
                [void]$notes.Add("tempo massimo superato ($timeout s)")
                foreach ($cp in $clientProcs) {
                    if (-not $cp.Proc.HasExited) { [void]$notes.Add("$($cp.Label) ancora acceso: chiuso a forza"); Stop-Proc $cp.Proc }
                }
                break
            }
            Start-Sleep -Milliseconds 250
        }

        # ---- Server figlio (Gioca) ----
        if ($spec.checkChildExit) {
            $cc = $spec.checkChildExit
            $cp = $procs | Where-Object { $_.Kind -eq "client" -and $_.Name -eq $cc.client } | Select-Object -First 1
            if ($cp -and (Test-Path $cp.Out)) {
                $r = (Read-Shared $cp.Out) | ConvertFrom-Json
                $childPid = [int]$r.data.childPid
            }
            if ($childPid -le 0) {
                $failed = $true
                [void]$notes.Add("pid del server figlio non trovato nel risultato")
            } else {
                $within = if ($cc.withinSec) { [double]$cc.withinSec } else { 10 }
                $sw = [Diagnostics.Stopwatch]::StartNew()
                $gone = Wait-Until { -not (Get-Process -Id $childPid -ErrorAction SilentlyContinue) } $within
                if ($gone) {
                    [void]$notes.Add(("server figlio (pid {0}) chiuso in {1:n1} s" -f $childPid, $sw.Elapsed.TotalSeconds))
                } else {
                    $failed = $true
                    [void]$notes.Add("server figlio (pid $childPid) ancora acceso dopo $within s")
                    Stop-Pid $childPid
                }
            }
        }
    }
    catch {
        $failed = $true
        [void]$notes.Add("errore dello script: $($_.Exception.Message)")
    }
    finally {
        # ---- Chiusura dei server: file server.stop, poi Kill ----
        foreach ($sp in ($procs | Where-Object { $_.Kind -eq "server" })) {
            if (-not $sp.Proc.HasExited) {
                try { Set-Content -Path (Join-Path $sp.DataDir "server.stop") -Value "stop" } catch {}
            }
        }
        foreach ($sp in ($procs | Where-Object { $_.Kind -eq "server" })) {
            $proc = $sp.Proc
            $ok = Wait-Until { $proc.HasExited } 15
            if (-not $ok) {
                $failed = $true
                [void]$notes.Add("$($sp.Label) non si chiude con server.stop: chiuso a forza")
                Stop-Proc $proc
            }
        }
        foreach ($pp in $procs) { Stop-Proc $pp.Proc }
        if ($childPid -gt 0) { Stop-Pid $childPid }
    }

    # ---- Valutazione ----
    foreach ($pp in $procs) {
        $exit = if ($pp.Proc.HasExited) { $pp.Proc.ExitCode } else { "?" }
        $result = "-"
        $why = @()
        $passed = $true
        if ($pp.Out) {
            if (Test-Path $pp.Out) {
                try {
                    $r = (Read-Shared $pp.Out) | ConvertFrom-Json
                    $result = if ($r.passed) { "passato" } else { "fallito" }
                    if (-not $r.passed) {
                        $passed = $false
                        foreach ($ck in (As-Array $r.checks)) { if (-not $ck.ok) { $why += "controllo fallito: $($ck.name) ($($ck.detail))" } }
                        if ($r.aborted) { $why += "interrotto: $($r.aborted)" }
                    }
                } catch { $passed = $false; $result = "illeggibile" }
            } else { $passed = $false; $result = "mancante"; $why += "nessun risultato JSON" }
        }
        if ($exit -ne 0) { $passed = $false; $why += "codice di uscita $exit" }
        $ex = @(Find-Exceptions $pp.Log)
        if ($ex.Count -gt 0) { $passed = $false; $why += "eccezioni nel log: $($ex.Count) (prima: $($ex[0]))" }
        if (-not $passed) { $failed = $true }
        [void]$rows.Add([pscustomobject]@{ Scenario = $name; Processo = $pp.Label; Uscita = $exit; Risultato = $result; Esito = $(if ($passed) { "PASS" } else { "FAIL" }); Note = ($why -join "; ") })
    }
    foreach ($extra in (As-Array $spec.extraLogs)) {
        $path = & $expand ([string]$extra)
        $ex = @(Find-Exceptions $path)
        $exists = Test-Path $path
        $ok = $exists -and $ex.Count -eq 0
        if (-not $ok) { $failed = $true }
        $msg = if (-not $exists) { "log mancante" } elseif ($ex.Count -gt 0) { "eccezioni nel log: $($ex.Count) (prima: $($ex[0]))" } else { "" }
        [void]$rows.Add([pscustomobject]@{ Scenario = $name; Processo = "log " + [IO.Path]::GetFileName($path); Uscita = "-"; Risultato = "-"; Esito = $(if ($ok) { "PASS" } else { "FAIL" }); Note = $msg })
    }
    [void]$rows.Add([pscustomobject]@{ Scenario = $name; Processo = "(scenario)"; Uscita = "-"; Risultato = "-"; Esito = $(if ($failed) { "FAIL" } else { "PASS" }); Note = ($notes -join "; ") })
    return [pscustomobject]@{ Name = $name; Failed = $failed; Rows = $rows }
}

# ---------------- Programma ----------------

if (-not $SkipBuild) {
    Write-Host "Build del gioco (usa -SkipBuild per saltarla)..."
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $toolsDir "unity.ps1") -Action build
    if ($LASTEXITCODE -ne 0) { Write-Host "Build fallita."; exit 2 }
}
if (-not (Test-Path $Exe)) { Write-Host "Eseguibile non trovato: $Exe"; exit 2 }

if ($Scenario -eq "all") {
    $files = @(Get-ChildItem $scenarioDir -Filter *.json | Sort-Object Name | ForEach-Object { $_.FullName })
} else {
    $f = Join-Path $scenarioDir "$Scenario.json"
    if (-not (Test-Path $f)) { Write-Host "Scenario non trovato: $f"; exit 2 }
    $files = @($f)
}

$runDir = Join-Path ([IO.Path]::GetTempPath()) ("underworld-tests\" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force $runDir | Out-Null
$all = @()
$anyFail = $false
try {
    for ($k = 0; $k -lt $files.Count; $k++) {
        $res = Invoke-Scenario $files[$k] $k $runDir
        $all += $res.Rows
        if ($res.Failed) { $anyFail = $true }
        Write-Host ("    -> {0}: {1}" -f $res.Name, $(if ($res.Failed) { "FAIL" } else { "PASS" }))
    }
} finally {
    foreach ($p in $script:AllProcs) { Stop-Proc $p }
}

Write-Host ""
$all | Format-Table -AutoSize -Wrap Scenario, Processo, Uscita, Risultato, Esito, Note | Out-String -Width 220 | Write-Host
Write-Host "Log e risultati: $runDir"
if ($anyFail) { Write-Host "RISULTATO: FAIL"; exit 1 }
Write-Host "RISULTATO: PASS"
exit 0
