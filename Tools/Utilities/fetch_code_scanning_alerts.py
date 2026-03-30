#!/usr/bin/env python3
"""Fetch GitHub Code Scanning alerts via gh and generate todo files.

This script is intended for developers to generate file-todo entries from current
open alerts in a GitHub repository. It uses the `gh` CLI tool to query the Code Scanning alerts API

Usage:
  python Tools/Utilities/fetch_code_scanning_alerts.py \
    --owner jakeroxs --repo dsos \
    --write-todos

By default it fetches open alerts (state=open) and requests 100 items per page.
"""

from __future__ import annotations

import argparse
import glob
import json
import os
import re
import subprocess
import sys
import time
import traceback
import unittest
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional

# Constants to avoid literal duplication and improve unit test stability
DEFAULT_SCAN_WORKFLOWS = "codeql.yml"
UNKNOWN_GROUP = "<unknown>"
THIRD_PARTY_PREFIX = "Source/ThirdParty/"


def _parse_git_remote_url(url: str) -> Optional[tuple[str, str]]:
    """Parse a git remote URL into (owner, repo) if possible."""
    # Supported forms:
    # - git@github.com:owner/repo.git
    # - https://github.com/owner/repo.git
    # - https://github.com/owner/repo
    if url.startswith("git@"):
        # git@github.com:owner/repo.git
        try:
            _, rest = url.split(":", 1)
            owner, repo = rest.split("/", 1)
            repo = repo.removesuffix(".git")
            return owner, repo
        except (ValueError, IndexError):
            return None
    if url.startswith("https://") or url.startswith("http://"):
        try:
            parts = url.split("/")
            # e.g. https://github.com/owner/repo.git
            owner = parts[3]
            repo = parts[4].removesuffix(".git")
            return owner, repo
        except IndexError:
            return None
    return None


def get_git_origin_repo() -> Optional[tuple[str, str]]:
    """Return (owner, repo) from the current git origin remote if available."""
    try:
        output = subprocess.check_output(
            ["git", "config", "--get", "remote.origin.url"],
            stderr=subprocess.DEVNULL,
            text=True,
        ).strip()
        if not output:
            return None
        return _parse_git_remote_url(output)
    except (subprocess.CalledProcessError, OSError):
        return None


def run_gh_api(
    owner: str,
    repo: str,
    state: str,
    per_page: int,
    check_output_fn: Callable[..., str] | None = None,
) -> Any:
    """Run `gh api` against the code-scanning alerts endpoint and return parsed JSON.

    This function uses `--paginate` so it fetches all pages of results, and returns the
    parsed JSON (typically a list of alerts).
    """

    if check_output_fn is None:
        check_output_fn = subprocess.check_output

    query = f"?state={state}&per_page={per_page}"
    cmd = [
        "gh",
        "api",
        "-H",
        "Accept: application/vnd.github+json",
        f"/repos/{owner}/{repo}/code-scanning/alerts{query}",
        "--paginate",
    ]

    try:
        try:
            output = check_output_fn(cmd, stderr=subprocess.STDOUT, text=True)
        except TypeError:
            # Some injected check_output substitutes may not accept kwargs.
            output = check_output_fn(cmd)
    except subprocess.CalledProcessError as e:
        print(f"ERROR: failed to run gh api: {e}", file=sys.stderr)
        traceback.print_exc(file=sys.stderr)
        raise SystemExit(1)

    try:
        payload = json.loads(output)
        # Some endpoints return {"items": [...]}; normalize to the list.
        if isinstance(payload, dict) and "items" in payload:
            return payload["items"]
        return payload
    except json.JSONDecodeError as e:
        print(f"ERROR: failed to parse JSON from gh api output: {e}", file=sys.stderr)
        traceback.print_exc(file=sys.stderr)
        raise SystemExit(1)


def _parse_iso_timestamp(value: Any, workflow_file: str) -> Optional[float]:
    if not isinstance(value, str):
        return None
    if value.endswith("Z"):
        value = value[:-1] + "+00:00"
    try:
        dt = datetime.fromisoformat(value)
        return dt.astimezone(timezone.utc).timestamp()
    except ValueError as e:
        print(
            f"WARNING: failed to parse timestamp from workflow '{workflow_file}' response: {e}",
            file=sys.stderr,
        )
        return None


