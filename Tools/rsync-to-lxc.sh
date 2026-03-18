#!/usr/bin/env bash
# Sync the dsos repo from WSL to a remote nixOS LXC container.
#
# Usage:
#   ./Tools/rsync-to-lxc.sh [--dry-run] [--no-delete] [--exclude-file <path>] [--dest user@host:/path]
#
# Example:
#   ./Tools/rsync-to-lxc.sh --dry-run
#   ./Tools/rsync-to-lxc.sh --dest root@192.168.1.250:/home/user/projects/dsos
#
# Requirements:
#   - Run from within WSL (Linux environment).
#   - `rsync` installed in WSL (e.g. `sudo apt install rsync`).
#   - SSH key access to the remote host (e.g. `ssh-copy-id root@192.168.1.250`).
#
# Notes:
#   - This script mirrors the repo into the remote directory.
#   - By default it deletes files on the remote that are removed locally.
#   - Adjust the exclude list in `RSYNC_EXCLUDE_FILE` or pass `--exclude-file`.

set -euo pipefail

# Default values
DEST="root@192.168.1.250:/home/user/projects/dsos"
RSYNC_EXCLUDE_FILE=".rsync-excludes"
DRY_RUN=false
DELETE_FLAG="--delete"

# Locate repo root (where this script lives).
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

usage() {
  cat <<EOF
Usage: $0 [options]

Options:
  --dry-run              Run rsync in dry-run mode (shows what would change).
  --no-delete            Do not delete remote files that are removed locally.
  --exclude-file <path>  Use a custom rsync exclude file (default: ${RSYNC_EXCLUDE_FILE}).
  --dest <user@host:path>  Override the remote destination (default: ${DEST}).
  -h, --help             Show this help message.

Example:
  $0 --dry-run
  $0 --dest root@192.168.1.250:/home/user/projects/dsos
EOF
  exit 1
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --dry-run)
      DRY_RUN=true
      shift
      ;;
    --no-delete)
      DELETE_FLAG=""
      shift
      ;;
    --exclude-file)
      shift
      RSYNC_EXCLUDE_FILE="$1"
      shift
      ;;
    --dest)
      shift
      DEST="$1"
      shift
      ;;
    -h|--help)
      usage
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      ;;
  esac
done

# Ensure we are in the repo root so relative paths work consistently.
cd "$REPO_ROOT"

# If the user has a passphrase-protected key, ssh-agent can cache the unlocked key
# so we don't get a prompt on every rsync run. This is optional; if the user prefers
# to manage ssh-agent themselves, this is safe to skip.
ensure_ssh_agent_and_key_loaded() {
  # If agent is not running, start it
  if [[ -z "${SSH_AUTH_SOCK:-}" ]] || ! ssh-add -l >/dev/null 2>&1; then
    if [[ -z "${SSH_AUTH_SOCK:-}" ]]; then
      eval "$(ssh-agent -s)"
    fi

    # Attempt to load default key (prompting for passphrase once)
    # Skip if already loaded.
    if ! ssh-add -l >/dev/null 2>&1; then
      echo "[info] Loading SSH key into agent (passphrase may be required)"
      ssh-add ~/.ssh/id_ed25519 || true
    fi
  fi
}

ensure_ssh_agent_and_key_loaded

if [[ "$DRY_RUN" == true ]]; then
  echo "[info] Running in dry-run mode (no changes will be made)"
  DRY_FLAG="--dry-run"
else
  DRY_FLAG=""
fi

# Build the rsync command.
RSYNC_CMD=(
  rsync -avz --progress --compress --copy-links --human-readable
  $DRY_FLAG
  $DELETE_FLAG
  --exclude-from="$RSYNC_EXCLUDE_FILE"
  --exclude ".git/"
  --exclude "build/"
  --exclude "build_*"
  --exclude "*.user" --exclude "*.suo" --exclude "*.vcxproj.user"
  --exclude "*.pdb" --exclude "*.obj" --exclude "*.exe" --exclude "*.dll"
  --exclude "*.log" --exclude "*.tmp" --exclude "*.cache"
  --exclude "Tools/rsync-to-lxc.sh"
  .
  "$DEST"
)

# Print the command to make it easy to copy.
printf "\n[info] Running:\n  %s\n\n" "${RSYNC_CMD[*]}"

"${RSYNC_CMD[@]}"
