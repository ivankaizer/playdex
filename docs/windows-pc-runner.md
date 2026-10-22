# Windows builds on the PC

The Windows workflows ([`build-windows.yaml`](../.github/workflows/build-windows.yaml) and
[`release-windows.yaml`](../.github/workflows/release-windows.yaml)) run either on a self-hosted runner on the
Windows PC (label `windows-pc`) or on GitHub's `windows-latest`. This repository is private on GitHub Free, so
GitHub-hosted runners get 2000 minutes a month, and Windows minutes count double. The PC goes first.

## Choosing where a build runs

- Pull requests, pushes to main and release tags use `auto`: the PC when its runner is online, GitHub otherwise.
  [`windows-runner.yaml`](../.github/workflows/windows-runner.yaml) checks on macserv before the build, and the
  build job's name ends with where it ran, `(pc)` or `(github)`.
- To choose, run the workflow by hand with `runner` set to `auto`, `pc` or `github`: Actions → Build Windows apps →
  Run workflow, or

  ```sh
  gh workflow run build-windows.yaml --ref <branch> -f runner=pc
  gh workflow run release-windows.yaml --ref v<version> -f runner=github   # on the tag: the tag is the version
  ```

- `pc` waits for the PC if it's off, for up to a day. If the PC sleeps in the middle of a build, the job fails:
  use **Re-run all jobs**, because **Re-run failed jobs** keeps the earlier pick.
- Stopping the runner service on the PC sends `auto` builds to GitHub.

## Setting up the PC

Once, in an elevated PowerShell.

1. The tools, skipping those already installed: PowerShell 7 (the workflows' default shell), Git, and the Build
   Tools with C++, whose MSVC and Windows SDK Native AOT and the MSIX need. The workflows install .NET and Bun.

   ```powershell
   winget install -e --id Microsoft.PowerShell --source winget --scope machine
   winget install -e --id Git.Git
   winget install -e --id Microsoft.VisualStudio.2022.BuildTools --override "--quiet --wait --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
   ```

   The runner service only sees machine-wide installs: PowerShell 7 from the Microsoft Store doesn't count, and
   without `C:\Program Files\PowerShell\7\pwsh.exe` steps fall back to Windows PowerShell, whose execution policy
   blocks them ("running scripts is disabled on this system").

2. The runner, as a service. It runs as NETWORK SERVICE, so jobs don't touch your profile. The token is on
   Settings → Actions → Runners → New self-hosted runner (Windows) and is valid for an hour.

   ```powershell
   mkdir C:\actions-runner; cd C:\actions-runner
   Invoke-WebRequest https://github.com/actions/runner/releases/download/v2.338.1/actions-runner-win-x64-2.338.1.zip -OutFile runner.zip
   Expand-Archive runner.zip .; Remove-Item runner.zip
   .\config.cmd --unattended --url https://github.com/ivankaizer/playdex --token <token> --name windows-pc --labels windows-pc --runasservice
   ```

   After installing tools later, reboot the PC: a service only sees a changed PATH after a reboot.

3. The token `auto` uses to see the runner: a [fine-grained personal access
   token](https://github.com/settings/personal-access-tokens/new) for the playdex repository only, with Repository
   permissions → Administration: Read-only. Save it as the `RUNNERS_TOKEN` secret:

   ```sh
   gh secret set RUNNERS_TOKEN --repo ivankaizer/playdex
   ```

   Without it, or once it expires, `auto` builds on GitHub and the run shows a warning.

Never attach the PC's runner to a public repository: a pull request from a fork would run its code on the PC. A
release on the PC gets the signing secrets (`LAUNCHER_SIGNING_PFX`) like the GitHub runner does.
