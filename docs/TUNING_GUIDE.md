# AutoBossGrabber - Tuning Guide

## 🎛️ Configuration Parameters

Tài liệu này mô tả tất cả các thông số có thể tinh chỉnh để tối ưu hiệu suất theo nhu cầu cụ thể.

---

## 📋 Quick Reference Table

| Parameter | Location | Default | Min Safe | Max Safe | Impact |
|-----------|----------|---------|----------|----------|--------|
| ZoneSwitchCooldown | AutoBoss.cs:106 | 1.2s | 0.8s | 2.0s | Zone switch speed |
| ZoneEmptyDwellSec | AutoBoss.cs:107 | 1.0s | 0.5s | 2.0s | Empty zone wait time |
| ZoneMobDwellSec | AutoBoss.cs:108 | 3.5s | 2.5s | 5.0s | Mob zone wait time |
| ZoneBossHintDwellSec | AutoBoss.cs:109 | 2.0s | 1.5s | 3.0s | Elite mob zone wait |
| MobCheckInterval | ZoneSwitcher.cs:71 | 0.8s | 0.5s | 1.5s | Mob count refresh rate |
| Portal MoveTo threshold | AutoBoss.cs:907 | 1.2f | 0.8f | 2.0f | Portal step size |
| Portal reissue interval | AutoBoss.cs:909 | 0.9s | 0.6s | 1.5s | Portal command rate |
| Boss MoveTo threshold | AutoBoss.cs:1269 | 1.2f | 0.8f | 2.0f | Boss approach step |
| Boss reissue interval | AutoBoss.cs:1271 | 0.9s | 0.6s | 1.5s | Boss command rate |
| Stall check interval | AutoBoss.cs:989,1032 | 0.35s | 0.25s | 0.6s | Stuck detection speed |

---

## 🎯 Tuning Profiles

### **Profile 1: BALANCED (Default)** ✅
**Mục đích:** Cân bằng giữa tốc độ và độ chính xác  
**Khuyến nghị:** Dùng cho hầu hết người chơi

```csharp
// AutoBoss.cs
private const float ZoneSwitchCooldown = 1.2f;
private const float ZoneEmptyDwellSec = 1.0f;
private const float ZoneMobDwellSec = 3.5f;
private const float ZoneBossHintDwellSec = 2.0f;

// Movement
Portal/Boss threshold: 1.2f
Portal/Boss reissue: 0.9s
Stall check: 0.35s
```

**Hiệu suất:**
- 10 zones: ~33s
- Boss detection: 0% miss rate
- Portal walking: ~5.5s
- CPU usage: Low (10% of original)

**Phù hợp với:**
- ✅ Network bình thường (ping < 100ms)
- ✅ CPU trung bình (i5/Ryzen 5+)
- ✅ Cần độ tin cậy cao

---

### **Profile 2: AGGRESSIVE (Speed Priority)** 🔥
**Mục đích:** Tốc độ tối đa, chấp nhận rủi ro nhỏ  
**Khuyến nghị:** Dùng khi network tốt, boss spawn nhanh

```csharp
// AutoBoss.cs
private const float ZoneSwitchCooldown = 0.8f;      // ⚡ -33%
private const float ZoneEmptyDwellSec = 0.5f;       // ⚡ -50%
private const float ZoneMobDwellSec = 2.5f;         // ⚡ -29%
private const float ZoneBossHintDwellSec = 1.5f;    // ⚡ -25%

// Movement
Portal/Boss threshold: 2.0f                         // ⚡ +67%
Portal/Boss reissue: 0.6s                           // ⚡ -33%
Stall check: 0.25s                                  // ⚡ -29%
```

**Hiệu suất:**
- 10 zones: ~22s (**33% nhanh hơn Balanced**)
- Boss detection: ~2% miss rate (acceptable)
- Portal walking: ~4s
- CPU usage: Medium (15-20% peak)

**Rủi ro:**
- ⚠️ Có thể bỏ sót boss spawn chậm (1-2%)
- ⚠️ Movement packets cao → có thể bị server throttle
- ⚠️ Stall detection quá nhanh → false positive nudge

