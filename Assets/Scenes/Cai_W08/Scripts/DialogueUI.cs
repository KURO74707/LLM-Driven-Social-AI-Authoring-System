using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Dialogue presentation and submission only; no response generation.</summary>
public class DialogueUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text npcLineText;
    [SerializeField] private TMP_InputField playerInput;
    [SerializeField] private Button sendButton;
    [SerializeField] private ScrollRect responseScroll;
    [Tooltip("A ConversationManager owns speaker labels and the shared history. Off preserves Iteration 1 behavior.")]
    [SerializeField] private bool groupConversation;
    public bool IsGroupConversation => groupConversation;

    [Header("Scrolling")]
    [Tooltip("Follow new messages only when this many pixels from the bottom. Wheel sensitivity is configured on DialogueScrollView's ScrollRect.")]
    [Min(0f)] [SerializeField] private float autoScrollBottomThreshold = 50f;

    /// <summary>Future NPC/API adapters can subscribe and call DisplayMessage with a reply.</summary>
    public event Action<string> OnPlayerSubmitted;

    [Header("Typewriter")]
    [SerializeField] private float charsPerSecond = 40f;

    private Coroutine typeRoutine;
    private Coroutine scrollRoutine;
    private bool followBottomAfterLayout;
    private Vector2 positionBeforeContentChange;
    private readonly StringBuilder transcript = new StringBuilder();
    private string statusMessage = "Start a conversation...";
    private bool busy;
    private Coroutine focusRoutine;
    private bool addedButtonListener;
    private int lastSubmissionFrame = -1;

    private void OnEnable()
    {
        if (npcLineText == null || playerInput == null || sendButton == null)
        {
            Debug.LogError("DialogueUI: assign all three UI references.", this);
            enabled = false;
            return;
        }

        playerInput.lineType = TMP_InputField.LineType.SingleLine;
        npcLineText.textWrappingMode = TextWrappingModes.Normal;
        npcLineText.overflowMode = TextOverflowModes.Overflow;
        if (groupConversation) { npcLineText.richText = true; statusMessage = "Message your team or use @Alex / @Sam."; }
        RefreshDialogue();
        playerInput.onSubmit.AddListener(HandleInputSubmit);

        // The scene has an Inspector onClick binding. Support manual setup too,
        // without adding a second callback when that persistent binding exists.
        addedButtonListener = !HasInspectorSendListener();
        if (addedButtonListener)
            sendButton.onClick.AddListener(SendMessage);

        QueueInputFocus();
    }

    private void OnDisable()
    {
        if (playerInput != null)
            playerInput.onSubmit.RemoveListener(HandleInputSubmit);
        if (sendButton != null && addedButtonListener)
            sendButton.onClick.RemoveListener(SendMessage);
        addedButtonListener = false;
        StopTypewriter();
        if (scrollRoutine != null) StopCoroutine(scrollRoutine);
        scrollRoutine = null;
        busy = false;
        if (sendButton != null) sendButton.interactable = true;
        if (playerInput != null) playerInput.readOnly = false;
        if (focusRoutine != null)
            StopCoroutine(focusRoutine);
        focusRoutine = null;
    }

    private bool HasInspectorSendListener()
    {
        for (int i = 0; i < sendButton.onClick.GetPersistentEventCount(); i++)
        {
            if (sendButton.onClick.GetPersistentTarget(i) == this &&
                sendButton.onClick.GetPersistentMethodName(i) == nameof(SendMessage) &&
                sendButton.onClick.GetPersistentListenerState(i) != UnityEventCallState.Off)
                return true;
        }
        return false;
    }

    private void HandleInputSubmit(string unused)
    {
        // Use TMP's submit event only. End-edit also fires on mouse focus changes.
        SendMessage();
    }

    public void SendMessage()
    {
        if (!isActiveAndEnabled || busy || playerInput == null || npcLineText == null)
            return;

        // TMP and the EventSystem can both process the same Enter press.
        if (lastSubmissionFrame == Time.frameCount)
            return;

        string message = playerInput.text;
        if (string.IsNullOrWhiteSpace(message))
        {
            QueueInputFocus();
            return;
        }

        lastSubmissionFrame = Time.frameCount;
        playerInput.SetTextWithoutNotify(string.Empty);
        if (!groupConversation) AppendDialogue("You", message);
        QueueInputFocus();
        OnPlayerSubmitted?.Invoke(message);
    }

    public void DisplayMessage(string message)
    {
        StopTypewriter();
        QueueScrollAfterContentChange();
        statusMessage = message ?? string.Empty;
        RefreshDialogue();
    }

    public void ClearDialogue()
    {
        transcript.Clear();
        DisplayMessage(string.Empty);
    }

    public void SetBusy(bool value)
    {
        busy = value;
        if (sendButton != null) sendButton.interactable = !value;
        if (playerInput != null) playerInput.readOnly = value;
        if (!value && isActiveAndEnabled) QueueInputFocus();
    }

    public void ShowThinking()
    {
        DisplayMessage("Thinking...");
    }

    /// <summary>Measure the full response with the same font, width, prefix and wrapping used to display it.</summary>
    public bool ResponseFitsWithinLines(string response, int maximumLines) => ResponseFitsWithinLines(response, maximumLines, "NPC");

    public bool ResponseFitsWithinLines(string response, int maximumLines, string speaker)
    {
        if (npcLineText == null || string.IsNullOrWhiteSpace(response)) return false;
        Canvas.ForceUpdateCanvases();
        string previous = npcLineText.text;
        int visible = npcLineText.maxVisibleCharacters;
        try
        {
            npcLineText.maxVisibleCharacters = int.MaxValue;
            return npcLineText.GetTextInfo(speaker + ": " + (groupConversation ? EscapeMarkup(response) : response)).lineCount <= Mathf.Max(1, maximumLines);
        }
        finally
        {
            npcLineText.SetText(previous);
            npcLineText.maxVisibleCharacters = visible;
            npcLineText.ForceMeshUpdate();
        }
    }

    public void ShowNpcLine(string line)
    {
        StopTypewriter();
        int previousLength = transcript.Length;
        AppendDialogue("NPC", line ?? string.Empty);
        if (npcLineText != null && isActiveAndEnabled && charsPerSecond > 0f &&
            !string.IsNullOrEmpty(line) && ResponseFitsViewport(line))
            typeRoutine = StartCoroutine(TypeLine(previousLength));
    }

    /// <summary>Presentation only; the ConversationManager stores the canonical message once.</summary>
    public void ShowSpeakerLine(string speaker, string message, Color color)
    {
        StopTypewriter();
        QueueScrollAfterContentChange();
        if (transcript.Length > 0) transcript.Append('\n');
        transcript.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(color)).Append(">")
            .Append(EscapeMarkup(speaker)).Append(":</color> ").Append(EscapeMarkup(message));
        statusMessage = string.Empty;
        RefreshDialogue();
    }

    // Keep player/model text literal while allowing our own colored speaker labels.
    private static string EscapeMarkup(string value) => (value ?? string.Empty).Replace("<", "<\u200B");

    private bool ResponseFitsViewport(string line)
    {
        if (responseScroll == null || responseScroll.viewport == null) return true;
        float width = npcLineText.rectTransform.rect.width - npcLineText.margin.x - npcLineText.margin.z;
        // Long replies are immediately readable; short replies retain the typewriter effect.
        string completeText = npcLineText.text;
        float height = npcLineText.GetPreferredValues(line, Mathf.Max(1f, width), Mathf.Infinity).y;
        // TMP's measurement API replaces its internal text buffers; restore the transcript.
        npcLineText.SetText(completeText);
        return height <= responseScroll.viewport.rect.height;
    }

    private IEnumerator TypeLine(int previousLength)
    {
        // Keep the complete text in TMP so content height is correct from the start.
        npcLineText.ForceMeshUpdate();
        int characterCount = npcLineText.textInfo.characterCount;
        for (int i = Mathf.Min(previousLength, characterCount); i <= characterCount; i++)
        {
            npcLineText.maxVisibleCharacters = i;
            yield return new WaitForSecondsRealtime(1f / Mathf.Max(1f, charsPerSecond));
        }
        npcLineText.maxVisibleCharacters = int.MaxValue;
        typeRoutine = null;
    }

    private void StopTypewriter()
    {
        if (typeRoutine != null) StopCoroutine(typeRoutine);
        typeRoutine = null;
        if (npcLineText != null) npcLineText.maxVisibleCharacters = int.MaxValue;
    }

    private void AppendDialogue(string speaker, string message)
    {
        StopTypewriter();
        QueueScrollAfterContentChange();
        if (transcript.Length > 0) transcript.Append("\n\n");
        transcript.Append(speaker).Append(": ").Append(message);
        statusMessage = string.Empty;
        RefreshDialogue();
    }

    private void RefreshDialogue()
    {
        if (npcLineText == null) return;
        npcLineText.text = transcript.ToString();
        if (!string.IsNullOrEmpty(statusMessage))
            npcLineText.text += (transcript.Length > 0 ? "\n\n" : string.Empty) + statusMessage;
    }

    private void QueueScrollAfterContentChange()
    {
        if (responseScroll == null || responseScroll.content == null ||
            responseScroll.viewport == null || !isActiveAndEnabled) return;
        // Several updates can happen in one frame (player message + thinking status).
        // Keep the position from before the first update, rather than testing new bounds.
        if (scrollRoutine != null) return;
        positionBeforeContentChange = responseScroll.content.anchoredPosition;
        float scrollableHeight = Mathf.Max(0f,
            responseScroll.content.rect.height - responseScroll.viewport.rect.height);
        float distanceFromBottom = Mathf.Clamp01(responseScroll.verticalNormalizedPosition) * scrollableHeight;
        followBottomAfterLayout = distanceFromBottom <= autoScrollBottomThreshold;
        scrollRoutine = StartCoroutine(UpdateScrollAfterLayout());
    }

    private IEnumerator UpdateScrollAfterLayout()
    {
        yield return null;
        if (responseScroll == null || responseScroll.content == null)
        {
            scrollRoutine = null;
            yield break;
        }
        Vector2 readingPosition = responseScroll.content.anchoredPosition;
        bool playerMoved = Mathf.Abs(readingPosition.y - positionBeforeContentChange.y) > 0.5f;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(responseScroll.content);
        if (followBottomAfterLayout && !playerMoved)
        {
            responseScroll.StopMovement();
            responseScroll.verticalNormalizedPosition = 0f;
        }
        else
        {
            // Preserve the visible passage in pixels, not a fraction of the growing history.
            float maximumOffset = Mathf.Max(0f,
                responseScroll.content.rect.height - responseScroll.viewport.rect.height);
            readingPosition.y = Mathf.Clamp(readingPosition.y, 0f, maximumOffset);
            responseScroll.content.anchoredPosition = readingPosition;
        }
        // No Update/LateUpdate or typewriter callbacks force the scroll position.
        scrollRoutine = null;
    }
    private void QueueInputFocus()
    {
        if (focusRoutine != null)
            StopCoroutine(focusRoutine);
        focusRoutine = StartCoroutine(FocusInputNextFrame());
    }

    private IEnumerator FocusInputNextFrame()
    {
        // TMP deactivates AFTER onSubmit returns; restore focus the next frame.
        yield return null;
        focusRoutine = null;
        if (playerInput == null || !playerInput.IsActive() || !playerInput.IsInteractable())
            yield break;

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(playerInput.gameObject);
        playerInput.ActivateInputField();
    }
}
