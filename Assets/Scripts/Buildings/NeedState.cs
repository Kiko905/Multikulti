namespace Multikulti.Buildings
{
    public enum NeedState
    {
        Ok,
        Bottleneck,
        Recovering,
        Satisfied,
        /// <summary>The building of this need is not bought yet: no demand, not on the panel (SCRUM-34).</summary>
        Locked
    }

    public static class NeedStateNames
    {
        public static string ToJiraName(this NeedState state) => state switch
        {
            NeedState.Ok => "OK",
            NeedState.Bottleneck => "BOTTLENECK",
            NeedState.Recovering => "RECOVERING",
            NeedState.Satisfied => "SATISFIED",
            NeedState.Locked => "LOCKED",
            _ => state.ToString()
        };
    }
}
