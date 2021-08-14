using System;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace AknResources {
    public static class ArkProtocol {
        public static bool TryDecrypt(string keyString, string ivMaskString, byte[] src, int offset, out byte[] result) {
            try {
                var key = Encoding.UTF8.GetBytes(keyString);
                var ivMask = Encoding.UTF8.GetBytes(ivMaskString);
                var iv = new byte[16];

                for (var i = 0; i < 16; ++i) {
                    iv[i] = (byte) (src[i + offset] ^ ivMask[i]);
                }

                using var aes = Aes.Create();
                aes.Key = key;
                aes.IV = iv;
                using var decryptor = aes.CreateDecryptor();
                result = decryptor.TransformFinalBlock(src, 16 + offset, src.Length - 16 - offset);
                return true;
            }
            catch (Exception ex) {
                Log.Error(ex, "Failed to decrypt");
                result = src;
                return false;
            }
        }
    }
}
