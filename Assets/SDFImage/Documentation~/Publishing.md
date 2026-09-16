# Publishing SDF Image

Repository: <https://github.com/phucnguyen752/sdf-image>.

`main` contains the Unity project and the library at `Assets/SDFImage`. `upm` is generated from that directory with `git subtree split`, so `package.json` is at the branch root. Release tags point to the package commit, not the Unity project commit. Edit the library on `main`; regenerate `upm` for each release.

## Prepare and verify

1. Update `Assets/SDFImage/package.json`, `CHANGELOG.md`, the README installation tag, and `VALIDATION.md` to the release version. Keep all existing `.meta` GUIDs.
2. Run `SDFUI.Tests` in isolated Built-in and URP projects, including a local UPM installation with `com.sdfimage.ugui` in `testables`. Verify the demo, source attachment/reimport, and player script compilation. Record actual results and remaining limitations; a script compilation does not establish a complete player build.
3. Commit the reviewed package and project changes on `main`. Keep `Library`, `Temp`, `Logs`, `obj`, and `Build` outputs out of the release, except deliberately copied evidence inside the package documentation. Start the split from a clean, committed checkout.

## Publish 0.6.0

Run these commands from a clean committed checkout in PowerShell after verification. When the working project contains unrelated edits, commit only the reviewed release files and create a detached worktree at that commit for the split. For later releases, replace every `0.6.0` below with the new version.

```powershell
git fetch origin
$packageCommit = git subtree split --prefix=Assets/SDFImage main
git show "${packageCommit}:package.json"
git ls-tree --name-only $packageCommit
```

Confirm the package version and root contents before continuing. When `origin/upm` already exists, verify that it is an ancestor of the split commit with `git merge-base --is-ancestor origin/upm $packageCommit`; stop and investigate a nonzero exit code. `upm` must not be the current checkout. The local branch below is a generated pointer; remote updates remain ordinary, non-force pushes.

```powershell
git branch -f upm $packageCommit
git tag -a 0.6.0 $packageCommit -m "SDF Image 0.6.0"
git push --atomic origin main upm refs/tags/0.6.0
git ls-remote origin refs/heads/main refs/heads/upm refs/tags/0.6.0 'refs/tags/0.6.0^{}'
```

The remote `upm` hash and peeled tag hash (`0.6.0^{}`) must equal `$packageCommit`. Never move an existing release tag; use a new version for changes after publication.

Finally, install `https://github.com/phucnguyen752/sdf-image.git#0.6.0` in a clean Unity 6 project, confirm package identity and sample import, and create the GitHub release from the annotated tag using the verified release notes. Attach a package archive built from that same commit and verify its uploaded digest.
