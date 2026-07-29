using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Chat y consola básica de partida inspirada en el flujo clásico de WoW:
/// Enter abre, Enter envía y cierra, Escape cancela, / abre directamente un
/// comando y las flechas arriba/abajo recorren el historial local.
/// </summary>
public sealed class GameTextInputHudController : MonoBehaviour
{
    private const int MaxVisibleMessages = 50;
    private const int MaxSentHistory = 30;

    private readonly Queue<Label> messageLabels = new();
    private readonly List<string> sentTextHistory = new();

    private UIDocument uiDocument;
    private PanelSettings runtimePanelSettings;
    private VisualElement panel;
    private VisualElement dragHandle;
    private ScrollView history;
    private TextField input;
    private Button sendButton;
    private Label statusLabel;
    private DraggableHudPanel draggablePanel;

    private bool channelSubscribed;
    private bool inputFocused;
    private bool consoleOpen;
    private int ignoreEnterThroughFrame = -1;
    private int sentHistoryIndex;
    private string pendingDraft = string.Empty;

    private void Awake()
    {
        VisualTreeAsset tree = Resources.Load<VisualTreeAsset>("UI/GameHud/GameTextInputHud");
        if (tree == null)
        {
            Debug.LogError("[GameTextInputHudController] No se encontró GameTextInputHud.uxml.");
            enabled = false;
            return;
        }

        uiDocument = HudDocumentFactory.Create(gameObject, tree, 850, out runtimePanelSettings);
        if (uiDocument == null)
        {
            enabled = false;
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;
        panel = root.Q<VisualElement>("game-text-panel");
        dragHandle = root.Q<VisualElement>("game-text-drag-handle");
        history = root.Q<ScrollView>("game-text-history");
        input = root.Q<TextField>("game-text-input");
        sendButton = root.Q<Button>("game-text-send-button");
        statusLabel = root.Q<Label>("game-text-status");

        if (panel != null)
        {
            draggablePanel = new DraggableHudPanel(
                root,
                panel,
                "GammaSix.Hud.GameTextInput",
                dragHandle,
                allowDragWhileModalOpen: true);
        }

        if (input != null)
        {
            input.RegisterCallback<FocusInEvent>(OnInputFocusIn);
            input.RegisterCallback<FocusOutEvent>(OnInputFocusOut);
            input.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
        }

        if (sendButton != null)
            sendButton.clicked += SubmitCurrentText;

        SetStatus("Enter: enviar · Esc: cancelar · ↑↓: historial", false);
        SetConsoleOpen(false, clearInput: true);
        TrySubscribeChannel();
    }

    private void Update()
    {
        TrySubscribeChannel();

        if (consoleOpen)
        {
            if (GameInputReader.EscapePressedThisFrame)
            {
                CloseConsole(clearInput: true);
                return;
            }

            // Si el usuario hizo clic en el historial o encabezado, Enter vuelve
            // a llevar el foco al campo en lugar de permitir input de gameplay.
            if (!inputFocused &&
                Time.frameCount > ignoreEnterThroughFrame &&
                GameInputReader.EnterPressedThisFrame)
            {
                FocusInput(selectAll: false);
            }

            return;
        }

        if (Time.frameCount <= ignoreEnterThroughFrame || GameUiModalService.IsModalOpen)
            return;

        if (GameInputReader.SlashPressedThisFrame)
        {
            OpenConsole(commandMode: true);
            return;
        }

        if (GameInputReader.EnterPressedThisFrame)
            OpenConsole(commandMode: false);
    }

    private void OnDestroy()
    {
        if (input != null)
        {
            input.UnregisterCallback<FocusInEvent>(OnInputFocusIn);
            input.UnregisterCallback<FocusOutEvent>(OnInputFocusOut);
            input.UnregisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
        }

        if (sendButton != null)
            sendButton.clicked -= SubmitCurrentText;

        if (channelSubscribed && MatchTextChannelController.Instance != null)
            MatchTextChannelController.Instance.TextDisplayed -= OnTextDisplayed;

        draggablePanel?.Dispose();
        GameUiModalService.Release(this);
        if (runtimePanelSettings != null)
            Destroy(runtimePanelSettings);
    }

    private void TrySubscribeChannel()
    {
        if (channelSubscribed || MatchTextChannelController.Instance == null)
            return;

        MatchTextChannelController.Instance.TextDisplayed += OnTextDisplayed;
        channelSubscribed = true;
    }

    private void OpenConsole(bool commandMode)
    {
        if (panel == null || input == null)
            return;

        consoleOpen = true;
        panel.visible = true;
        panel.pickingMode = PickingMode.Position;
        panel.AddToClassList("game-text-panel-open");
        GameUiModalService.SetOpen(this, true);

        input.value = commandMode ? "/" : string.Empty;
        pendingDraft = input.value;
        sentHistoryIndex = sentTextHistory.Count;
        SetStatus(
            commandMode
                ? "Comando abierto · /help muestra los comandos disponibles"
                : "Escribe para la partida · / inicia un comando",
            false);

        input.schedule.Execute(() => FocusInput(selectAll: !commandMode)).ExecuteLater(1);
    }

    private void CloseConsole(bool clearInput)
    {
        if (input != null)
        {
            if (clearInput)
                input.value = string.Empty;
            input.Blur();
        }

        inputFocused = false;
        consoleOpen = false;
        pendingDraft = string.Empty;
        sentHistoryIndex = sentTextHistory.Count;
        panel?.RemoveFromClassList("game-text-panel-focused");
        panel?.RemoveFromClassList("game-text-panel-open");

        if (panel != null)
        {
            panel.visible = false;
            panel.pickingMode = PickingMode.Ignore;
        }

        ignoreEnterThroughFrame = Time.frameCount + 1;
        GameUiModalService.SetOpen(this, false);
    }

    private void SetConsoleOpen(bool open, bool clearInput)
    {
        if (open)
            OpenConsole(commandMode: false);
        else
            CloseConsole(clearInput);
    }

    private void FocusInput(bool selectAll)
    {
        if (input == null || !consoleOpen)
            return;

        input.Focus();
        if (selectAll && !string.IsNullOrEmpty(input.value))
            input.SelectAll();
    }

    private void OnInputFocusIn(FocusInEvent _)
    {
        inputFocused = true;
        panel?.AddToClassList("game-text-panel-focused");
    }

    private void OnInputFocusOut(FocusOutEvent _)
    {
        inputFocused = false;
        panel?.RemoveFromClassList("game-text-panel-focused");
    }

    private void OnInputKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Escape)
        {
            evt.StopImmediatePropagation();
            CloseConsole(clearInput: true);
            return;
        }

