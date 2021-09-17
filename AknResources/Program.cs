﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Serilog;

namespace AknResources {
    internal class Program {
        private static void Main(string[] args) {
            Console.OutputEncoding = Encoding.UTF8;

            Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
            AssetHandler.Initialize();

            var config = LoadConfig();
            var manager = new ResourceManager(config);
            foreach (var server in config.Servers) {
                Log.Information($"Process server: {server}, url: {ResourceManager.Servers[server]}");
                if (!config.DecryptKeys.TryGetValue(server, out var keys)) {
                    keys = new List<string>();
                    config.DecryptKeys[server] = keys;
                }

                while (keys.Count < 2) {
                    keys.Add(null);
                }

                var version = manager.GetLatestVersion(server);
                Log.Information($"Latest version: {version}");
                manager.DownloadFiles(server, version);
                manager.ExtractFiles(server, version);
                manager.ExtractAssets(server);
            }
        }

        private static Config LoadConfig() {
            var cfg = new Config();
            if (File.Exists("config.json")) {
                var json = File.ReadAllText("config.json");
                cfg = JsonSerializer.Deserialize<Config>(json);
            }

            return cfg;
        }
    }
}