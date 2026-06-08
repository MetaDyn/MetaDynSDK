using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Simple Player Lip Sync for AvatarSDK avatars
/// Only blend shapes - no animator, no AI integration
/// Works with any AudioSource
/// </summary>
public class AvatarSdkPlayerLipSync : MonoBehaviour
{
    [Header("Audio Source")]
    [Tooltip("AudioSource to monitor (player voice, microphone, etc.)")]
    public AudioSource audioSource;
    
    [Header("Mesh References")]
    [Tooltip("AvatarHead mesh")]
    public SkinnedMeshRenderer headMesh;
    
    [Tooltip("AvatarTeethLower mesh (optional)")]
    public SkinnedMeshRenderer teethMesh;
    
    [Header("Lip Sync Settings")]
    [Range(0f, 1f)]
    [Tooltip("Smoothness transizioni visemi")]
    public float smoothness = 0.1f;
    
    [Range(0f, 100f)]
    [Tooltip("Intensità visemi")]
    public float visemeIntensity = 50f;
    
    [Range(0f, 50f)]
    [Tooltip("Jaw open intensity")]
    public float jawIntensity = 10f;
    
    [Header("Facial Expressions")]
    [Tooltip("Abilita espressioni facciali automatiche")]
    public bool enableExpressions = true;
    
    [Range(0f, 100f)]
    public float blinkIntensity = 100f;
    
    [Range(2f, 10f)]
    public float blinkFrequency = 4f;
    
    [Range(0f, 100f)]
    public float browRaiseIntensity = 20f;
    
    [Range(5f, 20f)]
    public float browRaiseFrequency = 12f;
    
    [Range(0f, 100f)]
    public float subtleSmileIntensity = 15f;
    
    [Range(5f, 20f)]
    public float subtleSmileFrequency = 10f;
    
    [Range(0f, 100f)]
    public float eyeSquintIntensity = 20f;
    
    [Range(5f, 20f)]
    public float eyeSquintFrequency = 15f;
    
    [Header("Natural Mouth Movements (during talking)")]
    [Tooltip("Abilita micro-movimenti bocca durante parlato")]
    public bool enableMouthMovements = true;
    
    [Range(0f, 50f)]
    public float mouthStretchIntensity = 15f;
    
    [Range(0f, 50f)]
    public float mouthPuckerIntensity = 12f;
    
    [Range(0f, 50f)]
    public float mouthLateralIntensity = 10f;
    
    [Range(0.5f, 5f)]
    [Tooltip("Frequenza cambio espressione bocca")]
    public float mouthExpressionChangeFrequency = 2f;
    
    // Blend shape indices cache
    private Dictionary<string, int> headBlendShapeIndices = new Dictionary<string, int>();
    private Dictionary<string, int> teethBlendShapeIndices = new Dictionary<string, int>();
    private int jawOpenHeadIndex = -1;
    private int jawOpenTeethIndex = -1;
    
    // Runtime state
    private bool isSpeaking = false;
    private bool webRTCControlled = false;
    private Coroutine lipSyncCoroutine;
    private Coroutine expressionsCoroutine;
    private System.Random random;
    
    // Natural mouth movements
    private float mouthExpressionTimer = 0f;
    private float currentStretch = 0f;
    private float targetStretch = 0f;
    private float currentPucker = 0f;
    private float targetPucker = 0f;
    private float currentLateral = 0f;
    private float targetLateral = 0f;
    
    void Start()
    {
        random = new System.Random();
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
        CacheBlendShapeIndices();
        
        // Start facial expressions
        if (enableExpressions)
        {
            expressionsCoroutine = StartCoroutine(RunFacialExpressions());
        }
    }
    
