using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using AssetStudio;
using Serilog;

namespace AknResources {
    internal class ResourceManager {
        public static readonly Dictionary<string, string> Servers = new() {
            ["cn"] = "ak.hycdn.cn",
            ["us"] = "ark-us-static-online.yo-star.com",
            ["jp"] = "ark-jp-static-online.yo-star.com",
        };

        private static readonly JsonSerializerOptions _jsonOptions = new() {
            PropertyNameCaseInsensitive = true,
        };

        private readonly DirectoryInfo _root;
        private readonly Config _config;

        public ResourceManager(Config config) {
            _config = config;

            _root = new DirectoryInfo(config.DataRoot);
            Directory.CreateDirectory(config.DataRoot);

            Log.Information($"ResourceManager data root: {_root.FullName}");
        }

        public string GetLatestVersion(string server) {
            using var client = new AknWebClient(_config);
            var json = server == "cn" ?
                client.DownloadString("https://ak-conf.hypergryph.com/config/prod/official/IOS/version") :
                client.DownloadString(Url(server, "version"));
            var info = JsonSerializer.Deserialize<VersionInfo>(json, _jsonOptions);

            return info.ResVersion;
        }

        public void DownloadFiles(string server, string version) {
            var updateList = GetHotUpdateList(server, version);
            var pending = new Queue<HotUpdateList.AbInfo>(updateList.AbInfos.Where(info => !File.Exists(GetRawFilePath(info.Md5))));

            var total = pending.Count;
            var count = 0;
            var taskLock = new object();
            var tasks = new Task[Math.Max(_config.Workers, 1)];
            for (var i = 0; i < tasks.Length; ++i) {
                var workerId = i + 1;
                tasks[i] = Task.Run(() => {
                    while (true) {
                        HotUpdateList.AbInfo info;
                        lock (taskLock) {
                            if (pending.Count == 0) {
                                break;
                            }

                            info = pending.Dequeue();
                            count += 1;
                        }

                        Log.Information($"[Worker {workerId}] Download {count} / {total}: {info.Name}");
                        var name = HttpUtility.UrlEncode(info.Name.Replace("#", "__").Replace("/", "_").Replace(".ab", ".dat").Replace(".mp4", ".dat"));
                        DownloadOfficialResourceFile(server, version, name, GetRawFilePath(info.Md5));
                    }
                });
            }

            Task.WaitAll(tasks);
        }

        public void ExtractFiles(string server, string version) {
            var updateList = GetHotUpdateList(server, version);
            var root = Path.Combine(_root.FullName, server, "bundles");
            Directory.CreateDirectory(root);

            var count = 0;
            foreach (var info in updateList.AbInfos) {
                count += 1;
                var rawFile = GetRawFilePath(info.Md5);
                if (!File.Exists(rawFile)) {
                    continue;
                }

                var rawFileTime = File.GetLastWriteTimeUtc(rawFile);
                using var fs = File.OpenRead(rawFile);
                using var zipFile = new ZipArchive(fs, ZipArchiveMode.Read);
                foreach (var entry in zipFile.Entries) {
                    var fullName = Path.Combine(root, entry.FullName);
                    var overwrite = !File.Exists(fullName) ||
                                    File.GetLastWriteTimeUtc(fullName) < rawFileTime;
                    if (!overwrite) {
                        continue;
                    }

                    Log.Information($"Extract {count} / {updateList.AbInfos.Count}: {entry.FullName}");
                    Directory.CreateDirectory(Path.GetDirectoryName(fullName));
                    using var zfs = entry.Open();
                    using var ofs = File.Open(fullName, FileMode.Create);
                    zfs.CopyTo(ofs);
                }
            }
        }