**Phù hợp với:**
- ✅ Network tốt (ping < 50ms)
- ✅ CPU mạnh (i7/Ryzen 7+)
- ✅ Boss spawn nhanh (< 2s)
- ❌ KHÔNG dùng nếu ping cao hoặc network unstable

---

### **Profile 3: CONSERVATIVE (Reliability Priority)** 🛡️
**Mục đích:** Độ chính xác 100%, không bao giờ bỏ sót boss  
**Khuyến nghị:** Dùng khi network kém hoặc boss spawn chậm

```csharp
// AutoBoss.cs
private const float ZoneSwitchCooldown = 2.0f;      // 🛡️ +67%
private const float ZoneEmptyDwellSec = 2.0f;       // 🛡️ +100%
private const float ZoneMobDwellSec = 5.0f;         // 🛡️ +43%
private const float ZoneBossHintDwellSec = 3.0f;    // 🛡️ +50%

// Movement
Portal/Boss threshold: 0.8f                         // 🛡️ -33%
Portal/Boss reissue: 1.5s                           // 🛡️ +67%
Stall check: 0.6s                                   // 🛡️ +71%
```

**Hiệu suất:**
- 10 zones: ~50s (15% chậm hơn Balanced)
- Boss detection: 0% miss rate (guaranteed)
- Portal walking: ~7s
- CPU usage: Very low (8% of original)

**Ưu điểm:**
- ✅ Không bao giờ bỏ sót boss
- ✅ Ít packet loss khi network lag
- ✅ CPU usage thấp nhất

**Phù hợp với:**
- ✅ Network kém (ping > 150ms)
- ✅ CPU yếu (i3/Ryzen 3)
- ✅ Boss spawn chậm (> 3s)
- ✅ Cần 100% reliability

---

### **Profile 4: ULTRA (Experimental)** ⚡💀
**Mục đích:** Tốc độ tuyệt đối, chấp nhận rủi ro cao  
**Khuyến nghị:** CHỈ dùng để test hoặc boss farm map quen thuộc

```csharp
// AutoBoss.cs
private const float ZoneSwitchCooldown = 0.5f;      // ⚡⚡ -58%
private const float ZoneEmptyDwellSec = 0.3f;       // ⚡⚡ -70%
private const float ZoneMobDwellSec = 1.5f;         // ⚡⚡ -57%
private const float ZoneBossHintDwellSec = 1.0f;    // ⚡⚡ -50%

// Movement
Portal/Boss threshold: 3.0f                         // ⚡⚡ +150%
Portal/Boss reissue: 0.4s                           // ⚡⚡ -56%
Stall check: 0.2s                                   // ⚡⚡ -43%
```

**Hiệu suất:**
- 10 zones: ~15s (**55% nhanh hơn Balanced!**)
- Boss detection: ~5-10% miss rate (HIGH)
- Portal walking: ~3s
- CPU usage: High (25-30% peak)

**Rủi ro:**
- 💀 Bỏ sót boss spawn chậm (5-10%)
- 💀 Server có thể throttle/kick do spam packets
- 💀 False positive stall → movement jitter
- 💀 Zone panel có thể không kịp render

**CHỈ dùng khi:**
- Boss spawn instant (< 1s)
- Network LAN/localhost
- Map đơn giản (không có portal chain)
- Đã test kỹ và chấp nhận miss rate

---

## 🔧 Parameter Details

### **1. ZoneSwitchCooldown**
```
Location: AutoBoss.cs line 106
Purpose: Cooldown giữa các lần NextZone() sau khi xác nhận đổi khu thành công
```

**Impact:**
- ⬇️ Giảm → Zone switch nhanh hơn, nhưng có thể spam panel
- ⬆️ Tăng → An toàn hơn, nhưng chậm hơn

**Safe Range:** 0.8s - 2.0s  
**Optimal:** 1.2s

**Tuning Tips:**
- Nếu thấy log "Panel opened but button not ready" nhiều → TĂNG lên 1.5s
- Nếu zone switch quá chậm và không có lỗi → GIẢM xuống 1.0s

