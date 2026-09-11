using MarblesECS.PhysX;
using UnityEngine;

namespace MarblesECS.Presentation
{
    internal static class MarbleBoardGeometry
    {
        internal static Transform Create(Transform board, GameBalance balance, MarbleVisualPalette palette)
        {
            var b = balance.Board;
            Primitive("Sloped board", PrimitiveType.Cube, board, new Vector3(0, -b.Thickness / 2, 0),
                new Vector3(b.Width, b.Thickness, b.Length), palette.Floor);
            foreach (int side in new[] { -1, 1 })
            {
                Primitive("Side rail", PrimitiveType.Cube, board,
                    new Vector3(side * (b.Width + b.WallThickness) / 2, b.WallHeight / 2, 0),
                    new Vector3(b.WallThickness, b.WallHeight, b.Length + b.WallThickness * 2), palette.Rail);
                Primitive("End rail", PrimitiveType.Cube, board,
                    new Vector3(0, b.WallHeight / 2, side * (b.Length + b.WallThickness) / 2),
                    new Vector3(b.Width, b.WallHeight, b.WallThickness), palette.Rail);
            }
            for (int row = 0; row < b.FixedPinRows; row++)
            {
                int count = b.FixedPinColumns - row % 2;
                for (int col = 0; col < count; col++)
                    Primitive("Fixed pin " + row + "/" + col, PrimitiveType.Cylinder, board,
                        new Vector3((col - (count - 1) * .5f) * b.FixedPinSpacingX, b.PinHeight / 2,
                            b.FixedPinStartZ - row * b.FixedPinSpacingZ),
                        new Vector3(b.FixedPinRadius * 2, b.PinHeight / 2, b.FixedPinRadius * 2), palette.Pin);
            }
            MarbleZoneGeometry.Create(board, balance, palette);
            var launcher = new GameObject("Launcher / A D or slider").transform;
            launcher.SetParent(board, false);
            launcher.localPosition = new Vector3(b.LauncherX, b.LauncherHeight, b.LauncherZ);
            float angle = b.LaunchAngleDegrees * Mathf.Deg2Rad;
            launcher.localRotation = Quaternion.LookRotation(new Vector3(0, Mathf.Sin(angle), -Mathf.Cos(angle)));
            var barrel = Primitive("Launcher barrel", PrimitiveType.Cylinder, launcher,
                new Vector3(0, 0, -.4f), new Vector3(.42f, .38f, .42f), palette.Multiplier, false);
            barrel.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Primitive("Launcher track", PrimitiveType.Cube, board, new Vector3(b.LauncherX, .035f, b.LauncherZ + .5f),
                new Vector3(b.LauncherMoveHalfWidth * 2, .05f, .07f), palette.Multiplier, false);
            return launcher;
        }

        internal static GameObject Primitive(string name, PrimitiveType type, Transform parent,
            Vector3 position, Vector3 scale, Material material, bool solid = true)
        {
            GameObject result = GameObject.CreatePrimitive(type);
            result.name = name;
            result.transform.SetParent(parent, false);
            result.transform.localPosition = position;
            result.transform.localScale = scale;
            result.GetComponent<Renderer>().sharedMaterial = material;
            if (!solid)
            {
                Collider collider = result.GetComponent<Collider>();
                collider.enabled = false;
                Object.Destroy(collider);
            }
            return result;
        }
    }
}
