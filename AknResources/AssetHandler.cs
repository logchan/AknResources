using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using AssetStudio;
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

        private static void HandleTexture2D(Texture2D obj, HandlingContext ctx) {
            var fullName = Path.Combine(ctx.Directory, obj.m_Name + ".png");

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
            var fullName = Path.Combine(ctx.Directory, obj.m_Name + extension);

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

        private static readonly string[] _unencryptedGameData = {"story", "art.ab", "building.ab", "data_version.ab"};
        private static void HandleTextAsset(TextAsset obj, HandlingContext ctx) {
            var fullName = Path.Combine(ctx.Directory, obj.m_Name + ".txt");

            var data = obj.m_Script;
            var needDecrypt = ctx.DecryptKey != null && ctx.DecryptIvMask != null &&
                              ctx.Directory.Contains("gamedata") &&
                              _unencryptedGameData.All(name => !fullName.Contains(name + Path.DirectorySeparatorChar));

            if (needDecrypt) {
                ArkProtocol.TryDecrypt(ctx.DecryptKey, ctx.DecryptIvMask, data, 128, out data);
            }
            File.WriteAllBytes(fullName, data);
        }

        public class HandlingContext {
            public string Directory { get; set; }
            public DateTime BundleTime { get; set; }
            public string DecryptKey { get; set; }
            public string DecryptIvMask { get; set; }
            public bool ConvertAudio { get; set; }
            public bool Verbose { get; set; }
        }
    }
}
