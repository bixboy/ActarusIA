using System;
using DoNotModify;
using UnityEngine;
using UnityEngine.Serialization;
using UnityUtils;

namespace UtilityAI {
    [CreateAssetMenu(menuName = "UtilityAI/Considerations/InRangeConsideration")]
    public class InRangeConsideration : CurveConsideration
    {
        
        [Tooltip("Maximum frontal angle (degrees) considered valid. Set to 360 for angle agnostic checks.")]
        public float maxAngle = 360f;

        [Tooltip("Context key used to fetch the target. Defaults to EnemyShip if left empty.")]
        public string contextTarget = "EnemyShip";
        

        public override float Evaluate(Context context) {
            Preconditions.CheckNotNull(context, "Context cannot be null when evaluating an InRange consideration.");

            SpaceShipView origin = context.GetData<SpaceShipView>("MyShip");
            Preconditions.CheckNotNull(origin, "InRange consideration requires the controller ship in context (MyShip).");

            string lookupKey = string.IsNullOrWhiteSpace(contextTarget) ? "EnemyShip" : contextTarget;
            SpaceShipView target = context.GetData<SpaceShipView>(lookupKey);
            if (target == null) {
                return 0f;
            }

            Vector2 originPos = origin.Position;
            Vector2 targetPos = target.Position;
            float distance = Vector2.Distance(originPos, targetPos);

            if (inputMax <= 0f) {
                return 0f;
            }

            float normalizedDistance = Mathf.Clamp(distance, inputMin, inputMax) / inputMax;
            float distanceUtility = EvaluateDistanceUtility(normalizedDistance);

            float angleUtility = 1f;
            
            if (maxAngle < 360f) {
                Vector2 forward = origin.LookAt.normalized;
                Vector2 toTarget = (targetPos - originPos).normalized;
                float angle = Vector2.Angle(forward, toTarget);
                float normalizedAngle = maxAngle <= 0f ? 1f : Mathf.Clamp01(angle / maxAngle);
                angleUtility = 1f - normalizedAngle;
            }

            float utility = Mathf.Clamp01(distanceUtility * angleUtility);
            return utility;
        }
        
        
        float EvaluateDistanceUtility(float normalizedDistance) {
            if (curve == null || curve.length == 0) {
                return 1f - normalizedDistance;
            }

            float remapped = curve.Evaluate(normalizedDistance);
            return Mathf.InverseLerp(scoreMin, scoreMax, remapped);
        }
    }
}
