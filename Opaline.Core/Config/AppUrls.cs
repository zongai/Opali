namespace Opaline.Core.Config;

/// <summary>Central URL constants — mirrors iOS AppURLs.</summary>
public static class AppUrls
{
    public static class YouTubeOAuth
    {
        public const string DeviceCode = "https://www.youtube.com/o/oauth2/device/code";
        public const string Token = "https://www.youtube.com/o/oauth2/token";
        /// <summary>TV login page used to scrape embedded client_id / client_secret.</summary>
        public const string TvLogin = "https://www.youtube.com/tv";
    }

    /// <summary>
    /// Remote solver-server (n-solve + GVS pot). Override via settings.
    /// </summary>
    public static class SolverServer
    {
        public const string DefaultBaseUrl =
            "https://ytlite-solver.wonderfulpond-77505dfd.westus2.azurecontainerapps.io";

        private static string? _override;

        public static string BaseUrl
        {
            get
            {
                var v = string.IsNullOrWhiteSpace(_override) ? DefaultBaseUrl : _override!;
                return v.TrimEnd('/');
            }
            set => _override = value;
        }

        public static Uri? Endpoint(string path) =>
            string.IsNullOrEmpty(BaseUrl) ? null : new Uri(BaseUrl + path);
    }

    public static class NSolver
    {
        public static Uri? Solve => SolverServer.Endpoint("/solve");
        public static Uri? Sts => SolverServer.Endpoint("/sts");
    }

    public static class PoTokenProvider
    {
        public static Uri? GetPot => SolverServer.Endpoint("/get_pot");
    }

    public static class Ryd
    {
        public const string Api = "https://returnyoutubedislikeapi.com";
        public const string Web = "https://returnyoutubedislike.com";
    }

    public static class SponsorBlock
    {
        public const string Api = "https://sponsor.ajay.app";
    }

    public static class Suggest
    {
        public const string Base = "https://suggestqueries.google.com";
    }
}
