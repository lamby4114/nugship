using UnityEngine;

namespace OreSeas
{
    /// <summary>Scene entry point (placed on the "Game" object in Main.unity). Also self-boots if the scene lacks it.</summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        void Awake() { GameApp.Boot(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot() { if (GameApp.I == null) GameApp.Boot(); }
    }
}
