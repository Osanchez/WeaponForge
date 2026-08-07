# WEAPON SELF-HARM: does a weapon hurt the player holding it, with nothing else in the picture?
#
# WHY THIS LIVES HERE. A weapon that damages its own owner is a WeaponForge question, not a
# multiplayer one -- but it was found in a multiplayer session, where a co-op mod had also widened
# collision masks and flipped faction checks. Two codebases, one symptom, and no way to tell which
# was responsible.
#
# So this removes the multiplayer half entirely. The run is Standard mode with friendly fire OFF,
# which is the exact condition under which PunkMultiverse's PvP patches are inert -- they all gate
# on the same flag. Nothing widens a mask, nothing flips a faction, nothing opens a collision
# matrix. If a weapon still burns its own owner down here, the cause is in this repo (or in
# vanilla), and no amount of fixing the multiplayer mod will touch it.
#
# MEASURED BY HEALTH, not by log lines. Damage arrives through several different paths -- a
# hitscan cast, a physics impact, a burn tick, an electric arc -- and each is traced differently or
# not at all. Health is the one number every path has to move.
#
# SELF-DAMAGE IS NOT ALWAYS WRONG. A point-blank explosive should hurt you. Pass -SelfDamageOk for
# those; anything else that draws its owner's blood is reported as a defect.
#
# Requires: the game, BepInEx, WeaponForge, and PunkMultiverse (whose devcmd file is what makes
# any of this scriptable at all -- there is no other way to drive a headless PUNK).
#
# ASCII only. BOM-free configs.
param(
    [string[]]$Weapons,
    [string[]]$SelfDamageOk = @(),
    [int]$FireSeconds = 8,
    [string]$Install = "PUNK Playtest - OD Dev5",
    [string]$SteamRoot = "C:\Program Files (x86)\Steam\steamapps\common"
)
$ErrorActionPreference = "Stop"
$Dir  = Join-Path $SteamRoot $Install
$Plug = Join-Path $Dir "BepInEx\plugins\PunkMultiverse"
$Log  = Join-Path $Dir "BepInEx\LogOutput.log"
$Out  = Join-Path $Plug "devout.txt"

function CountIn($p,$pat){ if(-not(Test-Path $p)){return 0}; return @(Select-String -Path $p -Pattern $pat -AllMatches -EA SilentlyContinue).Count }
function Lines($p,$pat){ if(-not(Test-Path $p)){return @()}; return @(Select-String -Path $p -Pattern $pat -AllMatches -EA SilentlyContinue) }
function Cmd($txt){ Add-Content -Path (Join-Path $Plug "devcmd.txt") -Value $txt -Encoding Ascii }
function Line($label,$text){ Write-Host ("  {0,-24} {1}" -f $label, $text) }
function Fail($msg){ Write-Host "  FAIL: $msg" -ForegroundColor Red; $script:ok = $false }
function WaitFor($p,$pat,$to,$what){
    $d=(Get-Date).AddSeconds($to)
    while((Get-Date) -lt $d){ if((CountIn $p $pat) -ge 1){ return $true }; Start-Sleep 2 }
    Write-Host "  TIMEOUT $what"; return $false
}

$script:CfgBackups = @()
function SetCfg([string]$path, [hashtable]$kv, [string]$section = "Session") {
    $cfg = Get-Content -Raw $path
    foreach ($k in $kv.Keys) {
        $pat = "(?m)^{0}\s*=.*$" -f [regex]::Escape($k)
        $m = [regex]::Match($cfg, $pat)
        $script:CfgBackups += @{ Path=$path; Key=$k; Line=$(if($m.Success){$m.Value}else{$null}); Existed=$m.Success }
        $line = "{0} = {1}" -f $k, $kv[$k]
        if ($cfg -match $pat) { $cfg = $cfg -replace $pat, $line }
        else {
            $hdr = "(?m)^\[{0}\]" -f [regex]::Escape($section)
            if ($cfg -match $hdr) { $cfg = $cfg -replace $hdr, ("[{0}]`r`n{1}" -f $section, $line) }
            else { $cfg = $cfg.TrimEnd() + "`r`n`r`n[$section]`r`n$line`r`n" }
        }
    }
    [System.IO.File]::WriteAllText($path, $cfg)
}
function RestoreCfg() {
    foreach ($b in $script:CfgBackups) {
        if (-not (Test-Path $b.Path)) { continue }
        $cfg = Get-Content -Raw $b.Path
        $pat = "(?m)^{0}\s*=.*$" -f [regex]::Escape($b.Key)
        if ($b.Existed) { $cfg = [regex]::Replace($cfg, $pat, $b.Line) } else { $cfg = [regex]::Replace($cfg, $pat, "") }
        [System.IO.File]::WriteAllText($b.Path, $cfg)
    }
    if ($script:CfgBackups.Count -gt 0) { Write-Host "restored $($script:CfgBackups.Count) config key(s)"; $script:CfgBackups = @() }
}

function LocalHp() {
    $before = (CountIn $Out "hpsnap:")
    Cmd "hpsnap"
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 700
        if ((CountIn $Out "hpsnap:") -gt $before) { break }
    }
    $m = @(Lines $Out "hpsnap: P(\d) hp=([0-9.]+)/([0-9.]+) local")
    if ($m.Count -lt 1) { return $null }
    return [double]$m[-1].Matches[0].Groups[2].Value
}

if (Get-Process Punk -EA SilentlyContinue | Where-Object { (Split-Path $_.Path -Parent) -eq $Dir }) {
    "ABORT: $Install is already running."; exit 2
}

