using UnityEngine;

namespace UtilityAI
{
    [CreateAssetMenu(menuName = "UtilityAI/Considerations/FloatConsideration")]
    public class FloatConsideration : Consideration
    {
        public string contextKey;
        
        public override float Evaluate(Context context)
        {
            return context.GetData<float>(contextKey);
        }
    }
}