using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using MetaDyn; // For SDK components

namespace MetaDyn.AI
{
    /// <summary>
    /// The "Visual Cortex" of the AI Agent.
    /// Scans the environment to provide spatial context to the LLM.
    /// </summary>
    public class AIPerceptionManager : MonoBehaviour
    {
        [Header("Sensory Settings")]
        [Tooltip("How far the AI can 'see' or 'hear' (meters)")]
        public float perceptionRadius = 15.0f;

        [Tooltip("Maximum number of objects to track in short-term memory")]
        public int maxShortTermMemory = 10;

        [Tooltip("Layers to scan for relevant objects")]
        public LayerMask scanLayers = ~0; // Default to everything

        [Header("Social Settings")]
        [Tooltip("Current active user interacting with the AI (Auto-found if null)")]
        public Transform activeUser;

        // --- Events ---
        public event System.Action<Transform> OnUserDetected;

        private string _cachedUserName = "User";
        private Transform _previousUser;

        private void Start()
        {
            // Load user name from PlayerPrefs
            _cachedUserName = PlayerPrefs.GetString("PlayerName", "User");
            
            // Try to find the local player if not assigned
            if (activeUser == null)
            {
                activeUser = FindLocalPlayer();
            }

            // Fire initial detection if we started with a user
            if (activeUser != null)
            {
                _previousUser = activeUser;
                OnUserDetected?.Invoke(activeUser);
            }
        }

        private void Update()
        {
            // Constant vigilance: Check if we lost the user or found a new one
            if (activeUser == null)
            {
                activeUser = FindLocalPlayer();
            }

            // Detect change
            if (activeUser != _previousUser && activeUser != null)
            {
                Debug.Log($"[MetaDyn AI] User detected: {activeUser.name}");
                OnUserDetected?.Invoke(activeUser);
                _previousUser = activeUser;
            }
        }

        private Transform FindLocalPlayer()
        {
            var ugsPlayers = Object.FindObjectsByType<global::MetaDyn.Networking.MetaDynUGSPlayerController>(FindObjectsSortMode.None);
            foreach (var player in ugsPlayers)
            {
                if (player != null && player.IsOwner)
                {
                    return player.transform;
                }
            }

            return null;
        }

        // --- Data Structures for JSON Serialization ---

        [System.Serializable]
        private class ContextSnapshot
        {
            public UserContext user;
            public List<ObjectContext> environment;
            public string location;
        }

        [System.Serializable]
        private class UserContext
        {
            public string name;
            public string distance;
            public string position; // "In front", "To the left", etc.
        }

        [System.Serializable]
        private class ObjectContext
        {
            public string name;
            public string type;
            public string status; // "Occupied", "Open", etc.
            public string distance;
        }

        /// <summary>
        /// Generates a JSON snapshot of the AI's current reality.
        /// </summary>
        /// <returns>JSON string of ContextSnapshot</returns>
        public string GetPerceptionContext()
        {
            // Refresh user if lost (e.g. respawn)
            if (activeUser == null) activeUser = FindLocalPlayer();
            _cachedUserName = PlayerPrefs.GetString("PlayerName", "User");

            ContextSnapshot snapshot = new ContextSnapshot();
            snapshot.location = "Pavilion"; // Could be dynamic based on triggers/zones later

            // 1. Perception: User
            if (activeUser != null)
            {
                float dist = Vector3.Distance(transform.position, activeUser.position);
                snapshot.user = new UserContext
                {
                    name = _cachedUserName,
                    distance = $"{dist:F1}m",
                    position = GetRelativeDirection(activeUser.position)
                };
            }
            else
            {
                snapshot.user = new UserContext { name = _cachedUserName, distance = "Unknown", position = "Unknown" };
            }

            // 2. Perception: Environment
            snapshot.environment = ScanEnvironment();

            return JsonUtility.ToJson(snapshot);
        }

