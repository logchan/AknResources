using System;
using System.Net;

namespace AknResources {
    internal class AknWebClient: WebClient {
        private readonly Config _config;

        public AknWebClient(Config config) {
            _config = config;
        }

        protected override WebRequest GetWebRequest(Uri uri) {
            var baseReq = base.GetWebRequest(uri);
            if (!(baseReq is HttpWebRequest req))
                return baseReq;

            req.Accept = "*/*";
            req.Headers[HttpRequestHeader.AcceptLanguage] = "en-us";
            req.Headers[HttpRequestHeader.AcceptEncoding] = "deflate";
            req.Headers["X-Unity-Version"] = _config.UnityVersion;
            req.UserAgent = _config.UserAgent;
            req.AutomaticDecompression = DecompressionMethods.All;

            return baseReq;
        }
    }
}