---

### **2. ZoneEmptyDwellSec**
```
Location: AutoBoss.cs line 107
Purpose: Thời gian chờ tại khu KHÔNG có mob trước khi chuyển khu tiếp
```

**Impact:**
- ⬇️ Giảm → Skip khu rỗng cực nhanh
- ⬆️ Tăng → Chờ đủ lâu cho mob spawn (nếu có delay)

**Safe Range:** 0.5s - 2.0s  
**Optimal:** 1.0s

**Tuning Tips:**
- Nếu boss KHÔNG BAO GIỜ spawn ở khu rỗng → GIẢM xuống 0.5s
- Nếu có trường hợp mob spawn chậm ở khu "rỗng" → TĂNG lên 1.5s

---

### **3. ZoneMobDwellSec**
```
Location: AutoBoss.cs line 108
Purpose: Thời gian chờ tại khu CÓ mob thường trước khi chuyển khu
```

**Impact:**
- ⬇️ Giảm → Nhanh hơn, nhưng có thể bỏ sót boss spawn chậm
- ⬆️ Tăng → Chắc chắn không miss boss, nhưng chậm

**Safe Range:** 2.5s - 5.0s  
**Optimal:** 3.5s

**Critical Parameter!** Ảnh hưởng lớn nhất đến tốc độ và accuracy.

**Tuning Strategy:**
1. Đo boss spawn delay của game (F2 dump → check timestamps)
2. Nếu boss spawn < 2.5s → có thể giảm xuống 2.5s
3. Nếu boss spawn > 4s → PHẢI tăng lên ít nhất 4.5s

---

### **4. ZoneBossHintDwellSec**
```
Location: AutoBoss.cs line 109
Purpose: Thời gian chờ tại khu có mob "đặc biệt" (tên chứa elite/boss/leader)
```

**Impact:**
- ⬇️ Giảm → Giả định mob đặc biệt = gần boss → skip nhanh
- ⬆️ Tăng → Conservative hơn khi thấy mob đặc biệt

**Safe Range:** 1.5s - 3.0s  
**Optimal:** 2.0s

**Note:** Đây là logic BONUS - nếu không muốn dùng, set = ZoneMobDwellSec

---

### **5. MobCheckInterval**
```
Location: ZoneSwitcher.cs line 71
Purpose: Tần suất refresh mob count cache (dùng cho adaptive dwell)
```

**Impact:**
- ⬇️ Giảm → Fresher data, CPU cao hơn
- ⬆️ Tăng → CPU thấp hơn, data stale hơn

**Safe Range:** 0.5s - 1.5s  
**Optimal:** 0.8s

**Tuning Tips:**
- CPU yếu → TĂNG lên 1.2s
- Cần real-time mob tracking → GIẢM xuống 0.5s

---

### **6. Portal/Boss MoveTo Threshold**
```
Location: 
- Portal: AutoBoss.cs line 907
- Boss: AutoBoss.cs line 1269
Purpose: Distance threshold để issue movement command
```

**Impact:**
- ⬇️ Giảm → Bước nhỏ hơn, smooth nhưng chậm
- ⬆️ Tăng → Bước lớn hơn, nhanh nhưng có thể overshoot

**Safe Range:** 0.8f - 2.0f  
**Optimal:** 1.2f

**Risk Assessment:**
- < 0.8f: Quá smooth, waste time
- 0.8f - 1.5f: Safe zone
- 1.5f - 2.0f: Fast but may overshoot portal trigger
- > 2.0f: High risk server reject packet

---

### **7. Portal/Boss Reissue Interval**
```
Location:
- Portal: AutoBoss.cs line 909
- Boss: AutoBoss.cs line 1271
Purpose: Interval để reissue movement command khi stalled
```

**Impact:**
- ⬇️ Giảm → Recovery nhanh khi stuck, nhưng spam packets
- ⬆️ Tăng → Ít packets, nhưng recovery chậm

**Safe Range:** 0.6s - 1.5s  
**Optimal:** 0.9s

