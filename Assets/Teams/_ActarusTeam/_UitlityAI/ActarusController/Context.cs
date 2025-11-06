using System.Collections.Generic;
using UnityEngine;


namespace Teams.Actarus {
    public class Context
    {
        public ActarusControllerUtilityAI ControllerUtilityAI;
        readonly Dictionary<string, object> data = new();

        public Context(ActarusControllerUtilityAI controllerUtilityAI)
        {
            if (!controllerUtilityAI) return;
            
            ControllerUtilityAI = controllerUtilityAI;
        }
        
        public T GetData<T>(string key)
        {
            if (!data.TryGetValue(key, out var value))
            {
                Debug.LogWarning($"⚠️ Context key '{key}' not found.");
                return default;
            }

            if (value is T typedValue)
                return typedValue;

            Debug.LogWarning($"⚠️ Context key '{key}' is not of expected type {typeof(T)}.");
            return default;
        }
        public void SetData(string key, object value) => data[key] = value;
    }
}