def _get_workflow_latest_run_time(
    owner: str,
    repo: str,
    workflow_file: str,
    check_output_fn: Callable[..., str],
) -> Optional[float]:
    cmd = [
        "gh",
        "api",
        "-H",
        "Accept: application/vnd.github+json",
        f"/repos/{owner}/{repo}/actions/workflows/{workflow_file}/runs?status=completed&per_page=50",
    ]
    try:
        try:
            output = check_output_fn(cmd, stderr=subprocess.STDOUT, text=True)
        except TypeError:
            output = check_output_fn(cmd)

        payload = json.loads(output)
        runs = payload.get("workflow_runs") or []
        if not runs:
            return None

        parsed_runs = []
        for run in runs:
            ts = _parse_iso_timestamp(run.get("updated_at") or run.get("created_at"), workflow_file)
            if ts is not None:
                parsed_runs.append((ts, run.get("conclusion")))

        if not parsed_runs:
            return None

        successful = [t for t in parsed_runs if t[1] == "success"]
        candidate = max(successful or parsed_runs, key=lambda t: t[0])
        return candidate[0]
    except (subprocess.CalledProcessError, json.JSONDecodeError) as e:
        print(
            f"WARNING: failed to query GitHub Actions API for workflow '{workflow_file}': {e}",
            file=sys.stderr,
        )
        return None


def get_last_scan_workflow_run_time(
    owner: str,
    repo: str,
    workflow_files: List[str] | str = DEFAULT_SCAN_WORKFLOWS,
    check_output_fn: Callable[..., str] | None = None,
) -> Optional[float]:
    if isinstance(workflow_files, str):
        workflow_files = [wf.strip() for wf in workflow_files.split(",") if wf.strip()]

    if check_output_fn is None:
        check_output_fn = subprocess.check_output

    latest_ts: Optional[float] = None
    for workflow_file in workflow_files:
        ts = _get_workflow_latest_run_time(owner, repo, workflow_file, check_output_fn)
        if ts is not None and (latest_ts is None or ts > latest_ts):
            latest_ts = ts

    return latest_ts


def safe_value(v: Any) -> str:
    """Convert a JSON value into a compact string safe for table-style output."""
    if v is None:
        return ""
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, (int, float)):
        return str(v)
    if isinstance(v, str):
        # Remove newlines and pipe characters (output is pipe-delimited)
        s = v.replace("|", "\\|")
        s = s.replace("\r", " ").replace("\n", " ")
        return s
    # For complex types, fall back to compact JSON
    return json.dumps(v, separators=(",", ":"), ensure_ascii=False)


def _extract_alert_summary(alert: Dict[str, Any]) -> Dict[str, Any]:
    """Extract a compact, table-friendly summary from a code-scanning alert."""
    rule = alert.get("rule", {}) or {}
    inst = alert.get("most_recent_instance", {}) or {}
    loc = inst.get("location", {}) or {}
    message = inst.get("message", {}) or {}

    help_field = rule.get("help")
    if isinstance(help_field, dict):
        help_text = help_field.get("text")
    else:
        help_text = help_field

    return {
        "number": alert.get("number", ""),
        "state": alert.get("state", ""),
        "rule": rule.get("name", ""),
        "severity": rule.get("severity", ""),
        "description": rule.get("description", ""),
        "path": loc.get("path", ""),
        "line": loc.get("start_line", ""),
        "message": message.get("text", ""),
        "html_url": alert.get("html_url", ""),
        "help_text": help_text,
        "help_uri": rule.get("helpUri"),
    }


def _group_by_path(alert: Dict[str, Any]) -> str:
    return (alert.get("most_recent_instance", {}) or {}).get("location", {}).get("path") or UNKNOWN_GROUP


def _group_by_severity(alert: Dict[str, Any]) -> str:
    return (alert.get("rule", {}) or {}).get("severity") or UNKNOWN_GROUP


def _group_by_thirdparty(alert: Dict[str, Any]) -> str:
    path = (alert.get("most_recent_instance", {}) or {}).get("location", {}).get("path", "")
    if path.startswith(THIRD_PARTY_PREFIX):
        remainder = path[len(THIRD_PARTY_PREFIX) :]
        return remainder.split("/", 1)[0] or "<thirdparty>"
    return "<repo>"


