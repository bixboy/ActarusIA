using Teams.ActarusController.Shahine;
using UnityEngine;
using UnityUtils;

namespace UtilityAI
{
    [CreateAssetMenu(menuName = "UtilityAI/Considerations/CombatMode")]
    public class CombatModeConsideration : Consideration
    {
        [Tooltip("Combat mode required to return a high utility value.")]
        public ActarusControllerUtilityAI.CombatMode requiredMode = ActarusControllerUtilityAI.CombatMode.Capture;

        [Tooltip("If enabled, the consideration returns 1 when the current mode differs from the required mode.")]
        public bool invert;

        public override float Evaluate(Context context)
        {
            Preconditions.CheckNotNull(context, "Context is required to evaluate combat mode considerations.");

            ActarusControllerUtilityAI controller = context.ControllerUtilityAI;
            if (!controller)
                return 0f;

            bool isMatch = controller.CurrentCombatMode == requiredMode;
            return (isMatch ^ invert) ? 1f : 0f;
        }
    }
}
