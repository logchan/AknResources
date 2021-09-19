using System.Collections.Generic;

namespace AknResources {
    internal class Config {
        public string UnityVersion { get; set; } = "2017.4.39f1";
        public string UserAgent { get; set; } = "Arknights/14 CFNetwork/1240.0.4 Darwin/20.6.0";
        public string Platform { get; set; } = "IOS";
        public string Version { get; set; } = null;
        public List<string> Servers { get; set; } = new() {"cn", "us", "jp"};
        public string DataRoot { get; set; } = "Data";
        public Dictionary<string, List<string>> DecryptKeys { get; set; } = new();
        public bool ConvertAudio { get; set; } = false;
        public int Workers { get; set; } = 8;
        public bool VerboseExport { get; set; } = false;
        public List<string> Include { get; set; } = new();
        public List<string> Exclude { get; set; } = new();
    }
}