def _group_by_thirdparty_rule(alert: Dict[str, Any]) -> str:
    (alert.get("most_recent_instance", {}) or {}).get("location", {}).get("path", "")
    lib = _group_by_thirdparty(alert)
    rule_name = (alert.get("rule", {}) or {}).get("name") or UNKNOWN_GROUP
    return f"{lib}::{rule_name}"


def _group_by_rule(alert: Dict[str, Any]) -> str:
    return (alert.get("rule", {}) or {}).get("name") or UNKNOWN_GROUP


def group_alerts(alerts: List[Dict[str, Any]], group_by: str = "rule") -> Dict[str, List[Dict[str, Any]]]:
    """Group alerts in a stable way for todo generation.

    Currently supported groupings:
      - rule: group by rule name
      - path: group by file path (most_recent_instance.location.path)
      - severity: group by rule severity
      - thirdparty: group by third-party library folder under Source/ThirdParty
      - thirdparty_rule: group by <third-party library>::<rule name>
    """
    out: Dict[str, List[Dict[str, Any]]] = {}

    key_selector = {
        "path": _group_by_path,
        "severity": _group_by_severity,
        "thirdparty": _group_by_thirdparty,
        "thirdparty_rule": _group_by_thirdparty_rule,
        "rule": _group_by_rule,
    }.get(group_by, _group_by_rule)

    for a in alerts:
        out.setdefault(key_selector(a), []).append(a)

    return out


def severity_to_priority(severity: str | None) -> str:
    """Map CodeQL severity to file-todos priority."""
    if not severity:
        return "p2"
    sev = severity.lower()
    if sev == "error":
        return "p1"
    if sev == "warning":
        return "p2"
    if sev in ("note", "recommendation"):
        return "p3"
    return "p2"


def _todo_priority(alerts: List[Dict[str, Any]], default_priority: str) -> str:
    ranks = {"p1": 1, "p2": 2, "p3": 3}
    best = default_priority
    for a in alerts:
        sev = (a.get("rule", {}) or {}).get("severity")
        p = severity_to_priority(sev)
        if ranks.get(p, 2) < ranks.get(best, 2):
            best = p
    return best


def _rule_help_fields(rule_obj: Any) -> tuple[Optional[str], Optional[str]]:
    if not isinstance(rule_obj, dict):
        return None, None
    help_field = rule_obj.get("help")
    if isinstance(help_field, dict):
        return help_field.get("text"), rule_obj.get("helpUri")
    return help_field, rule_obj.get("helpUri")


def render_todo_body(
    issue_id: str, group_key: str, alerts: List[Dict[str, Any]], default_priority: str = "p2"
) -> str:
    """Render a markdown todo body for a set of alerts in file-todos format."""

    title = f"Code scanning alerts: {group_key}"
    priority = _todo_priority(alerts, default_priority)

    lines: List[str] = [
        "---",
        "status: pending",
        f"priority: {priority}",
        f"issue_id: \"{issue_id}\"",
        "tags: [security, code-scanning]",
        "dependencies: []",
        "---",
        "",
        f"# {title}",
        "",
        f"This todo summarizes {len(alerts)} alert(s) in this group.",
        "",
        "## Alerts",
        "",
    ]

    for a in alerts:
        num = a.get("number")
        url = a.get("html_url")
        rule = (a.get("rule", {}) or {}).get("name")
        desc = (a.get("rule", {}) or {}).get("description")
        inst = (a.get("most_recent_instance", {}) or {})
        loc = (inst.get("location", {}) or {})
        path = loc.get("path")
        line = loc.get("start_line")
        msg = (inst.get("message", {}) or {}).get("text")

        help_text, help_uri = _rule_help_fields(a.get("rule"))

        lines.append(f"- **#{num}** [{rule}]({url}) — {desc}")
        if path:
            lines.append(f"  - `{path}:{line}`")
        if msg:
            lines.append(f"  - {msg}")
        if help_text:
            lines.append(f"  - **Recommendation:** {help_text}")
        if help_uri:
            lines.append(f"  - **More info:** {help_uri}")

    return "\n".join(lines) + "\n"