        if (evt.keyCode == KeyCode.UpArrow)
        {
            evt.StopImmediatePropagation();
            NavigateSentHistory(-1);
            return;
        }

        if (evt.keyCode == KeyCode.DownArrow)
        {
            evt.StopImmediatePropagation();
            NavigateSentHistory(1);
            return;
        }

        if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
            return;

        evt.StopImmediatePropagation();
        ignoreEnterThroughFrame = Time.frameCount + 1;
        SubmitCurrentText();
    }

    private void NavigateSentHistory(int direction)
    {
        if (input == null || sentTextHistory.Count == 0)
            return;

        if (sentHistoryIndex == sentTextHistory.Count)
            pendingDraft = input.value ?? string.Empty;

        sentHistoryIndex = Mathf.Clamp(
            sentHistoryIndex + direction,
            0,
            sentTextHistory.Count);

        input.value = sentHistoryIndex >= sentTextHistory.Count
            ? pendingDraft
            : sentTextHistory[sentHistoryIndex];
    }

    private void SubmitCurrentText()
    {
        string text = input?.value?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            CloseConsole(clearInput: true);
            return;
        }

        MatchTextChannelController channel = MatchTextChannelController.Instance;
        if (channel == null)
        {
            SetStatus("El canal de comunicación todavía no está disponible.", true);
            FocusInput(selectAll: false);
            return;
        }

        AddSentHistory(text);
        channel.SubmitLocalText(text);
        CloseConsole(clearInput: true);
    }

    private void AddSentHistory(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (sentTextHistory.Count == 0 ||
            !string.Equals(sentTextHistory[sentTextHistory.Count - 1], text, StringComparison.Ordinal))
        {
            sentTextHistory.Add(text);
        }

        while (sentTextHistory.Count > MaxSentHistory)
            sentTextHistory.RemoveAt(0);

        sentHistoryIndex = sentTextHistory.Count;
        pendingDraft = string.Empty;
    }

    private void OnTextDisplayed(MatchTextDisplayPayload payload)
    {
        if (payload == null || history == null)
            return;

        Label message = new();
        message.AddToClassList("game-text-message");
        if (payload.IsSystem)
            message.AddToClassList("game-text-message-system");
        if (payload.IsError)
            message.AddToClassList("game-text-message-error");

        string prefix = string.IsNullOrWhiteSpace(payload.SenderName)
            ? string.Empty
            : $"{payload.SenderName}: ";
        message.text = prefix + payload.Text;
        history.Add(message);
        messageLabels.Enqueue(message);

        while (messageLabels.Count > MaxVisibleMessages)
            messageLabels.Dequeue().RemoveFromHierarchy();

        history.schedule.Execute(() => history.ScrollTo(message));
        SetStatus(payload.IsError ? "El último comando fue rechazado." : "Mensaje recibido.", payload.IsError);
    }

    private void SetStatus(string text, bool error)
    {
        if (statusLabel == null)
            return;

        statusLabel.text = text ?? string.Empty;
        if (error)
            statusLabel.AddToClassList("game-text-status-error");
        else
            statusLabel.RemoveFromClassList("game-text-status-error");
    }
}
