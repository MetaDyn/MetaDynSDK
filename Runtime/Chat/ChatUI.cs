using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using MetaDyn.Networking;

namespace MetaDyn.Chat
{
    /// <summary>
    /// Controls the Chat UI, displaying messages in a scrollable view with input field.
    /// Updated to use MetaDynVivoxService for UGS-native chat.
    /// </summary>
    public class ChatUI : MonoBehaviour
    {
        [Header("UI Settings")]
        [SerializeField]
        [Tooltip("Show the chat panel on start")]
        private bool startVisible = false;

        [SerializeField]
        [Tooltip("Key to toggle the chat visibility")]
        private KeyCode toggleChatKey = KeyCode.Return;

        [SerializeField]
        [Tooltip("Auto-scroll to bottom when new messages arrive")]
        private bool autoScrollToBottom = true;

        [Header("UI References")]
        [SerializeField] private GameObject chatPanel;
        [SerializeField] private Transform messageContainer;
        [SerializeField] private GameObject messageEntryPrefab;
        [SerializeField] private TMP_InputField messageInput;
        [SerializeField] private Button sendButton;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text headerText;

        [Header("Layout Settings")]
        [SerializeField] private float messageEntryHeight = 50f;
        [SerializeField] private float messageSpacing = 5f;

        [Header("Color Settings")]
        [SerializeField] private Color localPlayerColor = new Color(0.2f, 0.8f, 1f); // Cyan
        [SerializeField] private Color otherPlayerColor = Color.white;
        [SerializeField] private Color systemMessageColor = new Color(1f, 1f, 0f); // Yellow

        // Entry tracking
        private List<GameObject> _activeMessages = new List<GameObject>();

        // Object pooling for performance
        private Queue<GameObject> _messagePool = new Queue<GameObject>();
        private const int INITIAL_POOL_SIZE = 20;

        // Local state
        private bool _isSubscribed = false;
        private string _localUserName;
        private bool _wasInputFieldFocused = false; // Track focus state for input locking

        // Thread-safe message queue for main-thread processing
        private struct QueuedMessage { public string sender; public string message; }
        private readonly Queue<QueuedMessage> _messageQueue = new Queue<QueuedMessage>();
        private readonly object _queueLock = new object();

        #region Unity Lifecycle

        private void Start()
        {
            // Validate references
            if (!ValidateReferences())
            {
                enabled = false;
                return;
            }

            // Initialize object pool
            InitializePool();

            // Setup layout group if not present
            SetupLayoutGroup();

            // Setup UI listeners
            if (sendButton != null)
                sendButton.onClick.AddListener(OnSendButtonClicked);

            if (messageInput != null)
                messageInput.onSubmit.AddListener(OnInputSubmit);

            // Set initial visibility
            if (chatPanel != null)
                chatPanel.SetActive(startVisible);

            // Subscribe to Vivox events
            if (MetaDynVivoxService.Instance != null)
            {
                SubscribeToVivoxEvents();
            }
            else
            {
                Debug.LogWarning("[ChatUI] MetaDynVivoxService not found. Will retry in Update.");
                UpdateStatus("Vivox not initialized");
            }

            // Get local username from PlayerPrefs
            _localUserName = PlayerPrefs.GetString("PlayerName", "Unknown");

            // Add a startup message to verify UI is working
            DisplayMessage("", "Chat Ready");
        }

