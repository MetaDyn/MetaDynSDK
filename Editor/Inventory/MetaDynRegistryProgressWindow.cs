using UnityEditor;
using UnityEngine;
using System;

namespace MetaDyn.Editor
{
    public class MetaDynRegistryProgressWindow : EditorWindow
    {
        public enum WindowState { Working, Success, Error }

        private WindowState _state = WindowState.Working;
        private string _headerTitle = "MetaDyn Registry";
        private string _statusTitle = "Processing...";
        private string _currentTask = "Initialising...";
        private float _progress = 0f;
        private string _errorMessage = "";
        private Action _onClose;

        public static MetaDynRegistryProgressWindow ShowProgress(string header, string status)
        {
            // Set utility to false to restore standard window decorations including the top-right close button
            var window = GetWindow<MetaDynRegistryProgressWindow>(false, "MetaDyn SDK", true);
            window.minSize = new Vector2(400, 300);
            window.maxSize = new Vector2(400, 300);
            window._headerTitle = header;
            window._statusTitle = status;
            window._state = WindowState.Working;
            window._progress = 0f;
            window.CenterOnEditor();
            window.Show();
            return window;
        }

        public void UpdateStatus(string task, float progress)
        {
            _currentTask = task;
            _progress = progress;
            Repaint();
        }

        public void SetSuccess(string message, Action onOk = null)
        {
            _state = WindowState.Success;
            _currentTask = message;
            _progress = 1f;
            _onClose = onOk;
            Repaint();
        }

        public void SetError(string error)
        {
            _state = WindowState.Error;
            _errorMessage = error;
            Repaint();
        }

        private void OnGUI()
        {
            MetaDynEditorHeader.DrawHeader(_headerTitle, _statusTitle);

            MetaDynStyle.BeginSection();
            GUILayout.Space(10);

            if (_state == WindowState.Working)
            {
                EditorGUILayout.LabelField(_currentTask, EditorStyles.wordWrappedLabel);
                GUILayout.Space(10);
                
                Rect rect = EditorGUILayout.GetControlRect(false, 20);
                EditorGUI.ProgressBar(rect, _progress, $"{(_progress * 100):F0}%");
            }
            else if (_state == WindowState.Success)
            {
                EditorGUILayout.HelpBox("Operation Successful!", MessageType.Info);
                EditorGUILayout.LabelField(_currentTask, EditorStyles.wordWrappedLabel);
                
                // Add clickable link support
                if (_currentTask.Contains("http"))
                {
                    string url = ExtractUrl(_currentTask);
                    if (!string.IsNullOrEmpty(url))
                    {
                        GUILayout.Space(5);
                        GUI.color = new Color(0.2f, 0.6f, 1f);
                        if (GUILayout.Button("🌐 OPEN LIVE SITE", GUILayout.Height(30)))
                        {
                            Application.OpenURL(url);
                        }
                        GUI.color = Color.white;
                    }
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("OK", GUILayout.Height(30), GUILayout.Width(120)))
                {
                    _onClose?.Invoke();
                    Close();
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            else if (_state == WindowState.Error)
            {
                EditorGUILayout.HelpBox("Operation Failed", MessageType.Error);
                EditorGUILayout.LabelField(_errorMessage, EditorStyles.wordWrappedLabel);
                
                GUILayout.FlexibleSpace();
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Close", GUILayout.Height(30), GUILayout.Width(120)))
                {
                    Close();
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(10);
            MetaDynStyle.EndSection();
        }

        private string ExtractUrl(string text)
        {
            int index = text.IndexOf("http");
            if (index < 0) return null;
            
            // Get everything from http to the end of the string, or until a space/newline
            string sub = text.Substring(index);
            int end = sub.IndexOfAny(new char[] { ' ', '\n', '\r' });
            return end > 0 ? sub.Substring(0, end) : sub;
        }

        private void CenterOnEditor()
        {
            var main = EditorGUIUtility.GetMainWindowPosition();
            var pos = position;
            float w = (main.width - pos.width) * 0.5f;
            float h = (main.height - pos.height) * 0.5f;
            pos.x = main.x + w;
            pos.y = main.y + h;
            position = pos;
        }
    }
}