﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Dataline.HealthInsurance.ContributionRateImport.V5_1.Loaders
{
    /// <summary>
    /// Herunterladen der Beitragssatzdatei vom ITSG-Server
    /// </summary>
    public class ItsgServerLoader : WebBeitragssatzdateiLoader
    {
        private static readonly Uri _baseUri = new Uri(@"https://beitragssatz.itsg.de/Downloads/");

        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="deserializer"></param>
        /// <param name="proxy"></param>
        public ItsgServerLoader(IDeserializer deserializer, IWebProxy proxy)
            : base(deserializer, proxy)
        {
        }

        private HttpClient CreateClient(Uri baseUrl = null)
        {
            var handler = new HttpClientHandler();
            if (handler.SupportsRedirectConfiguration)
            {
                handler.AllowAutoRedirect = true;
            }
            if (Proxy != null && handler.SupportsProxy)
            {
                handler.Proxy = Proxy;
                handler.UseProxy = true;
            }
            var client = new HttpClient(handler);
            if (baseUrl != null)
                client.BaseAddress = baseUrl;
            return client;
        }

        /// <summary>
        /// Informationen für eine bestimmte Beitragssatzdatei-Version laden (abbrechbar)
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        public override async Task<BeitragssatzdateiInfo> LoadInfoAsync(CancellationToken ct)
        {
            using (var client = CreateClient(_baseUri))
            {
                var request = new HttpRequestMessage(HttpMethod.Head, @"DownloadGes51.aspx");
                var response = await client.SendAsync(request, ct);
                var fileName = response.EnsureSuccessStatusCode().Content.Headers.ContentType.Parameters.Where(x => x.Name == "filename").Select(x => x.Value).SingleOrDefault();
                if (string.IsNullOrEmpty(fileName))
                    throw new InvalidOperationException("Der ITSG-Server lieferte keinen Dateinamen für die Beitragssatzdatei.");
                var length = response.Content.Headers.ContentLength;
                return new BeitragssatzdateiInfo(fileName, length);
            }
        }

        /// <summary>
        /// Laden der Beitragssatzdateien (abbrechbar)
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        public override async Task<Beitragssatzdatei> LoadAsync(CancellationToken ct)
        {
            var buffer = new byte[10000];

            OnDownloadStarting();
            try
            {
                using (var client = CreateClient(_baseUri))
                {
                    using (var response = await client.GetAsync(@"DownloadGes51.aspx", HttpCompletionOption.ResponseHeadersRead, ct))
                    {
                        var fileName = response
                            .EnsureSuccessStatusCode()
                            .Content.Headers.ContentType.Parameters.Where(x => x.Name == "filename")
                            .Select(x => x.Value)
                            .SingleOrDefault();
                        if (string.IsNullOrEmpty(fileName))
                            throw new InvalidOperationException("Der ITSG-Server lieferte keinen Dateinamen für die Beitragssatzdatei.");

                        var length = response.Content.Headers.ContentLength;
                        var info = new BeitragssatzdateiInfo(fileName, length);

                        OnFileDownloadStarting(info);
                        try
                        {
                            using (var data = new MemoryStream())
                            {
                                using (var stream = await response.Content.ReadAsStreamAsync())
                                {
                                    long fileBytesRead = 0;
                                    int bufferBytesRead;
                                    while ((bufferBytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) != 0)
                                    {
                                        data.Write(buffer, 0, bufferBytesRead);
                                        fileBytesRead += bufferBytesRead;
                                        OnFileDownloadProgress(fileBytesRead);
                                    }

                                    data.Position = 0;
                                }

                                ILocalLoader loader;
                                if (string.Equals(info.Format, "xml", StringComparison.OrdinalIgnoreCase))
                                {
                                    loader = new StreamLoader(Deserializer);
                                }
                                else
                                {
                                    Debug.Assert(string.Equals(info.Format, "zip", StringComparison.OrdinalIgnoreCase),
                                        $"Unbekanntes Dateiformat {info.Format}");
                                    loader = new ZipArchiveLoader(Deserializer);
                                }

                                return await loader.LoadAsync(data, ct);
                            }
                        }
                        finally
                        {
                            OnFileDownloadFinished();
                        }
                    }
                }
            }
            finally
            {
                OnDownloadFinished();
            }
        }
    }
}