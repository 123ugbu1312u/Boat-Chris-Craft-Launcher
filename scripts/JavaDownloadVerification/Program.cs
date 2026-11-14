using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using BCCL.JavaDownloads;

var root = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? throw new ArgumentException("Specify a temporary verification directory."));
Directory.CreateDirectory(root);
void Check(bool result, string name) { if (!result) throw new Exception(name); Console.WriteLine("PASS: " + name); }
byte[] Package(params string[] paths)
{
    using var bytes = new MemoryStream();
    using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        foreach (var path in paths) { using var file = new StreamWriter(archive.CreateEntry(path).Open()); file.Write("fixture"); }
    return bytes.ToArray();
}
var contents = Package("jdk-test/bin/java.exe", "jdk-test/bin/javac.exe", "jdk-test/lib/modules", "jdk-test/legal/LICENSE");
var portReservation = new TcpListener(IPAddress.Loopback, 0);
portReservation.Start();
var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
portReservation.Stop();
using var server = new HttpListener();
server.Prefixes.Add($"http://localhost:{port}/");
server.Start();
var requests = 0;
var serving = Task.Run(async () =>
{
    try
    {
        while (true)
        {
            var context = await server.GetContextAsync();
            Interlocked.Increment(ref requests);
            context.Response.ContentLength64 = contents.Length;
            await context.Response.OutputStream.WriteAsync(contents);
            context.Response.Close();
        }
    }
    catch (Exception) when (!server.IsListening) { }
});
var package = new JavaPackage($"http://localhost:{port}/jdk.zip", Convert.ToHexString(SHA256.HashData(contents)));
var destination = Path.Combine(root, "中文 目录", "Java21");
Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
var progress = new Progress<string>();
await JavaDownloadService.InstallAsync(package, destination, progress, CancellationToken.None);
Check(File.Exists(Path.Combine(destination, "bin/java.exe")) && File.Exists(Path.Combine(destination, "bin/javac.exe")) &&
    File.Exists(Path.Combine(destination, "lib/modules")) && File.Exists(Path.Combine(destination, "legal/LICENSE")), "complete JDK layout, licenses, Chinese/spaced paths and flattened archive root");
try { await JavaDownloadService.InstallAsync(package, destination, progress, CancellationToken.None); throw new Exception("Existing destination overwritten"); }
catch (IOException) { Check(requests == 1, "existing installations are preserved without starting another download"); }
try { await JavaDownloadService.InstallAsync(package with { Sha256 = new string('0', 64) }, Path.Combine(root, "BadHash"), progress, CancellationToken.None); throw new Exception("Invalid checksum accepted"); }
catch (InvalidDataException) { Check(!Directory.Exists(Path.Combine(root, "BadHash")), "checksum mismatch leaves no installed directory"); }
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try { await JavaDownloadService.InstallAsync(package, Path.Combine(root, "Cancelled"), progress, cancelled.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { Check(!Directory.Exists(Path.Combine(root, "Cancelled")), "cancellation leaves no partial installation"); }
}
var attack = Path.Combine(root, "traversal.zip");
await File.WriteAllBytesAsync(attack, Package("../escaped.exe"));
var extraction = Path.Combine(root, "extract");
Directory.CreateDirectory(extraction);
try { JavaDownloadService.Extract(attack, extraction, CancellationToken.None); throw new Exception("Traversal accepted"); }
catch (InvalidDataException) { Check(!File.Exists(Path.Combine(root, "escaped.exe")), "ZIP traversal rejected"); }
Check(!Directory.EnumerateDirectories(root, ".bccl-java-*", SearchOption.AllDirectories).Any(), "temporary files removed after success, failure and cancellation");
server.Stop();
await serving;
if (args.Contains("--live"))
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    var versions = await JavaDownloadService.GetVersionsAsync(deadline.Token);
    Check(new[] { 8, 11, 17, 21, 25, 26, 27 }.All(versions.Contains), "live official version catalog covers requested majors");
    var openJdk = await JavaDownloadService.ResolveAsync(8, JavaVendor.OpenJdk, deadline.Token);
    Check(openJdk.Url.StartsWith("https://") && openJdk.Sha256.Length == 64, "live Temurin Java 8 Windows JDK metadata and checksum");
    var oracle = await JavaDownloadService.ResolveAsync(versions.Max(), JavaVendor.Oracle, deadline.Token);
    Check(oracle.Url.StartsWith("https://download.oracle.com/") && oracle.Sha256.Length == 64, "live Oracle latest Windows JDK URL and checksum");
    try { await JavaDownloadService.ResolveAsync(8, JavaVendor.Oracle, deadline.Token); throw new Exception("Gated Oracle package accepted"); }
    catch (OracleDownloadUnavailableException) { Console.WriteLine("PASS: Oracle login-required version reports explicit fallback"); }
    if (args.Contains("--install"))
    {
        var actual = Path.Combine(root, "LiveOracle", $"Java{versions.Max()}");
        Directory.CreateDirectory(Path.GetDirectoryName(actual)!);
        await JavaDownloadService.InstallAsync(oracle, actual, progress, deadline.Token);
        Check(File.Exists(Path.Combine(actual, "bin/java.exe")) && File.Exists(Path.Combine(actual, "bin/javac.exe")),
            "real Oracle JDK downloaded, checksum verified and fully unpacked into the requested layout");
    }
}
