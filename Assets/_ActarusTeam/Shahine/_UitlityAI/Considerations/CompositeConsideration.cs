using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UtilityAI {
    [CreateAssetMenu(menuName = "UtilityAI/Considerations/CompositeConsideration")]
    public class CompositeConsideration : Consideration {
        public enum OperationType { Average, Multiply, Add, Subtract, Divide, Max, Min }
        
        public bool allMustBeNonZero = true;
        
        public OperationType operation = OperationType.Max;
        public Considerations considerations;

        public override float Evaluate(Context context) {
            if (considerations == null) return 0f;
            
            float result = considerations.FirstConsideration.Evaluate(context);
            if (result == 0f && allMustBeNonZero) return 0f;

            float value = considerations.SecondConsideration.Evaluate(context);
            if (value == 0f && allMustBeNonZero) return 0f;

            switch (operation) {
                case OperationType.Average:
                    result = (result + value) / 2;
                    break;
                case OperationType.Multiply:
                    result *= value;
                    break;
                case OperationType.Add:
                    result += value;
                    break;
                case OperationType.Subtract:
                    result -= value;
                    break;
                case OperationType.Divide:
                    result = value != 0 ? result / value : result; // Prevent division by zero
                    break;
                case OperationType.Max:
                    result = Mathf.Max(result, value);
                    break;
                case OperationType.Min:
                    result = Mathf.Min(result, value);
                    break;
            }
            
            
            return Mathf.Clamp01(result);
        }
    }
    

    [Serializable]
    public struct Considerations : IEnumerable<Consideration>
    {
        public Consideration? FirstConsideration;
        public Consideration? SecondConsideration;
        
        public bool IsNull => !FirstConsideration && !SecondConsideration;
        
        public static bool operator ==(Considerations a, object b)
        {

            if (b is null)
                return a.IsNull;
            
            if (b is Considerations other)
                return a.Equals(other);

            return false;
        }

        public static bool operator !=(Considerations a, object b) => !(a == b);


        public IEnumerator<Consideration> GetEnumerator()
        {
            if (FirstConsideration)
                yield return FirstConsideration;
            if (SecondConsideration)
                yield return SecondConsideration;
        }

        public override bool Equals(object obj)
        {
            if (obj is not Considerations other)
                return false;

            return Nullable.Equals(FirstConsideration, other.FirstConsideration) &&
                   Nullable.Equals(SecondConsideration, other.SecondConsideration);
        }

        public override int GetHashCode()
        {
            int hash1 = FirstConsideration?.GetHashCode() ?? 0;
            int hash2 = SecondConsideration?.GetHashCode() ?? 0;
            return hash1 ^ hash2;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

}