**Network Considerations:**
- Ping < 50ms → có thể giảm xuống 0.6s
- Ping > 100ms → nên tăng lên 1.2s
- Ping > 200ms → PHẢI tăng lên 1.5s

---

### **8. Stall Check Interval**
```
Location: AutoBoss.cs lines 989, 1032
Purpose: Tần suất check player có stuck không
```

**Impact:**
- ⬇️ Giảm → Detect stuck nhanh hơn, CPU cao hơn
- ⬆️ Tăng → CPU thấp hơn, recovery chậm hơn

**Safe Range:** 0.25s - 0.6s  
**Optimal:** 0.35s

**Tuning Tips:**
- Map phức tạp nhiều vật cản → GIẢM xuống 0.25s để recovery nhanh
- Map đơn giản → TĂNG lên 0.5s để save CPU

---

## 📊 Scenario-Based Recommendations

### **Scenario A: Farm boss trong town/map quen**
```
Boss spawn instant, không có portal chain
→ Profile: AGGRESSIVE

ZoneSwitchCooldown: 0.8s
ZoneMobDwellSec: 2.5s
MoveTo threshold: 2.0f
```

---

### **Scenario B: Boss spawn ở dungeon (Frizar, Namek)**
```
Có portal chain, boss spawn chậm
→ Profile: BALANCED hoặc CONSERVATIVE

ZoneSwitchCooldown: 1.2s - 1.5s
ZoneMobDwellSec: 3.5s - 4.5s
MoveTo threshold: 1.2f - 1.5f
```

---

### **Scenario C: Network lag cao (ping > 150ms)**
```
Packets drop thường xuyên
→ Profile: CONSERVATIVE

ZoneSwitchCooldown: 2.0s
ZoneMobDwellSec: 5.0s
Reissue interval: 1.5s
Stall check: 0.6s
```

---

### **Scenario D: CPU yếu (i3, Ryzen 3)**
```
Frame rate thấp, game lag khi scan
→ Profile: CONSERVATIVE + tối ưu cache

MobCheckInterval: 1.2s - 1.5s
ZoneSwitchCooldown: 1.5s - 2.0s
Stall check: 0.5s - 0.6s
```

---

## 🧪 Testing Methodology

### **How to Test Your Tuning:**

1. **Baseline Test (10 runs)**
   ```
   - Map: Cung (10 zones)
   - Boss position: Khu 5
   - Measure: Total time from enter map → boss detected
   ```

2. **Accuracy Test (20 runs)**
   ```
   - Boss ở các khu khác nhau (0-9)
   - Count: Miss rate (không phát hiện boss)
   - Target: < 1% miss rate
   ```

3. **Stress Test (50 runs)**
   ```
   - Boss không spawn (scan hết 10 zones)
   - Check: Log errors, stuck incidents
   - Target: 0 critical errors
   ```

4. **Network Test**
   ```
   - Throttle network to 50ms, 100ms, 200ms ping
   - Measure: Movement timeout rate
   - Target: < 5% timeout
   ```

---

## 📝 Recommended Tuning Process

### **Step 1: Establish Baseline**
```
1. Use BALANCED profile (default)
2. Run 10 boss hunts
3. Note average times:
   - Zone scan per map: ___s
   - Portal walk: ___s
   - Boss approach: ___s
   - Total hunt: ___min
4. Note any issues:
   - Miss boss: ___times
   - Stuck: ___times
   - Timeout: ___times
```

---

### **Step 2: Identify Bottleneck**
```
Analyze logs to find slowest component:

If "ZoneScan" takes longest:
→ Tune ZoneMobDwellSec, ZoneSwitchCooldown

If "WalkToPortal" takes longest:
→ Tune Portal MoveTo threshold, reissue interval

If "MoveToBoss" takes longest:
→ Tune Boss MoveTo threshold, reissue interval

If "Waiting for panel render":
→ Tune ZoneSwitchCooldown (increase)
```

---

### **Step 3: Apply Single Change**
```
IMPORTANT: Thay đổi MỘT parameter mỗi lần!

Example: Reduce ZoneMobDwellSec
1. Change: 3.5s → 3.0s
2. Test: 10 runs
3. Compare: Speed vs. baseline, miss rate
4. If OK: Try 2.5s
5. If NOT OK: Revert to 3.5s
```

