using Cinemachine;
using UnityEngine;

namespace MarblesECS.Presentation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CinemachineVirtualCamera), typeof(CharacterController))]
    public sealed class RoomCameraMovement : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float speed = 11f;
        [SerializeField, Min(0f)] private float lookSensitivity = 2f;
        [SerializeField] private Vector2 walkMin = new Vector2(-58f, -18f);
        [SerializeField] private Vector2 walkMax = new Vector2(-3f, 19f);

        private CharacterController body;
        private Vector3 wakePosition;
        private Quaternion wakeRotation;
        private float yaw, pitch;
        internal bool CanMove { get; set; }
        internal CinemachineVirtualCamera Camera { get; private set; }

        private void Awake()
        {
            body = GetComponent<CharacterController>();
            Camera = GetComponent<CinemachineVirtualCamera>();
            wakePosition = transform.position;
            wakeRotation = transform.rotation;
            ReadLookAngles();
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!CanMove) return;
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxisRaw("Mouse X") * lookSensitivity;
                pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * lookSensitivity, -65f, 65f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            }
            var input = new Vector2(
                (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            Move(input, Time.unscaledDeltaTime);
#endif
        }

        internal void Move(Vector2 input, float deltaTime)
        {
            if (!CanMove || input == Vector2.zero) return;
            input = Vector2.ClampMagnitude(input, 1);
            var heading = Quaternion.Euler(0, yaw, 0);
            var position = transform.position;
            var destination = position + heading * new Vector3(input.x, 0, input.y) * (speed * deltaTime);
            destination.x = Mathf.Clamp(destination.x, walkMin.x, walkMax.x);
            destination.z = Mathf.Clamp(destination.z, walkMin.y, walkMax.y);
            body.Move(destination - position);
        }

        internal void ResetWakePose()
        {
            CanMove = false;
            body.enabled = false;
            transform.SetPositionAndRotation(wakePosition, wakeRotation);
            body.enabled = true;
            ReadLookAngles();
        }

        private void ReadLookAngles()
        {
            var angles = transform.eulerAngles;
            yaw = angles.y;
            pitch = Mathf.DeltaAngle(0, angles.x);
        }
    }
}
