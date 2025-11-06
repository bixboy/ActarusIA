using System;
using UnityEngine;

namespace Teams.Actarus
{
    
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


        public static void DrawThisFrame(Action drawCallback)
        {
            if (_instance == null)
            {
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
            _instance._lastFrameDrawn = -1;
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