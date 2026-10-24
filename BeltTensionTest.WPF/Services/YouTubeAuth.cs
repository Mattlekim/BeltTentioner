using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>
    /// Google sign-in for the YouTube chat overlay: where the OAuth client
    /// secret and cached token live, installing the secret that ships with the
    /// app, and logging in / out (Preferences → OpenXR).
    /// </summary>
    public static class YouTubeAuth
    {
        public const string UserId = "user";

        public static readonly string[] Scopes =
        {
            YouTubeService.Scope.Youtube,
            YouTubeService.Scope.YoutubeForceSsl,
            YouTubeService.Scope.YoutubeReadonly,
        };

        /// <summary>Directory holding client_secret.json and the cached OAuth token.</summary>
        public static string AuthDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BeltTensioner");

        public static string SecretPath => Path.Combine(AuthDir, "client_secret.json");
        private static string TokenDir => Path.Combine(AuthDir, "youtube_token");

        /// <summary>The client secret shipped next to the exe (youtube\client_secret.json).</summary>
        private static string BundledSecretPath => Path.Combine(AppContext.BaseDirectory, "youtube", "client_secret.json");

        public static FileDataStore TokenStore => new FileDataStore(TokenDir, true);

        /// <summary>A Google token is cached, so the overlay connects without a browser sign-in.</summary>
        public static bool IsLoggedIn =>
            Directory.Exists(TokenDir) && Directory.EnumerateFiles(TokenDir).Any();

        /// <summary>
        /// Copy the bundled client secret into <see cref="AuthDir"/>. Returns
        /// false (with the reason) when no secret ships with this build.
        /// </summary>
        public static bool InstallSecret(out string error)
        {
            error = string.Empty;
            try
            {
                if (!File.Exists(BundledSecretPath))
                {
                    if (File.Exists(SecretPath)) return true; // installed by hand earlier
                    error = $"No client_secret.json was found next to the app ({BundledSecretPath}).";
                    return false;
                }
                Directory.CreateDirectory(AuthDir);
                File.Copy(BundledSecretPath, SecretPath, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Raised (on whatever thread logged in/out) after <see cref="SignInAsync"/>
        /// or <see cref="LogoutAsync"/>, so a running chat overlay can reconnect
        /// or drop its connection without an app restart.
        /// </summary>
        public static event Action? LoginChanged;

        private static string UserNamePath => Path.Combine(AuthDir, "youtube_user.txt");

        /// <summary>The signed-in channel's name, cached at login; null when unknown.</summary>
        public static string? UserName
        {
            get
            {
                try { return File.Exists(UserNamePath) ? File.ReadAllText(UserNamePath).Trim() : null; }
                catch { return null; }
            }
        }

        /// <summary>
        /// The cached credential, or a browser sign-in when none is cached.
        /// Raises nothing — use <see cref="SignInAsync"/> for the user-facing login.
        /// </summary>
        public static async Task<UserCredential> LoginAsync(CancellationToken ct)
        {
            using var stream = new FileStream(SecretPath, FileMode.Open, FileAccess.Read);
            return await GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets, Scopes, UserId, ct, TokenStore);
        }

        /// <summary>Sign in through the browser, cache the channel name, and tell the overlay.</summary>
        public static async Task<string?> SignInAsync(CancellationToken ct)
        {
            var credential = await LoginAsync(ct);
            string? name = await RefreshUserNameAsync(credential, ct);
            LoginChanged?.Invoke();
            return name;
        }

        /// <summary>Look up (and cache) the signed-in channel's name; null on failure.</summary>
        public static async Task<string?> RefreshUserNameAsync(UserCredential? credential, CancellationToken ct)
        {
            try
            {
                credential ??= await LoginAsync(ct);
                using var service = new YouTubeService(new Google.Apis.Services.BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "BeltTensioner YouTube Chat",
                });
                var request = service.Channels.List("snippet");
                request.Mine = true;
                var response = await request.ExecuteAsync(ct);
                string? name = response.Items?.FirstOrDefault()?.Snippet?.Title;
                if (!string.IsNullOrWhiteSpace(name))
                    File.WriteAllText(UserNamePath, name);
                return name;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Revoke the cached token with Google (best-effort) and delete it.</summary>
        public static async Task LogoutAsync()
        {
            await RevokeAndDeleteAsync();
            try { File.Delete(UserNamePath); } catch { }
            LoginChanged?.Invoke();
        }

        private static async Task RevokeAndDeleteAsync()
        {
            try
            {
                if (File.Exists(SecretPath))
                {
                    using var stream = new FileStream(SecretPath, FileMode.Open, FileAccess.Read);
                    var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                    {
                        ClientSecrets = GoogleClientSecrets.FromStream(stream).Secrets,
                        Scopes = Scopes,
                        DataStore = TokenStore,
                    });
                    var token = await flow.LoadTokenAsync(UserId, CancellationToken.None);
                    if (token != null)
                        await flow.RevokeTokenAsync(UserId, token.RefreshToken ?? token.AccessToken, CancellationToken.None);
                }
            }
            catch { /* offline or already revoked: deleting the local token still logs out */ }

            try { if (Directory.Exists(TokenDir)) Directory.Delete(TokenDir, recursive: true); } catch { }
        }
    }
}
