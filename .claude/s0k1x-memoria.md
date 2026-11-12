# Napkin Runbook — AUREVIX OS

## Curation Rules
- Re-prioritize on every read.
- Keep recurring, high-value notes only.
- Max 10 items per category.
- Each item includes date + "Do instead".

## Execution & Validation (Highest Priority)
1. **[2026-11-11] WPF projects (ControlCenter, Installer) do not build on Linux**
   Do instead: push to `claude/hola-q5pzma` and read the `CI` run (Windows job); only `tests/Aurevix.Tests` and `src/Aurevix.Service` build locally.
2. **[2026-11-11] UI is only visible through the `Capturas` workflow**
   Do instead: run `screenshots.yml` on the branch, then `git fetch origin ci-capturas` and read `capturas/*.png`. Desktop captures (`escritorio/`) are useless: the runner sits on OOBE.
3. **[2026-11-11] The installer ISO engine has a real-Windows self-test in CI**
   Do instead: after touching `ImapiIsoWriter`/`MediaEngine.BuildIsoAsync`, check the CI step "Autoprueba del motor de ISOs" before releasing.
4. **[2026-11-11] Release = `release.yml` via workflow_dispatch with `version` input on `main`**
   Do instead: update `RELEASE_NOTES.md`, merge PR, dispatch; tag pushes are blocked.

5. **[2026-11-11] AUREVIX Shell = native C++ TAP (`src/Aurevix.Shell`) loaded via InitializeXamlDiagnosticsEx**
   Do instead: build with `src/Aurevix.Shell/build.ps1 -Out out/shell` before `dotnet build/publish` of ControlCenter (csproj copies it if present); verify with the `shell` job in `screenshots.yml` (`ci-capturas/shell/`).

## Shell & Command Reliability
1. **[2026-11-11] Supabase and GitHub Pages hosts are blocked from this container**
   Do instead: read the `releases` table with the Supabase MCP `execute_sql` (project `zttqiphwurnferzlubzy`); never curl them.
2. **[2026-11-11] GitHub MCP only accepts the old repo name**
   Do instead: call `mcp__github__*` with repo `sistema-OS-aurevix-OS` (it redirects to `aurevix-OS`).
3. **[2026-11-11] Force push is denied**
   Do instead: normal push and merge commits only.

## Domain Behavior Guardrails
1. **[2026-11-11] Release downloads need the GitHub repo to be PUBLIC**
   Do instead: if installer/update downloads return 404, check repo visibility first (repo is now `s0k1x/aurevix-OS`, was `sistema-OS-aurevix-OS`).
2. **[2026-11-11] Never redistribute Windows or host a prebuilt ISO**
   Do instead: installer downloads the official ISO from Microsoft (Fido) and adds AUREVIX files on the user's PC.
3. **[2026-11-11] No patching of protected Windows files (uxtheme, msstyles, shell injection by default)**
   Do instead: style through registry (HKCU), `.theme` files and official APIs; third-party shell tools only as opt-in via winget.
4. **[2026-11-11] Security-reducing actions never go through the Core Service silently**
   Do instead: keep UAC for Defender exclusions; validate every service input in `CoreRequestHandler`.

## User Directives
1. **[2026-11-11] Reply in Spanish, plain words; user is not a developer**
   Do instead: say what to click/download; no jargon without a gloss.
2. **[2026-11-11] No "WinterOS" or other distro names anywhere in the product**
   Do instead: AUREVIX-owned names; the catalog test enforces it.
3. **[2026-11-11] Logo: lime "A", exact original shape**
   Do instead: reuse `Assets/aurevix.ico` / `web/logo.png`; never redraw it.
4. **[2026-11-11] Kit s0k1x-skill is mandatory (design skills optional)**
   Do instead: investigar → parche/construir → verificar; keep this runbook curated.
5. **[2026-11-12] El usuario quiere reparto por modelos y menos tokens, siempre**
   Do instead: usar la skill `enrutador` y los subagentes `aurevix-explorador` (Haiku), `aurevix-constructor`/`aurevix-revisor` (Sonnet), `aurevix-arquitecto` (Opus); esperas de CI siempre en segundo plano.
6. **[2026-11-12] Iconos: el usuario quiere los de Windows; cursores también los de Windows**
   Do instead: no aplicar iconos ni cursores AUREVIX; el estilo es barra flotante con cristal, logo en Inicio, Inicio sin «Recomendado», Configuración y Explorador con cristal.

