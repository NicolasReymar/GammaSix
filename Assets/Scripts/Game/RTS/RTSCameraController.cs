using UnityEngine;

/// <summary>
/// Cámara local del jugador. Mantiene un estado RTS independiente del modo
/// de tercera persona. Al desbloquearse, conserva la altura y orientación RTS,
/// pero centra la cámara sobre la entidad desde la que se salió.
/// </summary>
public sealed class RTSCameraController : MonoBehaviour
{
    [Header("Estado inicial RTS")]
    [SerializeField] private Vector3 initialRtsPosition = new(0f, 16f, -14f);
    [SerializeField] private Vector3 initialRtsEulerAngles = new(48f, 0f, 0f);

    [Header("Transiciones")]
    [SerializeField, Min(0.1f)] private float followSmoothness = 9f;
    [SerializeField, Min(0.1f)] private float returnToRtsSmoothness = 8f;
    [SerializeField, Min(0.001f)] private float returnPositionTolerance = 0.02f;
    [SerializeField, Min(0.01f)] private float returnRotationTolerance = 0.15f;

    public bool IsLocked => lockedTarget != null;
    public bool IsReturningToRts => returningToRts;
    public NetworkEntityView LockedTarget => lockedTarget;

    /// <summary>
    /// Verdadero cuando la cámara sigue a una entidad en tercera persona y el
    /// cursor está capturado, oculto y dedicado al control de la cámara.
    /// </summary>
    public bool IsThirdPersonPointerLocked => lockedTarget != null && !thirdPersonPointerUnlocked;

    /// <summary>
    /// Indica si el cursor puede utilizarse para seleccionar entidades.
    /// En tercera persona esto solo ocurre después de desbloquear el cursor
    /// mediante una doble pulsación de Alt.
    /// </summary>
    public bool CanSelectWithPointer => lockedTarget == null || thirdPersonPointerUnlocked;

    private Camera controlledCamera;
    private NetworkEntityView lockedTarget;

    private Vector3 freeMoveVelocity;
    private Vector2 middleDragVelocity;
    private Vector2 previousMousePosition;
    private float yaw;
    private float pitch = 38f;
    private float distance = 9f;
    private bool middleDragging;
    private bool thirdPersonPointerUnlocked;
    private float lastAltPressTime = -10f;

    // Último estado válido de la cámara RTS. Se captura antes de entrar en tercera persona.
    private Vector3 savedRtsPosition;
    private Quaternion savedRtsRotation;
    private bool hasSavedRtsState;
    private bool returningToRts;
    private bool initialized;
    private bool wasUiModalOpen;

    private const float FreeMoveSpeed = 18f;
    private const float KeyboardAcceleration = 8f;
    private const float KeyboardDamping = 7f;
    private const float DragSensitivity = 0.035f;
    private const float DragInertiaDamping = 4.5f;
    private const float OrbitSensitivity = 0.18f;
    private const float DoubleAltWindow = 0.32f;

    public void Initialize(Camera cameraToControl)
    {
        controlledCamera = cameraToControl;
        if (controlledCamera == null)
            return;

        if (!initialized)
        {
            ApplyInitialRtsState();
            initialized = true;
        }

        Vector3 euler = controlledCamera.transform.rotation.eulerAngles;
        yaw = euler.y;
        pitch = 38f;
    }

    /// <summary>
    /// Aplica los valores iniciales configurables de la cámara RTS.
    /// </summary>
    public void ApplyInitialRtsState()
    {
        if (controlledCamera == null)
            controlledCamera = Camera.main;
        if (controlledCamera == null)
            return;

        controlledCamera.transform.SetPositionAndRotation(
            initialRtsPosition,
            Quaternion.Euler(initialRtsEulerAngles));

        savedRtsPosition = initialRtsPosition;
        savedRtsRotation = Quaternion.Euler(initialRtsEulerAngles);
        hasSavedRtsState = true;
        returningToRts = false;
        ResetFreeMovement();
    }

