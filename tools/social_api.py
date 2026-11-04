#!/usr/bin/env python3
"""Read SLOPArena's own posts and their numbers from the official Instagram, Threads and YouTube APIs.

Reading your own accounts needs no app review: the Meta app has @sloparena as its Instagram and Threads tester, and
the Google project is the channel owner's. Keys and tokens live in ~/.config/sloparena/social.json (0600, never in
Git); docs/systems/content-factory.md has the one-time setup. `factory.py views` calls posts() for each connected
platform; TikTok stays on yt-dlp's public numbers.

    python3 tools/social_api.py login instagram --app-id <id> --secret-file <file>   # first time; later: login instagram
    python3 tools/social_api.py login threads --app-id <id> --secret-file <file>
    python3 tools/social_api.py login youtube --client-json <downloaded client.json>
    python3 tools/social_api.py status                     # what is connected, token ages
    python3 tools/social_api.py posts instagram|threads|shorts   # what views would see, as JSON
"""
from __future__ import annotations

import argparse
import http.server
import json
import os
import secrets
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import date, datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import factory  # noqa: E402  (the board's address and the /oauth hand-off)

CONFIG = Path.home() / ".config/sloparena/social.json"
REDIRECT = "https://alfred.tail026bc8.ts.net/oauth"  # registered in the Meta app; server.py serves it
IG = "https://graph.instagram.com/v26.0"
THREADS = "https://graph.threads.net/v1.0"
YT = "https://www.googleapis.com/youtube/v3"
META = {
    "instagram": {"authorize": "https://www.instagram.com/oauth/authorize",
                  "scope": "instagram_business_basic,instagram_business_manage_insights",
                  "code": "https://api.instagram.com/oauth/access_token",
                  "long": "https://graph.instagram.com/access_token?grant_type=ig_exchange_token",
                  "refresh": "https://graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token"},
    "threads": {"authorize": "https://threads.net/oauth/authorize", "scope": "threads_basic,threads_manage_insights",
                "code": "https://graph.threads.net/oauth/access_token",
                "long": "https://graph.threads.net/access_token?grant_type=th_exchange_token",
                "refresh": "https://graph.threads.net/refresh_access_token?grant_type=th_refresh_token"},
}
YT_SCOPES = "https://www.googleapis.com/auth/youtube.readonly https://www.googleapis.com/auth/yt-analytics.readonly"
PLATFORM_KEY = {"instagram": "instagram", "threads": "threads", "shorts": "youtube"}  # factory platform -> config


class ApiError(Exception):
    pass


def load() -> dict:
    return json.loads(CONFIG.read_text()) if CONFIG.is_file() else {}


def save(config: dict) -> None:
    CONFIG.parent.mkdir(parents=True, exist_ok=True)
    tmp = CONFIG.with_suffix(".tmp")
    tmp.write_text(json.dumps(config, indent=1))
    tmp.chmod(0o600)
    tmp.replace(CONFIG)


def connected() -> set[str]:
    """Factory platforms whose API is set up: instagram, threads, shorts."""
    config = load()
    return {p for p, key in PLATFORM_KEY.items() if (config.get(key) or {}).get("token" if key != "youtube"
                                                                                 else "refresh_token")}


def http(url: str, form: dict | None = None) -> dict:
    data = urllib.parse.urlencode(form).encode() if form is not None else None
    request = urllib.request.Request(url, data=data, headers={"User-Agent": "sloparena-stats/1"})
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.loads(response.read())
    except urllib.error.HTTPError as error:
        detail = error.read().decode(errors="replace")[:400]
        raise ApiError(f"{error.code} {urllib.parse.urlsplit(url).path}: {detail}") from None


def with_query(url: str, **params) -> str:
    return url + ("&" if "?" in url else "?") + urllib.parse.urlencode(params)