    void CacheBlendShapeIndices()
    {
        // AvatarSDK viseme names
        string[] visemeNames = new string[]
        {
            "CH", "DD", "E", "FF", "PP", "RR", "SS", "TH",
            "aa", "ih", "kk", "nn", "oh", "ou"
        };
        
        foreach (var visemeName in visemeNames)
        {
            int headIndex = headMesh.sharedMesh.GetBlendShapeIndex(visemeName);
            if (headIndex >= 0)
            {
                headBlendShapeIndices[visemeName] = headIndex;
            }
            
            if (teethMesh != null)
            {
                int teethIndex = teethMesh.sharedMesh.GetBlendShapeIndex(visemeName);
                if (teethIndex >= 0)
                {
                    teethBlendShapeIndices[visemeName] = teethIndex;
                }
            }
        }
        
        // Cache jaw open
        jawOpenHeadIndex = headMesh.sharedMesh.GetBlendShapeIndex("jawOpen");
        if (teethMesh != null)
        {
            jawOpenTeethIndex = teethMesh.sharedMesh.GetBlendShapeIndex("jawOpen");
        }
    }
    
    void OnDestroy()
    {
        if (lipSyncCoroutine != null)
        {
            StopCoroutine(lipSyncCoroutine);
        }
        
        if (expressionsCoroutine != null)
        {
            StopCoroutine(expressionsCoroutine);
        }
    }
    