    public void ToggleLock(NetworkEntityView target)
    {
        if (target == null || controlledCamera == null)
            return;

        if (lockedTarget == target)
        {
            Unlock();
            return;
        }

        // Si cambia directamente de objetivo, conserva el estado RTS capturado
        // antes del primer bloqueo en vez de guardar una posición de tercera persona.
        if (lockedTarget == null)
            SaveCurrentRtsState();

        returningToRts = false;
        lockedTarget = target;
        thirdPersonPointerUnlocked = false;
        ApplyThirdPersonCursorState();
        yaw = controlledCamera.transform.rotation.eulerAngles.y;
        pitch = 32f;
        distance = Mathf.Max(6f, target.SelectionRadius * 5.5f);
        ResetFreeMovement();
    }

    public void Unlock()
    {
        if (lockedTarget == null && !returningToRts)
            return;

        // Antes de soltar el objetivo, construye el nuevo estado RTS sobre la
        // posición actual de la entidad. Se conserva la altura y rotación que
        // tenía la cámara RTS antes de entrar en tercera persona.
        if (lockedTarget != null)
            PrepareRtsReturnAboveTarget(lockedTarget);

        lockedTarget = null;
        thirdPersonPointerUnlocked = false;
        SetLockedCursorState(false);
        ResetFreeMovement();

        if (hasSavedRtsState)
            returningToRts = true;
        else
            ApplyInitialRtsState();
    }

    private void PrepareRtsReturnAboveTarget(NetworkEntityView target)
    {
        if (target == null || controlledCamera == null)
            return;

        Quaternion rtsRotation = hasSavedRtsState
            ? savedRtsRotation
            : Quaternion.Euler(initialRtsEulerAngles);

        float rtsHeight = hasSavedRtsState
            ? savedRtsPosition.y
            : initialRtsPosition.y;

        Vector3 groundFocus = target.transform.position;
        Vector3 forward = rtsRotation * Vector3.forward;

        // Calcula una posición cuya línea central de visión termine en la entidad
        // manteniendo la altura RTS anterior. Si el ángulo fuera casi horizontal,
        // utiliza un desplazamiento seguro basado en la posición inicial.
        float verticalLook = -forward.y;
        Vector3 targetCameraPosition;

        if (verticalLook > 0.01f)
        {
            float distanceToFocus = Mathf.Max(1f, (rtsHeight - groundFocus.y) / verticalLook);
            targetCameraPosition = groundFocus - forward * distanceToFocus;
        }
        else
        {
            Vector3 initialOffset = initialRtsPosition;
            initialOffset.y = 0f;
            targetCameraPosition = groundFocus + initialOffset;
            targetCameraPosition.y = rtsHeight;
        }

        targetCameraPosition.x = Mathf.Clamp(targetCameraPosition.x, -45f, 45f);
        targetCameraPosition.z = Mathf.Clamp(targetCameraPosition.z, -45f, 45f);
        targetCameraPosition.y = Mathf.Clamp(rtsHeight, 6f, 30f);

        savedRtsPosition = targetCameraPosition;
        savedRtsRotation = rtsRotation;
        hasSavedRtsState = true;
    }

    private void SaveCurrentRtsState()
    {
        savedRtsPosition = controlledCamera.transform.position;
        savedRtsRotation = controlledCamera.transform.rotation;
        hasSavedRtsState = true;
    }

    private void ResetFreeMovement()
    {
        freeMoveVelocity = Vector3.zero;
        middleDragVelocity = Vector2.zero;
        middleDragging = false;
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
            ApplyThirdPersonCursorState();
    }

