using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace DomainDashboard.Services;

public sealed partial class DomainChecker
{
    private const int ResponseBufferSize = 8192;

    private readonly IOptions<DashboardOptions> _options;
    private readonly Lazy<X509Certificate2?> _customCa;

    public DomainChecker(IOptions<DashboardOptions> options, ILogger<DomainChecker> log)
    {
        _options = options;
        _customCa = new Lazy<X509Certificate2?>(() => LoadCustomCa(options.Value.CaFile, log));
    }

    [GeneratedRegex(@"^HTTP/\d(?:\.\d)?\s+(\d{3})")]
    private static partial Regex StatusLine();

    private sealed class Probe
    {
        public int? Code;
        public long? LatencyMs;
        public string? Ip;
        public bool? CertValid;
        public int? CertDays;
        public string? CertError;
        public string? Error;
    }

    public async Task<DomainStatus> CheckAsync(DomainStatus entry, CancellationToken ct)
    {
        var o = _options.Value;
        var port = entry.Https ? o.HttpsPort : o.HttpPort;
        var probe = new Probe();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(o.CheckTimeout));

        try
        {
            await ProbeAsync(entry, port, probe, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            probe.Error = $"No response within {o.CheckTimeout:0.#} s";
        }
        catch (SocketException ex)
        {
            probe.Error = DescribeSocketError(ex, port);
        }
        catch (AuthenticationException ex)
        {
            probe.Error = $"TLS error: {ex.InnerException?.Message ?? ex.Message}";
        }
        catch (IOException ex)
        {
            probe.Error = ex.InnerException?.Message ?? ex.Message;
        }

        var result = entry with
        {
            Url = BuildUrl(entry, port),
            Code = probe.Code,
            LatencyMs = probe.LatencyMs,
            Ip = probe.Ip,
            CertValid = probe.CertValid,
            CertDays = probe.CertDays,
            CertError = probe.CertError,
            Error = probe.Error,
        };
        return result with { State = Classify(result, o) };
    }

    private async Task ProbeAsync(DomainStatus entry, int port, Probe probe, CancellationToken ct)
    {
        var o = _options.Value;
        var target = string.IsNullOrWhiteSpace(o.CheckViaIp) ? entry.Domain : o.CheckViaIp.Trim();
        var stopwatch = Stopwatch.StartNew();

        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(target, port, ct);
        probe.Ip = GetRemoteIp(socket);

        await using var network = new NetworkStream(socket, ownsSocket: false);
        await using var ssl = entry.Https
            ? new SslStream(network, leaveInnerStreamOpen: true)
            : null;
        if (ssl is not null)
            await ssl.AuthenticateAsClientAsync(CreateTlsOptions(entry.Domain, probe), ct);
        Stream stream = ssl is not null ? ssl : network;

        var request = Encoding.ASCII.GetBytes(
            $"GET {entry.Path} HTTP/1.1\r\nHost: {entry.Domain}\r\nUser-Agent: domain-dashboard/1.0\r\n"
                + "Accept: */*\r\nConnection: close\r\n\r\n"
        );
        await stream.WriteAsync(request, ct);
        await stream.FlushAsync(ct);

        var firstLine = await ReadFirstLineAsync(stream, ct);
        probe.LatencyMs = stopwatch.ElapsedMilliseconds;

        var match = StatusLine().Match(firstLine);
        if (match.Success)
            probe.Code = int.Parse(match.Groups[1].Value);
        else
            probe.Error = "No valid HTTP response";
    }

    private SslClientAuthenticationOptions CreateTlsOptions(string host, Probe probe) =>
        new()
        {
            TargetHost = host,
            RemoteCertificateValidationCallback = (_, cert, chain, errors) =>
            {
                (probe.CertValid, probe.CertError) = EvaluateCertificate(cert, chain, errors);
                if (cert is not null)
                    probe.CertDays = (int)
                        Math.Floor(
                            (
                                ToCertificate2(cert).NotAfter.ToUniversalTime() - DateTime.UtcNow
                            ).TotalDays
                        );
                return true;
            },
        };

    private static async Task<string> ReadFirstLineAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[ResponseBufferSize];
        var length = 0;

        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), ct);
            if (read == 0)
                break;
            length += read;
            if (buffer.AsSpan(0, length).IndexOf("\r\n"u8) >= 0)
                break;
        }

        return Encoding.Latin1.GetString(buffer, 0, length).Split("\r\n")[0];
    }

    private (bool valid, string? error) EvaluateCertificate(
        X509Certificate? cert,
        X509Chain? chain,
        SslPolicyErrors errors
    )
    {
        if (errors == SslPolicyErrors.None)
            return (true, null);
        if (cert is null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
            return (false, "no certificate");
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
            return (false, "name does not match certificate");
        if (IsTrustedByCustomCa(cert))
            return (true, null);

        var statuses = chain?.ChainStatus.Select(s => s.Status).ToList() ?? [];
        if (statuses.Contains(X509ChainStatusFlags.NotTimeValid))
            return (false, "expired");
        if (
            statuses.Contains(X509ChainStatusFlags.UntrustedRoot)
            || statuses.Contains(X509ChainStatusFlags.PartialChain)
        )
            return (false, "untrusted (self-signed or unknown CA)");
        return (false, string.Join(", ", statuses));
    }

    private bool IsTrustedByCustomCa(X509Certificate cert)
    {
        if (_customCa.Value is not { } ca)
            return false;

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(ToCertificate2(cert));
    }

    private static DomainState Classify(DomainStatus r, DashboardOptions o)
    {
        if (r.Code is null || r.IsServerError)
            return DomainState.Down;
        if (
            !r.IsRouterEnabled
            || r.CertValid == false
            || r.CertExpiresWithin(o.CertWarnDays)
            || r.IsClientError
        )
            return DomainState.Warn;
        return DomainState.Up;
    }

    private static string BuildUrl(DomainStatus entry, int port)
    {
        var scheme = entry.Https ? "https" : "http";
        var defaultPort = entry.Https ? 443 : 80;
        var portPart = port == defaultPort ? "" : $":{port}";
        var path = entry.Path == "/" ? "" : entry.Path;
        return $"{scheme}://{entry.Domain}{portPart}{path}";
    }

    private static string? GetRemoteIp(Socket socket) =>
        socket.RemoteEndPoint is IPEndPoint remote
            ? (
                remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address
            ).ToString()
            : null;

    private static string DescribeSocketError(SocketException ex, int port) =>
        ex.SocketErrorCode switch
        {
            SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData =>
                "Name could not be resolved (DNS)",
            SocketError.ConnectionRefused => $"Connection refused on port {port}",
            _ => ex.Message,
        };

    private static X509Certificate2 ToCertificate2(X509Certificate cert) =>
        cert as X509Certificate2 ?? X509CertificateLoader.LoadCertificate(cert.GetRawCertData());

    private static X509Certificate2? LoadCustomCa(string? file, ILogger log)
    {
        if (string.IsNullOrWhiteSpace(file))
            return null;
        try
        {
            return X509CertificateLoader.LoadCertificate(File.ReadAllBytes(file));
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Could not load CA file {File}", file);
            return null;
        }
    }
}