def safe_filename(name: str) -> str:
    """Create a filesystem-safe filename from an arbitrary string.

    This maintains readability by turning separators like `::`, `/`, and whitespace
    into single dashes, then collapsing repeated dashes.
    """

    name = name.strip().lower()
    # Replace common separators with a dash
    # Note: \ in file paths is a path separator on Windows.
    name = re.sub(r"::|/|\\", "-", name)
    name = re.sub(r"\s+", "-", name)

    # Remove everything except a-z0-9, dash, underscore, dot
    name = re.sub(r"[^a-z0-9._-]", "-", name)

    # Collapse repeated dashes
    name = re.sub(r"-+", "-", name)

    # Trim dashes
    name = name.strip("-")

    if not name:
        name = "alert"
    return name


try:
    from send2trash import send2trash
except ImportError:
    send2trash = None


def _safe_delete(path: str) -> None:
    """Delete a file via Recycle Bin if possible, otherwise unlink directly."""
    if send2trash is not None:
        try:
            send2trash(path)
            return
        except Exception as e:
            print(f"WARNING: send2trash failed for {path}, falling back to os.remove: {e}", file=sys.stderr)

    try:
        os.remove(path)
    except Exception as e:
        print(f"ERROR: failed to remove file {path}: {e}", file=sys.stderr)


def next_issue_id(todo_root: str) -> str:
    """Compute the next sequential issue id (3-digit) for a new todo file."""

    pattern = os.path.join(todo_root, "**", "[0-9][0-9][0-9]-*.md")
    max_id = 0
    for path in glob.glob(pattern, recursive=True):
        base = os.path.basename(path)
        m = re.match(r"^(\d{3})-", base)
        if m:
            try:
                num = int(m.group(1))
                max_id = max(max_id, num)
            except ValueError:
                continue
    return f"{max_id + 1:03d}"


def _resolve_owner_repo(owner: Optional[str], repo: Optional[str]) -> tuple[Optional[str], Optional[str]]:
    if owner and repo:
        return owner, repo

    inferred = get_git_origin_repo()
    if inferred:
        return owner or inferred[0], repo or inferred[1]

    return None, None


