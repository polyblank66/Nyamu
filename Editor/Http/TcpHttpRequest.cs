using System;
using System.Collections.Specialized;
using System.IO;
using System.Text;

namespace Nyamu.Http
{
    internal sealed class TcpHttpRequest
    {
        public string HttpMethod { get; }
        public string Body { get; }
        public TcpUrl Url { get; }

        // Compatibility properties used by existing request handlers
        public long ContentLength64 => Body.Length;
        public Encoding ContentEncoding => Encoding.UTF8;
        public Stream InputStream => new MemoryStream(Encoding.UTF8.GetBytes(Body));
        public NameValueCollection QueryString => Url.ParsedQuery;

        internal TcpHttpRequest(string method, string path, string rawQuery, string body)
        {
            HttpMethod = method;
            Body = body ?? "";
            Url = new TcpUrl(path, rawQuery);
        }

        public sealed class TcpUrl
        {
            public string AbsolutePath { get; }
            // Includes leading "?" to match HttpListenerRequest.Url.Query behaviour
            public string Query { get; }
            public NameValueCollection ParsedQuery { get; }

            internal TcpUrl(string path, string rawQuery)
            {
                AbsolutePath = path;
                Query = rawQuery.Length > 0 ? "?" + rawQuery : "";
                ParsedQuery = ParseQuery(rawQuery);
            }

            private static NameValueCollection ParseQuery(string raw)
            {
                var nvc = new NameValueCollection();
                if (string.IsNullOrEmpty(raw)) return nvc;
                foreach (var pair in raw.Split('&'))
                {
                    var idx = pair.IndexOf('=');
                    if (idx >= 0)
                        nvc[Uri.UnescapeDataString(pair.Substring(0, idx))] =
                            Uri.UnescapeDataString(pair.Substring(idx + 1));
                    else if (pair.Length > 0)
                        nvc[Uri.UnescapeDataString(pair)] = "";
                }
                return nvc;
            }
        }
    }
}
