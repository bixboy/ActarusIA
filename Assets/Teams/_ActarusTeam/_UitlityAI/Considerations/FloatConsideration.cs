using UnityEngine;

namespace Teams.Actarus
{
    [CreateAssetMenu(menuName = "UtilityAI/Considerations/FloatConsideration")]
    public class FloatConsideration : Consideration
    {
        public string contextKey;
        
        public override float Evaluate(Context context)
        {
            if (context == null)
            {
                Debug.LogWarning("Context in FloatConsideration : " + name + " is null");
            }
            return context?.GetData<float>(contextKey) ?? 1f;
        }
    }
}