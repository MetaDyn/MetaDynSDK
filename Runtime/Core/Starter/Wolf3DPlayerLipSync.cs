using UnityEngine;
using System.Collections;

/// <summary>
/// Simple Player Lip Sync for ReadyPlayerMe avatars
/// Only blink and mouth open/close
/// </summary>
public class Wolf3DPlayerLipSync : MonoBehaviour
{
    [Header("Audio Source")]
    [Tooltip("AudioSource to monitor (player voice, microphone, etc.)")]
    public AudioSource audioSource;
    
    [Header("Mesh References")]
    [Tooltip("Avatar head mesh (contains eyesClosed and mouthOpen)")]
    public SkinnedMeshRenderer headMesh;
    
    [Tooltip("Avatar teeth mesh (optional - also has mouthOpen)")]
    public SkinnedMeshRenderer teethMesh;
    
    [Header("Blink Settings")]
    [Range(2f, 10f)]
    [Tooltip("Frequenza blink (secondi)")]
    public float blinkFrequency = 4f;
    
    // Blend shape indices
    private int eyesClosedIndex = -1;
    private int mouthOpenHeadIndex = -1;
    private int mouthOpenTeethIndex = -1;
    
    // Runtime state
    private bool isSpeaking = false;
    private Coroutine lipSyncCoroutine;
    private Coroutine blinkCoroutine;
    
    void Start()
    {
        Initialize();
    }
    
    void Initialize()
    {
        if (headMesh == null)
        {
            Debug.LogError("[PlayerLipSync] Head mesh not assigned!");
            enabled = false;
            return;
        }
        
        // Cache blend shape indices
        eyesClosedIndex = headMesh.sharedMesh.GetBlendShapeIndex("eyesClosed");
        mouthOpenHeadIndex = headMesh.sharedMesh.GetBlendShapeIndex("mouthOpen");
        
        Debug.Log($"[LipSync] eyesClosed index: {eyesClosedIndex}");
        Debug.Log($"[LipSync] mouthOpen index: {mouthOpenHeadIndex}");
        Debug.Log($"[LipSync] Total blend shapes: {headMesh.sharedMesh.blendShapeCount}");
        
        // List all blend shapes
        for (int i = 0; i < headMesh.sharedMesh.blendShapeCount; i++)
        {
            Debug.Log($"[LipSync] Blend shape {i}: {headMesh.sharedMesh.GetBlendShapeName(i)}");
        }
        
        if (teethMesh != null)
        {
            mouthOpenTeethIndex = teethMesh.sharedMesh.GetBlendShapeIndex("mouthOpen");
            Debug.Log($"[LipSync] Teeth mouthOpen index: {mouthOpenTeethIndex}");
        }
        
        // Start blink loop
        blinkCoroutine = StartCoroutine(BlinkLoop());
    }
    
    void OnDestroy()
    {
        if (lipSyncCoroutine != null)
        {
            StopCoroutine(lipSyncCoroutine);
        }
        
        if (blinkCoroutine != null)
        {
            StopCoroutine(blinkCoroutine);
        }
    }
    
    void Update()
    {
        // Monitor AudioSource (only if assigned - WebRTC uses browser audio instead)
        if (audioSource != null)
        {
            if (audioSource.isPlaying && !isSpeaking)
            {
                StartLipSync();
            }
            else if (!audioSource.isPlaying && isSpeaking)
            {
                StopLipSync();
            }
        }
    }
    
    #region Lip Sync

    /// <summary>
    /// PUBLIC: Called by WebRTCAudioReceiver when remote player starts speaking
    /// </summary>
    public void StartSpeaking()
    {
        StartLipSync();
    }

    /// <summary>
    /// PUBLIC: Called by WebRTCAudioReceiver when remote player stops speaking
    /// </summary>
    public void StopSpeaking()
    {
        StopLipSync();
    }

    void StartLipSync()
    {
        isSpeaking = true;
        lipSyncCoroutine = StartCoroutine(AnimateMouth());
    }

    void StopLipSync()
    {
        isSpeaking = false;

        if (lipSyncCoroutine != null)
        {
            StopCoroutine(lipSyncCoroutine);
            lipSyncCoroutine = null;
        }

        // Reset mouth to closed
        SetMouthOpen(0f);
    }
    
    IEnumerator AnimateMouth()
    {
        float time = 0f;
        float speed = 6f; // Velocità apertura/chiusura
        
        while (isSpeaking)
        {
            time += Time.deltaTime * speed;
            
            // Sinusoidal pattern: 0 to 1 to 0
            float value = Mathf.Abs(Mathf.Sin(time));
            
            SetMouthOpen(value);
            
            yield return null;
        }
    }
    
    void SetMouthOpen(float value)
    {
        // Value is 0-1, ReadyPlayerMe uses 0-1 (not 0-100!)
        
        if (mouthOpenHeadIndex >= 0)
        {
            headMesh.SetBlendShapeWeight(mouthOpenHeadIndex, value);
        }
        
        if (teethMesh != null && mouthOpenTeethIndex >= 0)
        {
            teethMesh.SetBlendShapeWeight(mouthOpenTeethIndex, value);
        }
    }
    
    #endregion
    
    #region Blink
    
    IEnumerator BlinkLoop()
    {
        while (true)
        {
            // Wait random time around frequency
            yield return new WaitForSeconds(blinkFrequency + Random.Range(-1f, 1f));
            
            // Perform blink
            yield return StartCoroutine(PerformBlink());
        }
    }
    
    IEnumerator PerformBlink()
    {
        if (eyesClosedIndex < 0) yield break;
        
        float duration = 0.15f;
        float elapsed = 0f;
        
        // Close eyes (0 to 1) - ReadyPlayerMe uses 0-1, not 0-100
        while (elapsed < duration / 2f)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / (duration / 2f);
            float value = Mathf.Lerp(0f, 1f, t);
            
            headMesh.SetBlendShapeWeight(eyesClosedIndex, value);
            
            yield return null;
        }
        
        // Open eyes (1 to 0)
        elapsed = 0f;
        while (elapsed < duration / 2f)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / (duration / 2f);
            float value = Mathf.Lerp(1f, 0f, t);
            
            headMesh.SetBlendShapeWeight(eyesClosedIndex, value);
            
            yield return null;
        }
        
        // Ensure fully open
        headMesh.SetBlendShapeWeight(eyesClosedIndex, 0f);
    }
    
    #endregion
}