using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AutoBossGrabber;

/// <summary>
/// Game API integration for BFS Pathfinder.
/// Provides real implementations for portal discovery, map queries, and navigation.
/// 
/// Design: BFS Pathfinder - Game Integration Layer
/// Requirements: 1.1 (Scan MapGateway objects), 5.3-5.4 (Portal interaction)
/// </summary>
public static class PathfinderGameAPI
{
    /// <summary>
    /// Finds all MapGateway objects in the game world.
    /// Scans Unity scene for active MapGateway MonoBehaviours.
    /// 
    /// Requirements: 1.1
    /// </summary>
    /// <returns>List of all map gateways (portals) with their connection data</returns>
    public static List<MapGatewayData> FindAllMapGateways()
    {
        var result = new List<MapGatewayData>();
        
        try
        {
            // Find all MapGateway objects in the scene
            var gateways = UnityEngine.Object.FindObjectsOfType<MapGateway>();
            
            if (gateways == null || gateways.Length == 0)
            {
                Plugin.Log?.LogWarning("[PathfinderGameAPI] No MapGateway objects found in scene");
                return result;
            }
            
            Plugin.Log?.LogInfo($"[PathfinderGameAPI] Found {gateways.Length} MapGateway objects");
            
            foreach (var gateway in gateways)
            {
                if (gateway == null) continue;
                
                try
                {
                    // Get source map (current map where gateway is located)
                    int sourceMapId = GetCurrentMapId();
                    string sourceMapName = GameAPI.GetCurrentMapName();
                    
                    // Get destination map from gateway
                    // NOTE: MapGateway may have fields like destinationMapId, targetMapId, etc.
                    // Need to inspect actual game structure - this is a placeholder
                    int destinationMapId = GetGatewayDestinationMapId(gateway);
                    string destinationMapName = GetGatewayDestinationMapName(gateway);
                    
                    // Get portal position
                    var transform = gateway.transform;
                    Vector3 position = transform.position;
                    
                    var data = new MapGatewayData
                    {
                        SourceMapId = sourceMapId,
                        SourceMapName = sourceMapName,
                        DestinationMapId = destinationMapId,
                        DestinationMapName = destinationMapName,
                        Position = position
                    };
                    
                    result.Add(data);
                    Plugin.Log?.LogInfo($"[PathfinderGameAPI] Gateway: {sourceMapName}({sourceMapId}) -> {destinationMapName}({destinationMapId}) @ {position}");
                }
                catch (System.Exception ex)
                {
                    Plugin.Log?.LogWarning($"[PathfinderGameAPI] Failed to extract gateway data: {ex.Message}");
                }
            }
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"[PathfinderGameAPI] FindAllMapGateways failed: {ex.Message}");
        }
        
