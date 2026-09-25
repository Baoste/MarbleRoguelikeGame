using System;
using MarblesECS.PhysX;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.Editor
{
    public static class DevicePlacementValidation
    {
        public static void Run()
        {
            Scene scene = SceneManager.CreateScene("Placement collider validation", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var root = new GameObject("Placement validation objects");
            SceneManager.MoveGameObjectToScene(root, scene);
            try
            {
                PhysicsScene physics = scene.GetPhysicsScene();
                var obstacles = new DevicePlacementObstacles();
                var pin = new GameObject("Manual fixed pin");
                pin.transform.SetParent(root.transform, false);
                var capsule = pin.AddComponent<CapsuleCollider>();
                capsule.radius = .1f;
                capsule.height = 1f;
                var roots = new[] { pin.transform };

                Check(!obstacles.IsClear(physics, roots, Vector3.zero, .2f), "actual pin blocks placement");
                Check(obstacles.IsClear(physics, roots, new Vector3(0, 0, 3.7f), .2f), "empty position from old generated layout remains usable");
                pin.transform.position = new Vector3(5, 2, 3);
                Check(obstacles.IsClear(physics, roots, Vector3.zero, .2f), "moving a pin releases its old position immediately");
                Check(!obstacles.IsClear(physics, roots, pin.transform.position, .2f), "moved pin blocks its actual position");
                pin.transform.rotation = Quaternion.Euler(0, 0, 90);
                pin.transform.localScale = new Vector3(1, 3, 1);
                Check(!obstacles.IsClear(physics, roots, pin.transform.position + Vector3.right, .2f), "rotated and scaled collider shape is respected");
                capsule.enabled = false;
                Check(obstacles.IsClear(physics, roots, pin.transform.position, .2f), "disabled collider does not block");
                capsule.enabled = true;
                pin.SetActive(false);
                Check(obstacles.IsClear(physics, roots, pin.transform.position, .2f), "inactive pin does not block");
                pin.SetActive(true);
                capsule.isTrigger = true;
                Check(obstacles.IsClear(physics, roots, pin.transform.position, .2f), "trigger is not a solid placement obstacle");
                capsule.isTrigger = false;

                // Unbound floor/device geometry must neither block nor hide a bound pin when the query buffer fills.
                for (int i = 0; i < 40; i++)
                {
                    var floor = new GameObject("Unbound collider " + i);
                    floor.transform.SetParent(root.transform, false);
                    floor.AddComponent<BoxCollider>();
                }
                Check(obstacles.IsClear(physics, roots, Vector3.zero, .2f), "unbound floor and device colliders are ignored");
                pin.transform.position = Vector3.zero;
                Check(!obstacles.IsClear(physics, roots, Vector3.zero, .2f), "crowded query still finds bound obstacles");
                Check(obstacles.IsClear(physics, Array.Empty<Transform>(), Vector3.zero, .2f), "scene may intentionally have no fixed obstacles");
                Debug.Log("PLACEMENT_COLLIDER_VALIDATION_PASSED: 11 assertions.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                SceneManager.UnloadSceneAsync(scene);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Scene placement: " + message);
        }
    }
}