    void Update()
    {
        // Monitor AudioSource (only if assigned AND not controlled by WebRTC)
        if (audioSource != null && !webRTCControlled)
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
    
    #region Lip Sync Control

    /// <summary>
    /// PUBLIC: Called by WebRTCAudioReceiver when remote player starts speaking
    /// </summary>
    public void StartSpeaking()
    {
        webRTCControlled = true;
        StartLipSync();
    }

    /// <summary>
    /// PUBLIC: Called by WebRTCAudioReceiver when remote player stops speaking
    /// </summary>
    public void StopSpeaking()
    {
        webRTCControlled = false;
        StopLipSync();
    }

    void StartLipSync()
    {
        if (isSpeaking) return;

        isSpeaking = true;
        lipSyncCoroutine = StartCoroutine(AnimateLipSyncContinuous());
    }

    void StopLipSync()
    {
        if (!isSpeaking) return;

        isSpeaking = false;

        if (lipSyncCoroutine != null)
        {
            StopCoroutine(lipSyncCoroutine);
            lipSyncCoroutine = null;
        }

        StartCoroutine(ResetAllBlendShapes());
    }

    #endregion
    
    #region Procedural Lip Sync
    
    IEnumerator AnimateLipSyncContinuous()
    {
        float visemeChangeRate = 0.15f;
        float nextVisemeChange = Time.time;
        
        string[] vowels = { "aa", "E", "ih", "oh", "ou" };
        string[] consonants = { "PP", "FF", "DD", "kk", "SS", "CH", "TH", "RR", "nn" };
        
        string currentViseme = "";
        string targetViseme = "";
        float currentWeight = 0f;
        float targetWeight = 0f;
        float currentJaw = 0f;
        float targetJaw = 0f;
        
        mouthExpressionTimer = 0f;
        GenerateNewMouthExpression();
        
        while (isSpeaking)
        {
            // Change viseme periodically
            if (Time.time >= nextVisemeChange)
            {
                nextVisemeChange = Time.time + visemeChangeRate;
                
                bool useVowel = random.Next(0, 100) > 40;
                
                if (useVowel)
                {
                    targetViseme = vowels[random.Next(0, vowels.Length)];
                    targetWeight = visemeIntensity;
                    targetJaw = jawIntensity;
                }
                else
                {
                    targetViseme = consonants[random.Next(0, consonants.Length)];
                    targetWeight = visemeIntensity * 0.8f;
                    targetJaw = jawIntensity * 0.5f;
                }
            }
            
            // Change mouth expression periodically
            if (enableMouthMovements)
            {
                mouthExpressionTimer += Time.deltaTime;
                if (mouthExpressionTimer >= mouthExpressionChangeFrequency)
                {
                    mouthExpressionTimer = 0f;
                    GenerateNewMouthExpression();
                }
            }
            
            // Smooth interpolation
            currentWeight = Mathf.Lerp(currentWeight, targetWeight, Time.deltaTime / smoothness);
            currentJaw = Mathf.Lerp(currentJaw, targetJaw, Time.deltaTime / smoothness);
            
            // Reset previous viseme
            if (currentViseme != targetViseme && !string.IsNullOrEmpty(currentViseme))
            {
                SetBlendShapeWeight(currentViseme, 0f);
                currentViseme = targetViseme;
            }
            else if (string.IsNullOrEmpty(currentViseme))
            {
                currentViseme = targetViseme;
            }
            
            // Apply current viseme
            SetBlendShapeWeight(currentViseme, currentWeight);
            
            // Apply jaw
            if (jawOpenHeadIndex >= 0)
            {
                headMesh.SetBlendShapeWeight(jawOpenHeadIndex, currentJaw);
            }
            if (teethMesh != null && jawOpenTeethIndex >= 0)
            {
                teethMesh.SetBlendShapeWeight(jawOpenTeethIndex, currentJaw);
            }
            
            // Apply natural mouth movements
            if (enableMouthMovements)
            {
                AnimateMouthMovements();
            }
            
            yield return null;
        }
    }
    
    void GenerateNewMouthExpression()
    {
        // Generate random mouth expression targets
        targetStretch = Random.value > 0.6f ? Random.Range(mouthStretchIntensity * 0.4f, mouthStretchIntensity) : 0f;
        
        // Don't combine stretch and pucker
        if (targetStretch < 5f)
        {
            targetPucker = Random.value > 0.5f ? Random.Range(mouthPuckerIntensity * 0.3f, mouthPuckerIntensity) : 0f;
        }
        else
        {
            targetPucker = 0f;
        }
        
        // Lateral movement: -1 = left, 0 = center, 1 = right
        int direction = Random.Range(-1, 2);
        if (direction == 0)
        {
            targetLateral = 0f;
        }
        else
        {
            targetLateral = direction * Random.Range(mouthLateralIntensity * 0.3f, mouthLateralIntensity);
        }
    }
    
    void AnimateMouthMovements()
    {
        // Smooth interpolation
        currentStretch = Mathf.Lerp(currentStretch, targetStretch, Time.deltaTime / (smoothness * 2f));
        currentPucker = Mathf.Lerp(currentPucker, targetPucker, Time.deltaTime / (smoothness * 2f));
        currentLateral = Mathf.Lerp(currentLateral, targetLateral, Time.deltaTime / (smoothness * 2f));
        
        // Apply stretch
        ApplyBlendShape("mouthStretchLeft", currentStretch);
        ApplyBlendShape("mouthStretchRight", currentStretch);
        
        // Apply pucker
        ApplyBlendShape("mouthPucker", currentPucker);
        
        // Apply lateral (left/right)
        if (currentLateral < 0)
        {
            ApplyBlendShape("mouthLeft", Mathf.Abs(currentLateral));
            ApplyBlendShape("mouthRight", 0f);
        }
        else
        {
            ApplyBlendShape("mouthLeft", 0f);
            ApplyBlendShape("mouthRight", currentLateral);
        }
    }
    
    #endregion
    
    #region Blend Shape Helpers
    
    void SetBlendShapeWeight(string shapeName, float weight)
    {
        if (headBlendShapeIndices.ContainsKey(shapeName))
        {
            headMesh.SetBlendShapeWeight(headBlendShapeIndices[shapeName], weight);
        }
        
        if (teethMesh != null && teethBlendShapeIndices.ContainsKey(shapeName))
        {
            teethMesh.SetBlendShapeWeight(teethBlendShapeIndices[shapeName], weight);
        }
    }
    
    void ApplyBlendShape(string shapeName, float weight)
    {
        int headIndex = headMesh.sharedMesh.GetBlendShapeIndex(shapeName);
        if (headIndex >= 0)
        {
            headMesh.SetBlendShapeWeight(headIndex, weight);
        }
        
        if (teethMesh != null)
        {
            int teethIndex = teethMesh.sharedMesh.GetBlendShapeIndex(shapeName);
            if (teethIndex >= 0)
            {
                teethMesh.SetBlendShapeWeight(teethIndex, weight);
            }
        }
    }
    
    IEnumerator ResetAllBlendShapes()
    {
        float resetTime = 0f;
        float resetDuration = 0.3f;
        
        // Get current weights
        Dictionary<string, float> startWeights = new Dictionary<string, float>();
        foreach (var kvp in headBlendShapeIndices)
        {
            startWeights[kvp.Key] = headMesh.GetBlendShapeWeight(kvp.Value);
        }
        
        float startJaw = jawOpenHeadIndex >= 0 ? headMesh.GetBlendShapeWeight(jawOpenHeadIndex) : 0f;
        float startStretch = currentStretch;
        float startPucker = currentPucker;
        float startLateral = currentLateral;
        
        while (resetTime < resetDuration)
        {
            resetTime += Time.deltaTime;
            float t = resetTime / resetDuration;
            
            // Reset visemes
            foreach (var kvp in startWeights)
            {
                float weight = Mathf.Lerp(kvp.Value, 0f, t);
                SetBlendShapeWeight(kvp.Key, weight);
            }
            
            // Reset jaw
            float jawWeight = Mathf.Lerp(startJaw, 0f, t);
            if (jawOpenHeadIndex >= 0)
            {
                headMesh.SetBlendShapeWeight(jawOpenHeadIndex, jawWeight);
            }
            if (teethMesh != null && jawOpenTeethIndex >= 0)
            {
                teethMesh.SetBlendShapeWeight(jawOpenTeethIndex, jawWeight);
            }
            
            // Reset mouth movements
            if (enableMouthMovements)
            {
                currentStretch = Mathf.Lerp(startStretch, 0f, t);
                currentPucker = Mathf.Lerp(startPucker, 0f, t);
                currentLateral = Mathf.Lerp(startLateral, 0f, t);
                
                ApplyBlendShape("mouthStretchLeft", currentStretch);
                ApplyBlendShape("mouthStretchRight", currentStretch);
                ApplyBlendShape("mouthPucker", currentPucker);
                
                if (currentLateral < 0)
                {
                    ApplyBlendShape("mouthLeft", Mathf.Abs(currentLateral));
                    ApplyBlendShape("mouthRight", 0f);
                }
                else
                {
                    ApplyBlendShape("mouthLeft", 0f);
                    ApplyBlendShape("mouthRight", currentLateral);
                }
            }
            
            yield return null;
        }
        
        // Final reset
        currentStretch = 0f;
        currentPucker = 0f;
        currentLateral = 0f;
        targetStretch = 0f;
        targetPucker = 0f;
        targetLateral = 0f;
    }
    
    #endregion
    
    #region Facial Expressions
    
    IEnumerator RunFacialExpressions()
    {
        float nextBlink = Time.time + blinkFrequency;
        float nextBrowRaise = Time.time + browRaiseFrequency;
        float nextSmile = Time.time + subtleSmileFrequency;
        float nextSquint = Time.time + eyeSquintFrequency;
        
        while (true)
        {
            float currentTime = Time.time;
            
            // Blink (always)
            if (currentTime >= nextBlink)
            {
                StartCoroutine(PlayBlink());
                nextBlink = currentTime + blinkFrequency + Random.Range(-1f, 1f);
            }
            
            // Brow raise (occasional)
            if (currentTime >= nextBrowRaise)
            {
                StartCoroutine(PlayBrowRaise());
                nextBrowRaise = currentTime + browRaiseFrequency + Random.Range(-2f, 2f);
            }
            
            // Subtle smile (occasional)
            if (currentTime >= nextSmile)
            {
                StartCoroutine(PlaySubtleSmile());
                nextSmile = currentTime + subtleSmileFrequency + Random.Range(-2f, 2f);
            }
            
            // Eye squint (occasional)
            if (currentTime >= nextSquint)
            {
                StartCoroutine(PlayEyeSquint());
                nextSquint = currentTime + eyeSquintFrequency + Random.Range(-3f, 3f);
            }
            
            yield return new WaitForSeconds(0.5f);
        }
    }
    
    IEnumerator PlayBlink()
    {
        int blinkLeftIndex = headMesh.sharedMesh.GetBlendShapeIndex("eyeBlinkLeft");
        int blinkRightIndex = headMesh.sharedMesh.GetBlendShapeIndex("eyeBlinkRight");
        
        if (blinkLeftIndex < 0 || blinkRightIndex < 0) yield break;
        
        float duration = 0.15f;
        float elapsed = 0f;
        
        // Close
        while (elapsed < duration / 2f)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / (duration / 2f);
            float weight = Mathf.Lerp(0f, blinkIntensity, t);
            
            headMesh.SetBlendShapeWeight(blinkLeftIndex, weight);
            headMesh.SetBlendShapeWeight(blinkRightIndex, weight);
            
            yield return null;
        }
        
        // Open
        elapsed = 0f;
        while (elapsed < duration / 2f)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / (duration / 2f);
            float weight = Mathf.Lerp(blinkIntensity, 0f, t);
            
            headMesh.SetBlendShapeWeight(blinkLeftIndex, weight);
            headMesh.SetBlendShapeWeight(blinkRightIndex, weight);
            
            yield return null;
        }
        
