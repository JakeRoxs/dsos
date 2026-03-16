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
    except (subprocess.CalledProcessError, FileNotFoundError, OSError):
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


def get_last_scan_workflow_run_time(
    owner: str,
    repo: str,
    workflow_files: List[str] | str = "codeql.yml",
    check_output_fn: Callable[..., str] | None = None,
) -> Optional[float]:
    """Return the Unix timestamp of the last successful scan workflow run.

    Uses the GitHub Actions API via `gh api` to fetch the latest completed, successful
    workflow run for one or more workflow files.

    Args:
        owner: GitHub repo owner.
        repo: GitHub repo name.
        workflow_files: A workflow filename or comma-separated list of workflow filenames
            (e.g. codeql.yml, sonar.yml) to consider.

    Returns:
        Unix timestamp (seconds) of the most recent run across all workflows, or None if unknown.
    """

    if isinstance(workflow_files, str):
        workflow_files = [wf.strip() for wf in workflow_files.split(",") if wf.strip()]

    latest_ts: Optional[float] = None
    if check_output_fn is None:
        check_output_fn = subprocess.check_output

    for workflow_file in workflow_files:
        try:
            # The GitHub Actions API does not reliably support `conclusion=success` as a query parameter.
            # Request completed runs and select the first successful one in code.
            cmd = [
                "gh",
                "api",
                "-H",
                "Accept: application/vnd.github+json",
                f"/repos/{owner}/{repo}/actions/workflows/{workflow_file}/runs?status=completed&per_page=50",
            ]
            # Allow injection of a custom check_output-like function for testing.
            # Some callables may not accept `stderr`/`text`, so fall back if needed.
            try:
                output = check_output_fn(cmd, stderr=subprocess.STDOUT, text=True)
            except TypeError:
                output = check_output_fn(cmd)
            payload = json.loads(output)
            runs = payload.get("workflow_runs") or []
            if not runs:
                # No completed runs for this workflow; try the next one.
                continue

            # Select the most recent successful run. If no successful runs exist,
            # fall back to the most recent completed run.
            def _parse_ts(run: Dict[str, Any]) -> Optional[float]:
                updated_at = run.get("updated_at") or run.get("created_at")
                if not updated_at:
                    return None
                # GitHub timestamps are ISO 8601 with 'Z' (UTC) and may include fractional seconds.
                # Example: 2024-03-15T18:37:12.345Z
                try:
                    if isinstance(updated_at, str) and updated_at.endswith("Z"):
                        updated_at = updated_at[:-1] + "+00:00"
                    dt = datetime.fromisoformat(updated_at)
                    return dt.astimezone(timezone.utc).timestamp()
                except ValueError as e:
                    print(
                        f"WARNING: failed to parse timestamp from workflow '{workflow_file}' response: {e}",
                        file=sys.stderr,
                    )
                    return None

            parsed_runs = []
            for run in runs:
                ts = _parse_ts(run)
                if ts is not None:
                    parsed_runs.append((ts, run.get("conclusion"), run))

            if not parsed_runs:
                # None of the returned runs contained a parseable timestamp.
                continue

            # Prefer the latest successful run by timestamp.
            successful_runs = [t for t in parsed_runs if t[1] == "success"]
            candidate = max(successful_runs or parsed_runs, key=lambda t: t[0])
            ts = candidate[0]
            if latest_ts is None or ts > latest_ts:
                latest_ts = ts
        except (subprocess.CalledProcessError, json.JSONDecodeError) as e:
            print(
                f"WARNING: failed to query GitHub Actions API for workflow '{workflow_file}': {e}",
                file=sys.stderr,
            )
            continue

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

    def thirdparty_name(path: str) -> str:
        if path.startswith("Source/ThirdParty/"):
            remainder = path[len("Source/ThirdParty/") :]
            return remainder.split("/", 1)[0] or "<thirdparty>"
        return "<repo>"

    for a in alerts:
        if group_by == "path":
            key = (a.get("most_recent_instance", {}) or {}).get("location", {}).get("path") or "<unknown>"
        elif group_by == "severity":
            key = (a.get("rule", {}) or {}).get("severity") or "<unknown>"
        elif group_by == "thirdparty":
            path = (a.get("most_recent_instance", {}) or {}).get("location", {}).get("path", "")
            key = thirdparty_name(path)
        elif group_by == "thirdparty_rule":
            path = (a.get("most_recent_instance", {}) or {}).get("location", {}).get("path", "")
            lib = thirdparty_name(path)
            rule = (a.get("rule", {}) or {}).get("name") or "<unknown>"
            key = f"{lib}::{rule}"
        else:
            key = (a.get("rule", {}) or {}).get("name") or "<unknown>"

        out.setdefault(key, []).append(a)

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