def _load_cache(
    cache_path: str,
    cache_ttl: int,
    owner: str,
    repo: str,
    scan_workflows: str,
    cache_based_on_codeql: bool,
) -> Optional[Any]:
    try:
        if not os.path.exists(cache_path):
            return None

        if cache_based_on_codeql:
            last_scan = get_last_scan_workflow_run_time(owner, repo, scan_workflows)
            if last_scan is not None and last_scan > os.path.getmtime(cache_path):
                return None

        age = time.time() - os.path.getmtime(cache_path)
        if age > cache_ttl:
            return None

        with open(cache_path, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception as e:
        print(f"WARNING: failed to load cache {cache_path}: {e}", file=sys.stderr)
        traceback.print_exc(file=sys.stderr)
        return None


def _save_cache(cache_path: str, value: Any) -> None:
    try:
        cache_dir = os.path.dirname(cache_path)
        if cache_dir:
            os.makedirs(cache_dir, exist_ok=True)
        with open(cache_path, "w", encoding="utf-8") as f:
            json.dump(value, f, separators=(",", ":"), ensure_ascii=False)
    except Exception as e:
        print(f"WARNING: failed to save cache {cache_path}: {e}", file=sys.stderr)
        traceback.print_exc(file=sys.stderr)


def _clean_todo_dir(todo_root: str) -> None:
    pattern = os.path.join(todo_root, "**", "[0-9][0-9][0-9]-*.md")
    for existing in glob.glob(pattern, recursive=True):
        _safe_delete(existing)


def _write_todos(groups: Dict[str, List[Dict[str, Any]]], todo_root: str) -> int:
    issue_id = next_issue_id(todo_root)
    for group_key, alerts in groups.items():
        if not alerts:
            continue
        filename = safe_filename(group_key)
        first_alert = alerts[0]
        severity = (first_alert.get("rule", {}) or {}).get("severity")
        todo_path = os.path.join(
            todo_root,
            f"{issue_id}-pending-{severity_to_priority(severity)}-{filename}.md",
        )
        with open(todo_path, "w", encoding="utf-8") as f:
            f.write(render_todo_body(issue_id, group_key, alerts))
        issue_id = f"{int(issue_id) + 1:03d}"
    return len(groups)


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description="Fetch GitHub Code Scanning alerts and write todos for each alert group."
    )
    parser.add_argument(
        "--owner",
        default=None,
        help="GitHub repository owner (default: inferred from git origin)",
    )
    parser.add_argument(
        "--repo",
        default=None,
        help="GitHub repository name (default: inferred from git origin)",
    )
    parser.add_argument(
        "--state",
        default="open",
        choices=["open", "closed", "dismissed"],
        help="Alert state to fetch",
    )
    parser.add_argument(
        "--per-page",
        type=int,
        default=100,
        help="Number of alerts to request per page (API limit is 100)",
    )
    parser.add_argument(
        "--cache-file",
        default="docs/logs/code_scanning_alerts_cache.json",
        help="Local cache file path for storing the last API response",
    )
    parser.add_argument(
        "--cache-ttl",
        type=int,
        default=3600,
        help="Time in seconds before cached response is considered stale",
    )
    parser.add_argument(
        "--no-cache-based-on-codeql",
        dest="cache_based_on_codeql",
        action="store_false",
        help="Do not treat the cache as stale based on the latest successful CodeQL workflow run (enabled by default)",
    )
    parser.add_argument(
        "--scan-workflows",
        default=DEFAULT_SCAN_WORKFLOWS,
        help="Comma-separated list of workflow filenames (e.g. CodeQL, SonarQube) used to determine cache freshness",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Force re-fetching alerts from the API even if cache is fresh",
    )
    parser.add_argument(
        "--preview-todos",
        action="store_true",
        help="Print a summary of how many todos would be generated and group counts (no files written)",
    )
    parser.add_argument(
        "--write-todos",
        action="store_true",
        help="Write todo markdown files under the provided directory (default: todos/). This is implied when no output mode is specified.",
    )
    parser.add_argument(
        "--todo-dir",
        default="todos/code-scanning",
        help="Root directory to write code scanning todo files when --write-todos is enabled (default: todos/code-scanning)",
    )
    parser.add_argument(
        "--clean",
        action="store_true",
        help="Remove previously-generated code scanning todo files under --todo-dir before writing new ones",
    )
    parser.add_argument(
        "--group-by",
        default="thirdparty_rule",
        choices=["rule", "path", "severity", "thirdparty", "thirdparty_rule"],
        help="How to group alerts when generating todo items",
    )

    args = parser.parse_args(argv)

    owner, repo = _resolve_owner_repo(args.owner, args.repo)
    if not owner or not repo:
        parser.error("Unable to determine GitHub owner/repo. Pass --owner/--repo or run from a git clone with origin set.")

    cache_path = os.path.abspath(args.cache_file)

    data = None
    if not args.force:
        data = _load_cache(
            cache_path,
            args.cache_ttl,
            owner,
            repo,
            args.scan_workflows,
            args.cache_based_on_codeql,
        )

    if data is None:
        data = run_gh_api(owner, repo, args.state, args.per_page)
        _save_cache(cache_path, data)

    if not isinstance(data, list):
        print("ERROR: expected a list of alerts from the API", file=sys.stderr)
        return 1

    groups = group_alerts(data, group_by=args.group_by)
    print(f"Found {len(data)} alerts in {len(groups)} groups (grouped by {args.group_by}).")
    for k, v in sorted(groups.items(), key=lambda i: (-len(i[1]), i[0])):
        print(f" - {len(v):4d} alerts in group '{k}'")

    if args.preview_todos and args.write_todos:
        parser.error("Cannot use --preview-todos and --write-todos together. Choose one.")

    if args.preview_todos:
        return 0

    todo_root = os.path.abspath(args.todo_dir)
    if args.clean and os.path.abspath(todo_root) in (os.path.abspath("todos"), os.path.abspath(".")):
        parser.error("--clean on top-level dirs is not allowed. Use --todo-dir todos/code-scanning (default).")

    os.makedirs(todo_root, exist_ok=True)
    if args.clean:
        _clean_todo_dir(todo_root)

    written = _write_todos(groups, todo_root)
    print(f"Wrote {written} todo file(s) to {todo_root}")
    return 0


