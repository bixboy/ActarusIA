using UnityEngine;

namespace Teams.Actarus {
    public abstract class Consideration : ScriptableObject {
        public abstract float Evaluate(Context context);
    }
}