        private void Update()
        {
            // Process queued messages on the main thread
            lock (_queueLock)
            {
                while (_messageQueue.Count > 0)
                {
                    var msg = _messageQueue.Dequeue();
                    DisplayMessage(msg.sender, msg.message);
                }
            }

            // Retry connection to Vivox if not found
            if (MetaDynVivoxService.Instance != null && !_isSubscribed)
            {
                SubscribeToVivoxEvents();
            }

            // Toggle visibility with key
            if (Input.GetKeyDown(toggleChatKey))
            {
                ToggleVisibility();
            }

            // Focus input when panel is visible
            if (chatPanel != null && chatPanel.activeSelf)
            {
                // Keep input focused when visible (except when user clicks away)
                if (messageInput != null && !messageInput.isFocused && Input.GetMouseButtonDown(0))
                {
                    if (RectTransformUtility.RectangleContainsScreenPoint(
                        messageInput.GetComponent<RectTransform>(),
                        Input.mousePosition))
                    {
                        messageInput.ActivateInputField();
                    }
                }
            }

            // --- INPUT LOCKING BASED ON CHAT FOCUS ---
            if (messageInput != null)
            {
                bool isCurrentlyFocused = messageInput.isFocused;
                if (isCurrentlyFocused && !_wasInputFieldFocused)
                {
                    InputManager.LockInput("ChatInput");
                }
                else if (!isCurrentlyFocused && _wasInputFieldFocused)
                {
                    InputManager.UnlockInput("ChatInput");
                }
                _wasInputFieldFocused = isCurrentlyFocused;
            }
        }

        private void OnDestroy()
        {
            if (_wasInputFieldFocused)
            {
                InputManager.UnlockInput("ChatInput");
            }

            if (MetaDynVivoxService.Instance != null)
            {
                MetaDynVivoxService.Instance.OnTextMessageReceived -= OnMessageReceived;
                MetaDynVivoxService.Instance.OnLoginStatusChanged -= UpdateConnectionStatus;
            }

            if (sendButton != null)
                sendButton.onClick.RemoveListener(OnSendButtonClicked);

            if (messageInput != null)
                messageInput.onSubmit.RemoveListener(OnInputSubmit);
        }

        #endregion

        #region Initialization

        private bool ValidateReferences()
        {
            bool isValid = true;
            if (chatPanel == null || messageContainer == null || messageEntryPrefab == null || messageInput == null)
            {
                isValid = false;
            }
            return isValid;
        }

