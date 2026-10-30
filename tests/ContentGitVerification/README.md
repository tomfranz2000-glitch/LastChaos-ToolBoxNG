# Content Git verification

Run `dotnet run --project tests/ContentGitVerification/ContentGitVerification.csproj`.
An optional argument filters fixture names, for example `-- LFS`.

The console fixture builds the recorder independently and uses real Git in uniquely
named temporary repositories. It never connects to the game database, records the
game checkout, launches an application, pushes, or changes a user's branch.

Coverage includes unrelated staged/unstaged/untracked files, clean-filter comparisons
and dirty no-ops, dirty target rejection, hook and signing failure rollback,
commit-message hooks, path traversal/private data/junction rejection, HEAD and branch
changes during capture/publication, concurrent edits, detached HEAD/merge state, and
hidden index flags. With Git LFS installed, a temporary localhost locking endpoint
also exercises real LFS pointer creation, Unicode/new assets, existing user locks,
lock cleanup, failed-commit intent-to-add cleanup, and an unavailable locking service.

The application does not disable Git hooks or signing and does not push. Changes
are committed with `git commit --only` so unrelated staged entries stay staged.
Existing edits on a generated target block recording; unchanged exports leave even
a dirty target untouched. A failed recording restores only files/index entries it
published, preserving concurrent edits and reporting paths that require attention.
