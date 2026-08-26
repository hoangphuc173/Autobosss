using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AutoBossGrabber;

/// <summary>
/// Orchestrates portal traversal for executing computed paths.
/// Handles moving player to portals, interacting, and verifying map transitions.
/// 
/// Design: BFS Pathfinder - NavigationController Component
/// Requirements: 5.1 (Process portals sequentially)
///               5.2-5.8 (Portal traversal with retry, timeout, verification)
/// </summary>
public class NavigationController
{
    private MapGraph _graph;
    private bool _isNavigating;
    
    /// <summary>
    /// Initializes NavigationController with a map graph.
    /// </summary>
    /// <param name="graph">The map graph containing portal data</param>
    public NavigationController(MapGraph graph)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _isNavigating = false;
    }
    
    /// <summary>
    /// Executes a computed path by traversing portals sequentially.
    /// Must be called from Unity main thread (uses coroutines).
    /// 
    /// Requirements: 5.1, 5.2, 5.7
    /// </summary>
    /// <param name="path">Path as list of map IDs from source to destination</param>
    /// <returns>IEnumerator for Unity coroutine</returns>
    public IEnumerator ExecutePath(List<int> path)
    {
        // Validate path (Requirement 5.1)
        if (path == null || path.Count < 2)
        {
            Plugin.Log.LogWarning("[NavigationController] Invalid path (null or length < 2)");
            yield break;
        }
        
        _isNavigating = true;
        Plugin.Log.LogInfo($"[NavigationController] Executing path: {path.Count}");
        
        // Process portals sequentially (Requirement 5.1)
        for (int i = 0; i < path.Count - 1; i++)
        {
            int currentMap = path[i];
            int nextMap = path[i + 1];
            
            // Find portal to next map
            var portal = _graph.GetEdges(currentMap)
                .FirstOrDefault(e => e.DestinationMapId == nextMap);
            
            if (portal == null)
            {
                Plugin.Log.LogError($"[NavigationController] Portal not found: {currentMap} ? {nextMap}");
                _isNavigating = false;
                yield break;
            }
            
            Plugin.Log.LogInfo($"[NavigationController] Step {i + 1}/{path.Count - 1}: Map {currentMap} ? {nextMap}");
            
            // Traverse portal with retry (Requirement 5.2, 5.4, 5.5)
            yield return TraversePortalWithRetry(portal, nextMap);
            
            // Check if navigation was aborted
            if (!_isNavigating)
            {
                Plugin.Log.LogWarning("[NavigationController] Navigation aborted");
                yield break;
            }
            
            // Verify arrival (Requirement 5.7)
            int arrivedMap = PathfinderGameAPI.GetCurrentMapId();
            if (arrivedMap != nextMap)
            {
                Plugin.Log.LogError($"[NavigationController] Wrong destination: expected {nextMap}, arrived at {arrivedMap}");
                
                // TODO: Recompute path from current location (Requirement 5.8)
                _isNavigating = false;
                yield break;
            }
            
            Plugin.Log.LogInfo($"[NavigationController] Arrived at map {arrivedMap}");
        }
        
        _isNavigating = false;
        Plugin.Log.LogInfo("[NavigationController] Navigation complete");
        
        // Send STATUS_UPDATE to Manager (Requirement 5.7)
        // TODO: Trigger status update event
    }
    
    /// <summary>
    /// Traverses a portal with exponential backoff retry.
    /// 
    /// Requirements: 5.3 (Retry with exponential backoff), 8.3 (Up to 3 attempts)
    /// </summary>
    /// <param name="portal">Portal edge to traverse</param>
    /// <param name="expectedDestination">Expected destination map ID</param>
    /// <returns>IEnumerator for Unity coroutine</returns>
    private IEnumerator TraversePortalWithRetry(PortalEdge portal, int expectedDestination)
    {
        const int maxRetries = 3;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            if (attempt > 1)
            {
                Plugin.Log.LogWarning($"[NavigationController] Retry attempt {attempt}/{maxRetries}");
            }
            
            // Traverse portal
            yield return TraversePortal(portal);
            
            // Verify destination
            int arrivedMap = PathfinderGameAPI.GetCurrentMapId();
            if (arrivedMap == expectedDestination)
            {
                // Success
                yield break;
            }
            
            // Failed - exponential backoff before retry (Requirement 5.3)
            if (attempt < maxRetries)
            {
                float delay = Mathf.Pow(2, attempt); // 2s, 4s, 8s
                Plugin.Log.LogWarning($"[NavigationController] Portal traversal failed, retrying in {delay}s");
                yield return new WaitForSeconds(delay);
            }
        }
        
        // All retries exhausted
        Plugin.Log.LogError($"[NavigationController] Portal traversal failed after {maxRetries} attempts");
        _isNavigating = false;
    }
    
    /// <summary>
    /// Traverses a single portal (move + interact + wait for transition).
    /// 
    /// Requirements: 5.2 (Move to portal, interact), 5.6 (10s timeout)
    /// </summary>
    /// <param name="portal">Portal edge to traverse</param>
    /// <returns>IEnumerator for Unity coroutine</returns>
    private IEnumerator TraversePortal(PortalEdge portal)
    {
        Plugin.Log.LogInfo($"[NavigationController] Traversing portal to map {portal.DestinationMapId} at {portal.PortalPosition}");
        
        // Remember current map before traversal
        int currentMap = PathfinderGameAPI.GetCurrentMapId();
        
        // Move player to portal position (Requirement 5.3)
        PathfinderGameAPI.MoveToPosition(portal.PortalPosition);
        yield return new WaitForSeconds(0.5f); // Wait for movement
        
        // Interact with portal (Requirement 5.4)
        PathfinderGameAPI.InteractWithPortal(portal.PortalPosition);
        
        // Poll for map transition with timeout (Requirement 5.5, 5.6)
        float timeout = 10f;
        float elapsed = 0f;
        
        while (elapsed < timeout)
        {
            // Check if map changed
            if (PathfinderGameAPI.GetCurrentMapId() != currentMap)
            {
                Plugin.Log.LogInfo($"[NavigationController] Portal traversed successfully (took {elapsed:F1}s)");
                yield break;
            }
            
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }
        
        // Timeout (Requirement 5.6)
        Plugin.Log.LogError($"[NavigationController] Portal traversal timeout after {timeout}s");
    }
    
    /// <summary>
    /// Aborts ongoing navigation.
    /// 
    /// Requirements: 4.7
    /// </summary>
    public void AbortNavigation()
    {
        if (_isNavigating)
        {
            _isNavigating = false;
            Plugin.Log.LogWarning("[NavigationController] Navigation aborted by user");
        }
    }
    
    /// <summary>
    /// Gets navigation status.
    /// </summary>
    public bool IsNavigating => _isNavigating;
}