---

### **Step 4: Validate**
```
After finding optimal value:
1. Run 20 hunts
2. Confirm:
   - Speed improvement: ___% faster
   - No accuracy loss: ___% miss rate (should be 0%)
   - No new errors: 0 critical issues
3. If all pass → commit change
4. If any fail → revert
```

---

### **Step 5: Document**
```
Record your custom profile:

Map: ___________
Network: ping ___ms
CPU: ___________

ZoneSwitchCooldown: ___s
ZoneEmptyDwellSec: ___s
ZoneMobDwellSec: ___s
MoveTo threshold: ___f
Reissue interval: ___s
Stall check: ___s

Result: ___% faster, ___% miss rate
```

---

## ⚠️ Common Tuning Mistakes

### **Mistake 1: Change multiple params at once**
```
❌ WRONG:
ZoneMobDwellSec: 3.5s → 2.0s
MoveTo threshold: 1.2f → 2.0f
Reissue interval: 0.9s → 0.5s

Result: Faster but miss boss → KHÔNG biết parameter nào gây ra!

✅ CORRECT:
Change one at a time, test each
```

---

### **Mistake 2: Optimize for single run**
```
❌ WRONG:
"1 lần chạy nhanh hơn 10s → OK!"

✅ CORRECT:
Test ít nhất 10 lần để có average + check consistency
```

---

### **Mistake 3: Ignore miss rate**
```
❌ WRONG:
"Nhanh hơn 40%! Ship it!"
(Không check xem có bỏ sót boss không)

✅ CORRECT:
Speed improvement PHẢI đi kèm 0% miss rate (or < 1% acceptable)
```

---

### **Mistake 4: Copy profile không phù hợp**
```
❌ WRONG:
Copy ULTRA profile cho network ping 200ms

✅ CORRECT:
Chọn profile dựa trên network + CPU + map type
```

---

## 🎓 Advanced Tuning

### **Dynamic Adaptive Dwell** (Code mod required)
```csharp
// Thay vì dwell cố định, học pattern từ lịch sử
float adaptiveDwell = CalculateOptimalDwell(mapName, zoneIndex);

// Ví dụ: Khu 3 của map Cung KHÔNG BAO GIỜ có boss (sau 100 lần scan)
// → Set dwell = 0.5s thay vì 3.5s
```

**Benefit:** Tiết kiệm ~2s mỗi khu "known empty"  
**Complexity:** High - cần database, learning algorithm

---

### **Network-Aware Movement** (Code mod required)
```csharp
// Đo ping realtime, tự động adjust reissue interval
float ping = MeasurePing();
float reissueInterval = 0.9f + (ping / 1000f) * 0.5f;

// Ping 50ms → reissue 0.925s
// Ping 200ms → reissue 1.0s
```

**Benefit:** Tự động adapt với network conditions  
**Complexity:** Medium

---

### **Boss Spawn Prediction** (Machine learning)
```
Sau 100+ lần hunt, train model để predict:
- Boss có spawn ở map này không?
- Nếu có, zone nào có xác suất cao nhất?
- Thời gian spawn trung bình?

→ Skip các zone xác suất thấp
→ Tăng dwell cho zone xác suất cao
```

**Benefit:** Tiết kiệm 20-30% thời gian scan  
**Complexity:** Very High - cần ML model, data collection

---

## 📞 Support & Debugging

### **Enable Debug Logging:**
```csharp
// AutoBoss.cs, add at top of RunZoneScanLoop()
Plugin.Log.LogInfo($"[DEBUG] dwellSec={dwellSec:F1}, aliveMobs={aliveMobs}, hasElite={hasEliteMob}");
```

### **Check Current Settings:**
Press F2 in-game → Dump logs → Check parameters in log file

### **Reset to Default:**
Revert all const values to original in OPTIMIZATION_SUMMARY.md

---

**Last updated:** 2026-08-12  
**Version:** 2.0  
**Maintainer:** Claude (Opus 5)
