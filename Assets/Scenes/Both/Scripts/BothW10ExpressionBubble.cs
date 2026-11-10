using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shows a scene-authored expression above one NPC without requiring a spoken reply.</summary>
[RequireComponent(typeof(RectTransform), typeof(Image))]
public sealed class BothW10ExpressionBubble : MonoBehaviour
{
    [Serializable]
    public sealed class ExpressionStyle
    {
        public string emotion;
        public string face;
        public Color color = Color.white;

        public ExpressionStyle() { }

        public ExpressionStyle(string emotion, string face, Color color)
        {
            this.emotion = emotion;
            this.face = face;
            this.color = color;
        }
    }

    [Header("Scene references")]
    [SerializeField] private TeamMember member;
    [SerializeField] private NPCState state;
    [SerializeField] private TeamAIDirector director;
    [SerializeField] private Camera viewer;
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text faceText;

    [Header("Presentation")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 2.2f, 0f);
    [SerializeField, Min(0.1f)] private float duration = 2.5f;
    [SerializeField] private bool showNeutral;
    [SerializeField, Min(0.01f)] private float minimumStateChange = 1f;
    [SerializeField, Min(0f)] private float strongStateChange = 12f;
    [SerializeField] private ExpressionStyle[] styles =
    {
        new ExpressionStyle("neutral", ":|", Color.white),
        new ExpressionStyle("joy", ":D", new Color(1f, 0.9f, 0.35f)),
        new ExpressionStyle("warmth", "(^^)", new Color(1f, 0.85f, 0.6f)),
        new ExpressionStyle("relief", "(^_^)", new Color(0.7f, 1f, 0.7f)),
        new ExpressionStyle("pride", "B-)", new Color(1f, 0.85f, 0.45f)),
        new ExpressionStyle("curiosity", "?_?", new Color(0.7f, 0.9f, 1f)),
        new ExpressionStyle("surprise", ":O", new Color(1f, 0.85f, 0.55f)),
        new ExpressionStyle("confusion", "o_O", new Color(0.8f, 0.8f, 1f)),
        new ExpressionStyle("worry", ":-/", new Color(0.75f, 0.85f, 1f)),
        new ExpressionStyle("sadness", ":(", new Color(0.65f, 0.8f, 1f)),
        new ExpressionStyle("hurt", "T_T", new Color(0.65f, 0.75f, 1f)),
        new ExpressionStyle("frustration", ">:/", new Color(1f, 0.7f, 0.4f)),
        new ExpressionStyle("anger", ">:(", new Color(1f, 0.5f, 0.45f)),
        new ExpressionStyle("embarrassment", "^^;", new Color(1f, 0.75f, 0.8f)),
        new ExpressionStyle("determination", ">:)", new Color(1f, 0.8f, 0.45f))
    };

    private float previousAnger;
    private float previousPlayerTrust;
    private float previousTeammateTrust;
    private float hideAt;
    private RectTransform bubbleRect;
    private bool showing;

    private void Awake() => bubbleRect = (RectTransform)transform;

    private void OnEnable()
    {
        showing = false;
        SetVisible(false);
        RememberState();
        if (state != null) state.Changed += OnStateChanged;
        if (director != null) director.OnEvaluated += OnEvaluated;
    }

    private void OnDisable()
    {
        if (state != null) state.Changed -= OnStateChanged;
        if (director != null) director.OnEvaluated -= OnEvaluated;
        SetVisible(false);
    }

    [ContextMenu("Preview Joy Bubble (Play Mode)")]
    private void PreviewJoyBubble()
    {
        if (Application.isPlaying) Show("joy");
    }

    private void LateUpdate()
    {
        if (showing && Time.unscaledTime >= hideAt) showing = false;
        if (background == null || faceText == null) return;
        if (member == null || !member.gameObject.activeInHierarchy || viewer == null
            || canvasRect == null || bubbleRect == null)
        {
            SetVisible(false);
            return;
        }
        Vector3 point = viewer.WorldToViewportPoint(member.transform.position + worldOffset);
        bool onScreen = point.z > 0f && point.x >= 0f && point.x <= 1f
            && point.y >= 0f && point.y <= 1f;
        if (onScreen)
        {
            Rect cameraRect = viewer.rect;
            Vector2 anchor = new Vector2(cameraRect.x + point.x * cameraRect.width,
                cameraRect.y + point.y * cameraRect.height);
            bubbleRect.anchorMin = anchor;
            bubbleRect.anchorMax = anchor;
            bubbleRect.anchoredPosition = Vector2.zero;
        }
        SetVisible(showing && onScreen);
    }

    private void SetVisible(bool value)
    {
        if (background != null) background.enabled = value;
        if (faceText != null) faceText.enabled = value;
    }

    private void RememberState()
    {
        if (state == null) return;
        previousAnger = state.Anger;
        previousPlayerTrust = state.TrustInPlayer;
        previousTeammateTrust = state.TrustInTeammate;
    }

    private void OnStateChanged()
    {
        if (state == null) return;
        float angerChange = state.Anger - previousAnger;
        float playerTrustChange = state.TrustInPlayer - previousPlayerTrust;
        float teammateTrustChange = state.TrustInTeammate - previousTeammateTrust;
        float trustChange = Mathf.Abs(playerTrustChange) >= Mathf.Abs(teammateTrustChange)
            ? playerTrustChange : teammateTrustChange;
        RememberState();

        float min = Mathf.Max(0.01f, minimumStateChange);
        float strong = Mathf.Max(min, strongStateChange);
        if (angerChange >= strong) Show("anger");
        else if (angerChange >= min) Show("frustration");
        else if (trustChange <= -strong) Show("hurt");
        else if (trustChange <= -min) Show("sadness");
        else if (angerChange <= -strong && trustChange >= min) Show("joy");
        else if (angerChange <= -min) Show("relief");
        else if (trustChange >= strong) Show("joy");
        else if (trustChange >= min) Show("warmth");
    }

    private void OnEvaluated(TeamEmotionUpdate[] updates)
    {
        if (member == null || updates == null) return;
        foreach (TeamEmotionUpdate update in updates)
            if (update != null && update.npc_id == member.SpeakerId)
            {
                Show(update.emotion);
                return;
            }
    }

    private void Show(string emotion)
    {
        if (string.IsNullOrEmpty(emotion) || (emotion == "neutral" && !showNeutral)
            || background == null || faceText == null || styles == null) return;
        foreach (ExpressionStyle style in styles)
        {
            if (style == null || style.emotion != emotion || string.IsNullOrWhiteSpace(style.face)) continue;
            faceText.text = style.face;
            faceText.color = style.color;
            hideAt = Time.unscaledTime + duration;
            showing = true;
            return;
        }
    }
}
