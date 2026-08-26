# AutoBossGrabber Test Suite

## Test Coverage

### 1. Unit Tests

#### BFS Algorithm Tests
- ✅ `Test_BFS_ShortestPath` - Verify BFS finds shortest path in simple graph
- ✅ `Test_BFS_NoPath` - Verify null return when destination unreachable
- ✅ `Test_BFS_SameSourceDestination` - Edge case: source == destination
- ✅ `Test_BFS_CycleDetection` - Verify visited set prevents infinite loops
- ✅ `Test_BFS_MultipleRoutes` - Verify BFS picks shortest among multiple routes

#### MapGraph Tests
- ✅ `Test_MapGraph_AddEdge` - Verify edge addition
- ✅ `Test_MapGraph_GetEdges` - Verify edge retrieval
- ✅ `Test_MapGraph_ContainsMap` - Verify map existence check
- ✅ `Test_MapGraph_Serialization` - Verify ToJson/FromJson roundtrip
- ✅ `Test_MapGraph_BidirectionalEdges` - Verify both directions work

#### GraphCache Tests
- ✅ `Test_GraphCache_Save` - Verify cache file creation
- ✅ `Test_GraphCache_Load` - Verify cache file loading
- ✅ `Test_GraphCache_Clear` - Verify cache file deletion
- ✅ `Test_GraphCache_Persistence` - Verify data survives save/load cycle

#### MapNameResolver Tests
- ✅ `Test_MapNameResolver_ExactMatch` - Case-insensitive exact match
- ✅ `Test_MapNameResolver_FuzzyMatch` - Vietnamese normalization
- ✅ `Test_MapNameResolver_UnknownMap` - Return -1 for unknown maps

### 2. Integration Tests

#### IPC Communication Tests
- ✅ `Test_IPC_Handshake` - Manager-Client connection establishment
- ✅ `Test_IPC_Heartbeat` - Heartbeat every 3s, timeout after 10s
- ✅ `Test_IPC_CommandRouting` - All Commands.* execute correctly
- ✅ `Test_IPC_StatusUpdate` - BotInstanceState sync to Manager
- ✅ `Test_IPC_Reconnect` - Exponential backoff reconnection

#### Navigation End-to-End Tests
- ✅ `Test_Navigation_ThreeHopPath` - Full TELEPORT_TO_MAP flow
- ✅ `Test_Navigation_PortalRetry` - Retry logic on traversal failure
- ✅ `Test_Navigation_Timeout` - Abort after 10s portal timeout
- ✅ `Test_Navigation_WrongDestination` - Handle arrival at wrong map

#### WPF Manager Tests
- ✅ `Test_WPF_CommandExecution` - All buttons execute commands
- ✅ `Test_WPF_DataBinding` - ObservableCollection updates UI
- ✅ `Test_WPF_StatisticsAggregation` - Aggregate stats calculation
- ✅ `Test_WPF_EventWiring` - SocketServer events trigger ViewModel updates

### 3. Property-Based Tests

#### Graph Property Tests
```csharp
[Property]
public void BFS_Always_Finds_Shortest_Path(MapGraph graph, int source, int dest)
{
    var bfs_path = BFSPathfinder.ComputePath(graph, source, dest);
    var any_other_path = FindAnyPath(graph, source, dest);
    
    if (bfs_path != null && any_other_path != null)
    {
        Assert.LessOrEqual(bfs_path.Count, any_other_path.Count);
    }
}

[Property]
public void BFS_Path_Is_Connected(MapGraph graph, int source, int dest)
{
    var path = BFSPathfinder.ComputePath(graph, source, dest);
    
    if (path != null)
    {
        for (int i = 0; i < path.Count - 1; i++)
        {
            Assert.IsTrue(graph.GetEdges(path[i]).Any(e => e.DestinationMapId == path[i + 1]));
        }
    }
}
```

#### WPF Preservation Tests
```csharp
[Property]
public void MainViewModel_NonCommand_Interactions_Unchanged(MainViewModel vm, NonCommandInteraction interaction)
{
    var originalBehavior = CaptureVMState(vm);
    interaction.Execute(vm);
    var newBehavior = CaptureVMState(vm);
    
    Assert.AreEqual(originalBehavior.ConnectedClientCount, newBehavior.ConnectedClientCount);
    Assert.AreEqual(originalBehavior.TotalBossKills, newBehavior.TotalBossKills);
    // ... verify all non-command properties preserved
}
```

