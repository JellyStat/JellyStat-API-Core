using Jellystat_API_Core.Enums;

namespace Jellystat_API_Core.Models.Jellystat
{
    public class SystemInfoModel
    {
        public SystemState State { get; set; } = SystemState.Unconfigured;

        public string Version { get; set; }
        public string UITag { get; set; } = string.Empty;

        public SystemInfoModel(SystemState State, string Version)
        {
            this.State = State;
            this.Version = Version;
        }
    }
}
