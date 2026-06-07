using UnityEngine;
using UnityEngine.UI;
using TMPro;

// One hanging scoreboard panel. ArenaHUD finds every ScoreboardFace in the scene and
// pushes the same values to all of them, so the player sees a board whichever way they face.
public class ScoreboardFace : MonoBehaviour
{
    [SerializeField] private Image healthFill;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private TMP_Text waveText;
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text ammoText;
    [SerializeField] private GameObject gameOverPanel;

    void Awake() { if (gameOverPanel != null) gameOverPanel.SetActive(false); }

    public void SetHealth(float h)
    {
        if (healthFill != null)
        {
            healthFill.fillAmount = h;
            healthFill.color = Color.Lerp(new Color(0.9f, 0.1f, 0.1f), new Color(0.2f, 0.85f, 0.2f), h);
        }
        if (healthText != null) healthText.text = Mathf.RoundToInt(h * 100f) + "%";
    }

    public void SetWave(int w) { if (waveText != null) waveText.text = "DALGA " + w; }
    public void SetScore(int s) { if (scoreText != null) scoreText.text = "SKOR " + s; }
    public void SetAmmo(string a) { if (ammoText != null) ammoText.text = a; }
    public void SetGameOver(bool v) { if (gameOverPanel != null) gameOverPanel.SetActive(v); }
}
