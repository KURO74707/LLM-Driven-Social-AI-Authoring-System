using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

/// <summary>Updates the UI objects already placed in the scene.</summary>
public class QH_RoundUI : MonoBehaviour
{
    [SerializeField] private QH_NPCGroup group;
    [SerializeField] private QH_BoxPush boxPush;
    [SerializeField] private QH_RandomMapGenerator mapGenerator;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private TMP_Text pusherCountText;
    [SerializeField] private TMP_Text mapInfoText;
    [Tooltip("Use only in the integrated scene to reset conversation, emotions, physics and layout together.")]
    [SerializeField] private bool reloadSceneOnReset;
    [SerializeField] private BothW10MovingBridge teammateBridge;
    [SerializeField] private BothW10MovingBridge boxBridge;

    private string StartMessage => teammateBridge == null
        ? "Choose an intention for each NPC."
        : "Get Sam across, then push the box to the goal.";

    private void Start()
    {
        SetMessage(StartMessage);
    }

    private void Update()
    {
        if (pusherCountText != null && boxPush != null)
            pusherCountText.text = "NPCs pushing box: " + boxPush.ActivePushers;

        if (mapInfoText != null && mapGenerator != null)
        {
            if (teammateBridge != null && boxBridge != null)
                mapInfoText.text = mapGenerator.CurrentLayoutName +
                    "\nSam bridge " + teammateBridge.Status + " | Box bridge " + boxBridge.Status;
            else
                mapInfoText.text = "Seed: " + mapGenerator.CurrentSeed +
                    "\nNavMesh build: " + mapGenerator.LastBuildMilliseconds.ToString("F1") + " ms";
        }
    }

    public void SetMessage(string newMessage)
    {
        if (messageText != null)
            messageText.text = newMessage;
    }

    public void ResetRound()
    {
        if (reloadSceneOnReset && Application.isPlaying)
        {
            SceneManager.LoadScene(gameObject.scene.name);
            return;
        }
        if (boxPush != null) boxPush.ResetBox();
        if (mapGenerator != null) mapGenerator.GenerateMap();
        if (group != null)
        {
            foreach (QH_NPCNavigation npc in group.Npcs)
            {
                if (npc != null) npc.ResetNpc();
            }
        }
        SetMessage(StartMessage);
    }
}
