# ============================================================
# AutoBossGrabber Deploy Script
# Build plugin (tu khi thieu) va copy DLL vao thu muc game BepInEx.
#
# Usage:
#   .\deploy.ps1                              # tu tim game trong Steam library
#   .\deploy.ps1 -GamePath "D:\Games\VuTu"    # chi dinh ro thu muc game
#   .\deploy.ps1 -Configuration Debug         # build Debug thay vi Release
# ============================================================

param(
    [Parameter(Mandatory = $false)]
    [string]$GamePath = "",

    [Parameter(Mandatory = $false)]
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

# Thu muc build output cua plugin (tinh tu vi tri script nay)
$repoRoot     = Split-Path $PSScriptRoot -Parent
$projectDir   = Join-Path $repoRoot "src\AutoBossGrabber"
$sourcePath   = Join-Path $projectDir "bin\$Configuration\net6.0"

Write-Host ""
Write-Host "+--------------------------------------------+" -ForegroundColor Cyan
Write-Host "|   AutoBossGrabber Deployment               |" -ForegroundColor Cyan
Write-Host "+--------------------------------------------+" -ForegroundColor Cyan

# --- 1. Build neu chua co DLL ---
$dllPath = Join-Path $sourcePath "AutoBossGrabber.dll"
if (-not (Test-Path $dllPath)) {
    Write-Host "`n[build] AutoBossGrabber.dll not found - building ($Configuration)..." -ForegroundColor Yellow
    dotnet build (Join-Path $projectDir "AutoBossGrabber.csproj") -c $Configuration
    if ($LASTEXITCODE -ne 0) { Write-Host "[build] FAILED" -ForegroundColor Red; exit 1 }
}

# --- 2. Tim game neu khong truyen -GamePath ---
if ($GamePath -eq "") {
    # Uu tien thu muc runtime trong repo (truong hop test local khong qua Steam)
    $localRuntime = Join-Path $repoRoot "runtime"
    if (Test-Path (Join-Path $localRuntime "BepInEx\plugins")) {
        $GamePath = $localRuntime
        Write-Host "`n[*] Auto-detected local runtime: $GamePath" -ForegroundColor Green
    } else {
        Write-Host "`n[*] Searching for games with BepInEx..." -ForegroundColor Yellow

        $searchPaths = @(
            "C:\Program Files (x86)\Steam\steamapps\common",
            "D:\Steam\steamapps\common",
            "D:\SteamLibrary\steamapps\common",
            "E:\Steam\steamapps\common"
        )

    $foundGames = @()
    foreach ($search in $searchPaths) {
        if (Test-Path $search) {
            Get-ChildItem $search -Directory | ForEach-Object {
                $pluginPath = Join-Path $_.FullName "BepInEx\plugins"
                if (Test-Path $pluginPath) {
                    $foundGames += [PSCustomObject]@{ Name = $_.Name; Path = $_.FullName }
                }
            }
        }
    }

        if ($foundGames.Count -eq 0) {
            Write-Host "`n[x] No games found. Please specify -GamePath" -ForegroundColor Red
            Write-Host "    Example: .\deploy.ps1 -GamePath `"$localRuntime`"" -ForegroundColor Yellow
            exit 1
        }

        Write-Host "`nFound $($foundGames.Count) game(s):" -ForegroundColor Green
        for ($i = 0; $i -lt $foundGames.Count; $i++) {
            Write-Host "  [$($i+1)] $($foundGames[$i].Name)" -ForegroundColor Cyan
        }

        $choice = Read-Host "`nSelect game number (1-$($foundGames.Count))"
        $GamePath = $foundGames[[int]$choice - 1].Path
    }
}

$targetPath = Join-Path $GamePath "BepInEx\plugins"

if (-not (Test-Path $targetPath)) {
    Write-Host "`n[x] Target not found: $targetPath" -ForegroundColor Red
    Write-Host "    Make sure BepInEx is installed!" -ForegroundColor Yellow
    exit 1
}

Write-Host "`n[*] Deploying to: $targetPath" -ForegroundColor Cyan

# --- 3. Backup DLL cu ---
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
Get-ChildItem $targetPath -Filter "AutoBoss*.dll" -ErrorAction SilentlyContinue | ForEach-Object {
    Copy-Item $_.FullName "$($_.FullName).backup_$timestamp" -ErrorAction SilentlyContinue
    Write-Host "[+] Backed up: $($_.Name)" -ForegroundColor Green
}

# --- 4. Copy DLL moi ---
Write-Host "`n[*] Copying plugin DLLs..." -ForegroundColor Yellow
# Newtonsoft.Json is required at runtime for IPC serialization (IL2CPP interop version is not sufficient)
$njSrc = Join-Path $sourcePath "Newtonsoft.Json.dll"
if (Test-Path $njSrc) {
    Copy-Item $njSrc $targetPath -Force
    Write-Host "[+] Newtonsoft.Json.dll ($([math]::Round((Get-Item (Join-Path $targetPath "Newtonsoft.Json.dll")).Length/1KB, 2)) KB)" -ForegroundColor Green
}
foreach ($file in @("AutoBossGrabber.dll", "AutoBossShared.dll")) {
    $src = Join-Path $sourcePath $file
    if (Test-Path $src) {
        Copy-Item $src $targetPath -Force
        $copied = Get-Item (Join-Path $targetPath $file)
        Write-Host "[+] $file ($([math]::Round($copied.Length/1KB, 2)) KB)" -ForegroundColor Green
    } else {
        Write-Host "[x] Not found: $src" -ForegroundColor Red
        exit 1
    }
}

# --- 5. Don cache BFS cu (graph se duoc rebuild) ---
$cacheFile = Join-Path $targetPath "bfs_map_cache.json"
if (Test-Path $cacheFile) {
    Remove-Item $cacheFile -Force
    Write-Host "[+] Removed old BFS cache" -ForegroundColor Green
}

# --- 6. Xac nhan ---
Write-Host "`n[*] Verification:" -ForegroundColor Cyan
Get-ChildItem $targetPath -Filter "AutoBoss*.dll" | Where-Object { $_.Name -notmatch 'backup_' } | ForEach-Object {
    Write-Host "    $($_.Name)  $([math]::Round($_.Length/1KB, 2)) KB  $($_.LastWriteTime)" -ForegroundColor Green
}

Write-Host ""
Write-Host "+--------------------------------------------+" -ForegroundColor Green
Write-Host "|         DEPLOYMENT COMPLETE!               |" -ForegroundColor Green
Write-Host "+--------------------------------------------+" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Launch the game"
Write-Host "  2. Watch BepInEx console for: [SocketClient] Connected to Manager"
Write-Host "  3. Open AutoBossManager and control bots"
Write-Host ""