$script:ok = $true
$proc = $null
$results = @()
try {
    # Standard mode, friendly fire OFF, hosting alone. This is precisely the state in which
    # PunkMultiverse's PvP patches do nothing -- they share one gate. Anything that happens here
    # happens without them.
    SetCfg (Join-Path $Plug "config.cfg") @{
        "Transport"="Udp"; "UdpPort"="7801"; "CommandFile"="devcmd.txt"; "AutoStart"="Host";
        "AutoReady"="true"; "AutoLaunchRun"="true"; "LogLevel"="Verbose";
        "GameMode"="Standard"; "FriendlyFire"="false"; "ContentRoot"=""
    }
    Remove-Item -Force -EA SilentlyContinue (Join-Path $Plug "devcmd.txt"), $Log, $Out

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = Join-Path $Dir "Punk.exe"; $psi.Arguments = "-batchmode -nographics"
    $psi.WorkingDirectory = $Dir; $psi.UseShellExecute = $false
    $psi.EnvironmentVariables["PUNKMV_BR_CHOOSE_SPAWN"] = "0"
    foreach($k in @($psi.EnvironmentVariables.Keys | Where-Object {$_ -like "DOORSTOP*"})){ $psi.EnvironmentVariables.Remove($k) }
    $proc = [System.Diagnostics.Process]::Start($psi)

    if (-not (WaitFor $Log "GO LIVE" 300 "run start")) { throw "the run never started" }
    Start-Sleep 12
    Write-Host "RUN LIVE (Standard, friendly fire off - PvP patches inert)"

    # Prove the gate really is off, rather than assuming it. If either line appears, this is NOT a
    # clean control and its result cannot attribute anything.
    $widened = (CountIn $Log "\[BR\] hitscan weapon widened") + (CountIn $Log "\[BR\] physics projectile layer")
    if ($widened -gt 0) {
        Fail "PvP patches were ACTIVE in this run - it is not a control and proves nothing about attribution"
    } else { Line "pvp patches" "inert (no mask widening, no matrix change)" }

    if (-not $Weapons -or $Weapons.Count -eq 0) {
        Cmd "weaponlist forge"
        Start-Sleep 6
        $Weapons = @(Lines $Out "weaponlist:   \* (\S+) \|" | ForEach-Object { $_.Matches[0].Groups[1].Value } | Select-Object -Unique)
        if ($Weapons.Count -eq 0) { throw "no custom weapons found - pass -Weapons explicitly" }
    }
    Line "weapons" ($Weapons -join ", ")
    Write-Host ""

    foreach ($w in $Weapons) {
        Write-Host "--- $w ---"
        Cmd "hpfull"
        Start-Sleep 3
        Cmd "autofly 0"
        Start-Sleep 1
        # Clear a pocket so nothing in the world can be blamed for the damage: no terrain to
        # splash off, no enemies to shoot back. Whatever hurts the ship here came from its weapon.
        Cmd "pvpstage 40"
        Start-Sleep 3
        Cmd "hpfull"
        Start-Sleep 3

        Cmd "equip $w"
        Start-Sleep 6
        if ((CountIn $Log ("equip: .*" + [regex]::Escape($w))) -lt 1) {
            Fail "$w : could not be equipped"
            $results += [pscustomobject]@{ Weapon=$w; Self="-"; Verdict="NO EQUIP" }
            continue
        }

        $before = LocalHp
        if ($null -eq $before) { Fail "$w : no health reading before firing"; continue }

        # Fire into empty space. No target, nothing to hit but itself.
        Cmd ("fire {0}" -f $FireSeconds)
        Start-Sleep ($FireSeconds + 8)

        $after = LocalHp
        if ($null -eq $after) {
            # A ship that has vanished died mid-test, which is the loudest possible self-harm.
            $died = (CountIn $Log "local ship died")
            if ($died -gt 0) {
                $killer = @(Lines $Log "local ship died . broadcast \(killed by ([^)]+)\)")
                $by = if ($killer.Count -gt 0) { $killer[-1].Matches[0].Groups[1].Value } else { "unknown" }
                Fail "$w : KILLED ITS OWN OWNER (killed by $by) with no other player in the game"
                $results += [pscustomobject]@{ Weapon=$w; Self="FATAL"; Verdict="killed by $by" }
            } else { Fail "$w : no health reading after firing" ; $results += [pscustomobject]@{ Weapon=$w; Self="-"; Verdict="NO HP" } }
            continue
        }

        $self = [math]::Round($before - $after, 3)
        Line "own hp" ("{0} -> {1}  (self {2})" -f $before, $after, $self)
        if ($self -gt 0) {
            if ($SelfDamageOk -contains $w) {
                $results += [pscustomobject]@{ Weapon=$w; Self=$self; Verdict="self-damage (expected)" }
            } else {
                Fail "$w : damaged its own owner ($self) with no other player in the game"
                $results += [pscustomobject]@{ Weapon=$w; Self=$self; Verdict="SELF-HARM" }
            }
        } else {
            $results += [pscustomobject]@{ Weapon=$w; Self=0; Verdict="safe" }
        }
        Write-Host ""
    }
}
finally {
    if ($proc) { try { $proc.Kill() } catch {} }
    Start-Sleep 3
    RestoreCfg
}

Write-Host "====================================================="
if ($results.Count -gt 0) { $results | Format-Table -AutoSize | Out-String | Write-Host }
Write-Host $(if ($script:ok) { "WEAPON SELF-HARM: PASS" } else { "WEAPON SELF-HARM: PROBLEMS ABOVE" })
if (-not $script:ok) { exit 1 }
