using UnityEngine;
using UnityEngine.Video;
using System.Collections.Generic;

namespace MetaDyn
{
    /// <summary>
    /// MetaDyn SDK Component: Manages a sequence of video URLs for a VideoPlayer.
    /// Supports sequential playback, looping, and dynamic modification of the sequence.
    /// </summary>
    [RequireComponent(typeof(VideoPlayer))]
    [AddComponentMenu("MetaDyn/Video Sequence")]
    public class VideoSequence : MonoBehaviour
    {
        [Header("Sequence Configuration")]
        [Tooltip("The list of video URLs to play in sequence.")]
        public List<string> videoUrls = new List<string>();

        [Tooltip("When enabled, the sequence will loop back to the first video after the last one finishes.")]
        public bool loopSequence = true;

        [Tooltip("Automatically start playing the sequence when the component starts.")]
        public bool playOnStart = true;

        [Header("Runtime Status")]
        [SerializeField]
        [Tooltip("Current index in the video sequence (read-only).")]
        private int _currentIndex = 0;

        [SerializeField]
        [Tooltip("Is the sequence currently playing (read-only).")]
        private bool _isPlaying = false;

        private VideoPlayer _videoPlayer;

        /// <summary>
        /// Gets the current index in the sequence.
        /// </summary>
        public int CurrentIndex => _currentIndex;

        /// <summary>
        /// Gets whether the sequence is currently playing.
        /// </summary>
        public bool IsPlaying => _isPlaying;

        private void Awake()
        {
            _videoPlayer = GetComponent<VideoPlayer>();
            
            // Subscribe to the finish event
            _videoPlayer.loopPointReached += OnVideoFinished;
            
            // We manage looping for the sequence ourselves, so the individual player should not loop.
            _videoPlayer.isLooping = false;
        }

        private void Start()
        {
            if (playOnStart && videoUrls.Count > 0)
            {
                PlaySequence();
            }
        }

        /// <summary>
        /// Starts playback from the current index.
        /// </summary>
        public void PlaySequence()
        {
            if (videoUrls.Count == 0)
            {
                Debug.LogWarning($"[MetaDyn.VideoSequence] No URLs in sequence on {gameObject.name}");
                return;
            }

            // Bound check the index
            if (_currentIndex < 0 || _currentIndex >= videoUrls.Count)
            {
                _currentIndex = 0;
            }

            PlayAtIndex(_currentIndex);
        }

        /// <summary>
        /// Stops video playback.
        /// </summary>
        public void StopSequence()
        {
            _videoPlayer.Stop();
            _isPlaying = false;
        }

        /// <summary>
        /// Advances to the next video in the sequence.
        /// </summary>
        public void PlayNext()
        {
            _currentIndex++;
            
            if (_currentIndex >= videoUrls.Count)
            {
                if (loopSequence)
                {
                    _currentIndex = 0;
                }
                else
                {
                    _isPlaying = false;
                    return;
                }
            }

            PlayAtIndex(_currentIndex);
        }

        /// <summary>
        /// Plays a specific index in the sequence.
        /// </summary>
        public void PlayAtIndex(int index)
        {
            if (index < 0 || index >= videoUrls.Count) return;

            _currentIndex = index;
            string url = videoUrls[_currentIndex];

            if (!string.IsNullOrEmpty(url))
            {
                _videoPlayer.url = url;
                _videoPlayer.Play();
                _isPlaying = true;
                Debug.Log($"[MetaDyn.VideoSequence] Playing video {_currentIndex + 1}/{videoUrls.Count}: {url}");
            }
        }

        /// <summary>
        /// Adds a new video URL to the sequence.
        /// </summary>
        public void AddVideo(string url)
        {
            if (!videoUrls.Contains(url))
            {
                videoUrls.Add(url);
            }
        }

        /// <summary>
        /// Removes a video URL from the sequence.
        /// </summary>
        public void RemoveVideo(string url)
        {
            videoUrls.Remove(url);
        }

        private void OnVideoFinished(VideoPlayer vp)
        {
            if (_isPlaying)
            {
                PlayNext();
            }
        }

        #if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Simple visual indicator for the editor
            Gizmos.color = _isPlaying ? Color.blue : Color.gray;
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.25f);
        }
        #endif
    }
}