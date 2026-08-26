# TESTING GUIDE - Phase 3 BFS Pathfinder

## PRE-TEST CHECKLIST:
- [ ] DLLs deployed to BepInEx\plugins
- [ ] Game launched successfully  
- [ ] BepInEx console visible
- [ ] Manager ready to connect

## TEST 1: Plugin Load
Expected log messages:
`
[Info] Plugin AutoBossGrabber loaded
[Info] [SocketClient] Initializing...
[Info] [SocketClient] BFS Pathfinder initialized
[Info] IPC server listening on port 5000
`

## TEST 2: Manager Connection
`python
import socket, json

sock = socket.socket()
sock.connect(('localhost', 5000))
print("? Connected!")
`

## TEST 3: TELEPORT_TO_MAP Command
`python
command = {
    "MessageType": "COMMAND",
    "Command": "TELEPORT_TO_MAP",
    "Payload": {"targetMap": "G?ng"}
}
sock.send(json.dumps(command).encode() + b'\n')
`

Expected logs:
`
[SocketClient] TELEPORT_TO_MAP received: G?ng
[BFSPathfinder] Computing path to: G?ng
[BFSPathfinder] Resolved 'G?ng' to ID: 5
[SocketClient] Path computed: 3 hops
[NavigationController] Executing path
`

## TEST 4: Cache System
Check file created: BepInEx\plugins\bfs_map_cache.json

## TEST 5: INVALIDATE_CACHE
`python
command = {
    "MessageType": "COMMAND",
    "Command": "INVALIDATE_CACHE"
}
sock.send(json.dumps(command).encode() + b'\n')
`

Expected: Cache cleared, graph rebuilt on next path

## DEBUG TIPS:
1. Enable verbose logging
2. Check portal discovery with GetMapId()
3. Verify MapGateway components found
4. Test navigation step-by-step

## COMMON ISSUES:
- No path found ? Check map name spelling
- No navigation ? Check graph initialization  
- Crash ? Check GameAPI method compatibility
- No portals ? Check MapGateway discovery logic
