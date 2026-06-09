using UnityEngine;

public class FrameRateController : MonoBehaviour
{
    [Header("Performance Settings")]
    [SerializeField] private int targetFrameRate = 120;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = targetFrameRate;
    }

    void Update()
    {
        Application.targetFrameRate = targetFrameRate;
    }
}