        private void SetupLayoutGroup()
        {
            if (messageContainer.GetComponent<VerticalLayoutGroup>() == null)
            {
                var layoutGroup = messageContainer.gameObject.AddComponent<VerticalLayoutGroup>();
                layoutGroup.spacing = messageSpacing;
                layoutGroup.childAlignment = TextAnchor.UpperLeft;
                layoutGroup.childControlHeight = false;
                layoutGroup.childControlWidth = true;
                layoutGroup.childForceExpandHeight = false;
                layoutGroup.childForceExpandWidth = true;
                layoutGroup.padding = new RectOffset(10, 10, 10, 10);
            }

            if (messageContainer.GetComponent<ContentSizeFitter>() == null)
            {
                var sizeFitter = messageContainer.gameObject.AddComponent<ContentSizeFitter>();
                sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        private void SubscribeToVivoxEvents()
        {
            if (MetaDynVivoxService.Instance == null || _isSubscribed)
                return;

            MetaDynVivoxService.Instance.OnTextMessageReceived += OnMessageReceived;
            MetaDynVivoxService.Instance.OnLoginStatusChanged += UpdateConnectionStatus;

            _isSubscribed = true;
            Debug.Log("[ChatUI] Subscribed to MetaDynVivoxService events");
        }

        #endregion

        #region Object Pooling

        private void InitializePool()
        {
            for (int i = 0; i < INITIAL_POOL_SIZE; i++)
            {
                GameObject entry = Instantiate(messageEntryPrefab);
                entry.SetActive(false);
                _messagePool.Enqueue(entry);
            }
        }

        private GameObject GetPooledMessage()
        {
            if (_messagePool.Count > 0)
            {
                GameObject entry = _messagePool.Dequeue();
                entry.SetActive(true);
                return entry;
            }
            else
            {
                return Instantiate(messageEntryPrefab);
            }
        }

        private void ReturnToPool(GameObject entry)
        {
            entry.SetActive(false);
            entry.transform.SetParent(null);
            _messagePool.Enqueue(entry);
        }

        #endregion

        #region UI Management

        public void ToggleVisibility()
        {
            if (chatPanel != null)
            {
                bool newState = !chatPanel.activeSelf;
                chatPanel.SetActive(newState);
                if (newState)
                {
                    if (messageInput != null)
                    {
                        messageInput.Select();
                        messageInput.ActivateInputField();
                    }
                    ScrollToBottom();
                }
            }
        }

        private void UpdateConnectionStatus(bool isConnected)
        {
            UpdateStatus(isConnected ? "Connected" : "Disconnected");
            if (messageInput != null) messageInput.interactable = isConnected;
            if (sendButton != null) sendButton.interactable = isConnected;
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }

        #endregion

        #region Message Display

        private void DisplayMessage(string sender, string message, string timestamp = null)
        {
            if (messageContainer == null || messageEntryPrefab == null) return;

            // Ensure layout group exists
            var layoutGroup = messageContainer.GetComponent<VerticalLayoutGroup>();
            if (layoutGroup == null)
            {
                layoutGroup = messageContainer.gameObject.AddComponent<VerticalLayoutGroup>();
                layoutGroup.spacing = messageSpacing;
                layoutGroup.childAlignment = TextAnchor.UpperLeft;
                layoutGroup.childControlHeight = true; // Let layout group control height
                layoutGroup.childControlWidth = true;
                layoutGroup.childForceExpandHeight = false;
                layoutGroup.childForceExpandWidth = true;
                layoutGroup.padding = new RectOffset(10, 10, 10, 10);
            }
            else
            {
                // Ensure correct settings if it exists
                layoutGroup.childControlHeight = true;
                layoutGroup.childControlWidth = true;
                layoutGroup.childForceExpandHeight = false;
                layoutGroup.childForceExpandWidth = true;
            }

            // Ensure ContentSizeFitter exists
            var sizeFitter = messageContainer.GetComponent<ContentSizeFitter>();
            if (sizeFitter == null)
            {
                sizeFitter = messageContainer.gameObject.AddComponent<ContentSizeFitter>();
                sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            GameObject messageObj = GetPooledMessage();
            if (messageObj == null) return;

            messageObj.transform.SetParent(messageContainer, false);
            messageObj.transform.SetAsLastSibling();

            ChatMessageEntry entry = messageObj.GetComponent<ChatMessageEntry>();
            if (entry != null)
            {
                bool isLocalPlayer = (sender == _localUserName);
                Color messageColor = isLocalPlayer ? localPlayerColor : otherPlayerColor;
                
                // If sender is empty or "System", use system color
                if (string.IsNullOrEmpty(sender) || sender == "System") 
                    messageColor = systemMessageColor;

                if (string.IsNullOrEmpty(timestamp)) timestamp = System.DateTime.Now.ToString("HH:mm:ss");
                entry.Initialize(sender, message, timestamp, messageColor);
            }

            _activeMessages.Add(messageObj);
            
            // Limit history
            if (_activeMessages.Count > 100)
            {
                GameObject oldest = _activeMessages[0];
                _activeMessages.RemoveAt(0);
                ReturnToPool(oldest);
            }

            if (autoScrollToBottom) ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            if (scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }

        #endregion

        #region Input Handling

        private void OnSendButtonClicked()
        {
            SendMessage();
        }

        private void OnInputSubmit(string text)
        {
            SendMessage();
        }

        private async void SendMessage()
        {
            if (messageInput == null || MetaDynVivoxService.Instance == null)
                return;

            string message = messageInput.text.Trim();
            if (string.IsNullOrEmpty(message))
                return;

            // Clear input immediately to feel responsive
            messageInput.text = "";
            messageInput.Select();
            messageInput.ActivateInputField();

            // Local Echo: Add message immediately so user sees it even if Vivox is slow or fails
            // This also helps verify if the UI is working vs network
            DisplayMessage(_localUserName, message);

            try
            {
                // Send to Vivox
                await MetaDynVivoxService.Instance.SendTextMessageAsync(message);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[ChatUI] Failed to send message: " + ex.Message);
                DisplayMessage("System", "Failed to send message: Vivox connection error.");
            }
        }

        #endregion

        #region Event Callbacks

        private void OnMessageReceived(string sender, string message)
        {
            // Ignore own message echo from Vivox since we added local echo in SendMessage
            if (sender == _localUserName) return;

            lock (_queueLock)
            {
                _messageQueue.Enqueue(new QueuedMessage { sender = sender, message = message });
            }
        }

        #endregion
}
}
