using System.Net;
using System.Net.Http;
using System.Security.Authentication;

var url = "https://www.okx.com/api/v5/public/time";
var cases = new (string Name, SslProtocols? Protocols)[]
{
    ("default", null),
    ("tls12", SslProtocols.Tls12),
    ("tls13", SslProtocols.Tls13),
    ("tls12+tls13", SslProtocols.Tls12 | SslProtocols.Tls13),
};

foreach (var item in cases)
{
    try
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(15),
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck
            }
        };
        if (item.Protocols is { } protocols)
            handler.SslOptions.EnabledSslProtocols = protocols;

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        using var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"{item.Name}: {(int)response.StatusCode} {body[..Math.Min(100, body.Length)]}");
    }
    catch (Exception ex)
    {
        var root = ex;
        while (root.InnerException is not null) root = root.InnerException;
        Console.WriteLine($"{item.Name}: ERROR {root.GetType().Name}: {root.Message}");
    }
}