class GetLastScanWorkflowRunTimeTests(unittest.TestCase):
    def _make_payload(self, updated_at: str, conclusion: str = "success") -> str:
        return json.dumps({"workflow_runs": [{"updated_at": updated_at, "conclusion": conclusion}]})

    def test_returns_timestamp_for_single_workflow(self):
        payload = self._make_payload("2026-03-15T12:00:00Z")
        ts = get_last_scan_workflow_run_time(
            "owner", "repo", DEFAULT_SCAN_WORKFLOWS, check_output_fn=lambda *args, **kwargs: payload
        )
        expected = datetime.fromisoformat("2026-03-15T12:00:00+00:00").timestamp()
        self.assertEqual(ts, expected)

    def test_returns_latest_timestamp_across_multiple_workflows(self):
        payloads = [
            self._make_payload("2026-03-15T12:00:00Z"),
            self._make_payload("2026-03-15T13:00:00Z"),
        ]
        it = iter(payloads)

        def side_effect(*args, **kwargs):
            try:
                return next(it)
            except StopIteration:
                raise AssertionError("check_output called more times than expected")

        ts = get_last_scan_workflow_run_time(
            "owner",
            "repo",
            "codeql.yml,sonar.yml",
            check_output_fn=side_effect,
        )
        expected = datetime.fromisoformat("2026-03-15T13:00:00+00:00").timestamp()
        self.assertEqual(ts, expected)

    def test_returns_none_when_no_runs(self):
        ts = get_last_scan_workflow_run_time(
            "owner",
            "repo",
            DEFAULT_SCAN_WORKFLOWS,
            check_output_fn=lambda *args, **kwargs: json.dumps({"workflow_runs": []}),
        )
        self.assertIsNone(ts)

    def test_selects_most_recent_successful_run(self):
        # A more recent failed run should not override an earlier successful run.
        payload = json.dumps(
            {
                "workflow_runs": [
                    {"updated_at": "2026-03-15T15:00:00Z", "conclusion": "failure"},
                    {"updated_at": "2026-03-15T14:00:00Z", "conclusion": "success"},
                ]
            }
        )

        ts = get_last_scan_workflow_run_time(
            "owner",
            "repo",
            DEFAULT_SCAN_WORKFLOWS,
            check_output_fn=lambda *args, **kwargs: payload,
        )
        expected = datetime.fromisoformat("2026-03-15T14:00:00+00:00").timestamp()
        self.assertEqual(ts, expected)

    def test_ignores_invalid_timestamp_values(self):
        ts = get_last_scan_workflow_run_time(
            "owner",
            "repo",
            DEFAULT_SCAN_WORKFLOWS,
            check_output_fn=lambda *args, **kwargs: json.dumps({
                "workflow_runs": [{"updated_at": "not-a-time"}]
            }),
        )
        self.assertIsNone(ts)


class AlertGroupingAndTodoRenderingTests(unittest.TestCase):
    def test_group_alerts_by_severity(self):
        alerts = [
            {"rule": {"severity": "error", "name": "R1"}},
            {"rule": {"severity": "warning", "name": "R2"}},
            {"rule": {"severity": "error", "name": "R3"}},
        ]
        grouped = group_alerts(alerts, group_by="severity")
        self.assertIn("error", grouped)
        self.assertIn("warning", grouped)
        self.assertEqual(len(grouped["error"]), 2)
        self.assertEqual(len(grouped["warning"]), 1)

    def test_render_todo_body_includes_recommendation_and_uri(self):
        alerts = [
            {
                "number": 42,
                "html_url": "https://example.com/alert/42",
                "rule": {
                    "name": "ExampleRule",
                    "description": "Example description",
                    "severity": "warning",
                    "help": {"text": "Do something"},
                    "helpUri": "https://example.com/docs",
                },
                "most_recent_instance": {
                    "location": {"path": "Source/Main.cs", "start_line": 10},
                    "message": {"text": "Example issue"},
                },
            }
        ]
        body = render_todo_body("100", "ExampleRule", alerts)
        self.assertIn("**Recommendation:** Do something", body)
        self.assertIn("**More info:** https://example.com/docs", body)
        self.assertIn("`Source/Main.cs:10`", body)


if __name__ == "__main__":
    sys.exit(main())
