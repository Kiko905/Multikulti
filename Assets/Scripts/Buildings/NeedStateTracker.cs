using System;

namespace Multikulti.Buildings
{
    public sealed class NeedStateTracker
    {
        readonly float m_Threshold;
        readonly float m_HysteresisSeconds;
        float m_PendingSince = -1f;

        public Need Need { get; }
        public NeedState State { get; private set; } = NeedState.Ok;

        public event Action<Need, NeedState, NeedState> StateChanged;

        public NeedStateTracker(Need need, float threshold, float hysteresisSeconds)
        {
            if (threshold <= 0f) throw new ArgumentOutOfRangeException(nameof(threshold));
            if (hysteresisSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(hysteresisSeconds));
            Need = need;
            m_Threshold = threshold;
            m_HysteresisSeconds = hysteresisSeconds;
        }

        public void Reset()
        {
            m_PendingSince = -1f;
            SetState(NeedState.Ok);
        }

        public void Update(float now, int backlog, bool allServed)
        {
            if (State == NeedState.Satisfied) return;

            if (allServed)
            {
                m_PendingSince = -1f;
                SetState(NeedState.Satisfied);
                return;
            }

            var target = Next(State, backlog >= m_Threshold);
            if (target == State)
            {
                m_PendingSince = -1f;
                return;
            }

            if (NeedsHysteresis(State, target))
            {
                if (m_PendingSince < 0f) m_PendingSince = now;
                if (now - m_PendingSince < m_HysteresisSeconds) return;
            }

            m_PendingSince = -1f;
            SetState(target);
        }

        static NeedState Next(NeedState state, bool overThreshold) => state switch
        {
            NeedState.Ok => overThreshold ? NeedState.Bottleneck : NeedState.Ok,
            NeedState.Bottleneck => overThreshold ? NeedState.Bottleneck : NeedState.Recovering,
            NeedState.Recovering => overThreshold ? NeedState.Bottleneck : NeedState.Ok,
            _ => state
        };

        static bool NeedsHysteresis(NeedState from, NeedState to) =>
            (from == NeedState.Ok && to == NeedState.Bottleneck) ||
            (from == NeedState.Recovering && to == NeedState.Ok);

        void SetState(NeedState next)
        {
            if (next == State) return;
            var previous = State;
            State = next;
            StateChanged?.Invoke(Need, previous, next);
        }
    }
}
