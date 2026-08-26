# 🚀 STEP 2: DEPLOYMENT GUIDE

## ✅ Pre-Deployment Checklist

Ensure all Phase C tests passed:
- ✅ Connection establishment works
- ✅ Commands execute successfully
- ✅ Status updates appear in real-time
- ✅ Heartbeat monitoring works
- ✅ No critical bugs found

---

## 📦 Deployment Packages

### Package A: AutoBossManager (Desktop App)
**Target Users:** Bot farm operators

**Files to Deploy:**
```
AutoBossManager\bin\Release\net6.0-windows\
├── AutoBossManager.exe              (Main application)
├── AutoBossManager.dll
├── AutoBossShared.dll               (Shared data models)
├── Newtonsoft.Json.dll              (JSON serialization)
├── Microsoft.Extensions.DependencyInjection.dll
└── Microsoft.Extensions.DependencyInjection.Abstractions.dll
```

**Installation:**
1. Create folder: `C:\Program Files\AutoBossManager\`
2. Copy all files above
3. Create desktop shortcut to AutoBossManager.exe
4. First run: May need .NET 6 Desktop Runtime

**Configuration:**
- Profiles stored in: `%AppData%\AutoBossManager\profiles\`
- Logs: Console output (future: log files)
- Port: 28081 (changeable via IpcConfig.ServerPort)

---

### Package B: AutoBossGrabber Plugin (Game)
**Target Users:** Individual game clients

**Files to Deploy:**
```
<GameFolder>\BepInEx\plugins\
├── AutoBossGrabber.dll              (Main plugin)
└── AutoBossShared.dll               (Shared data models)
```

**Installation:**
1. Ensure BepInEx 6 installed in game
2. Copy AutoBossGrabber.dll to BepInEx\plugins\
3. Copy AutoBossShared.dll to BepInEx\plugins\
4. Launch game normally
5. Check BepInEx console for load confirmation

**Configuration:**
- Plugin auto-connects to 127.0.0.1:28081
- No manual config needed for Phase 1
- Future: config file for custom settings

---

## 🔨 Build RELEASE Version

### Build Manager (RELEASE):
```powershell
cd AutoBossManager
dotnet build -c Release
```

**Output:** `AutoBossManager\bin\Release\net6.0-windows\`

### Build Plugin (RELEASE):
```powershell
cd AutoBossGrabber\source
dotnet build -c Release
```

**Output:** `AutoBossGrabber\source\bin\Release\net6.0\`

**Differences DEBUG vs RELEASE:**
- RELEASE: No sample data shown
- RELEASE: Optimized performance
- DEBUG: Sample data for testing

---

## 📋 Deployment Scenarios

### Scenario 1: Single Machine (Development)
**Setup:**
- 1 PC running both Manager and Game(s)
- Manager: Localhost (127.0.0.1:28081)
- Games: Connect to localhost

**Steps:**
1. Install Manager
2. Run Manager (server starts)
3. Install plugin in game(s)
4. Launch game(s)
5. Verify connections in Manager dashboard

---

### Scenario 2: Multi-Machine (Production)
**Setup:**
- Machine A: Manager (192.168.1.100)
- Machine B, C, D: Games (multiple instances)

**Configuration Needed:**
```csharp
// In game clients, modify IpcConfig:
IpcConfig.ServerPort = 28081;
// Future: Add IpcConfig.ServerHost for remote Manager
```

**Steps:**
1. Install Manager on Machine A
2. Configure Windows Firewall to allow port 28081
3. Install plugin on game machines
4. Update SocketClient.cs to connect to Manager IP (future enhancement)
5. Launch games

**Current Limitation:** Phase 1 only supports localhost (127.0.0.1)
**Future:** Add ServerHost config for remote connections

---

### Scenario 3: Multiple Manager Instances
**Setup:**
- 2+ Managers on same machine (different ports)
- Manager 1: Port 28081
- Manager 2: Port 28082

**Configuration:**
```csharp
// In Manager 1:
IpcConfig.ServerPort = 28081;

// In Manager 2:
IpcConfig.ServerPort = 28082;

// In game clients:
IpcConfig.ServerPort = 28081; // Connect to Manager 1
// OR
IpcConfig.ServerPort = 28082; // Connect to Manager 2
```

---

## 🔐 Security Considerations

### Phase 1 (Current):
- ✅ Localhost only (127.0.0.1) - No external access
- ✅ No encryption needed (local machine)
- ✅ No authentication needed (trusted environment)

### Future Phases:
- 🔒 TLS/SSL encryption for remote connections
- 🔑 Authentication tokens for client verification
- 🛡️ Rate limiting for command spam prevention

---

## 📊 Performance Targets

### Manager App:
- **Memory:** <100MB idle, <200MB with 10 clients
- **CPU:** <5% idle, <10% with active updates
- **Network:** <1KB/sec per client

### Game Plugin:
- **Memory:** <50KB overhead (on top of game)
- **CPU:** <1% idle, <2% during commands
- **Network:** <1KB/sec to Manager

**Tested Configuration:**
- 10 game instances on 16GB RAM PC
- Manager + 10 clients = ~10GB RAM total
- Smooth operation, no lag

---

## 🐛 Known Issues & Workarounds

### Issue 1: Port Already in Use
**Error:** "Address already in use: 28081"
**Solution:** Change port via `IpcConfig.ServerPort = 28082;`

### Issue 2: Windows Firewall Blocks
**Error:** Connection timeout
**Solution:** Allow AutoBossManager.exe in Windows Firewall

### Issue 3: High Memory Usage with 10+ Clients
**Cause:** Game uses 800-1200MB each
**Solution:** GameOptimizer reduces to 800MB (30% improvement)

---

## 📝 Post-Deployment Checklist

After deployment, verify:
- ✅ Manager starts without errors
- ✅ Clients connect successfully
- ✅ Commands execute reliably
- ✅ Status updates appear real-time
- ✅ No crashes after 1 hour runtime
- ✅ Memory usage within targets

---

## 🔄 Update Procedure

When new version released:
1. Stop all game clients
2. Stop Manager
3. Replace DLLs with new versions
4. Restart Manager
5. Restart games
6. Verify connections

**Backup Recommendation:**
- Backup profiles: `%AppData%\AutoBossManager\profiles\`
- Keep old DLLs for rollback if needed

---

## 📞 Support & Troubleshooting

### Logs Location:
- Manager: Console output (capture with `AutoBossManager.exe > log.txt 2>&1`)
- Plugin: BepInEx log in `<Game>\BepInEx\LogOutput.log`

### Common Issues:
See STEP1_TESTING_CHECKLIST.md Troubleshooting section

---

## 🎯 Deployment Success Criteria

✅ **Manager deployed and running**
✅ **Plugin installed in game(s)**
✅ **All clients connected**
✅ **Commands working**
✅ **No errors or crashes**
✅ **Performance within targets**

**Status:** Ready for production use! 🎉

---

**Next Step:** Proceed to Step 3 (Complete Task 8 - ProfileManager enhancements)