def open_browser(url: str) -> None:
    print(f"Log in here (opening it in the browser):\n{url}\n", flush=True)
    env = {**os.environ, "WAYLAND_DISPLAY": os.environ.get("WAYLAND_DISPLAY", "wayland-1"),
           "DISPLAY": os.environ.get("DISPLAY", ":0")}
    subprocess.Popen(["xdg-open", url], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


# Meta (Instagram, Threads): browser login -> alfred /oauth -> code picked up here -> long-lived token (60 days,
# refreshed by every views run).

def login_meta(platform: str, app_id: str | None, secret_file: Path | None) -> None:
    config = load()
    entry = config.setdefault(platform, {})
    if app_id:
        entry["app_id"] = app_id.strip()
    if secret_file:
        entry["app_secret"] = secret_file.read_text().strip()
    if not entry.get("app_id") or not entry.get("app_secret"):
        raise SystemExit(f"first login needs --app-id and --secret-file (the {platform} app id and secret)")
    save(config)
    urls, state = META[platform], secrets.token_urlsafe(18)
    open_browser(with_query(urls["authorize"], client_id=entry["app_id"], redirect_uri=REDIRECT, response_type="code",
                            scope=urls["scope"], state=state))
    deadline = time.time() + 600
    while True:
        try:
            got = factory.call("GET", f"/api/oauth/{state}")
            break
        except SystemExit:
            if time.time() > deadline:
                raise SystemExit("no login came back in 10 minutes") from None
            time.sleep(2)
    if got.get("error"):
        raise SystemExit(f"{platform} login refused: {got['error']}")
    short = http(urls["code"], {"client_id": entry["app_id"], "client_secret": entry["app_secret"],
                                "grant_type": "authorization_code", "redirect_uri": REDIRECT, "code": got["code"]})
    short = (short.get("data") or [short])[0]  # Instagram has wrapped this in data[] in some versions
    long = http(with_query(urls["long"], client_secret=entry["app_secret"], access_token=short["access_token"]))
    entry.update(token=long["access_token"], user_id=str(short.get("user_id", "")),
                 refreshed=datetime.now(timezone.utc).isoformat(timespec="seconds"))
    save(config)
    print(f"{platform}: connected")


def meta_token(platform: str) -> str:
    """The long-lived token, refreshed when older than a day (Meta refuses younger ones), so it never lapses
    while views runs at least every 60 days."""
    config = load()
    entry = config.get(platform) or {}
    if not entry.get("token"):
        raise ApiError(f"{platform} is not connected: python3 tools/social_api.py login {platform}")
    age = datetime.now(timezone.utc) - datetime.fromisoformat(entry["refreshed"])
    if age.days >= 1:
        fresh = http(with_query(META[platform]["refresh"], access_token=entry["token"]))
        entry.update(token=fresh["access_token"], refreshed=datetime.now(timezone.utc).isoformat(timespec="seconds"))
        save(config)
    return entry["token"]


def metric_values(reply: dict) -> dict:
    out = {}
    for metric in reply.get("data") or []:
        value = (metric.get("total_value") or {}).get("value")
        if value is None and metric.get("values"):
            value = metric["values"][0].get("value")
        out[metric["name"]] = value
    return out


def insights(base: str, media_id: str, token: str, metrics: list[str]) -> dict:
    """Asks for all metrics at once; a metric this media does not support fails the whole call, so retry one by one."""
    try:
        return metric_values(http(with_query(f"{base}/{media_id}/insights", metric=",".join(metrics),
                                             access_token=token)))
    except ApiError:
        out = {}
        for metric in metrics:
            try:
                out.update(metric_values(http(with_query(f"{base}/{media_id}/insights", metric=metric,
                                                         access_token=token))))
            except ApiError:
                pass
        return out


def instagram_posts(since: date) -> list[dict]:
    token = meta_token("instagram")
    fields = "id,caption,media_type,media_product_type,permalink,timestamp,like_count,comments_count"
    posts = []
    for media in http(with_query(f"{IG}/me/media", fields=fields, limit=50, access_token=token)).get("data") or []:
        if datetime.fromisoformat(media["timestamp"].replace("+0000", "+00:00")).date() < since:
            continue
        got = insights(IG, media["id"], token, ["views", "reach", "saved", "shares", "ig_reels_avg_watch_time"])
        watch = got.get("ig_reels_avg_watch_time")
        posts.append({"url": media["permalink"], "text": media.get("caption") or "", "at": media["timestamp"][:10],
                      "views": got.get("views"), "likes": media.get("like_count"),
                      "comments": media.get("comments_count"),
                      "extra": {"reach": got.get("reach"), "saves": got.get("saved"), "shares": got.get("shares"),
                                "watch_s": round(watch / 1000, 1) if isinstance(watch, (int, float)) else None}})
    return posts


def threads_posts(since: date) -> list[dict]:
    token = meta_token("threads")
    fields = "id,text,permalink,timestamp,media_type"
    posts = []
    for media in http(with_query(f"{THREADS}/me/threads", fields=fields, limit=50, access_token=token)).get("data") or []:
        if datetime.fromisoformat(media["timestamp"].replace("+0000", "+00:00")).date() < since:
            continue
        got = insights(THREADS, media["id"], token, ["views", "likes", "replies", "reposts", "quotes", "shares"])
        posts.append({"url": media["permalink"], "text": media.get("text") or "", "at": media["timestamp"][:10],
                      "views": got.get("views"), "likes": got.get("likes"), "comments": got.get("replies"),
                      "extra": {"reposts": got.get("reposts"), "quotes": got.get("quotes"),
                                "shares": got.get("shares")}})
    return posts


# YouTube: Desktop OAuth client with a loopback redirect; the refresh token lasts while the consent screen is
# "In production" (a "Testing" one expires it after 7 days).

def login_youtube(client_json: Path | None) -> None:
    config = load()
    entry = config.setdefault("youtube", {})
    if client_json:
        installed = json.loads(client_json.read_text())
        installed = installed.get("installed") or installed.get("web") or installed
        entry.update(client_id=installed["client_id"], client_secret=installed["client_secret"])
        save(config)
    if not entry.get("client_id"):
        raise SystemExit("first login needs --client-json (the Desktop OAuth client downloaded from Google Cloud)")
    state, got = secrets.token_urlsafe(18), {}

    class Landing(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            query = dict(urllib.parse.parse_qsl(urllib.parse.urlsplit(self.path).query))
            if query.get("state") == state:
                got.update(query)
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.end_headers()
            self.wfile.write("<body style='font:18px system-ui;margin:40px'>Connected. You can close this tab.".encode())

        def log_message(self, *args):
            pass

    server = http.server.HTTPServer(("127.0.0.1", 0), Landing)
    redirect = f"http://127.0.0.1:{server.server_port}"
    open_browser(with_query("https://accounts.google.com/o/oauth2/v2/auth", client_id=entry["client_id"],
                            redirect_uri=redirect, response_type="code", scope=YT_SCOPES, access_type="offline",
                            prompt="consent", state=state))
    server.timeout = 600
    while not got:
        server.handle_request()
    if "code" not in got:
        raise SystemExit(f"youtube login refused: {got.get('error', 'no code')}")
    tokens = http("https://oauth2.googleapis.com/token", {
        "code": got["code"], "client_id": entry["client_id"], "client_secret": entry["client_secret"],
        "redirect_uri": redirect, "grant_type": "authorization_code"})
    if not tokens.get("refresh_token"):
        raise SystemExit("Google sent no refresh token; remove the app's access in your Google account and retry")
    entry["refresh_token"] = tokens["refresh_token"]
    save(config)
    print("youtube: connected")


def youtube_token() -> str:
    entry = load().get("youtube") or {}
    if not entry.get("refresh_token"):
        raise ApiError("youtube is not connected: python3 tools/social_api.py login youtube")
    return http("https://oauth2.googleapis.com/token", {
        "client_id": entry["client_id"], "client_secret": entry["client_secret"],
        "refresh_token": entry["refresh_token"], "grant_type": "refresh_token"})["access_token"]


def google(url: str, token: str) -> dict:
    request = urllib.request.Request(url, headers={"Authorization": f"Bearer {token}"})
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.loads(response.read())
    except urllib.error.HTTPError as error:
        raise ApiError(f"{error.code} {urllib.parse.urlsplit(url).path}: "
                       f"{error.read().decode(errors='replace')[:400]}") from None


def shorts_posts(since: date) -> list[dict]:
    """Every upload on the channel since `since` (Shorts and long videos alike: matching picks the clip), with the
    public counts and, from YouTube Analytics, average view duration and percentage (a day or two behind)."""
    token = youtube_token()
    channel = google(with_query(f"{YT}/channels", part="contentDetails", mine="true"), token)["items"][0]
    uploads = channel["contentDetails"]["relatedPlaylists"]["uploads"]
    items = google(with_query(f"{YT}/playlistItems", part="contentDetails", playlistId=uploads, maxResults=50),
                   token).get("items") or []
    ids = [i["contentDetails"]["videoId"] for i in items
           if i["contentDetails"].get("videoPublishedAt", "9")[:10] >= since.isoformat()]
    if not ids:
        return []
    videos = google(with_query(f"{YT}/videos", part="snippet,statistics", id=",".join(ids)), token).get("items") or []
    report = google(with_query("https://youtubeanalytics.googleapis.com/v2/reports", ids="channel==MINE",
                               startDate=since.isoformat(), endDate=date.today().isoformat(),
                               metrics="averageViewDuration,averageViewPercentage", dimensions="video",
                               filters="video==" + ",".join(ids)), token)
    watch = {row[0]: row[1:] for row in report.get("rows") or []}
    posts = []
    for video in videos:
        stats, snippet = video.get("statistics") or {}, video["snippet"]
        seconds, percent = watch.get(video["id"], (None, None))
        number = lambda key: int(stats[key]) if key in stats else None  # noqa: E731
        posts.append({"url": f"https://www.youtube.com/shorts/{video['id']}", "at": snippet["publishedAt"][:10],
                      "text": f"{snippet.get('title', '')} {snippet.get('description', '')}",
                      "views": number("viewCount"), "likes": number("likeCount"), "comments": number("commentCount"),
                      "extra": {"watch_s": seconds, "watch_pct": round(percent) if percent is not None else None}})
    return posts


FETCHERS = {"instagram": instagram_posts, "threads": threads_posts, "shorts": shorts_posts}


def posts(platform: str, since: date) -> list[dict]:
    return FETCHERS[platform](since)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    login = sub.add_parser("login")
    login.add_argument("platform", choices=["instagram", "threads", "youtube"])
    login.add_argument("--app-id")
    login.add_argument("--secret-file", type=Path)
    login.add_argument("--client-json", type=Path)
    sub.add_parser("status")
    show = sub.add_parser("posts")
    show.add_argument("platform", choices=sorted(FETCHERS))
    show.add_argument("--since", default="2026-01-01")
    args = parser.parse_args()
    if args.command == "login":
        if args.platform == "youtube":
            login_youtube(args.client_json)
        else:
            login_meta(args.platform, args.app_id, args.secret_file)
    elif args.command == "status":
        config = load()
        for platform, key in PLATFORM_KEY.items():
            entry = config.get(key) or {}
            state = "connected" if platform in connected() else "app saved, not logged in" if entry else "not set up"
            print(f"{platform:10} {state}" + (f" (token refreshed {entry['refreshed']})" if entry.get("refreshed") else ""))
    elif args.command == "posts":
        print(json.dumps(posts(args.platform, date.fromisoformat(args.since)), indent=1, ensure_ascii=False))


if __name__ == "__main__":
    main()
