# NuGet releases

The four libraries ship together as `UTDotNet.Core`, `UTDotNet.Diagnostics`, `UTDotNet.UbuntuTouch` and `UTDotNet.Avalonia`. The Click demo has its own packaging and is not a NuGet package. `Directory.Build.props` defines the library release version; `src/Directory.Build.props` defines shared package metadata. The current release is `0.1.6-preview.1`; the first release was `0.1.2-preview.1`.

## Build and verify

With .NET 10, Python 3 and network access to nuget.org:

```sh
bash scripts/pack-nuget.sh
```

The script restores locked dependencies, runs the full test suite, creates four `.nupkg` and four portable-symbol `.snupkg` archives under `artifacts/nuget`, checks package metadata/dependencies, and compiles/runs a consumer outside the repository using a fresh package cache. Only the local feed can supply `UTDotNet.*` during that check. Start with an empty output directory when changing release versions; verification deliberately rejects leftover archives.

Commit the exact release source before producing distribution artifacts so SourceLink and the package repository commit identify the published code. .NET 10 supplies SourceLink support. SDK-style project references become same-version package dependencies. Each package includes its README, MIT license and XML documentation.

The tracked lock files describe a portable restore. A RID-specific Click build can regenerate them; restore the solution without a runtime identifier and inspect the diff before preparing a NuGet release.

## Trusted Publishing

The workflow `.github/workflows/publish-nuget.yml` validates builds on the main/release branch and pull requests. Only a tag named `nuget-v<Version>` can run the publishing job. Its tag must exactly match the package version in the source. Manual workflow runs validate artifacts without publishing.

Configure a policy on [nuget.org Trusted Publishing](https://www.nuget.org/account/trustedpublishing) for:

| Setting | Value |
| --- | --- |
| Owner / login username | `michelangelolattanzio` |
| Provider | GitHub Actions |
| Repository owner | `MiLattanzio` |
| Repository | `UTDotNet-Bridge` |
| Workflow file | `publish-nuget.yml` |
| Environment | `nuget` |
| Packages | `UTDotNet.*` |
| Scope | Push new packages and new versions |

The publish job has OIDC permission and uses the `nuget` GitHub environment. It downloads only the artifacts from the successful build job, requests a short-lived credential with `NuGet/login`, and pushes in dependency order. It does not require a stored API key. Actions are pinned to commit SHAs. GitHub environment protection rules can restrict who may publish release tags.

After the policy is active, create and push the release tag at the tested source commit:

```sh
git tag nuget-v0.1.6-preview.1
git push origin nuget-v0.1.6-preview.1
```

Wait for the workflow and NuGet validation/indexing, then verify that all four versions are publicly downloadable. NuGet versions are immutable. For a partial upload, rerun the failed job; `--skip-duplicate` allows already-published packages to remain in place. For source changes, use a new package version and tag. Do not move a published release tag.

Official reference: [Trusted Publishing on nuget.org](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).
