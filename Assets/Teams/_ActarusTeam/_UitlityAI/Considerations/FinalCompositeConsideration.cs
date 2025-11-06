using System.Collections.Generic;
using UnityEngine;

namespace Teams.Actarus
{
    [CreateAssetMenu(menuName = "UtilityAI/Considerations/FinalCompositeConsideration")]
    public class FinalCompositeConsideration : Consideration
    {
        public AnimationCurve curve;
        
        public float inputMin;
        public float inputMax;
        
        public float scoreMin;
        public float scoreMax;
        
        public bool allMustBeNonZero = true;
        
        public List<Consideration> considerations;
        
        public override float Evaluate(Context context)
        {
            if (considerations == null || considerations.Count == 0)
                return 0;

            float result = considerations[0].Evaluate(context);
            if (result == 0f && allMustBeNonZero) return 0f;

            for (int i = 1; i < considerations.Count; i++)
            {
                float val = considerations[i].Evaluate(context);
                if (val == 0f && allMustBeNonZero)
                    return 0f;
                result *= val;
            }


            float normalized = inputMax > 0f ? Mathf.Clamp(result, inputMin, inputMax) / inputMax : 0f;

            float evaluate = curve.Evaluate(normalized);
            
            return Mathf.InverseLerp(scoreMin, scoreMax, evaluate);
        }
    }
}