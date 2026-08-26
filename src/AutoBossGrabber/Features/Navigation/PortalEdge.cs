using Newtonsoft.Json;

namespace AutoBossGrabber;

/// <summary>
/// Represents a directed edge in the map graph (portal connection from one map to another).
/// Each edge stores the destination map ID and the world position of the portal to interact with.
///
/// Dung MapPoint (thuan .NET) thay vi UnityEngine.Vector3 de data model
/// khong phu thuoc il2cpp runtime - test duoc ngoai game.
/// Implicit conversion giup code goi van truyen/nhan Vector3 truc tiep.
///
/// Design: BFS Pathfinder - MapGraph Component
/// Requirements: 1.5 (Store edge data including destination map ID, portal world position)
/// </summary>
public class PortalEdge
{
    /// <summary>
    /// The map ID that this portal leads to.
    /// </summary>
    [JsonProperty("destinationMapId")]
    public int DestinationMapId { get; set; }

    /// <summary>
    /// The world position of the portal object that the player must interact with.
    /// Used by NavigationController to move the player to the portal location.
    /// </summary>
    [JsonProperty("portalPosition")]
    public MapPoint PortalPosition { get; set; }

    /// <summary>
    /// Default constructor for JSON deserialization.
    /// </summary>
    public PortalEdge()
    {
    }

    /// <summary>
    /// Constructor with parameters for convenient edge creation.
    /// </summary>
    /// <param name="destinationMapId">The destination map ID</param>
    /// <param name="portalPosition">The portal's world position</param>
    public PortalEdge(int destinationMapId, MapPoint portalPosition)
    {
        DestinationMapId = destinationMapId;
        PortalPosition = portalPosition;
    }

    public override string ToString()
    {
        return $"Portal -> Map {DestinationMapId} at {PortalPosition}";
    }
}
