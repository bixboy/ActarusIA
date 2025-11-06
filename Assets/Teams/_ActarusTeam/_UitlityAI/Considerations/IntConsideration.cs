using UnityEngine;

namespace Teams.Actarus
{
    [CreateAssetMenu(menuName = "UtilityAI/Considerations/IntConsideration")]
    public class IntConsideration : Consideration
    {
        public string contextKey;
        
        public override float Evaluate(Context context)
        {
            if (context == null)
            {
                Debug.LogWarning("Context in IntConsideration : " + name + " is null");
            }
            return context?.GetData<int>(contextKey) ?? 1f;
        }
    }
}