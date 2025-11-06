using System;
using UnityEngine;

namespace UtilityAI
{
    /// <summary>
    /// Centralized debug drawer for UtilityAI actions.
    /// Only draws Gizmos when an action has been executed during the current frame.
    /// </summary>
    [DefaultExecutionOrder(9999)]
    public class UtilityAIDebugDrawer : MonoBehaviour
    {
        private static UtilityAIDebugDrawer _instance;

        private Action _currentDrawCallback;
        private int _lastFrameDrawn = -1;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            _instance = this;
        }

        /// <summary>
        /// Register a temporary gizmo draw function to be executed this frame only.
        /// </summary>
        public static void DrawThisFrame(Action drawCallback)
        {
            if (_instance == null)
            {
                // Auto-create an invisible GameObject if not present
                GameObject go = new GameObject("[UtilityAIDebugDrawer]");
                go.hideFlags = HideFlags.HideAndDontSave;
                _instance = go.AddComponent<UtilityAIDebugDrawer>();
            }

            _instance._currentDrawCallback = drawCallback;
            _instance._lastFrameDrawn = Time.frameCount;
        }
        
        public static void DrawPersistent(Action drawCallback)
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("[UtilityAIDebugDrawer]");
                go.hideFlags = HideFlags.HideAndDontSave;
                _instance = go.AddComponent<UtilityAIDebugDrawer>();
            }

            _instance._currentDrawCallback = drawCallback;
            _instance._lastFrameDrawn = -1; // désactive le test par frame
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if ((_lastFrameDrawn == -1 || Time.frameCount == _lastFrameDrawn) && _currentDrawCallback != null)
            {
                _currentDrawCallback.Invoke();
            }
        }

#endif
    }
}