        headMesh.SetBlendShapeWeight(blinkLeftIndex, 0f);
        headMesh.SetBlendShapeWeight(blinkRightIndex, 0f);
    }
    
    IEnumerator PlayBrowRaise()
    {
        int browIndex = headMesh.sharedMesh.GetBlendShapeIndex("browInnerUp");
        if (browIndex < 0) yield break;
        
        float duration = 0.5f;
        float elapsed = 0f;
        
        // Raise
        while (elapsed < duration / 2f)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / (duration / 2f);
            float weight = Mathf.Lerp(0f, browRaiseIntensity, t);
            
            headMesh.SetBlendShapeWeight(browIndex, weight);
            
            yield return null;
        }
        
        // Lower
        elapsed = 0f;
        while (elapsed < duration / 2f)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / (duration / 2f);
            float weight = Mathf.Lerp(browRaiseIntensity, 0f, t);
            
            headMesh.SetBlendShapeWeight(browIndex, weight);
            
            yield return null;
        }
        
        headMesh.SetBlendShapeWeight(browIndex, 0f);
    }
    
    IEnumerator PlaySubtleSmile()
    {
        int smileLeftIndex = headMesh.sharedMesh.GetBlendShapeIndex("mouthSmileLeft");
        int smileRightIndex = headMesh.sharedMesh.GetBlendShapeIndex("mouthSmileRight");
        
        if (smileLeftIndex < 0 || smileRightIndex < 0) yield break;
        
        float duration = 1.2f;
        float elapsed = 0f;
        
        // Smile
        while (elapsed < duration / 2f)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / (duration / 2f);
            float weight = Mathf.Lerp(0f, subtleSmileIntensity, t);
            
            headMesh.SetBlendShapeWeight(smileLeftIndex, weight);
            headMesh.SetBlendShapeWeight(smileRightIndex, weight);
            
            yield return null;
        }
        
        // Relax
        elapsed = 0f;
        while (elapsed < duration / 2f)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / (duration / 2f);
            float weight = Mathf.Lerp(subtleSmileIntensity, 0f, t);
            
            headMesh.SetBlendShapeWeight(smileLeftIndex, weight);
            headMesh.SetBlendShapeWeight(smileRightIndex, weight);
            
            yield return null;
        }
        
        headMesh.SetBlendShapeWeight(smileLeftIndex, 0f);
        headMesh.SetBlendShapeWeight(smileRightIndex, 0f);
    }
    
    IEnumerator PlayEyeSquint()
    {
        int squintLeftIndex = headMesh.sharedMesh.GetBlendShapeIndex("eyeSquintLeft");
        int squintRightIndex = headMesh.sharedMesh.GetBlendShapeIndex("eyeSquintRight");
        
        if (squintLeftIndex < 0 || squintRightIndex < 0) yield break;
        
        float duration = 0.6f;
        float elapsed = 0f;
        
        // Squint
        while (elapsed < duration / 2f)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / (duration / 2f);
            float weight = Mathf.Lerp(0f, eyeSquintIntensity, t);
            
            headMesh.SetBlendShapeWeight(squintLeftIndex, weight);
            headMesh.SetBlendShapeWeight(squintRightIndex, weight);
            
            yield return null;
        }
        
        // Relax
        elapsed = 0f;
        while (elapsed < duration / 2f)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / (duration / 2f);
            float weight = Mathf.Lerp(eyeSquintIntensity, 0f, t);
            
            headMesh.SetBlendShapeWeight(squintLeftIndex, weight);
            headMesh.SetBlendShapeWeight(squintRightIndex, weight);
            
            yield return null;
        }
        
        headMesh.SetBlendShapeWeight(squintLeftIndex, 0f);
        headMesh.SetBlendShapeWeight(squintRightIndex, 0f);
    }
    
    #endregion
}