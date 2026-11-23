#!/usr/bin/env bash
# Deletes a local branch once its PR is merged and every line it added is on main.
# Squash merges rewrite history, so `git branch -d` and tree/diff comparisons give false alarms;
# checking the branch's added lines against main does not.
# Usage: scripts/delete-merged-branch.sh <branch> <pr-number>
set -euo pipefail
branch="${1:?branch name}"; pr="${2:?PR number}"

state=$(gh pr view "$pr" --json state --jq .state)
[ "$state" = "MERGED" ] || { echo "PR #$pr is $state, not MERGED. Keeping $branch." >&2; exit 1; }

git switch -q main
git pull -q --ff-only
git fetch -q --prune

base=$(git merge-base main "$branch")
missing=0
while IFS= read -r file; do
  git cat-file -e "main:$file" 2>/dev/null || { echo "Missing on main: $file" >&2; missing=1; continue; }
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    git show "main:$file" | grep -qxF -- "$line" || { echo "Not on main ($file): ${line:0:100}" >&2; missing=1; }
  done < <(git diff "$base" "$branch" -- "$file" | grep '^+' | grep -v '^+++' | sed 's/^+//')
done < <(git diff --name-only --diff-filter=AM "$base" "$branch")

[ "$missing" -eq 0 ] || { echo "$branch has work that is not on main. Not deleting; ask the owner." >&2; exit 1; }
git branch -D "$branch"
