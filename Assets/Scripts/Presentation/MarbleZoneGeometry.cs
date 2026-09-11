using MarblesECS.PhysX;
using UnityEngine;

namespace MarblesECS.Presentation
{
    internal static class MarbleZoneGeometry
    {
        internal static void Create(Transform board, GameBalance balance, MarbleVisualPalette palette)
        {
            foreach (var definition in balance.Zones)
            {
                var obj = new GameObject(definition.Name);
                obj.transform.SetParent(board, false);
                obj.transform.localPosition = new Vector3(definition.CenterX, balance.Board.WallHeight, definition.CenterZ);
                var collider = obj.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(definition.Width, balance.Board.WallHeight * 4, definition.Depth);
                var zone = obj.AddComponent<MarbleZone>();
                zone.TargetId = checked((int)definition.Id);
                zone.Kind = definition.Kind == 1 ? MarbleContactKind.RandomScore :
                    definition.Kind == 2 ? MarbleContactKind.Drain : MarbleContactKind.Score;
                zone.Priority = definition.Priority;
                zone.Multiplier = (float)definition.ScoreMultiplier;
                zone.RushEnabled = definition.RushEnabled;
                Material look = definition.Kind == 1 ? palette.Random : definition.Kind == 2 ? palette.Drain : palette.Score;
                MarbleBoardGeometry.Primitive(definition.Name + " marker", PrimitiveType.Cube, board,
                    new Vector3(definition.CenterX, .025f, definition.CenterZ),
                    new Vector3(definition.Width, .05f, definition.Depth), look, false);
            }
        }
    }
}