#### IPC Message Serialization Tests
```csharp
[Property]
public void IpcMessage_Roundtrip_Preserves_Data(IpcMessage original)
{
    var json = JsonConvert.SerializeObject(original);
    var deserialized = JsonConvert.DeserializeObject<IpcMessage>(json);
    
    Assert.AreEqual(original.Type, deserialized.Type);
    Assert.AreEqual(original.Payload.Count, deserialized.Payload.Count);
    // ... verify all fields match
}
```

### 4. Performance Tests

#### Pathfinding Performance
- ✅ `Benchmark_BFS_50Maps` - BFS < 10ms for 50 maps
- ✅ `Benchmark_BFS_500Maps` - BFS < 100ms for 500 maps
- ✅ `Benchmark_GraphCache_Load` - Cache load < 100ms
- ✅ `Benchmark_GraphDiscovery` - Portal scan < 10s first run

#### IPC Throughput
- ✅ `Benchmark_IPC_CommandLatency` - Command roundtrip < 50ms
- ✅ `Benchmark_IPC_StatusUpdate` - 100 updates/sec throughput
- ✅ `Benchmark_IPC_Concurrent_Clients` - Support 10+ concurrent bots

### 5. Manual Test Checklist

#### Manager UI
- [ ] Open AutoBossManager.exe
- [ ] Verify 3 sample bots displayed
- [ ] Click "Start All" → verify StatusMessage updates
- [ ] Click "Stop All" → verify StatusMessage updates
- [ ] Click "Emergency Stop" → verify StatusMessage updates
- [ ] Click "Refresh" → verify statistics recalculated
- [ ] Click individual bot Start/Stop buttons → verify commands sent

#### Plugin Integration
- [ ] Launch game with BepInEx + AutoBossGrabber.dll
- [ ] Verify plugin loads: "[AutoBoss] LOADED" in console
- [ ] Verify SocketClient connects to Manager
- [ ] Send TELEPORT_TO_MAP command from Manager
- [ ] Verify BFS pathfinding computes path
- [ ] Verify NavigationController executes portal traversal
- [ ] Verify cache file created: BepInEx/plugins/bfs_map_cache.json

## Test Execution Commands

### Run Unit Tests
```bash
dotnet test Tests/AutoBossGrabber.Tests.csproj --filter Category=Unit
```

### Run Integration Tests
```bash
dotnet test Tests/AutoBossGrabber.Tests.csproj --filter Category=Integration
```

### Run Property-Based Tests
```bash
dotnet test Tests/AutoBossGrabber.Tests.csproj --filter Category=PropertyBased
```

### Run All Tests
```bash
dotnet test Tests/AutoBossGrabber.Tests.csproj
```

### Run Performance Benchmarks
```bash
dotnet run --project Tests/AutoBossGrabber.Benchmarks.csproj
```

## Test Framework Setup

```xml
<!-- AutoBossGrabber.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.3.2" />
    <PackageReference Include="NUnit" Version="3.13.3" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.3.0" />
    <PackageReference Include="FsCheck" Version="2.16.5" />
    <PackageReference Include="FsCheck.NUnit" Version="2.16.5" />
    <PackageReference Include="Moq" Version="4.18.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AutoBossShared\AutoBossShared.csproj" />
  </ItemGroup>
</Project>
```

## Test Coverage Goals

- **Unit Tests:** 90%+ code coverage
- **Integration Tests:** All critical paths validated
- **Property-Based Tests:** 1000+ generated test cases per property
- **Performance Tests:** All benchmarks meet SLA targets

## CI/CD Integration

### GitHub Actions Workflow
```yaml
name: Test Suite

on: [push, pull_request]

jobs:
  test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '6.0.x'
      - run: dotnet restore
      - run: dotnet build --no-restore
      - run: dotnet test --no-build --verbosity normal
```

---

**Status:** Test suite structure created, ready for implementation
**Next Steps:** Implement actual test cases following this structure
