using System;
using UnityEngine;

namespace AutoBossGrabber;

/// <summary>
/// Diem toa do 3 chieu THUAN .NET cho data model (MapGraph / PortalEdge).
/// Khong phu thuoc UnityEngine -> MapGraph/BFS test duoc ngoai game
/// (interop Vector3 bat buoc co GameAssembly.dll khi khoi tao type).
///
/// Implicit conversion giup production code van truyen truc tiep:
///   Vector3 -> MapPoint : graph.AddEdge(src, dst, gateway.Position)
///   MapPoint -> Vector3 : MoveToPosition(portal.PortalPosition)
/// </summary>
public struct MapPoint
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public MapPoint(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static implicit operator MapPoint(Vector3 v) => new MapPoint(v.x, v.y, v.z);
    public static implicit operator Vector3(MapPoint p) => new Vector3(p.X, p.Y, p.Z);

    public override string ToString() => $"({X:F1}, {Y:F1}, {Z:F1})";
}
