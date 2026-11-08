using UnityEngine;
using UnityEngine.InputSystem;

namespace Multikulti.Waves
{
    /// <summary>
    /// Keyboard shortcuts to walk through the wave loop in the graybox scene (SCRUM-26).
    /// L = next step of the loop, P = pause / resume (simulates the app going to the background).
    /// Keys used elsewhere: Space, W, S, E (BuildingDebugInput), N (WorkerManagerDebugInput).
    /// Resolving by L skips the real end-of-wave check until SCRUM-25 drives it.
    /// </summary>
    [RequireComponent(typeof(WaveLoop))]
    public sealed class WaveLoopDebugInput : MonoBehaviour
    {
        WaveLoop m_Loop;

        void Start() => m_Loop = GetComponent<WaveLoop>();

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            var machine = m_Loop.Machine;

            if (keyboard.pKey.wasPressedThisFrame)
            {
                if (!machine.Pause()) machine.Resume();
            }

            if (!keyboard.lKey.wasPressedThisFrame) return;
            switch (machine.State)
            {
                case WaveState.AwaitingWave when machine.CanStartWave:
                    machine.StartWave();
                    break;
                case WaveState.WaveIncoming:
                    machine.GuestsArrived();
                    break;
                case WaveState.WaveActive:
                    machine.Resolve();
                    break;
                case WaveState.WaveComplete:
                    machine.Claim();
                    break;
                case WaveState.Upgrade:
                    machine.FinishUpgrade();
                    break;
            }
        }
    }
}
