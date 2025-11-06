using UnityEngine;

namespace UtilityAI
{
    [CreateAssetMenu(menuName = "UtilityAI/Considerations/IntConsideration")]
    public class IntConsideration : Consideration
    {
        public string contextKey;
        
        public override float Evaluate(Context context)
        {
            return context.GetData<int>(contextKey);
        }
    }
}