def render_todo_body(
    issue_id: str, group_key: str, alerts: List[Dict[str, Any]], default_priority: str = "p2"
) -> str:
    """Render a markdown todo body for a set of alerts in file-todos format."""

    title = f"Code scanning alerts: {group_key}"

    # Determine priority (use highest severity seen)
    def _priority_rank(prio: str) -> int:
        return {"p1": 1, "p2": 2, "p3": 3}.get(prio, 2)

    priority = default_priority
    best_rank = _priority_rank(priority)
    for a in alerts:
        sev = (a.get("rule", {}) or {}).get("severity")
        p = severity_to_priority(sev)
        pr = _priority_rank(p)
        if pr < best_rank:
            best_rank = pr
            priority = p

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
        rule_obj = a.get("rule")
        if isinstance(rule_obj, dict):
            help_field = rule_obj.get("help")
            if isinstance(help_field, dict):
                help_text = help_field.get("text")
            else:
                help_text = help_field
            help_uri = rule_obj.get("helpUri")
        else:
            help_text = None
            help_uri = None

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
        default="codeql.yml",
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
        help="Write todo markdown files under the provided directory (default: todos/)",
    )
    parser.add_argument(
        "--todo-dir",
        default="todos",
        help="Root directory to write todo files when --write-todos is enabled",
    )
    parser.add_argument(
        "--clean",
        action="store_true",
        help="Remove previously-generated todo files under --todo-dir before writing new ones",
    )
    parser.add_argument(
        "--group-by",
        default="thirdparty_rule",
        choices=["rule", "path", "severity", "thirdparty", "thirdparty_rule"],
        help="How to group alerts when generating todo items",
    )

    args = parser.parse_args(argv)

    owner = args.owner
    repo = args.repo
    if not owner or not repo:
        inferred = get_git_origin_repo()
        if inferred:
            if not owner:
                owner = inferred[0]
            if not repo:
                repo = inferred[1]

    if not owner or not repo:
        parser.error("Unable to determine GitHub owner/repo. Pass --owner/--repo or run from a git clone with origin set.")

    data = None

    cache_path = os.path.abspath(args.cache_file)
    cache_ttl = args.cache_ttl

    def _load_cache() -> Optional[Any]:
        try:
            if not os.path.exists(cache_path):
                return None

            # Treat cache as stale if CodeQL has run since the cache was written.
            if args.cache_based_on_codeql:
                last_scan = get_last_scan_workflow_run_time(owner, repo, args.scan_workflows)
                if last_scan is not None and last_scan > os.path.getmtime(cache_path):
                    return None

            # TTL-based freshness
            age = time.time() - os.path.getmtime(cache_path)
            if age > cache_ttl:
                return None

            with open(cache_path, "r", encoding="utf-8") as f:
                return json.load(f)
        except Exception as e:
            print(f"WARNING: failed to load cache {cache_path}: {e}", file=sys.stderr)
            traceback.print_exc(file=sys.stderr)
            return None

    def _save_cache(value: Any) -> None:
        try:
            cache_dir = os.path.dirname(cache_path)
            if cache_dir:
                os.makedirs(cache_dir, exist_ok=True)
            with open(cache_path, "w", encoding="utf-8") as f:
                json.dump(value, f, separators=(",", ":"), ensure_ascii=False)
        except Exception as e:
            print(f"WARNING: failed to save cache {cache_path}: {e}", file=sys.stderr)
            traceback.print_exc(file=sys.stderr)

    if not args.force:
        data = _load_cache()

    if data is None:
        data = run_gh_api(owner, repo, args.state, args.per_page)
        # Cache the raw data for faster subsequent runs
        _save_cache(data)

    if args.preview_todos or args.write_todos:
        if not isinstance(data, list):
            print("ERROR: expected a list of alerts from the API", file=sys.stderr)
            return 1

        groups = group_alerts(data, group_by=args.group_by)
        print(f"Found {len(data)} alerts in {len(groups)} groups (grouped by {args.group_by}).")
        for k, v in sorted(groups.items(), key=lambda i: (-len(i[1]), i[0])):
            print(f" - {len(v):4d} alerts in group '{k}'")

        if args.write_todos:
            todo_root = os.path.abspath(args.todo_dir)
            os.makedirs(todo_root, exist_ok=True)

            if args.clean:
                # Remove generated todo files (e.g. 001-pending-*.md) recursively.
                # This matches the same scope used by next_issue_id(), preventing stale todos or issue ID collisions.
                pattern = os.path.join(todo_root, "**", "[0-9][0-9][0-9]-*.md")
                for existing in glob.glob(pattern, recursive=True):
                    try:
                        os.remove(existing)
                    except OSError:
                        pass

            issue_id = next_issue_id(todo_root)
            for group_key, alerts in groups.items():
                if not alerts:
                    # Defensive: skip empty groups (should not happen under normal operation)
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

            print(f"Wrote {len(groups)} todo file(s) to {todo_root}")
            return 0

        # Preview only
        return 0

    return 0


class GetLastScanWorkflowRunTimeTests(unittest.TestCase):
    def _make_payload(self, updated_at: str, conclusion: str = "success") -> str:
        return json.dumps({"workflow_runs": [{"updated_at": updated_at, "conclusion": conclusion}]})

    def test_returns_timestamp_for_single_workflow(self):
        payload = self._make_payload("2026-03-15T12:00:00Z")
        ts = get_last_scan_workflow_run_time(
            "owner", "repo", "codeql.yml", check_output_fn=lambda *args, **kwargs: payload
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
            "codeql.yml",
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
            "codeql.yml",
            check_output_fn=lambda *args, **kwargs: payload,
        )
        expected = datetime.fromisoformat("2026-03-15T14:00:00+00:00").timestamp()
        self.assertEqual(ts, expected)

    def test_ignores_invalid_timestamp_values(self):
        ts = get_last_scan_workflow_run_time(
            "owner",
            "repo",
            "codeql.yml",
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