        private List<ObjectContext> ScanEnvironment()
        {
            List<ObjectContext> perceivedObjects = new List<ObjectContext>();
            HashSet<GameObject> processedObjects = new HashSet<GameObject>();

            // --- STRATEGY 1: Logic Scan (Find logical SDK components) ---
            // This is more robust for things like Seats that might not have colliders

            // 1. Seats
            var allSeats = Object.FindObjectsByType<SeatHotspot>(FindObjectsSortMode.None);
            foreach (var seat in allSeats)
            {
                float dist = Vector3.Distance(transform.position, seat.transform.position);
                if (dist <= perceptionRadius)
                {
                    perceivedObjects.Add(new ObjectContext
                    {
                        name = seat.name,
                        type = "Seat",
                        status = seat.IsOccupied ? "Occupied" : "Free",
                        distance = $"{dist:F1}m"
                    });
                    processedObjects.Add(seat.gameObject);
                }
            }

            // 2. Screens
            var allScreens = Object.FindObjectsByType<ProjectionSurface>(FindObjectsSortMode.None);
            foreach (var screen in allScreens)
            {
                float dist = Vector3.Distance(transform.position, screen.transform.position);
                if (dist <= perceptionRadius)
                {
                    perceivedObjects.Add(new ObjectContext
                    {
                        name = screen.name,
                        type = "Screen",
                        status = screen.IsProjecting ? "Active" : "Off",
                        distance = $"{dist:F1}m"
                    });
                    processedObjects.Add(screen.gameObject);
                }
            }

            // 3. Interactables
            var allInteractables = Object.FindObjectsByType<Interactable>(FindObjectsSortMode.None);
            foreach (var interactable in allInteractables)
            {
                float dist = Vector3.Distance(transform.position, interactable.transform.position);
                if (dist <= perceptionRadius)
                {
                    perceivedObjects.Add(new ObjectContext
                    {
                        name = interactable.name,
                        type = "Interactable",
                        status = "Available",
                        distance = $"{dist:F1}m"
                    });
                    processedObjects.Add(interactable.gameObject);
                }
            }

            // --- STRATEGY 2: Physics Scan (Find physical entities like Players) ---
            Collider[] hits = Physics.OverlapSphere(transform.position, perceptionRadius, scanLayers);
            
            foreach (var hit in hits)
            {
                GameObject obj = hit.gameObject;
                if (processedObjects.Contains(obj)) continue;

                // 4. Other Players
                bool isPlayer =
                    obj.CompareTag("Player") ||
                    obj.GetComponentInParent<global::MetaDyn.Networking.MetaDynUGSPlayerController>() != null;

                if (isPlayer && obj.transform != activeUser && obj.transform != transform)
                {
                     perceivedObjects.Add(new ObjectContext
                    {
                        name = "Another Player",
                        type = "Human",
                        status = "Standing",
                        distance = $"{Vector3.Distance(transform.position, obj.transform.position):F1}m"
                    });
                    processedObjects.Add(obj);
                }
            }

            // Sort by distance (closest first) and trim to max memory
            perceivedObjects.Sort((a, b) => 
            {
                float distA = float.Parse(a.distance.Replace("m", ""));
                float distB = float.Parse(b.distance.Replace("m", ""));
                return distA.CompareTo(distB);
            });

            if (perceivedObjects.Count > maxShortTermMemory)
            {
                perceivedObjects = perceivedObjects.GetRange(0, maxShortTermMemory);
            }

            return perceivedObjects;
        }

        /// <summary>
        /// Calculates relative direction (e.g. "Front Left") from the AI's forward vector.
        /// </summary>
        private string GetRelativeDirection(Vector3 targetPos)
        {
            Vector3 direction = (targetPos - transform.position).normalized;
            Vector3 localDir = transform.InverseTransformDirection(direction);

            if (localDir.z > 0.5f) return "In front";
            if (localDir.z < -0.5f) return "Behind";
            if (localDir.x > 0.5f) return "To the right";
            if (localDir.x < -0.5f) return "To the left";
            
            return "Nearby";
        }

        // Debug visualization
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, perceptionRadius);
        }
    }
}
