#!/usr/bin/env python3
"""Fetch GitHub Code Scanning alerts via gh and generate todo files.

This script is intended for developers to generate file-todo entries from current
open alerts on GitHub or SonarQube. It supports:

- `--source github` (default) using `gh api /repos/{owner}/{repo}/code-scanning/alerts`
- `--source sonarqube` using SonarQube API (`/api/issues/search`)
- SonarQube config via CLI args or `mcp.json` (`servers.sonarqube.env.SONARQUBE_*`)
- Fallback to `sonar-project.properties` for `sonar.projectKey`
- `--group-by rule` (default) to group by rule names across repositories
- `--group-by thirdparty` to use repository name (e.g. `dsos`) for in-repo code, and library folder for `Source/ThirdParty`
- `--group-by thirdparty_rule` to group by vendor+rule
- De-dup/update behavior: existing todo file is updated if content changed, otherwise left alone

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
import tempfile
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
DEFAULT_MCP_CONFIG_FILE = os.path.expanduser(
    os.path.join("~", "AppData", "Roaming", "Code", "User", "mcp.json")
)


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
    if url.startswith("https://"):
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


def load_mcp_sonarqube_config(config_path: str) -> Dict[str, str]:
    """Load SonarQube values from an mcp.json ["servers"]["sonarqube"] entry."""
    try:
        with open(config_path, "r", encoding="utf-8") as f:
            obj = json.load(f)
    except FileNotFoundError:
        return {}
    except Exception as e:
        print(f"WARNING: unable to parse MCP config {config_path}: {e}", file=sys.stderr)
        return {}

    sonar = (obj.get("servers") or {}).get("sonarqube") or {}
    if not isinstance(sonar, dict):
        return {}

    return {
        "sonarqube_url": str(sonar.get("env", {}).get("SONARQUBE_URL", "") or sonar.get("url", "") or "").strip(),
        "sonarqube_token": str(sonar.get("env", {}).get("SONARQUBE_TOKEN", "") or "").strip(),
        "sonarqube_project_key": str(sonar.get("env", {}).get("SONARQUBE_PROJECT_KEY", "") or "").strip(),
    }


def load_sonar_properties_project_key(properties_path: str) -> str:
    """Read sonar.projectKey from sonar-project.properties."""
    try:
        with open(properties_path, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if line.startswith("#") or not line:
                    continue
                if "=" in line:
                    key, value = line.split("=", 1)
                    if key.strip() == "sonar.projectKey":
                        return value.strip()
    except FileNotFoundError:
        return ""
    except Exception as e:
        print(f"WARNING: unable to parse sonar properties file {properties_path}: {e}", file=sys.stderr)
    return ""


def _mask_token(token: str) -> str:
    if not token:
        return ""
    if len(token) <= 8:
        return "*" * len(token)
    return f"{token[:4]}{'*' * (len(token) - 8)}{token[-4:]}"


def resolve_sonarqube_settings(args: Any, parser: argparse.ArgumentParser) -> bool:
    """Resolve SonarQube settings from legacy sources and validate required fields."""
    if args.source != "sonarqube":
        return False

    if not (args.sonarqube_url and args.sonarqube_token and args.sonarqube_project_key):
        mcp_cfg = load_mcp_sonarqube_config(args.mcp_config)
        args.sonarqube_url = args.sonarqube_url or mcp_cfg.get("sonarqube_url", "")
        args.sonarqube_token = args.sonarqube_token or mcp_cfg.get("sonarqube_token", "")
        args.sonarqube_project_key = args.sonarqube_project_key or mcp_cfg.get("sonarqube_project_key", "")

    if not args.sonarqube_project_key:
        args.sonarqube_project_key = load_sonar_properties_project_key(args.sonarqube_properties)

    if not args.sonarqube_url:
        parser.error("--sonarqube-url is required for --source sonar")
    if not args.sonarqube_token:
        parser.error("--sonarqube-token is required for --source sonar")
    if not args.sonarqube_project_key:
        parser.error("--sonarqube-project-key is required for --source sonar")

    # log resolved values for debugging, hiding sensitive token segments
    print(
        f"Resolved SonarQube config: url={args.sonarqube_url}, token={_mask_token(args.sonarqube_token)}, project_key={args.sonarqube_project_key}"
    )

    return True


def get_alert_data(args: Any, owner_str: str, repo_str: str, cache_path: str) -> List[Dict[str, Any]]:
    """Load or fetch scan data based on source selection."""
    data = None
    if args.source == "github" and not args.force:
        data = _load_cache(
            cache_path,
            args.cache_ttl,
            owner_str,
            repo_str,
            args.scan_workflows,
            args.cache_based_on_codeql,
        )

    if data is None:
        if args.source == "github":
            data = run_gh_api(owner_str, repo_str, args.state, args.per_page)
            _save_cache(cache_path, data)
        else:
            # Sanity check the key is loaded from sonar-project.properties if missing in mcp
            if not args.sonarqube_project_key:
                args.sonarqube_project_key = load_sonar_properties_project_key(args.sonarqube_properties)

            print(
                f"Fetching SonarQube issues with url={args.sonarqube_url}, token={_mask_token(args.sonarqube_token)}, project_key={args.sonarqube_project_key}"
            )

            sonar_issues = run_sonarqube_api(
                args.sonarqube_url,
                args.sonarqube_project_key,
                args.sonarqube_token,
                state=args.state.upper(),
                page_size=args.per_page,
            )
            data = [
                _sonarqube_issue_to_alert(issue, args.sonarqube_url, args.sonarqube_project_key)
                for issue in sonar_issues
            ]

    if not isinstance(data, list):
        print("ERROR: expected a list of alerts from the API", file=sys.stderr)
        sys.exit(1)

    return data


def run_todo_generation(args: Any, data: List[Dict[str, Any]], parser: argparse.ArgumentParser) -> int:
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
    return written


def run_arg_parser() -> argparse.ArgumentParser:
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
        "--source",
        choices=["github", "sonarqube"],
        default="github",
        help="Source to fetch security alerts from",
    )
    parser.add_argument(
        "--mcp-config",
        default=os.environ.get("MCP_CONFIG", DEFAULT_MCP_CONFIG_FILE),
        help="Path to MCP settings file (used to auto-load SonarQube settings)",
    )
    parser.add_argument(
        "--sonarqube-properties",
        default="sonar-project.properties",
        help="Path to sonar-project.properties (used to auto-load sonar.projectKey)",
    )
    parser.add_argument(
        "--sonarqube-url",
        default=os.environ.get("SONARQUBE_URL", ""),
        help="SonarQube server URL (required for --source sonar)",
    )
    parser.add_argument(
        "--sonarqube-token",
        default=os.environ.get("SONARQUBE_TOKEN", ""),
        help="SonarQube authentication token (required for --source sonar)",
    )
    parser.add_argument(
        "--sonarqube-project-key",
        default=os.environ.get("SONARQUBE_PROJECT_KEY", ""),
        help="SonarQube project key (required for --source sonar)",
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
        default="rule",
        choices=["rule", "path", "severity", "thirdparty", "thirdparty_rule"],
        help="How to group alerts when generating todo items",
    )

    return parser


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

def run_sonarqube_api(
    sonar_url: str,
    project_key: str,
    token: str,
    state: str = "OPEN",
    page_size: int = 500,
) -> List[Dict[str, Any]]:
    """Fetch issues from SonarQube and return as a list of issue dicts."""

    if not sonar_url.rstrip("/"):
        raise ValueError("SonarQube URL is required")
    if not project_key:
        raise ValueError("SonarQube project key is required")

    sonar_url = sonar_url.rstrip("/")
    api_endpoint = f"{sonar_url}/api/issues/search"

    issues: List[Dict[str, Any]] = []
    page = 1

    while True:
        query = (
            f"componentKeys={project_key}&statuses={state}&ps={page_size}&p={page}"
        )
        payload = _fetch_sonarqube_page(api_endpoint, token, query)

        page_issues = payload.get("issues", [])
        if not isinstance(page_issues, list):
            raise ValueError("Unexpected SonarQube API response format")

        issues.extend(page_issues)

        paging = payload.get("paging", {})
        total = int(paging.get("total", 0))
        if len(issues) >= total or len(page_issues) == 0:
            break

        page += 1

    return issues


def _fetch_sonarqube_page(api_endpoint: str, token: str, query: str) -> Dict[str, Any]:
    cmd = [
        "curl",
        "-s",
        "-u",
        f"{token}:",
        f"{api_endpoint}?{query}",
    ]

    try:
        result = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    except subprocess.SubprocessError as e:
        print(f"ERROR: failed to run SonarQube API: {e}", file=sys.stderr)
        traceback.print_exc(file=sys.stderr)
        raise SystemExit(1)

    if result.returncode != 0:
        print(
            f"ERROR: SonarQube API call failed (exit {result.returncode}). stderr: {result.stderr.strip()}",
            file=sys.stderr,
        )
        raise SystemExit(1)

    output = result.stdout.strip()
    if not output:
        print("ERROR: SonarQube API returned empty response.", file=sys.stderr)
        raise SystemExit(1)

    try:
        return json.loads(output)
    except json.JSONDecodeError:
        print("ERROR: failed to parse JSON from SonarQube API output:", file=sys.stderr)
        print(output, file=sys.stderr)
        traceback.print_exc(file=sys.stderr)
        raise SystemExit(1)



def _sonarqube_issue_to_alert(issue: Dict[str, Any], sonar_url: str, project_key: str) -> Dict[str, Any]:
    component = issue.get("component", "")
    path = component.split(":", 1)[1] if ":" in component else component

    return {
        "number": issue.get("key", ""),
        "state": issue.get("status", "OPEN"),
        "rule": {
            "name": issue.get("rule", ""),
            "severity": issue.get("severity", ""),
            "type": issue.get("type", ""),
            "description": issue.get("message", ""),
            "help": issue.get("message", ""),
            "helpUri": None,
        },
        "most_recent_instance": {
            "location": {
                "path": path,
                "start_line": issue.get("line"),
            },
            "message": {"text": issue.get("message", "")},
        },
        "html_url": f"{sonar_url}/project/issues?id={project_key}&resolved=false&severities={issue.get('severity','')}&rules={issue.get('rule','')}",
    }


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
    owner: Optional[str],
    repo: Optional[str],
    workflow_files: List[str] | str = DEFAULT_SCAN_WORKFLOWS,
    check_output_fn: Callable[..., str] | None = None,
) -> Optional[float]:
    if not owner or not repo:
        return None

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


def _group_by_thirdparty(alert: Dict[str, Any], repo_name: Optional[str] = None) -> str:
    path = (alert.get("most_recent_instance", {}) or {}).get("location", {}).get("path", "")
    if path.startswith(THIRD_PARTY_PREFIX):
        remainder = path[len(THIRD_PARTY_PREFIX) :]
        return remainder.split("/", 1)[0] or "thirdparty"

    if repo_name:
        return repo_name
    return "project"


def _group_by_thirdparty_rule(alert: Dict[str, Any], repo_name: Optional[str] = None) -> str:
    # include a human-friendly root tag for non-thirdparty path groupings
    lib = _group_by_thirdparty(alert, repo_name)
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

    repo_name = None
    origin = get_git_origin_repo()
    if origin:
        repo_name = origin[1]

    if group_by == "path":
        key_selector = _group_by_path
    elif group_by == "severity":
        key_selector = _group_by_severity
    elif group_by == "thirdparty":
        key_selector = lambda alert: _group_by_thirdparty(alert, repo_name)
    elif group_by == "thirdparty_rule":
        key_selector = lambda alert: _group_by_thirdparty_rule(alert, repo_name)
    else:
        key_selector = _group_by_rule

    for a in alerts:
        out.setdefault(key_selector(a), []).append(a)

    return out


def severity_to_priority(severity: str | None) -> str:
    """Map severity to file-todos priority.

    Supports CodeQL and SonarQube severities.
    SonarQube: BLOCKER -> p1, CRITICAL -> p1, MAJOR -> p2, MINOR -> p3, INFO -> p3.
    CodeQL: error -> p1, warning -> p2, note/recommendation -> p3.
    """
    if not severity:
        return "p2"

    sev = severity.strip().lower()
    # SonarQube severity names
    if sev in ("blocker", "critical"):
        return "p1"
    if sev == "major":
        return "p2"
    if sev in ("minor", "info"):
        return "p3"

    # CodeQL severity names
    if sev == "error":
        return "p1"
    if sev == "warning":
        return "p2"
    if sev in ("note", "recommendation"):
        return "p3"

    # fallback
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
        lines.extend(_render_todo_alert(a))

    return "\n".join(lines) + "\n"


def _render_todo_alert(alert: Dict[str, Any]) -> List[str]:
    num = alert.get("number")
    url = alert.get("html_url")
    rule = (alert.get("rule", {}) or {}).get("name")
    desc = (alert.get("rule", {}) or {}).get("description")
    inst = (alert.get("most_recent_instance", {}) or {})
    loc = (inst.get("location", {}) or {})
    path = loc.get("path")
    line = loc.get("start_line")
    msg = (inst.get("message", {}) or {}).get("text")

    help_text, help_uri = _rule_help_fields(alert.get("rule"))
    category = (alert.get("rule", {}) or {}).get("type")

    out = [f"- **#{num}** [{rule}]({url}) — {desc}"]
    if category:
        out.append(f"  - **Category/Type:** {category}")
    if path:
        out.append(f"  - `{path}:{line}`")
    if msg:
        out.append(f"  - {msg}")
    if help_text:
        out.append(f"  - **Recommendation:** {help_text}")
    if help_uri:
        out.append(f"  - **More info:** {help_uri}")

    return out


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


def _existing_todo_path(todo_root: str, group_key: str) -> Optional[str]:
    safe_name = safe_filename(group_key)
    pattern = os.path.join(todo_root, "**", f"*-*-{safe_name}.md")
    found = sorted(glob.glob(pattern, recursive=True))
    return found[0] if found else None


def _extract_issue_id_from_filename(path: str) -> Optional[str]:
    base = os.path.basename(path)
    m = re.match(r"^(\d{3})-", base)
    return m.group(1) if m else None


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
    owner: Optional[str],
    repo: Optional[str],
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
    issue_id_counter = int(next_issue_id(todo_root))
    created_or_updated = 0

    for group_key, alerts in groups.items():
        if not alerts:
            continue

        filename = safe_filename(group_key)
        existing_path = _existing_todo_path(todo_root, group_key)

        if existing_path:
            existing_id = _extract_issue_id_from_filename(existing_path) or f"{issue_id_counter:03d}"
            new_body = render_todo_body(existing_id, group_key, alerts)

            with open(existing_path, "r", encoding="utf-8") as f:
                existing_body = f.read()

            if existing_body != new_body:
                with open(existing_path, "w", encoding="utf-8") as f:
                    f.write(new_body)
                created_or_updated += 1
            continue

        # create a new todo file for this group
        todo_path = os.path.join(
            todo_root,
            f"{issue_id_counter:03d}-pending-{severity_to_priority((alerts[0].get('rule', {}) or {}).get('severity'))}-{filename}.md",
        )
        with open(todo_path, "w", encoding="utf-8") as f:
            f.write(render_todo_body(f"{issue_id_counter:03d}", group_key, alerts))

        issue_id_counter += 1
        created_or_updated += 1

    return created_or_updated


def main(argv: Optional[List[str]] = None) -> int:
    parser = run_arg_parser()
    args = parser.parse_args(argv)

    # Resolve SonarQube settings if needed.
    resolve_sonarqube_settings(args, parser)

    owner, repo = _resolve_owner_repo(args.owner, args.repo)
    if args.source == "github" and (not owner or not repo):
        parser.error("Unable to determine GitHub owner/repo. Pass --owner/--repo or run from a git clone with origin set.")

    owner_str = str(owner or "")
    repo_str = str(repo or "")
    cache_path = os.path.abspath(args.cache_file)

    data = get_alert_data(args, owner_str, repo_str, cache_path)
    run_todo_generation(args, data, parser)
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
                    "type": "CODE_SMELL",
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
        self.assertIn("**Category/Type:** CODE_SMELL", body)
        self.assertIn("**Recommendation:** Do something", body)
        self.assertIn("**More info:** https://example.com/docs", body)
        self.assertIn("`Source/Main.cs:10`", body)

    def test_group_by_thirdparty_project_defaults_for_repo_sources(self):
        alert = {
            "rule": {"name": "DummyRule"},
            "most_recent_instance": {"location": {"path": "Source/dsos/File.cs", "start_line": 1}},
        }
        self.assertEqual(_group_by_thirdparty(alert, "dsos"), "dsos")

    def test_group_by_thirdparty_rule_includes_repo_name_when_on_repo(self):
        alert = {
            "rule": {"name": "DummyRule"},
            "most_recent_instance": {"location": {"path": "Source/dsos/File.cs", "start_line": 1}},
        }
        self.assertEqual(_group_by_thirdparty_rule(alert, "dsos"), "dsos::DummyRule")

    def test_write_todos_reuses_existing_file_and_updates_if_changed(self):
        alerts = [
            {
                "number": 1,
                "html_url": "https://example.com/alert/1",
                "rule": {"name": "MyRule", "description": "Desc", "severity": "warning", "help": "Fix it", "helpUri": "https://docs"},
                "most_recent_instance": {"location": {"path": "Source/Example.cs", "start_line": 5}, "message": {"text": "Issue"}},
            }
        ]

        with tempfile.TemporaryDirectory() as tmpdir:
            written_first = _write_todos({"MyRule": alerts}, tmpdir)
            self.assertEqual(written_first, 1)

            written_second = _write_todos({"MyRule": alerts}, tmpdir)
            self.assertEqual(written_second, 0)

            modified_alerts = [
                {
                    "number": 1,
                    "html_url": "https://example.com/alert/1",
                    "rule": {"name": "MyRule", "description": "Desc", "severity": "warning", "help": "Fix it now", "helpUri": "https://docs"},
                    "most_recent_instance": {"location": {"path": "Source/Example.cs", "start_line": 5}, "message": {"text": "Issue"}},
                }
            ]

            written_third = _write_todos({"MyRule": modified_alerts}, tmpdir)
            self.assertEqual(written_third, 1)


if __name__ == "__main__":
    sys.exit(main())
