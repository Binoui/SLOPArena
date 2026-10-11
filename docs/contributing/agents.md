# Working with coding assistants

Read root [AGENTS.md](../../AGENTS.md) first. Project contracts live in public
`docs/`; maintained workflows live in [skills/](../../skills/README.md).
BMAD, Orca, OMP and T3 Code are optional tools, not contributor prerequisites.

## Discovery

| Setup | Project entry point | Workflow source |
|---|---|---|
| Codex, including Codex in T3 Code | Root `AGENTS.md` | Generated `.agents/skills/` adapters |
| OMP, including OMP in Orca | `.omp/AGENTS.md` → root `AGENTS.md` | Generated `.omp/skills/` adapters |
| Claude Code | Root `CLAUDE.md` → root `AGENTS.md` | Read `skills/<name>/SKILL.md` directly or configure documented skill discovery |
| Other file-capable assistants | Ask it to read root `AGENTS.md` | Read the relevant `skills/<name>/SKILL.md` directly |

Adapters refer back to this checkout; they are not standalone bundles. To install
skills elsewhere, copy complete canonical directories from `skills/`, keeping their
references and `agents/` policy files. Development skills also need this checkout's
docs and source. SlopBot's creator bundle is described in [its guide](../../skills/slopbot/README.md).

Start a fresh session or use the assistant's documented reload after changing
discovery. Confirm the resolved source before relying on a skill. A loader accepting
a skill does not prove that Unity, Blender, a paid service or deployment is available.
Use a concrete manual handoff when a required capability is missing.

OMP started in an enclosing workspace may need `skills.customDirectories` pointed
at this checkout's `.omp/skills`. Keep retired/global alias exclusions in
[.omp/config.yml](../../.omp/config.yml). Avoid rewriting user-level installations
to repair repository routing. Ignored local advisory skills are not dependencies
of the public project workflows.

## Checkout and task scope

Resolve an explicitly supplied checkout first. Validate `src/Shared/SlopArena.Shared.csproj`
and `tests/Shared.Tests/Shared.Tests.csproj` before running game commands. Otherwise
inspect the current Git root; an enclosing workspace is not necessarily the game.
Canonicalize paths with the host's path tools or Python `Path.resolve()`. Set command
`cwd` to the selected game root. An invalid explicit worktree is a blocker, never
permission to substitute another checkout.

Keep optional planning artifacts at their supplied location. Handoffs name the
actual checkout, spec/ticket if any, approved mutations and observable acceptance.
For live Unity work, include the explicit Editor project and coordination protocol.
A worktree or repository symlink does not isolate an Editor or other linked repositories.

Approval covers the agreed task through implementation and verification. A loaded
skill is guidance, not new permission. Preserve unrelated edits; commit, push,
installation and publication keep the boundaries in `AGENTS.md`. Delegate only when
the user or an applicable workflow asks for delegation. Human gamefeel acceptance
remains separate from automated results.

## Unity and offline checks

For Shared/Server checks that do not need a plugin refresh, pass
`-p:SkipUnityPluginCopy=true` to `dotnet build`/`dotnet test`. Focused tests do not
need a separate Shared build: their project reference already builds the tested code.

Live Editor automation requires Bun, the installed Unity CLI and the embedded
Pipeline package. Use the shipped `scripts/unity-editor-gateway.ts`, your own caller
identity, and an explicit approved project. The [Unity CLI guide](unity-cli.md) and
[coordination protocol](unity-editor-coordination.md) describe Orca, T3/Codex and
launcher-configured identities. Routine isolated release builds use their release
wrapper instead. Tool installation still requires user approval.

## Maintaining workflows

Edit `skills/` and public docs. Then run:

```bash
python3 tools/sync_agent_skills.py --write
python3 tools/sync_agent_skills.py --check
python3 tools/check_docs.py
```

The generator maintains discovery metadata, explicit invocation policy and the
`.gitignore` allowlist without copying workflow bodies. It refuses to overwrite an
unmanaged adapter. Documentation checks reject stale adapters, broken links,
author-specific paths and known retired workflows in current guidance. Historical
plans and receipts remain records.
