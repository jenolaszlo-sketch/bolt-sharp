using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using BoltSharp.Transport;

var certificate = CreateCertificate();
var unobserved = new ConcurrentBag<AggregateException>();
TaskScheduler.UnobservedTaskException += (_, args) =>
{
    unobserved.Add(args.Exception);
    args.SetObserved();
};

var runs = 0;
for (var round = 0; round < 100; round++)
{
    var server = BoltTcpServer.Create(new BoltTcpServerOptions
    {
        BindAddress = IPAddress.Loopback,
        Tls = new BoltTlsServerOptions(certificate),
        TlsHandshakeTimeout = TimeSpan.FromSeconds(30),
        ShutdownTimeout = TimeSpan.FromSeconds(5),
    });
    await server.StartAsync();

    using var client = new TcpClient(AddressFamily.InterNetwork);
    client.Connect(IPAddress.Loopback, server.BoundEndPoint!.Port);
    var wait = Stopwatch.StartNew();
    while (server.Diagnostics.ConnectionsActive != 1 && wait.ElapsedMilliseconds < 2_000)
    {
        await Task.Delay(5);
    }

    var stop = server.StopAsync();
    var done = await Task.WhenAny(stop, Task.Delay(10_000));
    if (done == stop)
    {
        await stop;
        runs++;
    }

    await server.DisposeAsync();
}

for (var i = 0; i < 5; i++)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    await Task.Delay(50);
}

Console.WriteLine($"runs={runs}/100 unobserved={unobserved.Count}");
foreach (var exception in unobserved.Take(2))
{
    Console.WriteLine(exception.Flatten().InnerExceptions[0].Message);
}

certificate.Dispose();

static X509Certificate2 CreateCertificate()
{
    using var rsa = RSA.Create(2048);
    var request = new CertificateRequest(
        "CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    var certificate = request.CreateSelfSigned(
        DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
    return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
}
