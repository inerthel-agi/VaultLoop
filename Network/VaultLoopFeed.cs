using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Xml;

namespace ReplayGlitchGTA;

internal enum FeedKind { Compatibility, Release }

internal sealed class VaultLoopFeed(FeedKind kind)
{
    internal const string ProjectUrl = "https://github.com/inerthel-agi/VaultLoop/";
    // The numeric repository ID survives GitHub account renames.
    private const string Api = "https://api.github.com/repositories/1312917488/";
    private const int MaximumBytes = 128 * 1024;
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false
    }) { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = MaximumBytes };
    private readonly string _cacheFile = kind == FeedKind.Release ? "latest-release.cache" : "compatibility.cache";
    private string? _lastJson;
    private DateTime _checkedUtc;
    private bool _loaded;

    internal string Read(bool checkOnline, Version installed, CancellationToken token) =>
        Read(checkOnline, installed, token, () => AppSettingsStorage.ReadBoundedCurrentText(_cacheFile, MaximumBytes + 128),
            body => AppSettingsStorage.WriteText(_cacheFile, body), () => Fetch(kind, token));

    internal string Read(bool checkOnline, Version installed, CancellationToken token,
        Func<string?> loadCache, Action<string> saveCache, Func<string> fetch)
    {
        token.ThrowIfCancellationRequested();
        if (!_loaded)
        {
            _loaded = true;
            try
            {
                var saved = loadCache();
                var newline = saved?.IndexOf('\n') ?? -1;
                if (newline > 0 && DateTime.TryParseExact(saved!.Substring(0, newline), "O",
                        CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp) &&
                    timestamp.Kind == DateTimeKind.Utc && timestamp <= DateTime.UtcNow)
                {
                    var body = saved.Substring(newline + 1);
                    Format(kind, body, installed, DateTime.UtcNow);
                    _lastJson = body;
                    _checkedUtc = timestamp;
                }
            }
            catch (Exception) { /* Ignore an unreadable or incompatible cache. */ }
        }
        string status = "";
        if (checkOnline)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var body = fetch();
                token.ThrowIfCancellationRequested();
                var now = DateTime.UtcNow;
                Format(kind, body, installed, now);
                token.ThrowIfCancellationRequested();
                _lastJson = body;
                _checkedUtc = now;
                try
                {
                    saveCache(now.ToString("O", CultureInfo.InvariantCulture) + "\n" + body);
                }
                catch (Exception) { status = "Available for this session only.\r\n"; }
            }
            catch (FileNotFoundException) when (kind == FeedKind.Compatibility && !token.IsCancellationRequested)
            {
                status = "Online catalog not found at the project URL.\r\n";
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                status = "Online check unsuccessful.\r\n";
            }
        }
        token.ThrowIfCancellationRequested();
        if (_lastJson is not null)
            return status + $"GitHub | Last successful check: {_checkedUtc:u}\r\n" +
                Format(kind, _lastJson, installed, DateTime.UtcNow);
        if (kind == FeedKind.Release)
            return status + $"Installed: v{installed.ToString(3)}\r\nLatest stable release: unknown.";
        return status + "Source: catalog included with VaultLoop\r\n" +
            Format(kind, BundledCatalog(), installed, DateTime.UtcNow);
    }

    private static string Fetch(FeedKind kind, CancellationToken token)
    {
        var release = kind == FeedKind.Release;
        using var request = new HttpRequestMessage(HttpMethod.Get, Api + (release ?
            "releases/latest" : "contents/docs/compatibility.json?ref=main"));
        request.Headers.UserAgent.ParseAdd("VaultLoop");
        request.Headers.Accept.ParseAdd(release ? "application/vnd.github+json" : "application/vnd.github.raw+json");
        using var response = Client.SendAsync(request, HttpCompletionOption.ResponseContentRead, token)
            .GetAwaiter().GetResult();
        if (!release && response.StatusCode == HttpStatusCode.NotFound)
            throw new FileNotFoundException();
        if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException();
        var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    internal static string BundledCatalog()
    {
        using var stream = typeof(VaultLoopFeed).Assembly.GetManifestResourceStream("VaultLoop.compatibility.json")
            ?? throw new InvalidDataException("Missing bundled compatibility catalog.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static bool IsProjectLink(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host == "github.com" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        uri.Query.Length == 0 && (uri.AbsoluteUri.StartsWith(ProjectUrl + "blob/", StringComparison.Ordinal) ||
            uri.AbsoluteUri.StartsWith(ProjectUrl + "issues/", StringComparison.Ordinal) ||
            uri.AbsoluteUri.StartsWith(ProjectUrl + "releases/tag/", StringComparison.Ordinal));

    internal static string Format(FeedKind kind, string json, Version installed, DateTime utcNow)
    {
        installed = new Version(installed.Major, installed.Minor, installed.Build);
        if (kind == FeedKind.Release)
        {
            var release = Parse<Release>(json);
            if (release.Draft != false || release.Prerelease != false || release.Tag is null)
                throw new InvalidDataException();
            var latest = ParseVersion(release.Tag.StartsWith("v", StringComparison.Ordinal)
                ? release.Tag.Substring(1) : release.Tag);
            var verdict = installed < latest ? "Update available" :
                installed == latest ? "Up to date" : "Installed version is newer";
            return $"{verdict} | Installed: v{installed} | Latest stable: v{latest}\r\n" +
                ProjectUrl + "releases/tag/" + release.Tag;
        }

        var catalog = Parse<Catalog>(json);
        if (catalog.Schema != 1 || catalog.Notices is null || catalog.Notices.Length > 64)
            throw new InvalidDataException();
        var expiry = ParseDate(catalog.ValidThrough);
        var expired = utcNow.Date > expiry;
        var result = new StringBuilder(expired ? "EXPIRED | Valid through " : "Valid through ")
            .Append(catalog.ValidThrough).Append(" UTC");
        var matches = 0;
        foreach (var notice in catalog.Notices)
        {
            if (notice is null) throw new InvalidDataException();
            var min = ParseVersion(notice.Min);
            var max = ParseVersion(notice.Max);
            var verified = ParseDate(notice.Verified);
            if (max < min || verified > expiry ||
                string.IsNullOrWhiteSpace(notice.Summary) || notice.Summary!.Length > 500 ||
                notice.Summary.Any(char.IsControl) || notice.Reference is null || !IsProjectLink(notice.Reference))
                throw new InvalidDataException();
            if (installed < min || installed > max) continue;
            matches++;
            result.AppendLine().AppendLine().Append($"v{min} to v{max} | Verified {notice.Verified}")
                .AppendLine().AppendLine(notice.Summary).Append(notice.Reference);
        }
        if (matches == 0)
            result.AppendLine().Append($"No entries for v{installed} in this catalog.");
        return result.ToString();
    }

    private static Version ParseVersion(string? text) =>
        Version.TryParse(text, out var value) && value.Build >= 0 && value.Revision < 0 &&
        value.ToString(3) == text ? value : throw new InvalidDataException();

    private static DateTime ParseDate(string? text) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
            out var date) ? date : throw new InvalidDataException();

    private static T Parse<T>(string json) where T : class
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException();
        using var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(json),
            new XmlDictionaryReaderQuotas { MaxDepth = 24, MaxStringContentLength = MaximumBytes });
        var value = new DataContractJsonSerializer(typeof(T)).ReadObject(reader) as T;
        if (value is null || reader.MoveToContent() != XmlNodeType.None) throw new InvalidDataException();
        return value;
    }

    [DataContract]
    private sealed class Release
    {
        [DataMember(Name = "tag_name")] public string? Tag { get; set; }
        [DataMember(Name = "draft")] public bool? Draft { get; set; }
        [DataMember(Name = "prerelease")] public bool? Prerelease { get; set; }
    }

    [DataContract]
    private sealed class Catalog
    {
        [DataMember(Name = "schema")] public int Schema { get; set; }
        [DataMember(Name = "validThrough")] public string? ValidThrough { get; set; }
        [DataMember(Name = "notices")] public Notice[]? Notices { get; set; }
    }

    [DataContract]
    private sealed class Notice
    {
        [DataMember(Name = "min")] public string? Min { get; set; }
        [DataMember(Name = "max")] public string? Max { get; set; }
        [DataMember(Name = "verified")] public string? Verified { get; set; }
        [DataMember(Name = "summary")] public string? Summary { get; set; }
        [DataMember(Name = "reference")] public string? Reference { get; set; }
    }
}
