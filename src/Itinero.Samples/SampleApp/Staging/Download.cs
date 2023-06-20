using System;
using System.IO;
using System.Net;
using Microsoft.Extensions.Hosting.Internal;

namespace Itinero.Samples.Staging;

internal static class Download
{
    public static async Task<string> Get(string localFile, string url)
    {
        if (File.Exists(localFile))
        {
            return localFile;
        }

        HttpClient client = new HttpClient();
        HostingEnvironment he = new HostingEnvironment();

        var response = await client.GetAsync(new Uri(url));
        using (var fs = new FileStream(localFile, FileMode.CreateNew))
        {
            await response.Content.CopyToAsync(fs);
        }
        return localFile;
    }
}
