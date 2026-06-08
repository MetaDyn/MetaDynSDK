using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;
using MetaDyn.Audio;

namespace MetaDyn.AI
{
    [Serializable]
    public class UserDetectionEvent : UnityEvent<Transform, string, bool> { }

    /// <summary>
    /// MetaDyn Voice Controller - Production Edition (WebGL Optimized)
    /// Features: OpenRouter (Gemini), Instant Interrupt, Streaming TTS, Vision, & Robust Audio Sync.
    /// </summary>
    [AddComponentMenu("MetaDyn/AI/Voice Controller")]
    public class MetaDynVoiceController : MonoBehaviour
    {
        [Header("🛡️ Service Credentials")]
        [Tooltip("Your OpenRouter API Key for brain logic.")]
        public string openRouterKey = "YOUR_OPENROUTER_KEY";

        [Tooltip("OpenAI Key for Whisper (STT).")]
        public string openAIKeyForWhisper = "YOUR_OPENAI_KEY";

        [Tooltip("ElevenLabs API Key for high-quality voice synthesis.")]
        public string elevenLabsKey = "YOUR_ELEVENLABS_API_KEY";

        [Space(10)]
        [Header("🧠 Core AI Behavior")]
        [Tooltip("Model ID: 'google/gemini-1.5-flash' (Reliable) or 'google/gemini-2.0-flash-exp:free' (Fastest)")]
        public string openRouterModel = "google/gemini-1.5-flash";

        [Tooltip("System instructions for the AI behavior. Defines persona and capabilities.")]
        [TextArea(10, 20)]
        public string systemInstruction = @"You are a helpful, concise metaverse assistant for MetaDyn. Keep responses relatively short for voice interaction.

CRITICAL: You will receive [INTERNAL CONTEXT], [MEMORY], and [SPATIAL] blocks. These are metadata for YOUR awareness only. NEVER read them aloud, quote them, or say things like 'I see in my context that...' - just USE the information naturally.

MOVEMENT CAPABILITIES:
You can move around the environment using these action tags:
- *walk_to:objectName* - Walk to a specific object (e.g., *walk_to:chair*, *walk_to:screen*)
- *follow_user* - Follow the user
- *stop_walking* - Stop moving

When the user asks you to show them something, go somewhere, or follow them, use the appropriate action tag in your response. The action tags will be executed automatically and removed from speech.

Example: 'Let me show you the chair. *walk_to:chair* I'm walking over to it now.'
Example: 'Sure, I'll follow you! *follow_user*'
Example: 'I'll stop here. *stop_walking*'";

        [Tooltip("Startup prompt to initialize conversation when the chat opens.")]
        public string startupPrompt = "Hello! I'm ready to help you.";

        [Tooltip("Goodbye message when closing the chat.")]
        public string goodbyeMessage = "Goodbye! Feel free to return anytime.";

        [Space(10)]
        [Header("👁️ Perception & Embodiment")]
        [Tooltip("Manages detection of nearby users.")]
        public AIPerceptionManager perceptionManager;
        [Tooltip("Controls the character's head tracking towards the user.")]
        public HeadLookController headLookController;
        [Tooltip("Allows the AI to 'see' the world via snapshots.")]
        public AIEye aiEye;
        [Tooltip("Handles walking and following navigation.")]
        public AIMovementController movementController;
        [Tooltip("Handles long-term user memory and relationship state.")]
        public AIMemoryManager memoryManager;

        [Space(10)]
        [Header("🎙️ Speech Recognition (STT)")]
        [Tooltip("Enable or disable voice interaction.")]
        public bool enableVoiceInput = true;
        [Tooltip("Whisper model to use for transcription.")]
        public string whisperModel = "whisper-1";
        [Tooltip("ISO code for language (e.g., 'en').")]
        public string whisperLanguage = "en";
        [Tooltip("The component that records audio from the microphone.")]
        public MicrophoneRecorder microphoneRecorder;

        [Space(10)]
        [Header("🔊 Speech Synthesis (TTS)")]
        [Tooltip("The ElevenLabs Voice ID (e.g., Rachel, Bella).")]
        public string elVoiceId = "21m00Tcm4TlvDq8ikWAM"; // Rachel
        [Tooltip("The TTS model (e.g., eleven_turbo_v2_5).")]
        public string elVoiceModel = "eleven_turbo_v2_5"; 
        [Tooltip("0 = Highest quality, 4 = Lowest latency (best for streaming).")]
        [Range(0, 4)] public int latencyOptimization = 3; 

        [Space(10)]
        [Header("🎭 Animation & Visuals")]
        [Tooltip("If enabled, applies talking/idle triggers to the animator.")]
        public bool animateAvatar = true;
        [Tooltip("Animator to control talking states.")]
        public Animator assistantAnimator;
        [Tooltip("Trigger name for the talking animation.")]
        public string talkingTrigger = "Talk";
        [Tooltip("Trigger name for the idle animation.")]
        public string idleTrigger = "Idle";

        [Space(10)]
        [Header("🖥️ UI & Feedback")]
        public TMP_InputField inputField;
        public Button sendButton;
        public Button openButton;
        public Button closeButton;
        public GameObject chatPanel;
        public GameObject chatBubblePanel;
        public TMP_Text chatBubble;
        [Tooltip("Separate text element for status messages (Listening, Thinking, Speaking). Keeps response text visible.")]
        public TMP_Text statusText;
        public Color userTextColor = Color.cyan;
        public Color chatTextColor = Color.white;
        [Tooltip("The AudioSource to play generated speech.")]
        public AudioSource audioSource;
        [Tooltip("Speed of the typewriter effect for text responses.")]
        public int charactersPerSecond = 20;

        [Space(10)]
        [Header("👤 User Interaction")]
        [Tooltip("Keywords that trigger the AI to take a visual snapshot.")]
        public List<string> visionKeywords = new List<string> { "look", "see", "watch", "read", "view", "vision", "what is that" };

        [Tooltip("If enabled, tracks which users have already been greeted this session.")]
        public bool evaluateGreetingStateInSession = true;

        [Tooltip("Automatically mark user as greeted when detected.")]
        public bool markUserAsGreetedWhenEventFires = false;

        [Tooltip("If enabled, greets a newly detected user immediately.")]
        public bool autoGreetDetectedUsers = true;

        [Tooltip("Fallback instruction for internal events.")]
        [TextArea(2, 5)]
        public string defaultInternalEventInstruction = "Respond to the latest internal event with a brief spoken response only if appropriate.";

