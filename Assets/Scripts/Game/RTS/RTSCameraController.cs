using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Cámara RTS local. No sincroniza su estado porque cada jugador controla su propia vista.
/// Soporta desplazamiento libre, arrastre con rueda, inercia y seguimiento de una unidad.
/// </summary>
public sealed class RTSCameraController : MonoBehaviour
{
    public bool IsLocked => lockedTarget != null;
    public NetworkUnitView LockedTarget => lockedTarget;

    private Camera controlledCamera;
    private NetworkUnitView lockedTarget;

    private Vector3 freeMoveVelocity;
    private Vector2 middleDragVelocity;
    private Vector2 previousMousePosition;
    private float yaw;
    private float pitch = 38f;
    private float distance = 9f;
    private bool middleDragging;
    private bool freeMouseOrbit;
    private float lastAltPressTime = -10f;

    private const float FreeMoveSpeed = 18f;
    private const float KeyboardAcceleration = 8f;
    private const float KeyboardDamping = 7f;
    private const float DragSensitivity = 0.035f;
    private const float DragInertiaDamping = 4.5f;
    private const float OrbitSensitivity = 0.18f;
    private const float FollowSmoothness = 9f;
    private const float DoubleAltWindow = 0.32f;

    public void Initialize(Camera cameraToControl)
    {
        controlledCamera = cameraToControl;
        if (controlledCamera == null)
            return;

        Vector3 euler = controlledCamera.transform.rotation.eulerAngles;
        yaw = euler.y;
        pitch = 38f;
    }

    public void ToggleLock(NetworkUnitView target)
    {
        if (target == null)
            return;

        if (lockedTarget == target)
        {
            Unlock();
            return;
        }

        lockedTarget = target;
        SetLockedCursorState(true);
        yaw = controlledCamera != null ? controlledCamera.transform.rotation.eulerAngles.y : 0f;
        pitch = 32f;
        distance = Mathf.Max(6f, target.SelectionRadius * 5.5f);
        freeMoveVelocity = Vector3.zero;
        middleDragVelocity = Vector2.zero;
    }

    public void Unlock()
    {
        lockedTarget = null;
        freeMouseOrbit = false;
        SetLockedCursorState(false);
    }

    private void OnDisable()
    {
        SetLockedCursorState(false);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            return;
        }

