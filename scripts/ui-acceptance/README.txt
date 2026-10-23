TraceFold Stage 5CC UI acceptance toolkit
=========================================

The package root carries Run-Stage5CC-Acceptance.cmd, the operator entry. It starts the system
Windows PowerShell 5.1 at %SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe with
scripts/Run-Stage5CC-Acceptance.ps1, which resolves the toolkit from its own location, finds the
sibling or explicit product, picks a fresh results leaf and starts the public entry. The console
always pauses and the entry's exit code is preserved. The complete mode (All plus -ThemeMatrix
Both) is refused with exit code 3 because this entry has no ThemeMatrix support; a single scenario
is started with -Scenario <name>.

The public entry is scripts/run-stage5cc-ui-acceptance.ps1. It owns the whole outer walk:
request precheck -> preparation -> one Runner invocation per scenario -> report and owned cleanup.
Nothing here starts the product by itself, touches the registry, or installs anything globally.

Two invocation shapes
---------------------

1. Source tree (development)

   pwsh -NoProfile -File scripts/run-stage5cc-ui-acceptance.ps1 `
       -ProductDirectory <published product> `
       -WinAppCliArchivePath <pinned driver zip> `
       [-OutputDirectory <fresh leaf>] [-Scenario StartupLayout]

   The driver archive is verified against apps/media-desktop/tools/winapp/win-x64/manifest.json
   and unpacked into this run's owned directory. The support CLI is published from its project
   with dotnet, so the .NET SDK is required. The default output lives under
   .superpowers/stage5cc-uia-gate/run-<id>.

2. Moved toolkit (no source tree, no Git, no SDK)

   pwsh -NoProfile -File <toolkit>/scripts/run-stage5cc-ui-acceptance.ps1 `
       -ToolkitDirectory <toolkit> -ProductDirectory <published product> `
       [-OutputDirectory <fresh leaf>] [-Scenario StartupLayout]

   The package already carries everything it needs. Nothing is compiled, downloaded or unpacked;
   the default output is <toolkit>/results/run-<id>.

Package layout and data contract
--------------------------------

toolkit.json at the package root is the data contract:

  schema           must be "tracefold-ui-acceptance-toolkit"
  schemaVersion    must be 1
  layout           supportRoot, driverRoot, contractPath, resultsRoot (package-relative)
  support          executable (must be TraceFold.Media.UiAcceptance.Support.exe), bytes, sha256
  driver           memberName (must be winapp.exe), bytes, sha256
  members[]        path, bytes, sha256, source for every file this entry consumes

The member table must cover the whole consumed closure, not only a few named files: the package-root
launcher, its scripts/ helper, the entry script, every module in scripts/ui-acceptance, every file
under report-template, the complete support payload, the complete driver payload, the contract, and
the contract's fixtureAssets.
report-template/index.html is required by name (toolkit.template_missing): an empty template
directory is not enough. A file that exists in one of those trees but has no row is refused with
toolkit.member_unlisted, because otherwise it would be imported or copied without ever being
hashed. The two executable names are fixed: the entry, the Runner, the scenario arguments and the
identity measurements all say TraceFold.Media.UiAcceptance.Support.exe and winapp.exe, so a package
that declares another name is refused before anything starts (toolkit.support_executable_unsupported
/ toolkit.driver_member_unsupported).

Two registry entries (Conflict, Library) import scenarios/TraceFold.UiAcceptance.Aggregate.psm1 and
fall back to the copy one directory up. The package check reads the same two candidates in the same
order and requires the file that would be imported to be a member; a request whose engine is absent
is refused with toolkit.scenario_dependency_missing before the support tool runs. Every entry in
the scenario registry is a required source file, so a package can never be assembled from whatever
happens to exist in the checkout.

The MainChain media-tools declaration (apps/media-desktop/tools/ffmpeg/win-x64/manifest.json) is a
required member: the producer copies the declaration it already verified against the FFmpeg
distribution, the structure smoke pins its sizes and digests to the product's ffmpeg.exe and
ffprobe.exe, and a declaration that is missing, unlisted or changed is refused before promotion.

The entry verifies the manifest before it starts anything: a rooted, traversing, non-canonical,
duplicate or reparse-pointed member, a missing member, a size or hash mismatch, an unlisted file
in a consumed tree, a scenario entry that is not a member, and a declared contract fixture that is
absent or changed all stop the invocation with exit code 2 and a toolkit.<reason> message.

The product and the toolkit may not contain one another, and a junction on the product path or on
the output path is refused (toolkit.product_reparse / toolkit.output_reparse), because a fresh
leaf name under an existing junction does not describe where the run writes. resultsRoot is the
directory the default output is created in; if it already exists it must not be a junction
(toolkit.results_reparse).

Identity domains stay separate
------------------------------

The driver archive digest (source mode only), the extracted/packaged executable digest and the
PE file version are different facts. In package mode there is no archive, so the archive fields
stay null / not-packaged and the executable digest never stands in for them. Without Git, the
script repository commit stays unknown instead of being guessed.

Where this package comes from
-----------------------------

scripts/publish-media-desktop.ps1 -StageCheckpoint 5C-C produces two artifacts from the same
tested source: the product (TraceFold-MediaDesktop-Stage5C-C-win-x64) and this toolkit
(TraceFold-MediaDesktop-Stage5C-C-UiAcceptance-win-x64), each with a same-named ZIP. Both are
built in staging, both ZIPs are created there, the structural smoke runs there, and only then
are the four final targets replaced together; a failed promotion restores the previous pair.

toolkit.json is generated from the bytes that were staged, never copied from an example: the
member table is walked out of the staged trees, and the support and driver payloads are listed
file by file. The driver's archive identity (file name, size and SHA-256 of winappcli-x64.zip)
is kept separate from the extracted winapp.exe digest, and the archive is only unpacked after
that identity and every member name have been checked. Debug symbols are not part of a
distributed payload; the dropped names are recorded under driver.excludedMembers.

scripts/smoke-stage5cc-ui-acceptance-toolkit.ps1 verifies the structure - manifest, member
rows, complete support and driver trees, contract fixtures, version stamp and the product
boundary - without starting the product, the support CLI or the driver. It also asks this entry
to verify itself with -ValidateOnly. Only -RunStartupLayout delegates to a real scenario run.
scripts/smoke-media-desktop-package.ps1 keeps the earlier business probes for the product. The
producer runs that smoke as the promotion gate: the complete 5C-C path (package identity, native
window and the four probe scenarios) must pass before any of the four final targets is touched.
A real publish passes no launcher and the smoke starts the packaged product; tests pass
-ProductSmokeLauncher so the same four requests and exit-code rules run against a controlled
process, and the recorded ProductLaunchProbe then stays NotRun. -IdentityOnly is a separate
read-only presence/version check and is never a promotion qualification.

Boundaries
----------

- 'All' still refuses a registry entry that does not exist; no placeholder scenarios are added.
- The toolkit never falls back to a source tree, an old cache or a ZIP that is not in the package.
- Output is first-writers-only: an existing results leaf is refused, never overwritten.
- This toolkit does not publish the product or produce a release archive.
