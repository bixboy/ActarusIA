using System;
using DoNotModify;
using UnityEngine;
using UnityUtils;

namespace UtilityAI {
    [CreateAssetMenu(menuName = "UtilityAI/Considerations/InRangeConsideration")]
    public class InRangeConsideration : Consideration {
        [Tooltip("Maximum distance in world units that still counts as being in range.")]
        public float maxDistance = 10f;

        [Tooltip("Maximum frontal angle (degrees) considered valid. Set to 360 for angle agnostic checks.")]
        public float maxAngle = 360f;

        [Tooltip("Context key used to fetch the target. Defaults to EnemyShip if left empty.")]
        public string targetTag = "EnemyShip";

        [Tooltip("Optional curve remapping the normalized distance (0 close, 1 far) to a utility score.")]
        public AnimationCurve curve;

        public override float Evaluate(Context context) {
            Preconditions.CheckNotNull(context, "Context cannot be null when evaluating an InRange consideration.");

            SpaceShipView origin = context.GetData<SpaceShipView>("MyShip");
            Preconditions.CheckNotNull(origin, "InRange consideration requires the controller ship in context (MyShip).");

            string lookupKey = string.IsNullOrWhiteSpace(targetTag) ? "EnemyShip" : targetTag;
            SpaceShipView target = context.GetData<SpaceShipView>(lookupKey);
            if (target == null) {
                return 0f;
            }

            Vector2 originPos = origin.Position;
            Vector2 targetPos = target.Position;
            float distance = Vector2.Distance(originPos, targetPos);

            if (maxDistance <= 0f) {
                return 0f;
            }

            float normalizedDistance = Mathf.Clamp01(distance / maxDistance);
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

        void Reset() {
            curve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0f)
            );
        }

        float EvaluateDistanceUtility(float normalizedDistance) {
            if (curve == null || curve.length == 0) {
                return 1f - normalizedDistance;
            }

            float remapped = curve.Evaluate(normalizedDistance);
            return Mathf.Clamp01(remapped);
        }
    }
}
