using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using AssetStudio;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using Serilog;

namespace AknResources {
    public static class AssetHandler {
        private static readonly Dictionary<Type, Action<AssetStudio.Object, HandlingContext>> _handlerFunctions =
            new();

        public static void Initialize() {
            // Register all handlers
            var methods = typeof(AssetHandler).GetMethods(BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var method in methods) {
                if (!method.Name.StartsWith("Handle")) {
                    continue;
                }

                var parameters = method.GetParameters();
                if (parameters.Length != 2 || 
                    parameters[1].ParameterType != typeof(HandlingContext)) {
                    continue;
                }

                var assetType = parameters[0].ParameterType;
                if (assetType.IsAbstract || !assetType.IsSubclassOf(typeof(AssetStudio.Object))) {
                    continue;
                }

                // For a handler of signature void HandleType(Type obj, HandlingContext ctx),
                // create a proxy lambda: (AssetStudio.Object obj, HandlingContext ctx) => HandleType((Type) obj, ctx),
                // which has type Action<AssetStudio.Object, HandlingContext>, and can be stored in _handlerFunctions

                var actionParameters = new [] {
                    Expression.Parameter(typeof(AssetStudio.Object)),
                    Expression.Parameter(typeof(HandlingContext))
                };

                var action = Expression.Lambda<Action<AssetStudio.Object, HandlingContext>>(Expression.Call(
                        null,
                        method,
                        Expression.Convert(actionParameters[0], assetType),
                        actionParameters[1]
                    ), actionParameters).Compile();

                Log.Information($"Add asset handler for: {assetType.Name}");
                _handlerFunctions[assetType] = action;
            }
        }

        public static bool TryExtractAsset(AssetStudio.Object obj, HandlingContext ctx) {
            if (!_handlerFunctions.TryGetValue(obj.GetType(), out var action)) {
                return false;
            }

            action(obj, ctx);
            return true;
        }

        private static string GetFullName(string name, string extension, HandlingContext ctx) {
            Directory.CreateDirectory(ctx.Directory);
            return Path.Combine(ctx.Directory, String.Concat(ctx.NameOverride ?? name, ctx.ExtensionOverride ?? extension));
        }

        private static void HandleTexture2D(Texture2D obj, HandlingContext ctx) {
            var fullName = GetFullName(obj.m_Name, ".png", ctx);

            using var ms = obj.ConvertToStream(ImageFormat.Png, true);
            if (ms == null) {
                Log.Warning($"Failed to export: {fullName}");
                return;
            }
            File.WriteAllBytes(fullName, ms.ToArray());
        }

        private static readonly object _audioConverterLock = new ();
        private static void HandleAudioClip(AudioClip obj, HandlingContext ctx) {
            var converter = new AudioClipConverter(obj);
            var convert = ctx.ConvertAudio && converter.IsSupport;
            var extension = convert ? ".wav" : converter.GetExtensionName();
            var fullName = GetFullName(obj.m_Name, extension, ctx);

            byte[] data;
            if (convert) {
                lock (_audioConverterLock) {
                    data = converter.ConvertToWav();
                }
            }
            else {
                data = obj.m_AudioData.GetData();
            }

            if (data == null) {
                Log.Warning($"Failed to export: {fullName}");
                return;
            }
            File.WriteAllBytes(fullName, data);
        }

        private static readonly Dictionary<string, int> _gameDataOffsets = new() {
            ["[uc]lua.ab"] = 128,
            ["excel"] = 128,
            ["battle"] = 128,
            ["buff_table"] = 128,
        };

        private static void HandleTextAsset(TextAsset obj, HandlingContext ctx) {
            var data = obj.m_Script;
            var isGameData = ctx.AbPath.StartsWith("gamedata");
            var needDecrypt = ctx.DecryptKey != null &&
                              ctx.DecryptIvMask != null &&
                              isGameData &&
                              ctx.ExtensionOverride == ".bytes";
            var gameDataDir = isGameData ? ctx.AbPath.Split(Path.DirectorySeparatorChar)[1] : String.Empty;

            if (needDecrypt) {
                if (!_gameDataOffsets.TryGetValue(gameDataDir, out var offset)) {
                    offset = 0;
                }

                ArkProtocol.TryDecrypt(ctx.DecryptKey, ctx.DecryptIvMask, data, offset, out data);
            }

            // try to find the actual extension
            var nameExt = Path.GetExtension(obj.m_Name);
            var name = obj.m_Name.Substring(0, obj.m_Name.Length - nameExt.Length);
            if (nameExt == String.Empty) {
                nameExt = isGameData && gameDataDir != "story" ? ".json" : ".txt";
            }

            // new format
            if (gameDataDir == "excel" && obj.m_Name != "data_version" && LooksLikeBsonData(data)) {
                Log.Information($"Treat {name} as BSON");

                using var ms = new MemoryStream(data);
                using var reader = new BsonDataReader(ms);
                var serializer = new JsonSerializer();
                var bsonObj = serializer.Deserialize(reader);
                
                data = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(bsonObj, Formatting.Indented));
            }

            if (gameDataDir == "excel" && obj.m_Name == "data_version") {
                nameExt = "";
            }

            ctx.NameOverride = null;
            ctx.ExtensionOverride = null;
            var fullName = GetFullName(name, nameExt, ctx);

            File.WriteAllBytes(fullName, data);
        }

        private static bool LooksLikeBsonData(byte[] data) {
            if (data.Length < 4) {
                return false;
            }

            var length = data[0] + data[1] * (1 << 8) + data[2] * (1 << 16) + data[3] * (1 << 24);
            return length == data.Length;
        }

        private static void HandleSprite(Sprite obj, HandlingContext ctx) {
            var fullName = GetFullName(obj.m_Name, ".png", ctx);

            using var ms = obj.GetImage(ImageFormat.Png);
            if (ms == null) {
                Log.Warning($"Failed to export: {fullName}");
                return;
            }
            File.WriteAllBytes(fullName, ms.ToArray());
        }

        public class HandlingContext {
            public string Directory { get; set; }
            public string AbPath { get; set; }
            public string NameOverride { get; set; }
            public string ExtensionOverride { get; set; }
            public DateTime BundleTime { get; set; }
            public string DecryptKey { get; set; }
            public string DecryptIvMask { get; set; }
            public bool ConvertAudio { get; set; }
            public bool Verbose { get; set; }
        }
    }
}