        [Tooltip("Instruction used specifically for greeting detected users.")]
        [TextArea(2, 5)]
        public string detectedUserGreetingInstruction = "If appropriate, greet the newly detected user briefly and naturally without mentioning system events or internal context.";

        [Space(10)]
        [Header("🛰️ Status Messages")]
        public bool showStatusMessages = true;
        public string listeningMessage = "🎤 Listening...";
        public string transcribingMessage = "🔄 Transcribing...";
        public string thinkingMessage = "💭 Thinking...";
        public string speakingMessage = "🔊 Speaking...";
        public string lookingMessage = "👁️ Looking...";

        [Space(10)]
        [Header("📡 Events")]
        public UnityEvent OnTalkingStarted;
        public UnityEvent OnIdleStarted;
        public UnityEvent OnVoiceInputReceived;
        public UnityEvent OnTranscriptionCompleted;
        [Tooltip("Fired when a different active user is detected.")]
        public UnityEvent OnDetectedUserChangedSimple;
        [Tooltip("Fires when perception detects a different active user with detailed parameters.")]
        public UserDetectionEvent OnDetectedUserChanged;

        // -- Private State --
        private List<ChatMessage> _conversationHistory = new List<ChatMessage>();
        private bool isTalking = false;
        private bool isProcessingVoice = false;
        private bool _wasInputFieldFocused = false;
        
        // Audio & Stream State
        private Queue<AudioClip> _audioQueue = new Queue<AudioClip>();
        private bool _isPlayingAudio = false;
        private Dictionary<int, AudioClip> _pendingAudioClips = new Dictionary<int, AudioClip>();
        private StringBuilder _currentSentenceBuffer = new StringBuilder();
        private StringBuilder _fullResponseAccumulator = new StringBuilder();
        private int _sentenceIndexCounter = 0;
        private int _nextClipIndex = 0;
        private int _ttsGeneration = 0;
        
        // INTERRUPT TRACKER
        private Coroutine _audioCoroutine;

        // Memory tracking
        private string _currentUserId;
        private bool _memoryRecalled = false;
        private int _turnCount = 0;
        private int _lastAutoSaveTurnCount = 0;
        private Coroutine _autoSaveCoroutine;
        private readonly HashSet<string> _greetedUserIdsThisSession = new HashSet<string>();

        // Dynamic context (injected at request time, not stored in history)
        private string _currentMemoryContext;
        private string _currentGreetingHint;
        private string _pendingOneShotInternalContext;
        private string _pendingOneShotInstruction;

        // API Constants
        private const string OPENROUTER_API_URL = "https://openrouter.ai/api/v1/chat/completions";
        private const string WHISPER_API_URL = "https://api.openai.com/v1/audio/transcriptions";

        void Start()
        {
            isTalking = false;
            isProcessingVoice = false;

            // Init History
            _conversationHistory.Add(new ChatMessage { role = "system", content = systemInstruction });

            if (sendButton != null) sendButton.onClick.AddListener(OnSendButtonClicked);
            if (openButton != null) openButton.onClick.AddListener(OnOpenButtonClicked);
            if (closeButton != null) closeButton.onClick.AddListener(OnCloseButtonClicked);

            if (inputField != null)
            {
                inputField.onSubmit.AddListener((string text) =>
                {
                    if (chatPanel != null && chatPanel.activeSelf) OnSendButtonClicked();
                });
            }

            if (enableVoiceInput && microphoneRecorder != null)
            {
                microphoneRecorder.OnRecordingCompleted.AddListener(ProcessVoiceInput);
            }

            if (perceptionManager != null) perceptionManager.OnUserDetected += OnUserDetected;

            Debug.Log("[MetaDyn.Voice] Controller initialized (Production Interrupt Edition)");
        }

        private void OnUserDetected(Transform user)
        {
            if (user == null)
            {
                return;
            }

            if (headLookController != null) headLookController.SetLookTarget(user);

            ResolveDetectedUserIdentity(user, out string odisplayName, out string oduserId);
            _currentUserId = oduserId;

            bool alreadyGreetedThisSession = evaluateGreetingStateInSession && HasUserBeenGreetedThisSession(oduserId);
            OnDetectedUserChangedSimple?.Invoke();
            OnDetectedUserChanged?.Invoke(user, oduserId, alreadyGreetedThisSession);

            if (evaluateGreetingStateInSession && markUserAsGreetedWhenEventFires)
            {
                MarkUserAsGreetedThisSession(oduserId);
            }

            // Record user in memory system
            if (memoryManager != null)
            {
                memoryManager.RecordUserSeen(_currentUserId, odisplayName, (response) =>
                {
                    if (response != null)
                    {
                        Debug.Log($"[MetaDyn.Voice] Memory: User '{odisplayName}' recorded - new={response.is_new}, count={response.user?.interaction_count}");
                    }
                });

                // Reset memory recall flag for new conversation
                _memoryRecalled = false;
                _turnCount = 0;
                _lastAutoSaveTurnCount = 0;

                // Start auto-save coroutine if enabled
                if (memoryManager.autoSaveInterval > 0)
                {
                    if (_autoSaveCoroutine != null) StopCoroutine(_autoSaveCoroutine);
                    _autoSaveCoroutine = StartCoroutine(AutoSaveMemoryRoutine());
                }
            }

            if (autoGreetDetectedUsers)
            {
                TriggerGreetingForCurrentDetectedUser();
            }
        }

        /// <summary>
        /// Periodically saves conversation to memory at configured interval.
        /// Only saves if new turns have occurred since last save.
        /// </summary>
        private IEnumerator AutoSaveMemoryRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(memoryManager.autoSaveInterval);