        return result;
    }
    
    /// <summary>
    /// Gets the current map ID where the player is located.
    /// Uses GameAPI to query current map and resolve to numeric ID.
    /// 
    /// Requirements: 2.1, 4.1
    /// </summary>
    /// <returns>Current map ID</returns>
    public static int GetCurrentMapId()
    {
        try
        {
            // Get current map name from game
            string mapName = GameAPI.GetCurrentMapFromMiniMap();
            
            if (string.IsNullOrEmpty(mapName))
            {
                mapName = GameAPI.GetCurrentMapName();
            }
            
            if (string.IsNullOrEmpty(mapName))
            {
                Plugin.Log?.LogWarning("[PathfinderGameAPI] GetCurrentMapId: Unable to determine current map name");
                return 0;
            }
            
            // Resolve map name to ID using MapNameResolver
            var resolver = new MapNameResolver();
            int mapId = resolver.Resolve(mapName);
            
            if (mapId == -1)
            {
                Plugin.Log?.LogWarning($"[PathfinderGameAPI] GetCurrentMapId: Unknown map '{mapName}'");
                return 0;
            }
            
            return mapId;
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"[PathfinderGameAPI] GetCurrentMapId failed: {ex.Message}");
            return 0;
        }
    }
    
    /// <summary>
    /// Moves the player character to a target position.
    /// Uses GameAPI.MoveTo for pathfinding and movement.
    /// 
    /// Requirements: 5.3
    /// </summary>
    /// <param name="targetPosition">World position to move to</param>
    public static void MoveToPosition(Vector3 targetPosition)
    {
        try
        {
            Plugin.Log?.LogInfo($"[PathfinderGameAPI] MoveToPosition: ({targetPosition.x:F0}, {targetPosition.y:F0})");
            
            // Use GameAPI to move player
            GameAPI.MoveTo(targetPosition.x, targetPosition.y);
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"[PathfinderGameAPI] MoveToPosition failed: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Interacts with a portal at the given position.
    /// Simulates player clicking the portal to trigger zone transition.
    /// 
    /// Requirements: 5.4
    /// </summary>
    /// <param name="portalPosition">World position of the portal</param>
    public static void InteractWithPortal(Vector3 portalPosition)
    {
        try
        {
            Plugin.Log?.LogInfo($"[PathfinderGameAPI] InteractWithPortal: ({portalPosition.x:F0}, {portalPosition.y:F0})");
            
            // Find the nearest MapGateway to the specified position
            var gateways = UnityEngine.Object.FindObjectsOfType<MapGateway>();
            MapGateway targetGateway = null;
            float minDistance = float.MaxValue;
            
            foreach (var gateway in gateways)
            {
                if (gateway == null) continue;
                
                float distance = Vector3.Distance(gateway.transform.position, portalPosition);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    targetGateway = gateway;
                }
            }
            
            if (targetGateway == null)
            {
                Plugin.Log?.LogWarning("[PathfinderGameAPI] InteractWithPortal: No gateway found near position");
                return;
            }
            
            if (minDistance > 10f)
            {
                Plugin.Log?.LogWarning($"[PathfinderGameAPI] InteractWithPortal: Nearest gateway is {minDistance:F0} units away");
            }
            
            // Trigger portal interaction
            // NOTE: MapGateway may have methods like OnClick(), Interact(), Use(), etc.
            // Need to inspect actual game structure - attempting common patterns
            TriggerGatewayInteraction(targetGateway);
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"[PathfinderGameAPI] InteractWithPortal failed: {ex.Message}");
        }
    }
    
    // === Helper Methods ===
    
    /// <summary>
    /// Extracts destination map ID from MapGateway object.
    /// Uses reflection to handle obfuscated field names.
    /// </summary>
    private static int GetGatewayDestinationMapId(MapGateway gateway)
    {
        try
        {
            // Attempt to read destinationMapId field (may be obfuscated)
            var type = gateway.GetType();
            
            // Try common field names
            string[] fieldNames = { "destinationMapId", "targetMapId", "mapId", "destMap", "toMap" };
            
            foreach (var fieldName in fieldNames)
            {
                var field = type.GetField(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null && field.FieldType == typeof(int))
                {
                    return (int)field.GetValue(gateway);
                }
            }
            
            // Fallback: scan all int fields and pick first reasonable value (> 0)
            var allFields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var field in allFields)
            {
                if (field.FieldType == typeof(int))
                {
                    int value = (int)field.GetValue(gateway);
                    if (value > 0 && value < 1000) // Reasonable map ID range
                    {
                        return value;
                    }
                }
            }
            
            Plugin.Log?.LogWarning("[PathfinderGameAPI] Could not extract destination map ID from MapGateway");
            return 0;
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogWarning($"[PathfinderGameAPI] GetGatewayDestinationMapId failed: {ex.Message}");
            return 0;
        }
    }
    
    /// <summary>
    /// Extracts destination map name from MapGateway object.
    /// </summary>
    private static string GetGatewayDestinationMapName(MapGateway gateway)
    {
        try
        {
            var type = gateway.GetType();
            
            // Try common field names
            string[] fieldNames = { "destinationMapName", "targetMapName", "mapName", "destName", "toMapName" };
            
            foreach (var fieldName in fieldNames)
            {
                var field = type.GetField(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null && field.FieldType == typeof(string))
                {
                    string value = (string)field.GetValue(gateway);
                    if (!string.IsNullOrEmpty(value))
                    {
                        return value;
                    }
                }
            }
            
            return "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }
    
    /// <summary>
    /// Triggers interaction with a MapGateway using reflection.
    /// </summary>
    private static void TriggerGatewayInteraction(MapGateway gateway)
    {
        try
        {
            var type = gateway.GetType();
            
            // Try common interaction method names
            string[] methodNames = { "OnClick", "Interact", "Use", "Activate", "OnMouseDown", "OnPointerClick" };
            
            foreach (var methodName in methodNames)
            {
                var method = type.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (method != null && method.GetParameters().Length == 0)
                {
                    method.Invoke(gateway, null);
                    Plugin.Log?.LogInfo($"[PathfinderGameAPI] Invoked {methodName}() on MapGateway");
                    return;
                }
            }
            
            Plugin.Log?.LogWarning("[PathfinderGameAPI] Could not find interaction method on MapGateway");
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogWarning($"[PathfinderGameAPI] TriggerGatewayInteraction failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Data model representing a map gateway (portal connection) in the game world.
/// Extracted from game objects during graph construction.
/// 
/// Design: BFS Pathfinder - Game Data Model
/// Requirements: 1.2 (Extract portal data: source, destination, position)
/// </summary>
public class MapGatewayData
{
    /// <summary>
    /// The map ID where this portal is located.
    /// </summary>
    public int SourceMapId { get; set; }
    
    /// <summary>
    /// The human-readable name of the source map.
    /// </summary>
    public string SourceMapName { get; set; }
    
    /// <summary>
    /// The map ID that this portal leads to.
    /// </summary>
    public int DestinationMapId { get; set; }
    
    /// <summary>
    /// The human-readable name of the destination map.
    /// </summary>
    public string DestinationMapName { get; set; }
    
    /// <summary>
    /// The world position of the portal GameObject.
    /// Used by NavigationController to move player to portal.
    /// </summary>
    public Vector3 Position { get; set; }
    
    public override string ToString()
    {
        return $"Portal: {SourceMapName} ({SourceMapId}) to {DestinationMapName} ({DestinationMapId}) at {Position}";
    }
}