        if (lockedTarget != null)
            SetLockedCursorState(true);
    }

    private static void SetLockedCursorState(bool locked)
    {
        if (locked)
        {
            // Locked mantiene el puntero fijo y entrega movimiento relativo del mouse.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void LateUpdate()
    {
        if (controlledCamera == null)
            controlledCamera = Camera.main;
        if (controlledCamera == null)
            return;

        HandleAltDoublePress();

        if (lockedTarget != null)
            UpdateLockedCamera();
        else
            UpdateFreeCamera();
    }

    private void UpdateFreeCamera()
    {
        Vector2 keyboard = ReadArrowInput();
        Vector3 flatForward = Vector3.ProjectOnPlane(controlledCamera.transform.forward, Vector3.up).normalized;
        Vector3 flatRight = Vector3.ProjectOnPlane(controlledCamera.transform.right, Vector3.up).normalized;
        Vector3 requested = (flatForward * keyboard.y + flatRight * keyboard.x) * FreeMoveSpeed;
        freeMoveVelocity = Vector3.Lerp(freeMoveVelocity, requested, KeyboardAcceleration * Time.deltaTime);

        HandleMiddleMouseDrag(flatForward, flatRight);

        if (!middleDragging)
        {
            Vector3 inertial = (-flatRight * middleDragVelocity.x - flatForward * middleDragVelocity.y) * DragSensitivity;
            freeMoveVelocity += inertial;
            middleDragVelocity = Vector2.Lerp(middleDragVelocity, Vector2.zero, DragInertiaDamping * Time.deltaTime);
        }

        controlledCamera.transform.position += freeMoveVelocity * Time.deltaTime;
        freeMoveVelocity = Vector3.Lerp(freeMoveVelocity, Vector3.zero, KeyboardDamping * Time.deltaTime);

        Vector3 position = controlledCamera.transform.position;
        position.x = Mathf.Clamp(position.x, -45f, 45f);
        position.z = Mathf.Clamp(position.z, -45f, 45f);
        position.y = Mathf.Clamp(position.y, 6f, 30f);
        controlledCamera.transform.position = position;
    }

    private void UpdateLockedCamera()
    {
        if (lockedTarget == null)
            return;

        bool orbitHeld = IsAltHeld() || freeMouseOrbit;
        if (orbitHeld)
        {
            Vector2 delta = ReadMouseDelta();
            yaw += delta.x * OrbitSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * OrbitSensitivity, 15f, 70f);
        }

        float scroll = ReadScroll();
        if (Mathf.Abs(scroll) > 0.01f)
            distance = Mathf.Clamp(distance - scroll * 0.01f, 4f, 16f);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focus = lockedTarget.transform.position + Vector3.up * Mathf.Max(1.2f, lockedTarget.SelectionRadius * 1.4f);
        Vector3 desiredPosition = focus - rotation * Vector3.forward * distance;

        controlledCamera.transform.position = Vector3.Lerp(
            controlledCamera.transform.position,
            desiredPosition,
            1f - Mathf.Exp(-FollowSmoothness * Time.deltaTime));

        Quaternion desiredRotation = Quaternion.LookRotation(focus - controlledCamera.transform.position, Vector3.up);
        controlledCamera.transform.rotation = Quaternion.Slerp(
            controlledCamera.transform.rotation,
            desiredRotation,
            1f - Mathf.Exp(-FollowSmoothness * Time.deltaTime));
    }

    private void HandleMiddleMouseDrag(Vector3 flatForward, Vector3 flatRight)
    {
        bool pressed = MiddlePressedThisFrame();
        bool held = MiddleIsPressed();
        bool released = MiddleReleasedThisFrame();
        Vector2 mousePosition = ReadMousePosition();

        if (pressed)
        {
            middleDragging = true;
            previousMousePosition = mousePosition;
            middleDragVelocity = Vector2.zero;
        }

        if (middleDragging && held)
        {
            Vector2 delta = mousePosition - previousMousePosition;
            previousMousePosition = mousePosition;
            middleDragVelocity = delta / Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            Vector3 movement = (-flatRight * delta.x - flatForward * delta.y) * DragSensitivity;
            controlledCamera.transform.position += movement;
        }

        if (released)
            middleDragging = false;
    }

    private void HandleAltDoublePress()
    {
        if (!AltPressedThisFrame() || lockedTarget == null)
            return;

        float now = Time.unscaledTime;
        if (now - lastAltPressTime <= DoubleAltWindow)
            freeMouseOrbit = !freeMouseOrbit;
        lastAltPressTime = now;
    }

    private static Vector2 ReadArrowInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null)
            return Vector2.zero;
        return new Vector2(
            (Keyboard.current.rightArrowKey.isPressed ? 1f : 0f) - (Keyboard.current.leftArrowKey.isPressed ? 1f : 0f),
            (Keyboard.current.upArrowKey.isPressed ? 1f : 0f) - (Keyboard.current.downArrowKey.isPressed ? 1f : 0f));
#else
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
    }

    private static bool IsAltHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed);
#else
        return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
#endif
    }

    private static bool AltPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.leftAltKey.wasPressedThisFrame || Keyboard.current.rightAltKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.RightAlt);
#endif
    }

    private static Vector2 ReadMouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#else
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
#endif
    }

    private static Vector2 ReadMousePosition()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    private static float ReadScroll()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
#else
        return Input.mouseScrollDelta.y * 120f;
#endif
    }

    private static bool MiddlePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(2);
#endif
    }

    private static bool MiddleReleasedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.middleButton.wasReleasedThisFrame;
#else
        return Input.GetMouseButtonUp(2);
#endif
    }

    private static bool MiddleIsPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.middleButton.isPressed;
#else
        return Input.GetMouseButton(2);
#endif
    }
}