        public void ExtractAssets(string server) {
            bool CheckIncludeCondition(string cond, string s) {
                return cond[0] == '^' && cond.Length > 1 ? s.StartsWith(cond.Substring(1)) : s.Contains(cond);
            }

            var root = Path.Combine(_root.FullName, server, "assets");
            Directory.CreateDirectory(root);

            var stack = new Stack<DirectoryInfo>();
            var inputRoot = new DirectoryInfo(Path.Combine(_root.FullName, server, "bundles"));
            stack.Push(inputRoot);
            var pending = new Queue<FileInfo>();
            while (stack.Count > 0) {
                var di = stack.Pop();
                foreach (var childDi in di.GetDirectories().Reverse()) {
                    stack.Push(childDi);
                }

                foreach (var fi in di.GetFiles()) {
                    if (fi.Extension == ".ab") {
                        pending.Enqueue(fi);
                    }
                }
            }

            var total = pending.Count;
            var count = 0;
            var taskLock = new object();
            var tasks = new Task[Math.Max(_config.Workers, 1)];
            for (var i = 0; i < tasks.Length; ++i) {
                var workerId = i + 1;
                tasks[i] = Task.Run(() => {
                    while (true) {
                        FileInfo fi;
                        lock (taskLock) {
                            if (pending.Count == 0) {
                                break;
                            }

                            fi = pending.Dequeue();
                            count += 1;
                        }

                        var abPath = fi.FullName.Substring(inputRoot.FullName.Length + 1);
                        var skip = false;
                        var action = "Extract";

                        // skip if include / exclude list in effect
                        if (_config.Include.Count > 0 && !_config.Include.Any(cond => CheckIncludeCondition(cond, abPath))) {
                            skip = true;
                            action = "Exclude";
                        }
                        if (_config.Exclude.Count > 0 && _config.Exclude.Any(cond => CheckIncludeCondition(cond, abPath))) {
                            skip = true;
                            action = "Exclude";
                        }

                        // skip if not newer
                        var outDir = Path.Combine(root, abPath);
                        if (Directory.Exists(outDir)) {
                            var dirTime = Directory.GetCreationTimeUtc(outDir);
                            var abTime = File.GetLastWriteTimeUtc(fi.FullName);
                            if (dirTime > abTime) {
                                skip = true;
                                action = "Skip";
                            }
                            else {
                                Directory.Delete(outDir, true);
                            }
                        }

                        if (!skip || _config.VerboseExport) {
                            Log.Information($"[Worker {workerId}] {action} {count} / {total} ({abPath})");
                        }

                        if (skip) {
                            continue;
                        }

                        Directory.CreateDirectory(outDir);
                        ExtractAssetBundle(server, fi.FullName, outDir);
                    }
                });
            }

            Task.WaitAll(tasks);
        }

        private void ExtractAssetBundle(string server, string bundlePath, string root) {
            var manager = new AssetsManager();
            manager.LoadFiles(bundlePath);
            var context = new AssetHandler.HandlingContext {
                BundleTime = File.GetLastWriteTimeUtc(bundlePath),
                Directory = root,
                DecryptKey = _config.DecryptKeys[server][0],
                DecryptIvMask = _config.DecryptKeys[server][1],
                ConvertAudio = _config.ConvertAudio,
                Verbose = _config.VerboseExport,
            };

            foreach (var asset in manager.assetsFileList.SelectMany(assetsFile => assetsFile.Objects)) {
                AssetHandler.TryExtractAsset(asset, context);
            }
        }

        private string Url(string server, string path) {
            // ReSharper disable once StringLiteralTypo
            return $"https://{Servers[server]}/assetbundle/official/{_config.Platform}/{path}";
        }

        private void DownloadOfficialResourceFile(string server, string version, string name, string saveTo) {
            var url = Url(server, $"assets/{version}/{name}");

            using var client = new AknWebClient(_config);
            var data = client.DownloadData(url);
            File.WriteAllBytes(saveTo, data);
        }

        private string GetServerFilePath(string server, string version, string name) {
            var dir = Path.Combine(_root.FullName, server, version);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, name);
        }

        private string GetRawFilePath(string md5) {
            var dir = Path.Combine(_root.FullName, "raw", md5.Substring(0, 2), md5.Substring(2, 2));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, md5);
        }

        private HotUpdateList GetHotUpdateList(string server, string version) {
            var updateListFile = GetServerFilePath(server, version, "hot_update_list.json");
            if (!File.Exists(updateListFile)) {
                DownloadOfficialResourceFile(server, version, "hot_update_list.json", updateListFile);
            }

            return JsonSerializer.Deserialize<HotUpdateList>(File.ReadAllText(updateListFile), _jsonOptions);
        }

        #region Data classes
        public sealed class VersionInfo {
            public string ResVersion { get; set; }
        }

        public sealed class HotUpdateList {
            public string VersionId { get; set; }
            public List<AbInfo> AbInfos { get; set; }

            public sealed class AbInfo {
                public string Name { get; set; }
                public string Hash { get; set; }
                public string Md5 { get; set; }
                public long TotalSize { get; set; }
                public long AbSize { get ; set; }
            }
        }
        #endregion
    }
}
