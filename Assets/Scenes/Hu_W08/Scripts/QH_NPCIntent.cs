using UnityEngine;

/// <summary>One NPC's current choice, set by the buttons or a future AI controller.</summary>
public class QH_NPCIntent : MonoBehaviour
{
    public enum Intent
    {
        PushBox,
        AvoidBox,
        AttackOther
    }

    [SerializeField] private Intent currentIntent = Intent.PushBox;

    public Intent CurrentIntent
    {
        get { return currentIntent; }
    }
    public bool IsEliminated { get; private set; }

    public void SetPushBox()
    {
        SetIntent(Intent.PushBox);
    }

    public void SetAvoidBox()
    {
        SetIntent(Intent.AvoidBox);
    }

    public void SetAttackOther()
    {
        SetIntent(Intent.AttackOther);
    }

    public void SetIntent(Intent newIntent)
    {
        if (IsEliminated || currentIntent == newIntent) return;
        currentIntent = newIntent;
    }

    public void Eliminate()
    {
        IsEliminated = true;
    }

    public void ResetIntent()
    {
        IsEliminated = false;
        currentIntent = Intent.PushBox;
    }
}
