# SmartInspectConsole deploy

Short launchers:

| Script | Target | FTP folder | Archive | Server copy |
|--------|--------|------------|---------|-------------|
| `c.ps1` | SureCourt | `si-deploy/si-c` | `si-c.zip` | `copy-c.cmd` |
| `g.ps1` | CC3 prod | `si-deploy/si-g` | `si-g.zip` | `copy-g.cmd` |

## First-time setup

1. Copy secrets templates (already present as `deploy.c.json` / `deploy.g.json`) and set the FTP password:
   - `deploy\secrets\deploy.c.json`
   - `deploy\secrets\deploy.g.json`
2. Password must not contain `REPLACE`.

## Build + upload

```powershell
cd C:\project\Utility\SmartInspectConsole
.\deploy\c.ps1      # SureCourt
.\deploy\g.ps1      # CC3 prod
```

Build only (no FTP):

```powershell
.\deploy\c.ps1 -T Build
.\deploy\g.ps1 -T Build
```

What it does:

1. `dotnet publish` self-contained `win-x64` → `publish\`
2. Zip → `deploy\artifacts\si-c.zip` or `si-g.zip`
3. FTP upload zip + matching `copy-*.cmd`

## On the server

From the FTP package folder:

```bat
copy-c.cmd
```

or

```bat
copy-g.cmd
```

Installs to `C:\Tools\SmartInspectConsole\current\` (kills running app first).
