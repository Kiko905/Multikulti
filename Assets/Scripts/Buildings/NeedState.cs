namespace Multikulti.Buildings
{
    public enum NeedState
    {
        Ok,
        Bottleneck,
        Recovering,
        Satisfied
    }

    public static class NeedStateNames
    {
        public static string ToJiraName(this NeedState state) => state switch
        {
            NeedState.Ok => "OK",
            NeedState.Bottleneck => "BOTTLENECK",
            NeedState.Recovering => "RECOVERING",
            NeedState.Satisfied => "SATISFIED",
            _ => state.ToString()
        };
    }
}
