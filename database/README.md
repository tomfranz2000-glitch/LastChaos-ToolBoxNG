# Transactional content storage and version control

ToolBoxNG saves edit the running database. Saving does **not** create a Git commit.
Use **Record content changes** in the main window to record outstanding saved
content in the game repository. The manual export workflow remains available.

## Storage prerequisite

`20260929-transactional-content.sql` converts the 48 content tables writable by
the registered editors, item-deletion dependencies, and the string translator to
InnoDB. It does not change player/account tables or authored content values.
The matching game migration is
`x64-server/db/migrations/0009-toolbox-transactional-content.sql`, applied with
that repository's migration runner. The SQL here is a byte-identical reference;
apply the canonical game migration once, not both copies.
Existing applied migrations must not be modified.

The editor uses database transactions and refuses nontransactional write targets.
MyISAM does not support rollback; adding `START TRANSACTION` around a save is not
sufficient. A failure after deleting child rows must roll back the whole save.

Five legacy tables (`t_action`, `t_item`, `t_npc`, `t_rareoption`, `t_skill`)
also exceed InnoDB's local-row limit. Their 208 `VARCHAR(255)` columns become
`TEXT` so long values can use overflow storage. Per-table CHECK constraints keep
the original 255-character limits, and each column retains its original
character set, collation, nullability and default. Numeric fields and indexes
remain unchanged. Widening only to `VARCHAR(256)` is insufficient because actual
values up to 255 bytes still remain inline. See
[MariaDB's DYNAMIC row format documentation](https://mariadb.com/docs/server/server-usage/storage-engines/innodb/innodb-row-formats/innodb-dynamic-row-format).

The conversion was verified on an isolated MariaDB 11.4 instance with all 48
schemas and real content copied read-only from the development database. Import
passed, and all five large tables accepted rows filling every original VARCHAR
column to its old maximum length. Those probe updates were rolled back. An
item description of 256 characters was rejected by the new length constraint.
Strict mode was enabled throughout.

A second isolated copy was populated while still using the original MyISAM
schemas, then migrated with the complete SQL. That also passed. Content-only
dumps of the migrated copy and the imported reference were byte-identical.

The SQL belongs in a numbered migration, not a startup script. Back up the
database and apply the canonical numbered migration. Its DDL is safe to resume
after a partial failure: existing length constraints are retained with
`ADD CONSTRAINT IF NOT EXISTS`. Two complete repeat runs passed under strict
mode with unchanged content. Do not manually apply both copies.

On this development machine the canonical migration was applied on 2026-09-29
after backing up `ep4_data` and its migration ledger. Before/after comparison
found all 77,116 rows in the 48 affected tables unchanged. The local backup and
verification evidence are under `artifacts/backups/` and `artifacts/review/`.

## Verification and remaining limitations

The transaction regression harness and disposable-database instructions are in
[`tests/DatabaseWriteVerification/README.md`](../tests/DatabaseWriteVerification/README.md).
It covers rollback after a SQL error or connection loss, strict validation,
nontransactional-table refusal, and temporary-table cleanup. Atomicity covers
one editor save/delete batch; a multi-item selection can contain several batches.

The Quest Editor in this upstream version is an unfinished rare-option editor
copy whose actions target the wrong table. Its loader and write actions are
disabled with a visible explanation. Implementing quest editing is separate work.

## Alchemy station access

In **Professions Editor → Infusions → Station**, **Required Artisan Rank** names
existing ranks and stores their permanent IDs. **None — unrestricted** stores 0;
new station definitions default to ID 3. Retired ranks remain selectable because
previously earned milestones remain valid. Display order does not change the saved
reference, and a missing reference must be corrected before saving. This field
requires game migration `0040-alchemy-artisan-rank-unlock.sql`.

The shared content schema includes the requirement in normal transactional saves,
draft JSON and content exports. Re-enter the private hall to refresh its station
catalogue after saving an access-rule change. Character achievements are not edited.

## Versioning editor changes

### One-click recording

In the main window's **Content version control** panel, use **Choose game
checkout…** once. Select the game Git checkout root, not the Toolbox repository
or assembled client directory. Its path is saved in the local `Settings.ini`
under `[ContentVersioning] GameCheckout`; no credentials are added to Git.

After saving edits in any editor, click **Record content changes**. It also
captures saved SQL edits made outside the Toolbox. Unsaved editor drafts are not
included. The button:

1. Verifies the selected checkout, named branch, Git identity and database identity.
2. Takes one consistent copy of the 124 reviewed authored tables. Player,
   account, inventory, progression and log tables are excluded. Runtime columns
   in the five mixed tables are normalized only in temporary copies.
3. Runs the game's deterministic seed exporter against that copy. Only tables
   that differ from the current Git commit are candidates for publication.
4. Regenerates the affected shipping client data and localized strings from the
   same copy, using the configured client as read-only input where needed.
5. Creates one local content commit containing only changed seeds and client
   outputs. If nothing changed, it makes no commit.

Nothing is pushed, merged, applied to the database or copied into the running
client. The normal game asset assembly and database deployment workflows still
apply after reviewing the commit. The button does not version arbitrary edited
textures/models or schema changes; those still use their normal Git workflows.

Unrelated staged, unstaged and untracked work is preserved. Existing edits to
an output file that needs replacement cause a clear refusal; commit or move
those edits aside first. Branch changes during generation also cause a refusal.
Git hooks, signing and identity settings apply normally. Git LFS must be installed
for client outputs; recording acquires and releases only its own asset locks.
Generation failures leave the checkout untouched. Commit failures restore this
operation's files and index entries when they have not changed concurrently;
concurrent edits are preserved and any paths needing attention are reported.

This first implementation supports the local Docker MariaDB instance, with the
Toolbox connected as `root` to `ep4_data`. The game checkout must contain the
snapshot-capable `export-seed.sh` (`--capabilities` lists
`immutable-source-prefix` and `explicit-empty-snapshot`). Docker and Git for
Windows must be available. A mismatched database, unsupported remote connection,
missing content table or unreviewed seed table stops recording before publication.
These restrictions do not affect ordinary editor saves.

The recording tests use disposable databases and temporary Git repositories;
see `tests/ContentDatabaseVerification`, `tests/ContentGitVerification`,
`tests/ContentClientVerification`, `tests/ContentRecordingVerification` and
`tests/ContentRecordingUiVerification`.

### Manual recording

After a content-editing session, export the affected database tables into the
**game repository**, review the diff, then commit the seeds. In Git Bash from
`C:/Users/Me/Desktop/x64modernizationprototype`:

```bash
set -a
source x64-server/compose/.env
set +a
LC_DB_CONN=docker bash x64-server/db/export-seed.sh ep4_data/t_item
git diff -- x64-server/db/seeds
```

Supply multiple table names for a save that affects several tables. If the
complete set is uncertain, run the exporter without table arguments and review
**all** resulting changes: item deletion can affect many dependencies, and a
full export can also capture unrelated server-written drift. Stage only the
reviewed seed files, then make a normal Git commit. This is separate from
committing changes to the editor's own source repository.

Seeds are the tracked content source used to reconstruct the database.
Reapplying them replaces the corresponding live rows, so unexported editor
changes can be lost. Player, account and log data must not become content seeds.
See the game repository's `x64-server/db/README.md` for the full round trip and
the handling of tables containing both authored and live state.

Client LOD exports are a second output. Put accepted exports at the matching
path under `x64-client/assets/`, take the required Git LFS lock when replacing
an existing binary, and commit the asset through Git LFS before assembling the
runtime. Files written only to the editor's output folder or the merged game
runtime are not version controlled.
