#!/usr/bin/env python3
import argparse
import json
import sys
import urllib.error
import urllib.request

DEFAULT_BASE_URL = "http://127.0.0.1:42420"


def fetch_state(base_url: str = DEFAULT_BASE_URL) -> dict:
    request = urllib.request.Request(
        f"{base_url.rstrip('/')}/v1/state",
        headers={"Accept": "application/json"},
        method="GET",
    )
    try:
        with urllib.request.urlopen(request, timeout=2.0) as response:
            return json.load(response)
    except urllib.error.HTTPError as exc:
        try:
            payload = json.load(exc)
        except Exception:
            payload = {"ok": False, "code": f"http_{exc.code}"}
        return payload


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Probe the Vintage Story Agent localhost bridge")
    parser.add_argument("command", choices=["state"])
    parser.add_argument("--base-url", default=DEFAULT_BASE_URL)
    parser.add_argument("--json", action="store_true", help="print compact JSON")
    args = parser.parse_args(argv)

    try:
        payload = fetch_state(args.base_url)
    except (urllib.error.URLError, TimeoutError) as exc:
        print(f"bridge unavailable: {exc}", file=sys.stderr)
        return 2

    if args.json:
        print(json.dumps(payload, separators=(",", ":")))
    else:
        print(json.dumps(payload, indent=2))
    return 0 if payload.get("ok") else 1


if __name__ == "__main__":
    raise SystemExit(main())
