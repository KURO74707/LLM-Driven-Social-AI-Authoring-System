using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class NPCStateUI : MonoBehaviour
{
    [SerializeField] private NPCState state;
    [SerializeField] private Image angerFill;
    [SerializeField] private Image trustFill;
    [SerializeField] private TMP_Text angerValue;
    [SerializeField] private TMP_Text trustValue;
    [SerializeField] private Image teammateTrustFill;
    [SerializeField] private TMP_Text teammateTrustValue;

    private void OnEnable()
    {
        if (state != null) state.Changed += Refresh;
        Refresh();
    }
    private void OnDisable()
    {
        if (state != null) state.Changed -= Refresh;
    }
    public void Refresh()
    {
        if (state == null) return;
        if (angerFill != null) angerFill.fillAmount = state.Anger / 100;
        if (trustFill != null) trustFill.fillAmount = state.TrustInPlayer / 100;
        if (angerValue != null) angerValue.text = state.Anger.ToString("0.#", CultureInfo.InvariantCulture);
        if (trustValue != null) trustValue.text = state.TrustInPlayer.ToString("0.#", CultureInfo.InvariantCulture);
        if (teammateTrustFill != null) teammateTrustFill.fillAmount = state.TrustInTeammate / 100;
        if (teammateTrustValue != null) teammateTrustValue.text = state.TrustInTeammate.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
