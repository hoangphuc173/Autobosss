# AutoBossGrabber Deploy Script
# Usage: .\deploy.ps1 -GamePath "C:\Path\To\Your\Game"

param(
    [Parameter(Mandatory=$false)]
    [string]$GamePath = ""
)

Write-Host "`n+--------------------------------------------+" -ForegroundColor Cyan
Write-Host "¦   AutoBossGrabber Phase 3 Deployment      ¦" -ForegroundColor Cyan
Write-Host "+--------------------------------------------+" -ForegroundColor Cyan

$sourcePath = "C:\Users\phuct\Downloads\tool\AutoBossGrabber\AutoBossGrabber\source\bin\Debug\net6.0"

# Find game if not provided
if ($GamePath -eq "") {
    Write-Host "`n?? Searching for games with BepInEx..." -ForegroundColor Yellow
    
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
                    $foundGames += [PSCustomObject]@{
                        Name = $_.Name
                        Path = $_.FullName
                        PluginPath = $pluginPath
                    }
                }
            }
        }
    }
    
    if ($foundGames.Count -eq 0) {
        Write-Host "`n? No games found. Please specify -GamePath" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "`nFound $($foundGames.Count) game(s):" -ForegroundColor Green
    for ($i = 0; $i -lt $foundGames.Count; $i++) {
        Write-Host "  [$($i+1)] $($foundGames[$i].Name)" -ForegroundColor Cyan
    }
    
    $choice = Read-Host "`nSelect game number (1-$($foundGames.Count))"
    $selectedGame = $foundGames[$choice - 1]
    $GamePath = $selectedGame.Path
}

$targetPath = Join-Path $GamePath "BepInEx\plugins"

# Verify paths
if (-not (Test-Path $sourcePath)) {
    Write-Host "`n? Source not found: $sourcePath" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $targetPath)) {
    Write-Host "`n? Target not found: $targetPath" -ForegroundColor Red
    Write-Host "   Make sure BepInEx is installed!" -ForegroundColor Yellow
    exit 1
}

Write-Host "`n?? Deploying to:" -ForegroundColor Cyan
Write-Host "   $targetPath" -ForegroundColor White

# Backup existing
Write-Host "`n?? Backing up existing DLLs..." -ForegroundColor Yellow
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
Get-ChildItem $targetPath -Filter "AutoBoss*.dll" -ErrorAction SilentlyContinue | ForEach-Object {
    $backup = "$($_.FullName).backup_$timestamp"
    Copy-Item $_.FullName $backup -ErrorAction SilentlyContinue
    Write-Host "   ? Backed up: $($_.Name)" -ForegroundColor Green
}

# Copy new DLLs
Write-Host "`n?? Copying Phase 3 DLLs..." -ForegroundColor Yellow
$files = @("AutoBossGrabber.dll", "AutoBossShared.dll")

foreach ($file in $files) {
    $source = Join-Path $sourcePath $file
    if (Test-Path $source) {
        Copy-Item $source $targetPath -Force
        $copied = Get-Item (Join-Path $targetPath $file)
        Write-Host "   ? $file ($([math]::Round($copied.Length/1KB, 2)) KB)" -ForegroundColor Green
    } else {
        Write-Host "   ? Not found: $file" -ForegroundColor Red
    }
}

# Clean old cache
Write-Host "`n???  Cleaning old cache..." -ForegroundColor Yellow
$cacheFile = Join-Path $targetPath "bfs_map_cache.json"
if (Test-Path $cacheFile) {
    Remove-Item $cacheFile -Force
    Write-Host "   ? Removed old cache" -ForegroundColor Green
} else {
    Write-Host "   • No cache to clean" -ForegroundColor Gray
}

# Verify
Write-Host "`n? Verification:" -ForegroundColor Cyan
Get-ChildItem $targetPath -Filter "AutoBoss*.dll" | ForEach-Object {
    Write-Host "   ? $($_.Name)" -ForegroundColor Green
    Write-Host "      Size: $([math]::Round($_.Length/1KB, 2)) KB" -ForegroundColor White
    Write-Host "      Date: $($_.LastWriteTime)" -ForegroundColor White
}

Write-Host "`n+--------------------------------------------+" -ForegroundColor Cyan
Write-Host "¦         DEPLOYMENT COMPLETE! ?            ¦" -ForegroundColor Green
Write-Host "+--------------------------------------------+" -ForegroundColor Cyan

Write-Host "`n?? Next steps:" -ForegroundColor Yellow
Write-Host "   1. Launch the game" -ForegroundColor White
Write-Host "   2. Watch BepInEx console" -ForegroundColor White
Write-Host "   3. Look for: [SocketClient] BFS Pathfinder initialized" -ForegroundColor White
Write-Host "   4. Connect Manager and test TELEPORT_TO_MAP" -ForegroundColor White
Write-Host ""
