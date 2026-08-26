# 🧪 STEP 1: TESTING CHECKLIST

## ✅ Pre-Test Verification

### Build Outputs Ready:
- ✅ **AutoBossManager.exe**: AutoBossManager\bin\Debug\net6.0-windows\AutoBossManager.exe
- ✅ **AutoBossGrabber.dll**: AutoBossGrabber\source\bin\Debug\net6.0\AutoBossGrabber.dll
- ✅ **AutoBossShared.dll**: AutoBossShared\bin\Debug\net6.0\AutoBossShared.dll

### Configuration Verified:
- ✅ Port: 28081 (configurable via IpcConfig.ServerPort)
- ✅ Host: 127.0.0.1 (localhost)
- ✅ Sample data: Disabled in RELEASE, enabled in DEBUG

---

## 🎮 Test Execution Steps

### Phase A: Manager Standalone Test (No Game Required)
**Duration:** 2 minutes

**Steps:**
1. Navigate to: `AutoBossManager\bin\Debug\net6.0-windows\`
2. Double-click `AutoBossManager.exe`
3. Verify UI shows:
   - ✅ 3 sample test bots (DEBUG build only)
   - ✅ Status bar: "Socket server started on port 28081"
   - ✅ Header: Connected Bots: 3, Total Kills: 20
   - ✅ DataGrid with 14 columns

**Expected Result:**
- Window opens successfully
- No crash or errors
- Sample data visible (if DEBUG build)

**If fails:**
- Check console for errors
- Check port 28081 not in use: `netstat -ano | findstr 28081`

---

### Phase B: Game Plugin Installation
**Duration:** 3 minutes

**Game Installation Path Required:**
You need to tell me where your game is installed, then:

**Steps:**
1. Copy files to game BepInEx plugins:
   ```
   <GameFolder>\BepInEx\plugins\AutoBossGrabber.dll
   <GameFolder>\BepInEx\plugins\AutoBossShared.dll
   ```

2. Copy dependencies (if not already there):
   ```
   <GameFolder>\BepInEx\plugins\Newtonsoft.Json.dll
   ```

**Automated Copy Script (if you provide game path):**
I can create a script to auto-copy files.

---

### Phase C: End-to-End IPC Test
**Duration:** 10-15 minutes

**Prerequisites:**
- ✅ Manager running
- ✅ Plugin installed
- ✅ Game ready to launch

**Test Cases:**

#### ✅ Test C1: Connection Establishment (CRITICAL)
1. Launch game with BepInEx
2. Check BepInEx console for:
   - `[SocketClient] Connecting to Manager at 127.0.0.1:28081...`
   - `[SocketClient] Connected to Manager successfully`
3. Check Manager:
   - New bot appears in dashboard (4th bot)
   - Status bar: "Client connected: <Guid>"

**Pass Criteria:** Connection established within 1 second

---

#### ✅ Test C2: Heartbeat (CRITICAL)
1. Client connected
2. Wait 10 seconds
3. Check: Client still connected (not timed out)
4. Check Manager console for ACK messages

**Pass Criteria:** No timeout, heartbeat working

---

#### ✅ Test C3: Remote Command - Single Bot (HIGH PRIORITY)
1. Click "▶ Start" button for connected bot
2. Check game: Bot starts farming (Config.Enabled = true)
3. Click "⏸ Stop" button
4. Check game: Bot stops (Config.Enabled = false)

**Pass Criteria:** Commands execute within 50ms

---

#### ✅ Test C4: Status Updates (HIGH PRIORITY)
1. Bot running and farming
2. Watch Manager dashboard update:
   - State changes (Idle → DetectBoss → MoveToBoss)
   - HP/MP percentages update
   - Map/Zone update
3. Check update frequency: Every 5 seconds or on state change

**Pass Criteria:** Dashboard updates within 1 second

---

#### ✅ Test C5: Broadcast Command (MEDIUM PRIORITY)
1. Multiple bots connected (or just 1)
2. Click "▶ Start All"
3. All bots start farming

**Pass Criteria:** All bots receive command

---

#### ✅ Test C6: Heartbeat Timeout (MEDIUM PRIORITY)
1. Bot connected
2. Kill game process (Alt+F4)
3. Wait 12 seconds
4. Check Manager: Bot removed from dashboard

**Pass Criteria:** Timeout detection works, bot removed

---

#### ✅ Test C7: Reconnection (LOW PRIORITY)
1. Kill game
2. Wait for timeout (12s)
3. Relaunch game
4. Check: New connection established

**Pass Criteria:** Reconnection successful

---

## 📊 Test Results Template

Copy this and fill in results:

```
=== TESTING RESULTS ===
Date: ________
Tester: ________

Phase A: Manager Standalone
[PASS/FAIL] Manager launches: ____
[PASS/FAIL] UI shows correctly: ____
[PASS/FAIL] Server starts on 28081: ____

Phase B: Plugin Installation
[PASS/FAIL] Files copied: ____
[PASS/FAIL] BepInEx loads plugin: ____

Phase C: End-to-End Tests
[PASS/FAIL] C1 - Connection: ____
[PASS/FAIL] C2 - Heartbeat: ____
[PASS/FAIL] C3 - Commands: ____
[PASS/FAIL] C4 - Status Updates: ____
[PASS/FAIL] C5 - Broadcast: ____
[PASS/FAIL] C6 - Timeout: ____
[PASS/FAIL] C7 - Reconnection: ____

Critical Issues Found:
1. ________
2. ________

Overall Result: [PASS/FAIL]
```

---

## 🐛 Troubleshooting

### Issue: Manager Won't Start
**Symptoms:** EXE crashes or error message
**Solutions:**
- Install .NET 6 Desktop Runtime
- Run as Administrator
- Check Windows Defender not blocking

### Issue: Plugin Not Loading
**Symptoms:** No BepInEx log for AutoBossGrabber
**Solutions:**
- Check DLL in correct folder (BepInEx\plugins\)
- Check BepInEx 6 installed
- Check game uses Il2Cpp (not Mono)

### Issue: Connection Failed
**Symptoms:** "Connection failed: No connection could be made"
**Solutions:**
- Check Manager running first
- Check port 28081 not blocked
- Check Windows Firewall allows localhost

### Issue: Commands Not Executing
**Symptoms:** Button click but nothing happens in game
**Solutions:**
- Check BepInEx log for "Received: COMMAND"
- Check Plugin.Instance.Runner exists
- Check Update() being called

---

## ⏱️ Estimated Time

- **Phase A:** 2 minutes
- **Phase B:** 3 minutes  
- **Phase C:** 10-15 minutes
- **Total:** ~20 minutes for complete testing

---

## 📝 Next Step After Testing

After Phase C complete:
- If ALL PASS → Proceed to Step 2 (Deployment)
- If ANY FAIL → Debug issues, fix, re-test

---

**Ready to Start?**
Provide your game installation path and I will create auto-copy script!

Game path example: `C:\Program Files\YourGame\`
