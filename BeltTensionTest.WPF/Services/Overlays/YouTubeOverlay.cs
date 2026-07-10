using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.Xna.Framework.Graphics;
using GameTime = Microsoft.Xna.Framework.GameTime;
using MonoXR.Client;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// In-VR YouTube live-chat panel, ported from IrachingHud's
    /// WindowYouTubeChat. Signs into the user's Google account (OAuth client
    /// secret expected at %AppData%\BeltTensioner\client_secret.json, token
    /// cached next to it), finds the active live broadcast's chat, then polls
    /// new messages at the interval YouTube asks for. Messages are colored by
    /// author type (owner/mod/member/super chat), word-wrapped to the panel,
    /// and expire after 60 seconds; while there is nothing to show the panel
    /// displays its connection status instead.
    /// </summary>
    public sealed class YouTubeOverlay : OverlayRenderTarget
    {
        public enum UserTypes
        {
            User,
            Admin,
            Moderator,
            ChannelMember,
            SuperChat,
        }

        private sealed class Msg
        {
            public string Author = string.Empty;
            public string Message = string.Empty;
            public UserTypes UserType;
            public string? SuperChatAmount;
            public float LifeTime;
        }

        public const int MaxMsgsToPull = 10;
        public const int MaxMsgLinesToDisplay = 10;
        public const float MsgLifetimeSeconds = 60f;

        private const int PanelWidth = 720;
        private const int TitleBarHeight = 48;
        private const int LineHeight = 34;
        private const int Pad = 10;

        // App palette (as the other overlays) with a YouTube-red accent.
        private static readonly XnaColor PanelBg = new XnaColor(0x12, 0x12, 0x1E, 235);
        private static readonly XnaColor TitleBg = new XnaColor(0x1C, 0x1C, 0x2E, 245);
        private static readonly XnaColor TitleText = new XnaColor(0xD0, 0xD0, 0xF0);
        private static readonly XnaColor Accent = new XnaColor(0xFF, 0x33, 0x33);
        private static readonly XnaColor Border = new XnaColor(0x46, 0x46, 0x6A);
        private static readonly XnaColor RowBg = new XnaColor(0x1A, 0x1A, 0x28, 220);
        private static readonly XnaColor RowText = new XnaColor(0xE6, 0xE6, 0xF5);
        private static readonly XnaColor StatusText = new XnaColor(0xA0, 0xA0, 0xBE);

        // Author colors per user type, same order as the original's array
        // (User, Admin, Moderator, ChannelMember, SuperChat).
        private static readonly XnaColor[] AuthorColors =
        {
            XnaColor.White,
            new XnaColor(0xFF, 0x45, 0x00), // owner — orange red
            new XnaColor(0x64, 0x96, 0xFF), // moderator — blue
            new XnaColor(0x50, 0xC8, 0x78), // member — green
            new XnaColor(0xFF, 0xD5, 0x2E), // super chat — gold
        };

        private static readonly string[] Scopes =
        {
            YouTubeService.Scope.Youtube,
            YouTubeService.Scope.YoutubeForceSsl,
            YouTubeService.Scope.YoutubeReadonly,
        };

        private readonly SpriteBatch _sb;
        private readonly Texture2D _white;
        private readonly SpriteFont _font;     // title
        private readonly SpriteFont _fontBody; // chat lines
        private readonly int _collapsedWidth;

        private readonly Queue<Msg> _chatLines = new(); // wrapped display lines
        private readonly object _statusLock = new();
        private string _status = "YouTube Chat: connecting...";
        private string _lastSnapshot = string.Empty;

        private readonly CancellationTokenSource _cts = new();
        private UserCredential? _credential;
        private YouTubeService? _youtubeService;
        private string? _liveChatId;
        private string? _newMsgsToken;
        private bool _pollLoopStarted;

        public bool IsConnected { get; private set; }

        public override int CollapsedWidth => _collapsedWidth;
        public override int CollapsedHeight => TitleBarHeight;

        private static int PanelHeight =>
            TitleBarHeight + Pad + MaxMsgLinesToDisplay * LineHeight + Pad;

        /// <summary>Directory holding client_secret.json and the cached OAuth token.</summary>
        private static string AuthDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BeltTensioner");

        public YouTubeOverlay(GraphicsDevice device, int x, int y)
            : base(device, PanelWidth, PanelHeight, x, y)
        {
            Name = "YouTube Chat";
            _sb = new SpriteBatch(device);
            _white = new Texture2D(device, 1, 1);
            _white.SetData(new[] { XnaColor.White });
            _font = RuntimeSpriteFont.Bake(device, "Segoe UI", 28f);
            _fontBody = RuntimeSpriteFont.Bake(device, "Segoe UI", 22f);
            _collapsedWidth = (int)_font.MeasureString(Name).X + 60;

            Task.Run(Connect);
        }

        // ----- chat client (background threads) ------------------------------

        private void SetStatus(string text)
        {
            lock (_statusLock)
                _status = text;
            System.Diagnostics.Debug.WriteLine($"[YouTubeChat] {text}");
        }

        private async Task<bool> Connect()
        {
            try
            {
                string secretPath = Path.Combine(AuthDir, "client_secret.json");
                if (!File.Exists(secretPath))
                {
                    SetStatus($"No client_secret.json — put your Google OAuth client in {AuthDir}");
                    return false;
                }

                using (var stream = new FileStream(secretPath, FileMode.Open, FileAccess.Read))
                {
                    _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.FromStream(stream).Secrets,
                        Scopes,
                        "user",
                        _cts.Token,
                        new FileDataStore(Path.Combine(AuthDir, "youtube_token"), true));
                }

                _youtubeService = new YouTubeService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = _credential,
                    ApplicationName = "BeltTensioner YouTube Chat",
                });

                await FindLiveChatId();

                IsConnected = true;
                if (!string.IsNullOrEmpty(_liveChatId))
                    SetStatus("Connected - waiting for chat...");

                // Fire the poll loop once; a reconnect reuses the running loop.
                if (!_pollLoopStarted)
                {
                    _pollLoopStarted = true;
                    _ = PollLoop();
                }
                return true;
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}");
                IsConnected = false;
                return false;
            }
        }

        private async Task PollLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                int wait = 4000;
                try
                {
                    wait = await GetNewMsgs();
                }
                catch (Exception ex)
                {
                    SetStatus($"Error: {ex.Message}");
                    wait = 8000; // back off a little after a failure
                }
                await Task.Delay(Math.Max(wait, 2000), _cts.Token).ContinueWith(_ => { });
            }
        }

        private async Task<int> GetNewMsgs()
        {
            if (_youtubeService == null || string.IsNullOrEmpty(_liveChatId))
                return 4000;

            var request = _youtubeService.LiveChatMessages.List(_liveChatId, "snippet,authorDetails");
            request.PageToken = _newMsgsToken;
            request.MaxResults = MaxMsgsToPull;

            var response = await request.ExecuteAsync(_cts.Token);
            _newMsgsToken = response.NextPageToken;

            foreach (var message in response.Items)
                AddMessage(message);

            // Respect the interval YouTube asks us to wait before polling again.
            return (int)(response.PollingIntervalMillis ?? 4000);
        }

        /// <summary>
        /// Finds the active broadcast's live chat id: active broadcasts first,
        /// then all broadcasts, then each video's liveStreamingDetails (the
        /// broadcast snippet often omits liveChatId) — same fallback chain as
        /// the original.
        /// </summary>
        private async Task FindLiveChatId()
        {
            _liveChatId = null;

            IList<LiveBroadcast>? active = await ListBroadcasts(
                LiveBroadcastsResource.ListRequest.BroadcastStatusEnum.Active);
            PickLiveChat(active);

            IList<LiveBroadcast>? all = null;
            if (string.IsNullOrEmpty(_liveChatId))
            {
                all = await ListBroadcasts(LiveBroadcastsResource.ListRequest.BroadcastStatusEnum.All);
                PickLiveChat(all);
            }

            if (string.IsNullOrEmpty(_liveChatId))
            {
                foreach (var b in (active ?? new List<LiveBroadcast>()).Concat(all ?? new List<LiveBroadcast>()))
                {
                    string? life = b.Status?.LifeCycleStatus;
                    if (life == "complete" || life == "revoked") continue;
                    string? cid = await GetActiveChatIdFromVideo(b.Id);
                    if (!string.IsNullOrEmpty(cid))
                    {
                        _liveChatId = cid;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(_liveChatId))
            {
                SetStatus("No live chat found — start a live stream and it will appear here.");
                return;
            }

            // Prime the page token with the current backlog.
            var request = _youtubeService!.LiveChatMessages.List(_liveChatId, "snippet,authorDetails");
            request.MaxResults = MaxMsgsToPull;
            var response = await request.ExecuteAsync(_cts.Token);
            _newMsgsToken = response.NextPageToken;
            foreach (var message in response.Items)
                AddMessage(message);
        }

        private async Task<IList<LiveBroadcast>?> ListBroadcasts(
            LiveBroadcastsResource.ListRequest.BroadcastStatusEnum status)
        {
            var request = _youtubeService!.LiveBroadcasts.List("id,snippet,status");
            // broadcastStatus already scopes to the signed-in user's broadcasts.
            request.BroadcastStatus = status;
            request.MaxResults = 50;
            var response = await request.ExecuteAsync(_cts.Token);
            return response.Items;
        }

        private void PickLiveChat(IList<LiveBroadcast>? broadcasts)
        {
            if (broadcasts == null) return;
            foreach (var broadcast in broadcasts)
            {
                string life = broadcast.Status?.LifeCycleStatus ?? "?";
                string? chatId = broadcast.Snippet?.LiveChatId;
                bool ended = life == "complete" || life == "revoked";
                if (!string.IsNullOrEmpty(chatId) && !ended)
                {
                    _liveChatId = chatId;
                    return;
                }
            }
        }

        private async Task<string?> GetActiveChatIdFromVideo(string videoId)
        {
            if (string.IsNullOrEmpty(videoId)) return null;
            try
            {
                var vreq = _youtubeService!.Videos.List("liveStreamingDetails");
                vreq.Id = videoId;
                var vres = await vreq.ExecuteAsync(_cts.Token);
                return vres.Items?.FirstOrDefault()?.LiveStreamingDetails?.ActiveLiveChatId;
            }
            catch
            {
                return null;
            }
        }

        private void AddMessage(LiveChatMessage message)
        {
            var msg = new Msg();
            var author = message.AuthorDetails;
            if (message.Snippet.SuperChatDetails != null)
            {
                msg.UserType = UserTypes.SuperChat;
                msg.SuperChatAmount = message.Snippet.SuperChatDetails.AmountDisplayString;
            }
            else if (author.IsChatOwner == true) msg.UserType = UserTypes.Admin;
            else if (author.IsChatModerator == true) msg.UserType = UserTypes.Moderator;
            else if (author.IsChatSponsor == true) msg.UserType = UserTypes.ChannelMember;
            else msg.UserType = UserTypes.User;

            msg.Author = author.DisplayName ?? "?";
            msg.Message = message.Snippet.DisplayMessage ?? string.Empty;

            // Wrap into display lines (author only on the first) and trim the
            // queue to the last MaxMsgLinesToDisplay lines.
            lock (_chatLines)
            {
                string prefix = msg.SuperChatAmount != null
                    ? $"{msg.Author} ({msg.SuperChatAmount}): "
                    : $"{msg.Author}: ";
                float prefixW = _fontBody.MeasureString(prefix).X;
                int wrapWidth = Math.Max(60, Width - Pad * 4 - (int)prefixW);

                bool firstLine = true;
                foreach (string line in WrapText(msg.Message, wrapWidth))
                {
                    _chatLines.Enqueue(new Msg
                    {
                        Author = firstLine ? msg.Author : string.Empty,
                        Message = line,
                        SuperChatAmount = firstLine ? msg.SuperChatAmount : null,
                        UserType = msg.UserType,
                    });
                    firstLine = false;
                }
                while (_chatLines.Count > MaxMsgLinesToDisplay)
                    _chatLines.Dequeue();
            }
        }

        /// <summary>
        /// Wraps text to the given pixel width, breaking on spaces when
        /// possible and hard-breaking words longer than a whole line.
        /// </summary>
        private IEnumerable<string> WrapText(string text, int maxWidth)
        {
            if (string.IsNullOrEmpty(text))
            {
                yield return string.Empty;
                yield break;
            }

            string line = string.Empty;
            int lastSpace = -1;
            for (int i = 0; i < text.Length; i++)
            {
                line += text[i];
                if (text[i] == ' ')
                    lastSpace = line.Length - 1;

                if (_fontBody.MeasureString(line).X > maxWidth && line.Length > 1)
                {
                    if (lastSpace > 0)
                    {
                        yield return line.Substring(0, lastSpace);
                        line = line.Substring(lastSpace + 1);
                    }
                    else
                    {
                        yield return line.Substring(0, line.Length - 1);
                        line = line.Substring(line.Length - 1);
                    }
                    lastSpace = -1;
                }
            }
            if (line.Length > 0)
                yield return line;
        }

        // ----- overlay integration (UI thread) --------------------------------

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            var sb = new StringBuilder();
            lock (_chatLines)
            {
                foreach (var msg in _chatLines)
                    msg.LifeTime += dt;
                while (_chatLines.Count > 0 && _chatLines.Peek().LifeTime > MsgLifetimeSeconds)
                    _chatLines.Dequeue();

                foreach (var msg in _chatLines)
                    sb.Append(msg.Author).Append('|').Append(msg.Message).Append('|');
            }
            if (sb.Length == 0)
            {
                lock (_statusLock)
                    sb.Append(_status);
            }

            // Only redraw/republish when the visible content changed.
            string snapshot = sb.ToString();
            if (snapshot != _lastSnapshot)
            {
                _lastSnapshot = snapshot;
                Invalidate();
            }
        }

        public override void Render(GameTime gameTime)
        {
            GraphicsDevice.Clear(XnaColor.Transparent);
            _sb.Begin();

            if (IsCollapsed)
            {
                var pill = new XnaRectangle(0, 0, CollapsedWidth, CollapsedHeight);
                MonoXRDraw.RoundedRect(_sb, pill, CollapsedHeight / 2, TitleBg);
                MonoXRDraw.RoundedRectOutline(_sb, pill, CollapsedHeight / 2, 2, Border);
                int dotR = 6;
                _sb.Draw(MonoXRDraw.Circle(GraphicsDevice, dotR),
                    new XnaRectangle(20 - dotR, CollapsedHeight / 2 - dotR, dotR * 2, dotR * 2), Accent);
                _sb.DrawString(_font, Name,
                    new XnaVector2(34, (CollapsedHeight - _font.LineSpacing) / 2f), TitleText);
                _sb.End();
                return;
            }

            var panel = new XnaRectangle(0, 0, Width, Height);
            MonoXRDraw.RoundedRect(_sb, panel, 16, PanelBg);

            // Title bar, same anatomy as the other panels but YouTube-red.
            MonoXRDraw.RoundedRect(_sb, new XnaRectangle(0, 0, Width, TitleBarHeight), 16,
                                   TitleBg, roundBottom: false);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, 0, Width, TitleBarHeight / 2), XnaColor.White * 0.05f);
            _sb.DrawString(_font, Name, new XnaVector2(20, 10), XnaColor.Black * 0.45f);
            _sb.DrawString(_font, Name, new XnaVector2(20, 8), TitleText);
            _sb.Draw(_white, new XnaRectangle(0, TitleBarHeight - 3, Width, 3), Accent);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, TitleBarHeight, Width, 12), Accent * 0.25f);

            // Snapshot under lock — the poll runs on background threads.
            Msg[] lines;
            lock (_chatLines)
                lines = _chatLines.ToArray();

            int y = TitleBarHeight + Pad;
            if (lines.Length > 0)
            {
                foreach (var msg in lines)
                {
                    string prefix = string.IsNullOrEmpty(msg.Author)
                        ? string.Empty
                        : msg.SuperChatAmount != null
                            ? $"{msg.Author} ({msg.SuperChatAmount}): "
                            : $"{msg.Author}: ";

                    float prefixW = prefix.Length > 0 ? _fontBody.MeasureString(prefix).X : 0f;
                    float textW = _fontBody.MeasureString(msg.Message).X;
                    int indent = prefix.Length > 0 ? 0 : 18; // continuation lines indent

                    // Rounded bubble sized to the line.
                    int bubbleW = Math.Min(Width - Pad * 2, (int)(prefixW + textW) + 20 + indent);
                    MonoXRDraw.RoundedRect(_sb,
                        new XnaRectangle(Pad, y + 1, bubbleW, LineHeight - 3), 8, RowBg);

                    float textY = y + (LineHeight - _fontBody.LineSpacing) / 2f;
                    if (prefix.Length > 0)
                        _sb.DrawString(_fontBody, prefix, new XnaVector2(Pad + 10, textY),
                                       AuthorColors[(int)msg.UserType]);
                    _sb.DrawString(_fontBody, msg.Message,
                                   new XnaVector2(Pad + 10 + indent + prefixW, textY), RowText);
                    y += LineHeight;
                }
            }
            else
            {
                // Nothing to show — display the connection/status line so the
                // panel is never a mystery blank.
                string status;
                lock (_statusLock)
                    status = _status;
                foreach (string line in WrapText(status, Width - Pad * 4))
                {
                    _sb.DrawString(_fontBody, line, new XnaVector2(Pad + 10, y + 4), StatusText);
                    y += LineHeight;
                }
            }

            MonoXRDraw.RoundedRectOutline(_sb, panel, 16, 2, Border);
            _sb.End();
        }

        public override void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _youtubeService?.Dispose();
            _fontBody.Texture.Dispose();
            _font.Texture.Dispose();
            _white.Dispose();
            _sb.Dispose();
            base.Dispose();
        }
    }
}
