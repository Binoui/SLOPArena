using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SlopArena.Client.Replays;
using SlopArena.Shared.Replays;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// The Replays list over Home: the folder it read, a Refresh button and one row per file, newest first. A row
    /// says when the file was downloaded, what it holds, whether it is complete, and the track this build plays.
    /// </summary>
    public sealed class ReplayListOverlay
    {
        private readonly VisualElement _root;
        private readonly ScrollView _rows;
        private readonly Label _status;
        private readonly Dictionary<string, ReplayTrackForecast> _forecasts = new();
        private ReplayContent? _content;
        private CancellationTokenSource? _scan;
        private string _directory = "";

        public ReplayListOverlay(VisualElement host)
        {
            _root = new VisualElement { name = "replay-list" };
            _root.style.position = Position.Absolute;
            _root.style.left = 60;
            _root.style.right = 60;
            _root.style.top = 80;
            _root.style.bottom = 60;
            _root.style.backgroundColor = new Color(0.07f, 0.08f, 0.11f, 1f);
            _root.style.paddingLeft = _root.style.paddingRight = 18;
            _root.style.paddingTop = _root.style.paddingBottom = 14;
            host.Add(_root);
            _root.BringToFront();

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            var title = Text("REPLAYS", 30);
            title.style.flexGrow = 1;
            header.Add(title);
            header.Add(new Button(Refresh) { name = "replay-list-refresh", text = "REFRESH" });
            header.Add(new Button(Close) { name = "replay-list-close", text = "CLOSE" });
            _root.Add(header);

            _status = Text("", 14);
            _status.name = "replay-list-root";
            _status.style.color = new Color(1f, 1f, 1f, 0.7f);
            _root.Add(_status);

            _rows = new ScrollView { name = "replay-list-rows" };
            _rows.style.flexGrow = 1;
            _rows.style.marginTop = 10;
            _root.Add(_rows);
            Refresh();
        }

        public bool IsOpen => _root.parent != null;
        public int RowCount => _rows.contentContainer.childCount;

        public void Close()
        {
            _scan?.Cancel();
            _root.RemoveFromHierarchy();
        }

        public async void Refresh()
        {
            _scan?.Cancel();
            _scan = new CancellationTokenSource();
            var token = _scan.Token;
            _directory = ReplayDirectory.Resolve(ReplayDirectoryProbe.Current());
            if (_content == null && ReplayContentSource.TryToday(out var content, out _)) _content = content;
            string? failure = ReplayLaunch.LastFailure;
            _status.text = $"Reading {_directory}";
            _rows.Clear();
            IReadOnlyList<ReplayListing> listings;
            try { listings = await ReplayLibrary.ScanAsync(_directory, token); }
            catch (OperationCanceledException) { return; }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            {
                if (!token.IsCancellationRequested && IsOpen) _status.text = $"Cannot read {_directory}: {e.Message}";
                return;
            }
            if (token.IsCancellationRequested || !IsOpen) return;
            _status.text = $"{listings.Count} in {_directory}" + (failure != null ? $" · Last Replay closed: {failure}" : "");
            for (int i = 0; i < listings.Count; i++) _rows.Add(Row(listings[i], i));
        }

        private VisualElement Row(ReplayListing listing, int index)
        {
            var row = new VisualElement { name = "replay-row-" + index };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = row.style.paddingBottom = 6;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(1f, 1f, 1f, 0.08f);

            var text = new VisualElement();
            text.style.flexGrow = 1;
            text.Add(Text(Title(listing), 17));
            var detail = Text(Detail(listing), 13);
            detail.name = "replay-row-detail-" + index;
            detail.style.color = new Color(1f, 1f, 1f, 0.75f);
            text.Add(detail);
            row.Add(text);

            var open = new Button(() => Open(listing)) { name = "replay-open-" + index, text = "WATCH" };
            open.SetEnabled(listing.CanOpen && CanStart());
            row.Add(open);
            return row;
        }

        private string Title(ReplayListing listing)
        {
            if (listing.Summary is not { } summary) return System.IO.Path.GetFileName(listing.Path);
            var header = summary.Header;
            string length = summary.Range is { } range ? ReplayText.Clock(range.Last - range.First + 1) : "0:00";
            string fighters = string.Join(" vs ", header.Roster.Select(p =>
                ReplayLabels.ReplayParticipantLabel(p, _content?.Catalog.ResolvePackage(p.Package.PackageId)?.DisplayName ?? p.Package.PackageId)));
            return $"{ReplayText.Mode(header.Rules.FormatId)} · {header.ArenaId} · {fighters} · {length}";
        }

        /// <summary>The words a row shows after its title; the list check reads them.</summary>
        public string Detail(ReplayListing listing)
        {
            string downloaded = "Downloaded " + listing.Downloaded.ToString("yyyy-MM-dd HH:mm");
            if (listing.Failure is { } failure) return $"{downloaded} · {ReplayText.Describe(failure)}";
            string completeness = listing.Completeness.ToString();
            if (listing.Completeness == ReplayCompleteness.Empty) return $"{downloaded} · {completeness}";
            var forecast = Forecast(listing.Summary!.Header);
            string track = forecast == null ? "View · today's content unavailable" : ReplayText.Track(forecast.Track, forecast.ExactRefusal);
            return $"{downloaded} · {completeness} · {track}";
        }

        private ReplayTrackForecast? Forecast(ReplayHeader header)
        {
            if (_content == null) return null;
            string key = header.Build.ContentHash + "|" + header.ArenaId + "|" + header.Build.Protocol;
            if (!_forecasts.TryGetValue(key, out var forecast)) _forecasts[key] = forecast = ReplaySummaries.Forecast(header, _content);
            return forecast;
        }

        private static bool CanStart() => ReplayLaunch.Refusal() == null;

        private void Open(ReplayListing listing)
        {
            if (ReplayLaunch.Refusal() is { } refusal)
            {
                _status.text = refusal;
                return;
            }
            Close();
            ReplayLaunch.ReopenListOnReturn = true;
            ReplayLaunch.Open(listing.Path, FrontendPage.Home);
        }

        private static Label Text(string text, int size)
        {
            var label = new Label(text) { enableRichText = false };
            label.style.color = Color.white;
            label.style.fontSize = size;
            return label;
        }
    }
}