                // Only save if there are new turns since last auto-save
                if (_turnCount > _lastAutoSaveTurnCount && _turnCount >= 2 && !string.IsNullOrEmpty(_currentUserId))
                {
                    Debug.Log($"[MetaDyn.Voice] Auto-saving conversation (turns: {_turnCount}, last save: {_lastAutoSaveTurnCount})");
                    _lastAutoSaveTurnCount = _turnCount;
                    StartCoroutine(StoreConversationWithAnalysis());
                }
            }
        }

        // --- Input Locking ---
        void Update()
        {
            if (inputField != null)
            {
                bool isCurrentlyFocused = inputField.isFocused;
                if (isCurrentlyFocused && !_wasInputFieldFocused) InputManager.LockInput("VoiceInput");
                else if (!isCurrentlyFocused && _wasInputFieldFocused) InputManager.UnlockInput("VoiceInput");
                _wasInputFieldFocused = isCurrentlyFocused;
            }
        }

        void OnDestroy()
        {
            if (_wasInputFieldFocused) InputManager.UnlockInput("VoiceInput");
            if (microphoneRecorder != null) microphoneRecorder.OnRecordingCompleted.RemoveListener(ProcessVoiceInput);
            if (perceptionManager != null) perceptionManager.OnUserDetected -= OnUserDetected;
        }

        #region INTERRUPT LOGIC

        // Call this to immediately stop all AI speech/thinking
        private void InterruptSpeech()
        {
            // 1. Hard stop the speakers
            if (audioSource != null) audioSource.Stop();
            
            // 2. Clear the pending audio
            _audioQueue.Clear();
            
            // 3. Kill the playback routine
            if (_audioCoroutine != null) 
            {
                StopCoroutine(_audioCoroutine);
                _audioCoroutine = null;
            }

            _isPlayingAudio = false;
            
            // 4. Clear any half-formed text in the buffer
            _currentSentenceBuffer.Clear(); 
        }

        #endregion

        #region UI Callbacks

        void OnOpenButtonClicked()
        {
            if (chatPanel != null) chatPanel.SetActive(true);
            if (!string.IsNullOrEmpty(startupPrompt)) StartCoroutine(ProcessUserMessage(startupPrompt, true));
        }

        void OnSendButtonClicked()
        {
            InterruptSpeech(); // <--- INTERRUPT ON CLICK

            if (inputField == null || string.IsNullOrEmpty(inputField.text)) return;
            string userMessage = inputField.text;
            inputField.text = "";
            StartCoroutine(ProcessUserMessage(userMessage));
        }

        void OnCloseButtonClicked()
        {
            InterruptSpeech(); // <--- INTERRUPT ON CLOSE

            // Store conversation summary before closing
            StoreConversationMemory();

            StopAllCoroutines();

            isTalking = false;
            isProcessingVoice = false;

            SetAnimatorTriggerSafe(idleTrigger);
            if (chatPanel != null) chatPanel.SetActive(false);
            if (chatBubblePanel != null) chatBubblePanel.SetActive(false);

            if (!string.IsNullOrEmpty(goodbyeMessage)) StartCoroutine(ProcessUserMessage(goodbyeMessage, true));
        }

        /// <summary>
        /// Store conversation summary to memory when conversation ends.
        /// </summary>
        private void StoreConversationMemory()
        {
            if (memoryManager == null || string.IsNullOrEmpty(_currentUserId) || _turnCount < 2) return;
            StartCoroutine(StoreConversationWithAnalysis());
        }

        /// <summary>
        /// Analyze conversation with Gemini and store with extracted topics/sentiment.
        /// </summary>
        private IEnumerator StoreConversationWithAnalysis()
        {
            // Build conversation summary from history
            StringBuilder summary = new StringBuilder();
            int messageCount = 0;
            for (int i = _conversationHistory.Count - 1; i >= 0 && messageCount < 6; i--)
            {
                var msg = _conversationHistory[i];
                if (msg.role == "user" || msg.role == "assistant")
                {
                    string content = msg.content;
                    if (content.Length > 100) content = content.Substring(0, 100) + "...";
                    summary.Insert(0, $"{msg.role}: {content}\n");
                    messageCount++;
                }
            }

            string conversationText = summary.ToString();

            // Get location from perception if available
            string location = null;
            if (perceptionManager != null)
            {
                location = perceptionManager.gameObject.scene.name;
            }

            // Analyze with Gemini for topics and sentiment
            string topics = null;
            string sentiment = null;

            string analysisPrompt = $@"Analyze this conversation and respond with ONLY a JSON object (no markdown, no code blocks):
{{""topics"": [""topic1"", ""topic2""], ""sentiment"": ""positive|neutral|negative""}}

Conversation:
{conversationText}";

            string analysisJson = JsonUtility.ToJson(new AnalysisRequest
            {
                model = openRouterModel,
                messages = new AnalysisMessage[]
                {
                    new AnalysisMessage { role = "user", content = analysisPrompt }
                }
            });

            using (var request = new UnityWebRequest(OPENROUTER_API_URL, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(analysisJson);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {openRouterKey}");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        // Parse response
                        string responseText = request.downloadHandler.text;
                        var response = JsonUtility.FromJson<OpenRouterResponse>(responseText);
                        if (response.choices != null && response.choices.Length > 0)
                        {
                            string analysisContent = response.choices[0].message.content;

                            // Clean up response (remove markdown code blocks if present)
                            analysisContent = analysisContent.Trim();
                            if (analysisContent.StartsWith("```"))
                            {
                                int start = analysisContent.IndexOf('{');
                                int end = analysisContent.LastIndexOf('}');
                                if (start >= 0 && end > start)
                                    analysisContent = analysisContent.Substring(start, end - start + 1);
                            }

                            var analysis = JsonUtility.FromJson<ConversationAnalysis>(analysisContent);
                            if (analysis != null)
                            {
                                topics = analysis.topics != null ? string.Join(", ", analysis.topics) : null;
                                sentiment = analysis.sentiment;
                                Debug.Log($"[MetaDyn.Voice] Analysis: topics=[{topics}], sentiment={sentiment}");
                            }
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[MetaDyn.Voice] Failed to parse analysis: {e.Message}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[MetaDyn.Voice] Analysis request failed: {request.error}");
                }
            }

            // Store to memory with extracted analysis
            memoryManager.StoreConversation(
                _currentUserId,
                conversationText,
                topics,
                sentiment,
                location,
                (success) =>
                {
                    if (success) Debug.Log($"[MetaDyn.Voice] Conversation stored with analysis - topics: {topics}, sentiment: {sentiment}");
                }
            );
        }

        // Helper classes for analysis
        [Serializable] private class AnalysisRequest { public string model; public AnalysisMessage[] messages; }
        [Serializable] private class AnalysisMessage { public string role; public string content; }
        [Serializable] private class OpenRouterResponse { public OpenRouterChoice[] choices; }
        [Serializable] private class OpenRouterChoice { public OpenRouterMessage message; }
        [Serializable] private class OpenRouterMessage { public string content; }
        [Serializable] private class ConversationAnalysis { public string[] topics; public string sentiment; }

        #endregion

        #region Voice Input (Whisper)

        public void ProcessVoiceInput(byte[] audioData)
        {
            InterruptSpeech(); // <--- INTERRUPT ON VOICE DETECTED

            if (!enableVoiceInput || isProcessingVoice || isTalking) return;
            OnVoiceInputReceived?.Invoke();
            StartCoroutine(TranscribeAudio(audioData));
        }

        IEnumerator TranscribeAudio(byte[] audioData)
        {
            isProcessingVoice = true;
            UpdateStatus(transcribingMessage);

            WWWForm form = new WWWForm();
            form.AddBinaryData("file", audioData, "audio.wav", "audio/wav");
            form.AddField("model", whisperModel);
            if (!string.IsNullOrEmpty(whisperLanguage)) form.AddField("language", whisperLanguage);

            using (UnityWebRequest www = UnityWebRequest.Post(WHISPER_API_URL, form))
            {
                www.SetRequestHeader("Authorization", $"Bearer {openAIKeyForWhisper}");
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    var response = JsonUtility.FromJson<WhisperResponse>(www.downloadHandler.text);
                    OnTranscriptionCompleted?.Invoke();
                    yield return StartCoroutine(ProcessUserMessage(response.text));
                }
                else
                {
                    Debug.LogError($"[MetaDyn.Voice] Whisper Error: {www.error}");
                    UpdateStatus("❌ Voice Error");
                }
            }
            isProcessingVoice = false;
        }

        #endregion

        #region LLM Streaming (OpenRouter)

        IEnumerator ProcessUserMessage(string userText, bool hidden = false)
        {
            if (!hidden) InterruptSpeech(); // Double check interrupt

            if (isTalking && !hidden) yield break;
            isTalking = true;
            
            if (!hidden && showStatusMessages)
            {
                if (chatBubblePanel) chatBubblePanel.SetActive(true);
                if (chatBubble) chatBubble.text = $"You: {userText}";
                if (chatBubble) chatBubble.color = userTextColor;
            }

            OnTalkingStarted?.Invoke();
            SetAnimatorTriggerSafe(talkingTrigger);

            // Track conversation turns
            _turnCount++;

            // 1. Memory Recall (first turn only - get relevant memories)
            if (memoryManager != null && !_memoryRecalled && !string.IsNullOrEmpty(_currentUserId))
            {
                _memoryRecalled = true;
                bool memoryReady = false;

                memoryManager.RecallMemories(userText, _currentUserId, (response) =>
                {
                    if (response != null)
                    {
                        // Store context for dynamic injection (not in history)
                        _currentMemoryContext = memoryManager.GetMemoryContext();
                        _currentGreetingHint = memoryManager.GetUserGreetingHint();
                    }
                    memoryReady = true;
                });

                // Wait for memory recall (with timeout)
                float timeout = 2f;
                float elapsed = 0f;
                while (!memoryReady && elapsed < timeout)
                {
                    yield return new WaitForSeconds(0.1f);
                    elapsed += 0.1f;
                }
            }

            // 2. Perception context is now injected dynamically in BuildChatJson (not stored in history)

            // 3. Vision Check (Base64)
            string base64Image = null;
            bool hasVisionIntent = IsVisionIntent(userText);
            Debug.Log($"[MetaDyn.Voice] Vision check: aiEye={aiEye != null}, hasVisionIntent={hasVisionIntent}, keywords={visionKeywords?.Count ?? 0}");

            if (aiEye != null && hasVisionIntent)
            {
                Debug.Log("[MetaDyn.Voice] Vision triggered! Capturing snapshot...");
                UpdateStatus(lookingMessage);
                byte[] imgBytes = aiEye.CaptureSnapshotBytes();
                if (imgBytes != null)
                {
                    base64Image = Convert.ToBase64String(imgBytes);
                    Debug.Log($"[MetaDyn.Voice] Snapshot captured: {base64Image.Length / 1024} KB");
                }
                else
                {
                    Debug.LogWarning("[MetaDyn.Voice] Snapshot capture returned null (cooldown or error)");
                }
            }
            else if (aiEye == null && hasVisionIntent)
            {
                Debug.LogWarning("[MetaDyn.Voice] Vision intent detected but AIEye reference is null! Assign it in inspector.");
            }

            // 3. Update History & Trim
            ChatMessage userMsg = new ChatMessage { role = "user", content = userText };
            _conversationHistory.Add(userMsg);
            TrimHistory(); // <--- Prevent Infinite Memory Growth

            UpdateStatus(thinkingMessage);

            // 4. Stream Response
            yield return StartCoroutine(StreamOpenRouterResponse(userText, base64Image, null));
        }

        IEnumerator StreamOpenRouterResponse(string originalText, string base64Image, string transientPrompt)
        {
            _currentSentenceBuffer.Clear();
            _fullResponseAccumulator.Clear();
            _audioQueue.Clear();
            _pendingAudioClips.Clear();
            _isPlayingAudio = false;
            _sentenceIndexCounter = 0;
            _nextClipIndex = 0;
            _ttsGeneration++;

            string json = BuildChatJson(openRouterModel, _conversationHistory, base64Image, transientPrompt, _pendingOneShotInternalContext);
            _pendingOneShotInternalContext = null;
            _pendingOneShotInstruction = null;

            var request = new UnityWebRequest(OPENROUTER_API_URL, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerStreamingText(OnTextChunkReceived);
            
            // --- OPENROUTER HEADERS ---
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {openRouterKey}");
            request.SetRequestHeader("HTTP-Referer", "https://metadyn.com"); 
            request.SetRequestHeader("X-Title", "MetaDyn Voice Controller"); 
            // ---------------------------

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // Note: Modified DownloadHandler now captures the error text!
                Debug.LogError($"[MetaDyn.Voice] OpenRouter Error: {request.error}\nResponse: {request.downloadHandler.text}");
                UpdateStatus("❌ API Error");
                EndTalkingToIdle();
            }
            else
            {
                // Flush remaining text
                if (_currentSentenceBuffer.Length > 0)
                {
                    string remaining = _currentSentenceBuffer.ToString().Trim();
                    if (!string.IsNullOrEmpty(remaining)) EnqueueSentenceForTTS(remaining);
                }

                string fullResponse = _fullResponseAccumulator.ToString();

                // Process action tags for movement
                ProcessActionTags(fullResponse);

                _conversationHistory.Add(new ChatMessage { role = "assistant", content = fullResponse });
            }
        }

        private void OnTextChunkReceived(string textChunk)
        {
            _fullResponseAccumulator.Append(textChunk);
            _currentSentenceBuffer.Append(textChunk);

            if (chatBubble != null && showStatusMessages)
            {
                if (_fullResponseAccumulator.Length == textChunk.Length) 
                {
                    chatBubble.text = "";
                    chatBubble.color = chatTextColor;
                }
                chatBubble.text += textChunk;
            }

            // Sentence Splitting
            string currentBuffer = _currentSentenceBuffer.ToString();
            if (Regex.IsMatch(currentBuffer, @"[.?!](\s|\n)"))
            {
                string[] sentences = Regex.Split(currentBuffer, @"(?<=[.?!])\s+");
                for (int i = 0; i < sentences.Length - 1; i++)
                {
                    string s = sentences[i].Trim();
                    if (!string.IsNullOrEmpty(s)) EnqueueSentenceForTTS(s);
                }
                _currentSentenceBuffer.Clear();
                _currentSentenceBuffer.Append(sentences[sentences.Length - 1]);
            }
        }

        #endregion

        #region TTS & Audio (ElevenLabs)

        private void EnqueueSentenceForTTS(string textToSpeak)
        {
            int sentenceIndex = _sentenceIndexCounter++;
            int generation = _ttsGeneration;
            StartCoroutine(RequestTTS(textToSpeak, sentenceIndex, generation));
        }

        IEnumerator RequestTTS(string textToSpeak, int sentenceIndex, int generation)
        {
            // CLEAN TEXT (Remove *actions*)
            string cleanText = CleanTextForTTS(textToSpeak);
            if (string.IsNullOrEmpty(cleanText) || cleanText.Length < 2) yield break;

            string url = $"https://api.elevenlabs.io/v1/text-to-speech/{elVoiceId}?optimize_streaming_latency={latencyOptimization}";

            string json = JsonUtility.ToJson(new ElevenLabsRequest 
            { 
                text = cleanText, // Use cleaned text
                model_id = elVoiceModel,
                voice_settings = new ELVoiceSettings { stability = 0.5f, similarity_boost = 0.7f }
            });

            using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.MPEG);
                www.SetRequestHeader("xi-api-key", elevenLabsKey);
                www.SetRequestHeader("Content-Type", "application/json");

                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                    if (clip != null)
                    {
                        if (generation != _ttsGeneration)
                            yield break;

                        _pendingAudioClips[sentenceIndex] = clip;
                        TryEnqueueReadyClips();
                    }
                }
                else
                {
                    Debug.LogError($"[MetaDyn.Voice] TTS Error: {www.error}");
                }
            }
        }

        private void ResolveDetectedUserIdentity(Transform user, out string displayName, out string userId)
        {
            displayName = "Unknown";
            userId = null;

            if (user == null)
            {
                return;
            }

            var ugsPlayer = user.GetComponent<global::MetaDyn.Networking.MetaDynUGSPlayerController>();
            if (ugsPlayer != null)
            {
                displayName = ugsPlayer.DisplayName;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = PlayerPrefs.GetString("PlayerName", user.gameObject.name);
                }

                userId = $"ugs_client_{ugsPlayer.OwnerClientId}";
                return;
            }

            displayName = user.gameObject.name;
            userId = $"entity_{displayName.ToLowerInvariant().Replace(" ", "_")}";
        }

        public bool HasUserBeenGreetedThisSession(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            return _greetedUserIdsThisSession.Contains(userId);
        }

        public bool HasCurrentUserBeenGreetedThisSession()
        {
            return HasUserBeenGreetedThisSession(_currentUserId);
        }

        public void MarkUserAsGreetedThisSession(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            _greetedUserIdsThisSession.Add(userId);
        }

        public void MarkCurrentUserAsGreetedThisSession()
        {
            MarkUserAsGreetedThisSession(_currentUserId);
        }

        public void ResetGreetedUsersForSession()
        {
            _greetedUserIdsThisSession.Clear();
        }

        public void TriggerGreetingForCurrentDetectedUser()
        {
            if (string.IsNullOrWhiteSpace(_currentUserId))
            {
                Debug.LogWarning("[MetaDyn.Voice] Cannot trigger greeting: no current detected user");
                return;
            }

            if (evaluateGreetingStateInSession && HasCurrentUserBeenGreetedThisSession())
            {
                Debug.Log("[MetaDyn.Voice] Greeting skipped because the current user was already greeted this session");
                return;
            }

            string internalContext =
                $"A user has just been detected in your perception. " +
                $"The active user id is '{_currentUserId}'. " +
                $"Already greeted this session: false. " +
                $"Instruction: {detectedUserGreetingInstruction}";

            TriggerInternalEventResponse(internalContext, detectedUserGreetingInstruction, markCurrentUserAsGreetedOnStart: true);
        }

        public void TriggerDetectedUserResponseFromInspector(string instruction)
        {
            if (string.IsNullOrWhiteSpace(_currentUserId))
            {
                Debug.LogWarning("[MetaDyn.Voice] Cannot trigger detected-user response: no current detected user");
                return;
            }

            if (evaluateGreetingStateInSession && HasCurrentUserBeenGreetedThisSession())
            {
                Debug.Log("[MetaDyn.Voice] Detected-user response skipped because the current user was already greeted this session");
                return;
            }

            string resolvedInstruction = string.IsNullOrWhiteSpace(instruction)
                ? detectedUserGreetingInstruction
                : instruction;

            string internalContext =
                $"A user has just been detected in your perception. " +
                $"The active user id is '{_currentUserId}'. " +
                $"Already greeted this session: false. " +
                "This was triggered by a Unity inspector event.";

            TriggerInternalEventResponse(internalContext, resolvedInstruction, markCurrentUserAsGreetedOnStart: true);
        }

        public void TriggerInternalEventResponse(string internalContext)
        {
            TriggerInternalEventResponse(internalContext, defaultInternalEventInstruction, false);
        }

        public void TriggerInternalEventResponse(string internalContext, string instruction)
        {
            TriggerInternalEventResponse(internalContext, instruction, false);
        }

        public void TriggerInternalEventResponse(string internalContext, bool markCurrentUserAsGreetedOnStart)
        {
            TriggerInternalEventResponse(internalContext, defaultInternalEventInstruction, markCurrentUserAsGreetedOnStart);
        }

        public void TriggerInternalEventResponse(string internalContext, string instruction, bool markCurrentUserAsGreetedOnStart)
        {
            if (string.IsNullOrWhiteSpace(internalContext))
            {
                Debug.LogWarning("[MetaDyn.Voice] Cannot trigger internal event: context was empty");
                return;
            }

            if (isTalking || isProcessingVoice)
            {
                Debug.Log("[MetaDyn.Voice] Internal event ignored because the controller is busy");
                return;
            }

            _pendingOneShotInternalContext = internalContext;
            _pendingOneShotInstruction = string.IsNullOrWhiteSpace(instruction)
                ? defaultInternalEventInstruction
                : instruction;

            if (markCurrentUserAsGreetedOnStart)
            {
                MarkCurrentUserAsGreetedThisSession();
            }

            StartCoroutine(ProcessInternalEventResponse());
        }

        private IEnumerator ProcessInternalEventResponse()
        {
            isTalking = true;

            OnTalkingStarted?.Invoke();
            SetAnimatorTriggerSafe(talkingTrigger);

            UpdateStatus(thinkingMessage);

            string transientPrompt = string.IsNullOrWhiteSpace(_pendingOneShotInstruction)
                ? defaultInternalEventInstruction
                : _pendingOneShotInstruction;
            yield return StartCoroutine(StreamOpenRouterResponse(null, null, transientPrompt));
        }

        private void TryEnqueueReadyClips()
        {
            while (_pendingAudioClips.TryGetValue(_nextClipIndex, out var clip))
            {
                _pendingAudioClips.Remove(_nextClipIndex);
                _audioQueue.Enqueue(clip);
                _nextClipIndex++;
            }

            // INTERRUPT FIX: Save the coroutine ref so we can stop it
            if (!_isPlayingAudio && _audioQueue.Count > 0)
                _audioCoroutine = StartCoroutine(PlayAudioQueue());
        }

        IEnumerator PlayAudioQueue()
        {
            _isPlayingAudio = true;
            UpdateStatus(speakingMessage);

            while (_audioQueue.Count > 0)
            {
                AudioClip clip = _audioQueue.Dequeue();
                
                if (audioSource != null && clip != null)
                {
                    audioSource.clip = clip;
                    audioSource.Play();

                    // FIX FOR WEBGL: 
                    // Trust the hardware, not the math. Wait while it is ACTUALLY playing.
                    yield return new WaitForSeconds(0.1f); // Brief buffer for state update
                    yield return new WaitWhile(() => audioSource.isPlaying);
                }
                else
                {
                    yield return new WaitForSeconds(0.1f);
                }
            }

            _isPlayingAudio = false;
            _audioCoroutine = null; // Clear tracking

            yield return new WaitForSeconds(0.2f); // Short buffer before idle

            if (_audioQueue.Count == 0 && !isProcessingVoice) EndTalkingToIdle();
        }

        private void EndTalkingToIdle()
        {
            isTalking = false;
            SetAnimatorTriggerSafe(idleTrigger);
            OnIdleStarted?.Invoke();
            if (showStatusMessages) UpdateStatus("");
        }

        private void SetAnimatorTriggerSafe(string triggerName)
        {
            if (!animateAvatar || assistantAnimator == null || string.IsNullOrEmpty(triggerName)) return;

            // Check if parameter exists to avoid "Parameter 'X' does not exist" warning
            foreach (AnimatorControllerParameter param in assistantAnimator.parameters)
            {
                if (param.name == triggerName)
                {
                    assistantAnimator.SetTrigger(triggerName);
                    return;
                }
            }
            
            // Only log once if missing to avoid spam
            // Debug.LogWarning($"[MetaDyn.Voice] Animator parameter '{triggerName}' not found on {assistantAnimator.name}");
        }

        private string CleanTextForTTS(string rawText)
        {
            // Remove supported action tags, but preserve emphasized words by stripping only the asterisks.
            string clean = Regex.Replace(rawText, @"\*(walk_to:[^*]+|follow_user|stop_walking|stop)\*", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\*([^*\r\n]+)\*", "$1");
            clean = Regex.Replace(clean, @"\[(INTERNAL CONTEXT|SYSTEM CONTEXT|MEMORY|SPATIAL)[^\]]*\]", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\(.*?\)", "");
            clean = Regex.Replace(clean, @"<.*?>", "");
            return Regex.Replace(clean, @"\s+", " ").Trim();
        }

        #endregion

        #region Action Tags (Movement Integration)

        /// <summary>
        /// Process action tags in LLM response and execute movement commands.
        /// Supported tags:
        /// - *walk_to:objectName* - Walk to a specific object in the environment
        /// - *follow_user* - Follow the active user
        /// - *stop_walking* - Stop all movement
        /// </summary>
        private void ProcessActionTags(string fullResponse)
        {
            if (movementController == null || string.IsNullOrEmpty(fullResponse)) return;

            // Pattern: *action* or *action:parameter*
            MatchCollection matches = Regex.Matches(fullResponse, @"\*([a-z_]+)(?::([a-z0-9_\s]+))?\*", RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                string action = match.Groups[1].Value.ToLower().Trim();
                string parameter = match.Groups.Count > 2 ? match.Groups[2].Value.Trim() : "";

                Debug.Log($"[MetaDyn VOICE] Action tag: '{action}' Param: '{parameter}'");

                switch (action)
                {
                    case "walk_to":
                        if (!string.IsNullOrEmpty(parameter))
                        {
                            Transform target = FindObjectByName(parameter);
                            if (target != null)
                            {
                                movementController.WalkToTarget(target);
                                Debug.Log($"[MetaDyn VOICE] AI walking to: {target.name}");
                            }
                        }
                        break;

                    case "follow_user":
                        if (perceptionManager != null && perceptionManager.activeUser != null)
                        {
                            movementController.FollowTarget(perceptionManager.activeUser);
                            Debug.Log("[MetaDyn VOICE] AI following user.");
                        }
                        break;

                    case "stop_walking":
                    case "stop":
                        movementController.StopMovement();
                        Debug.Log("[MetaDyn VOICE] AI stopped.");
                        break;
                }
            }
        }

        private Transform FindObjectByName(string objectName)
        {
            if (perceptionManager == null)
            {
                return null;
            }

            objectName = objectName.ToLower();

            // Search in perception radius
            float radius = perceptionManager.perceptionRadius;

            // Strategy 1: Search SDK components (Seats, Screens, Interactables)
            var seats = UnityEngine.Object.FindObjectsByType<SeatHotspot>(FindObjectsSortMode.None);
            foreach (var seat in seats)
            {
                float dist = Vector3.Distance(perceptionManager.transform.position, seat.transform.position);
                if (dist <= radius && seat.name.ToLower().Contains(objectName))
                {
                    Debug.Log($"[MetaDyn VOICE] Found seat: '{seat.name}'");
                    return seat.transform;
                }
            }

            var screens = UnityEngine.Object.FindObjectsByType<ProjectionSurface>(FindObjectsSortMode.None);
            foreach (var screen in screens)
            {
                float dist = Vector3.Distance(perceptionManager.transform.position, screen.transform.position);
                if (dist <= radius && screen.name.ToLower().Contains(objectName))
                {
                    Debug.Log($"[MetaDyn VOICE] Found screen: '{screen.name}'");
                    return screen.transform;
                }
            }

            var interactables = UnityEngine.Object.FindObjectsByType<Interactable>(FindObjectsSortMode.None);
            foreach (var interactable in interactables)
            {
                float dist = Vector3.Distance(perceptionManager.transform.position, interactable.transform.position);
                if (dist <= radius && interactable.name.ToLower().Contains(objectName))
                {
                    Debug.Log($"[MetaDyn VOICE] Found interactable: '{interactable.name}'");
                    return interactable.transform;
                }
            }

            // Strategy 2: Fallback
            GameObject[] allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var obj in allObjects)
            {
                float dist = Vector3.Distance(perceptionManager.transform.position, obj.transform.position);
                if (dist <= radius && obj.name.ToLower().Contains(objectName))
                {
                    Debug.Log($"[MetaDyn VOICE] Found object: '{obj.name}'");
                    return obj.transform;
                }
            }

            return null;
        }

        #endregion

        #region Helpers & Schemas

        private void TrimHistory()
        {
            // Keep System Prompt (0) + Last 20 messages
            if (_conversationHistory.Count > 21) _conversationHistory.RemoveAt(1);
        }

        void UpdateStatus(string message)
        {
            if (!showStatusMessages) return;

            // Use dedicated status element if available, fallback to chatBubble
            if (statusText != null)
            {
                statusText.text = message;
                statusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
            }
            else if (chatBubble != null)
            {
                // Fallback: old behavior (overwrites response text)
                chatBubble.text = message;
                chatBubble.color = Color.yellow;
            }
        }

        private bool IsVisionIntent(string message)
        {
            if (visionKeywords == null || visionKeywords.Count == 0)
            {
                Debug.LogWarning("[MetaDyn.Voice] visionKeywords list is null or empty!");
                return false;
            }

            string lower = message.ToLowerInvariant();
            foreach (var k in visionKeywords)
            {
                if (lower.Contains(k.ToLowerInvariant()))
                {
                    Debug.Log($"[MetaDyn.Voice] Vision keyword matched: '{k}' in message: '{message}'");
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Get a compact, token-efficient representation of the current spatial context.
        /// Format: "USER: Name, Xm, direction | SEATS: name(status,Xm), ... | SCREENS: ..."
        /// </summary>
        private string GetCompactPerceptionContext()
        {
            if (perceptionManager == null) return "";

            StringBuilder sb = new StringBuilder();

            // User info
            if (perceptionManager.activeUser != null)
            {
                string userName = GetDetectedUserDisplayName(perceptionManager.activeUser);
                float dist = Vector3.Distance(perceptionManager.transform.position, perceptionManager.activeUser.position);
                string dir = GetRelativeDirection(perceptionManager.activeUser.position);
                sb.Append($"USER: {userName}, {dist:F1}m, {dir}");
            }

            // Gather nearby objects
            float radius = perceptionManager.perceptionRadius;
            Vector3 aiPos = perceptionManager.transform.position;

            // Seats
            var seats = UnityEngine.Object.FindObjectsByType<SeatHotspot>(FindObjectsSortMode.None);
            List<string> seatInfo = new List<string>();
            foreach (var seat in seats)
            {
                float dist = Vector3.Distance(aiPos, seat.transform.position);
                if (dist <= radius)
                {
                    string status = seat.IsOccupied ? "taken" : "free";
                    seatInfo.Add($"{seat.name}({status},{dist:F0}m)");
                }
            }
            if (seatInfo.Count > 0)
            {
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append($"SEATS: {string.Join(", ", seatInfo)}");
            }

            // Screens
            var screens = UnityEngine.Object.FindObjectsByType<ProjectionSurface>(FindObjectsSortMode.None);
            List<string> screenInfo = new List<string>();
            foreach (var screen in screens)
            {
                float dist = Vector3.Distance(aiPos, screen.transform.position);
                if (dist <= radius)
                {
                    string status = screen.IsProjecting ? "on" : "off";
                    screenInfo.Add($"{screen.name}({status},{dist:F0}m)");
                }
            }
            if (screenInfo.Count > 0)
            {
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append($"SCREENS: {string.Join(", ", screenInfo)}");
            }

            // Interactables
            var interactables = UnityEngine.Object.FindObjectsByType<Interactable>(FindObjectsSortMode.None);
            List<string> interactInfo = new List<string>();
            foreach (var obj in interactables)
            {
                float dist = Vector3.Distance(aiPos, obj.transform.position);
                if (dist <= radius)
                {
                    interactInfo.Add($"{obj.name}({dist:F0}m)");
                }
            }
            if (interactInfo.Count > 0)
            {
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append($"OBJECTS: {string.Join(", ", interactInfo)}");
            }

            // Other players
            List<string> playerInfo = new List<string>();

            var ugsPlayers = UnityEngine.Object.FindObjectsByType<global::MetaDyn.Networking.MetaDynUGSPlayerController>(FindObjectsSortMode.None);
            foreach (var p in ugsPlayers)
            {
                if (perceptionManager.activeUser != null && p.transform == perceptionManager.activeUser) continue;
                if (p.transform == perceptionManager.transform) continue;

                float dist = Vector3.Distance(aiPos, p.transform.position);
                if (dist <= radius)
                {
                    string playerName = string.IsNullOrWhiteSpace(p.DisplayName) ? p.name : p.DisplayName;
                    playerInfo.Add($"{playerName}({dist:F0}m)");
                }
            }
            if (playerInfo.Count > 0)
            {
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append($"PLAYERS: {string.Join(", ", playerInfo)}");
            }

            return sb.ToString();
        }

        private static string GetDetectedUserDisplayName(Transform user)
        {
            if (user == null)
                return "Unknown";

            var ugsPlayer = user.GetComponent<global::MetaDyn.Networking.MetaDynUGSPlayerController>();
            if (ugsPlayer != null)
            {
                return string.IsNullOrWhiteSpace(ugsPlayer.DisplayName)
                    ? PlayerPrefs.GetString("PlayerName", user.name)
                    : ugsPlayer.DisplayName;
            }

            return user.name;
        }

        private string GetRelativeDirection(Vector3 targetPos)
        {
            Vector3 toTarget = targetPos - perceptionManager.transform.position;
            toTarget.y = 0;
            float angle = Vector3.SignedAngle(perceptionManager.transform.forward, toTarget, Vector3.up);

            if (angle > -45 && angle <= 45) return "front";
            if (angle > 45 && angle <= 135) return "right";
            if (angle > 135 || angle <= -135) return "behind";
            return "left";
        }

        private string BuildChatJson(string model, List<ChatMessage> history, string base64Image, string transientPrompt, string additionalInternalContext)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"model\": \"{model}\",");
            sb.Append($"\"stream\": true,");
            sb.Append("\"messages\": [");

            // 1. System prompt (always first, index 0 in history)
            if (history.Count > 0 && history[0].role == "system")
            {
                sb.Append($"{{\"role\": \"system\", \"content\": \"{EscapeJson(history[0].content)}\"}}");
            }

            // 2. Dynamic context injection (fresh memory + perception - NOT stored in history)
            StringBuilder contextBuilder = new StringBuilder();

            // Memory context (if available)
            if (!string.IsNullOrEmpty(_currentGreetingHint) || !string.IsNullOrEmpty(_currentMemoryContext))
            {
                contextBuilder.Append($"[MEMORY: {_currentGreetingHint}");
                if (!string.IsNullOrEmpty(_currentMemoryContext))
                    contextBuilder.Append($" {_currentMemoryContext}");
                contextBuilder.Append("]");
            }

            // Fresh perception context (compact format, always current)
            string perceptionContext = GetCompactPerceptionContext();
            if (!string.IsNullOrEmpty(perceptionContext))
            {
                if (contextBuilder.Length > 0) contextBuilder.Append(" ");
                contextBuilder.Append($"[SPATIAL: {perceptionContext}]");
            }

            if (!string.IsNullOrEmpty(additionalInternalContext))
            {
                if (contextBuilder.Length > 0) contextBuilder.Append(" ");
                contextBuilder.Append($"[EVENT: {additionalInternalContext}]");
            }

            // Add dynamic context as system message if we have any
            if (contextBuilder.Length > 0)
            {
                sb.Append(",");
                // Instruction BEFORE content is more effective at preventing spoken context
                sb.Append($"{{\"role\": \"system\", \"content\": \"[INTERNAL CONTEXT - Never read aloud, never quote, never reference directly. Use silently to inform responses.] {EscapeJson(contextBuilder.ToString())}\"}}");
            }

            // 3. Conversation history (skip index 0 which is system prompt, only user/assistant)
            for (int i = 1; i < history.Count; i++)
            {
                var msg = history[i];

                // Skip any old system messages that might still be in history (cleanup)
                if (msg.role == "system") continue;

                sb.Append(",");
                sb.Append("{");
                sb.Append($"\"role\": \"{msg.role}\",");

                bool isLast = (i == history.Count - 1);
                if (isLast && msg.role == "user" && !string.IsNullOrEmpty(base64Image))
                {
                    sb.Append("\"content\": [");
                    sb.Append($"{{\"type\": \"text\", \"text\": \"{EscapeJson(msg.content)}\"}},");
                    sb.Append($"{{\"type\": \"image_url\", \"image_url\": {{ \"url\": \"data:image/jpeg;base64,{base64Image}\" }} }}");
                    sb.Append("]");
                }
                else
                {
                    sb.Append($"\"content\": \"{EscapeJson(msg.content)}\"");
                }
                sb.Append("}");
            }

            if (!string.IsNullOrEmpty(transientPrompt))
            {
                sb.Append(",");
                sb.Append("{");
                sb.Append("\"role\": \"user\",");
                sb.Append($"\"content\": \"{EscapeJson(transientPrompt)}\"");
                sb.Append("}");
            }
            sb.Append("]");
            sb.Append("}");
            return sb.ToString();
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t");
        }

        // --- NEW: Error Buffering Download Handler ---
        public class DownloadHandlerStreamingText : DownloadHandlerScript
        {
            private Action<string> _onTextReceived;
            private StringBuilder _fullBuffer = new StringBuilder();

            public DownloadHandlerStreamingText(Action<string> onTextReceived) : base() { _onTextReceived = onTextReceived; }
            
            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                string text = Encoding.UTF8.GetString(data, 0, dataLength);
                _fullBuffer.Append(text); // Capture for debug

                string[] lines = text.Split('\n');
                foreach (string line in lines)
                {
                    if (line.StartsWith("data: ") && line.Trim() != "data: [DONE]")
                    {
                        try
                        {
                            string json = line.Substring(6).Trim();
                            StreamResponse resp = JsonUtility.FromJson<StreamResponse>(json);
                            if (resp.choices != null && resp.choices.Length > 0 && resp.choices[0].delta != null)
                            {
                                string content = resp.choices[0].delta.content;
                                if (!string.IsNullOrEmpty(content)) _onTextReceived?.Invoke(content);
                            }
                        }
                        catch { }
                    }
                }
                return true;
            }

            // Expose the raw text for error logging
            protected override string GetText() { return _fullBuffer.ToString(); }
        }

        [Serializable] public class ChatMessage { public string role; public string content; }
        [Serializable] public class WhisperResponse { public string text; }
        [Serializable] public class StreamResponse { public StreamChoice[] choices; }
        [Serializable] public class StreamChoice { public StreamDelta delta; }
        [Serializable] public class StreamDelta { public string content; }
        [Serializable] public class ElevenLabsRequest { public string text; public string model_id; public ELVoiceSettings voice_settings; }
        [Serializable] public class ELVoiceSettings { public float stability; public float similarity_boost; }

        #endregion
    }
}
