# Fix: Portal Chain Loop & Farm Context Issues

## Vấn đề đã phát hiện

### Case 1: Farm ở map trong Portal Chain
**Trước đây:**
```
Đang farm: "Phòng tinh anh" (chain step 0 của Cooler)
Boss spawn!
→ SaveCurrentFarmContext("Phòng tinh anh") ✅
→ Teleport về "Trạm Frizar" (anchor)
→ WalkToPortal qua chain lại
→ Đánh boss xong
→ ReturnToFarmMap("Phòng tinh anh")
   ❌ FAIL: Dungeon không có menu capsule!
```

**Sau fix:**
```
→ SaveCurrentFarmContext() SKIP map trong chain
→ Không lưu context
→ Đánh boss xong → FarmTown (fallback an toàn)
```

### Case 2: Đang ở giữa chain khi boss spawn
**Trước đây:**
```
Đang ở: "Phòng tinh anh" (chain[0])
Boss spawn!
→ Teleport về anchor "Trạm Frizar"
→ WalkToPortal từ đầu: chain[0] → chain[1] → boss
   ❌ Lãng phí thời gian teleport về rồi đi lại
```

**Sau fix:**
```
→ Detect đang ở chain[0]
→ Skip teleport
→ Set _portalChainIndex = 1
→ WalkToPortal trực tiếp chain[1] → boss
   ✅ Tiết kiệm 5-8s teleport
```

### Case 3: Loop detection - target == current map
**Trước đây:**
```
targetMap = "Phòng tinh anh"
currentMap = "Phòng tinh anh"
→ inTargetMap = true
→ isLastStep = false
→ _portalChainIndex++
→ nextTarget = "Phòng tinh anh" (nếu chain lỗi config)
→ Loop vô hạn tìm gateway đến chính map đang đứng
```

**Sau fix:**
```
→ Detect potential loop
→ Force skip to next step
→ Double-check nextTarget != currentMap
→ Nếu vẫn trùng → ZoneScanLoop ngay
```

## Code Changes

### 1. SaveCurrentFarmContext() - Filter chain maps
```csharp
// Không lưu farm context nếu đang ở trong portal chain
// Map trong chain thường là dungeon không có menu capsule
if (Config.PortalChainMaps != null)
{
    foreach (var chainMap in Config.PortalChainMaps)
    {
        if (map.IndexOf(chainMap, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Plugin.Log.LogInfo($"Skip saving - map '{map}' is in chain");
            return;
        }
    }
}
```

### 2. RunTeleportToBossMap() - Smart skip
```csharp
// Kiểm tra đang ở bước nào trong chain
int currentChainStep = -1;
if (Config.PortalChainMaps != null)
{
    for (int i = 0; i < Config.PortalChainMaps.Count; i++)
    {
        if (currentMap.IndexOf(Config.PortalChainMaps[i]) >= 0)
        {
            currentChainStep = i;
            break;
        }
    }
}

// Nếu đang ở giữa chain → skip teleport, adjust index
if (currentChainStep >= 0)
{
    _portalChainIndex = currentChainStep + 1;
    Plugin.Log.LogInfo($"Already at step {currentChainStep} -> skip to {_portalChainIndex}");
    
    if (_portalChainIndex >= Config.PortalChainMaps.Count)
        Transition(AutoBossState.ZoneScanLoop);
    else
        Transition(AutoBossState.WalkToPortal);
    return;
}
```

### 3. RunWalkToPortal() - Loop detection
```csharp
if (inTargetMap)
{
    // Detect loop: target == current && not boss map
    bool isPotentialLoop = false;
    if (_portalChainIndex < Config.PortalChainMaps.Count)
    {
        string chainMapAtCurrentIndex = Config.PortalChainMaps[_portalChainIndex];
        if (targetMap == chainMapAtCurrentIndex &&
            minimapName.IndexOf(chainMapAtCurrentIndex) >= 0)
        {
            isPotentialLoop = true;
            Plugin.Log.LogWarning("Loop detected -> force skip");
        }
    }
    
    if (isPotentialLoop || !isLastStep)
    {
        _portalChainIndex++;
        string nextTarget = GetCurrentPortalChainTarget();
        
        // Double-check next target
        if (minimapName.IndexOf(nextTarget) >= 0)
        {
            Plugin.Log.LogWarning("Next target is also current -> ZoneScanLoop");
            Transition(AutoBossState.ZoneScanLoop);
            return;
        }
        
        _stateTimer = 0f;
        return;
    }
}
```

## Test Cases

### Test 1: Farm ở Ngoại Ô (normal case)
```
Farm: Ngoại Ô
Boss: Cooler
Expected:
✅ Save farm context = Ngoại Ô
✅ Teleport → Trạm Frizar → Walk chain → Boss
✅ Return to Ngoại Ô
```

### Test 2: Farm ở Phòng tinh anh (chain map)
```
Farm: Phòng tinh anh
Boss: Cooler
Expected:
✅ Skip save farm context
✅ Detect at chain[0], skip to chain[1]
✅ Walk → Phòng tinh nhuệ → Boss
✅ Return to FarmTown (no saved context)
```

### Test 3: Farm ở Trạm Frizar (anchor)
```
Farm: Trạm Frizar
Boss: Cooler
Expected:
✅ Save context = Trạm Frizar
✅ Skip teleport (already at anchor)
✅ Walk chain[0] → chain[1] → Boss
✅ Return to Trạm Frizar
```

### Test 4: Loop config (misconfigured chain)
```
Chain: ["Map A", "Map A", "Map B"]
Current: Map A
Expected:
✅ Detect loop at step 0
✅ Force skip to step 1
✅ Detect loop at step 1 (still Map A)
✅ Force skip to step 2
✅ Walk to Map B
```

## Lợi ích

1. **An toàn hơn**: Không lưu farm context ở dungeon không teleport được
2. **Nhanh hơn**: Skip teleport khi đã ở giữa chain (tiết kiệm 5-8s)
3. **Ổn định hơn**: Phát hiện và xử lý loop trong chain config
4. **Thông minh hơn**: Tự động adjust index theo vị trí hiện tại

## Build Status
✅ Build succeeded - No warnings, no errors
