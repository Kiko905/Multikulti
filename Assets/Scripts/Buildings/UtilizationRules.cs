using Multikulti.Config;

namespace Multikulti.Buildings
{
    public static class UtilizationRules
    {
        public static float For(bool hasWorker, ServiceSettings service)
            => hasWorker ? service.UtilizationWorker : service.UtilizationEmpty;
    }
}
