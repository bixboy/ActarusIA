using System.Collections.Generic;
using Teams.ActarusController.Shahine;


namespace UtilityAI {
    public class Context
    {
        public ActarusControllerUtilityAI ControllerUtilityAI;
        readonly Dictionary<string, object> data = new();

        public Context(ActarusControllerUtilityAI controllerUtilityAI)
        {
            if (!controllerUtilityAI) return;
            
            ControllerUtilityAI = controllerUtilityAI;
        }
        
        public T GetData<T>(string key) => data.TryGetValue(key, out var value) ? (T)value : default;
        public void SetData(string key, object value) => data[key] = value;
    }
}