using System;
using System.Net.Http;

namespace BCCL.Core.IO.Net.Http;

public static class HttpBasicExtension
{
    extension(HttpRequestMessage requestMessage)
    {
        public HttpRequestMessage WithHttpVersionOption(Version httpVersion)
        {
            requestMessage.Version = httpVersion;
            return requestMessage;
        }
    }
}