    private static void SetLockedCursorState(bool locked)
    {
        if (locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void ApplyThirdPersonCursorState()
    {
        if (lockedTarget == null)
        {
            SetLockedCursorState(false);
            return;
        }

        SetLockedCursorState(!thirdPersonPointerUnlocked);
    }

    private void LateUpdate()
    {
        if (controlledCamera == null)
            controlledCamera = Camera.main;
        if (controlledCamera == null)
            return;

        if (GameUiModalService.BlocksGameplayInput)
        {
            if (!wasUiModalOpen)
            {
                wasUiModalOpen = true;
                SetLockedCursorState(false);
                ResetFreeMovement();
            }
            return;
        }

        if (wasUiModalOpen)
        {
            wasUiModalOpen = false;
            ApplyThirdPersonCursorState();
        }

        HandleAltDoublePress();

        if (lockedTarget != null)
        {
            UpdateLockedCamera();
            return;
        }

        if (returningToRts)
        {
            UpdateReturnToRts();
            return;
        }

        UpdateFreeCamera();
    }

    private void UpdateReturnToRts()
    {
        float blend = 1f - Mathf.Exp(-returnToRtsSmoothness * Time.deltaTime);
        Transform cameraTransform = controlledCamera.transform;

        cameraTransform.position = Vector3.Lerp(cameraTransform.position, savedRtsPosition, blend);
        cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, savedRtsRotation, blend);

        bool positionReady = Vector3.Distance(cameraTransform.position, savedRtsPosition) <= returnPositionTolerance;
        bool rotationReady = Quaternion.Angle(cameraTransform.rotation, savedRtsRotation) <= returnRotationTolerance;

        if (!positionReady || !rotationReady)
            return;

        cameraTransform.SetPositionAndRotation(savedRtsPosition, savedRtsRotation);
        returningToRts = false;

        Vector3 euler = savedRtsRotation.eulerAngles;
        yaw = euler.y;
    }

    private void UpdateFreeCamera()
    {
        Vector2 keyboard = GameInputReader.ArrowKeys;
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

        // En el modo bloqueado el movimiento del mouse controla siempre la cámara.
        // En el modo desbloqueado el cursor queda disponible para selección y la
        // cámara solo orbita mientras Alt está presionado.
        bool orbitHeld = IsThirdPersonPointerLocked || GameInputReader.AltHeld;
        if (orbitHeld)
        {
            Vector2 delta = GameInputReader.PointerDelta;
            yaw += delta.x * OrbitSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * OrbitSensitivity, 15f, 70f);
        }

        float scroll = GameInputReader.ScrollDelta;
        if (Mathf.Abs(scroll) > 0.01f)
            distance = Mathf.Clamp(distance - scroll * 0.01f, 4f, 16f);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focus = lockedTarget.transform.position + Vector3.up * Mathf.Max(1.2f, lockedTarget.SelectionRadius * 1.4f);
        Vector3 desiredPosition = focus - rotation * Vector3.forward * distance;
        float blend = 1f - Mathf.Exp(-followSmoothness * Time.deltaTime);

        controlledCamera.transform.position = Vector3.Lerp(
            controlledCamera.transform.position,
            desiredPosition,
            blend);

        Quaternion desiredRotation = Quaternion.LookRotation(focus - controlledCamera.transform.position, Vector3.up);
        controlledCamera.transform.rotation = Quaternion.Slerp(
            controlledCamera.transform.rotation,
            desiredRotation,
            blend);
    }

    private void HandleMiddleMouseDrag(Vector3 flatForward, Vector3 flatRight)
    {
        bool pressed = GameInputReader.MiddlePressedThisFrame;
        bool held = GameInputReader.MiddleIsPressed;
        bool released = GameInputReader.MiddleReleasedThisFrame;
        Vector2 mousePosition = GameInputReader.PointerPosition;

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
        if (!GameInputReader.AltPressedThisFrame || lockedTarget == null)
            return;

        float now = Time.unscaledTime;
        if (now - lastAltPressTime <= DoubleAltWindow)
        {
            thirdPersonPointerUnlocked = !thirdPersonPointerUnlocked;
            ApplyThirdPersonCursorState();
        }
        lastAltPressTime = now;
    }

}
