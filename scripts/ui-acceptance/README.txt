TraceFold Stage 5CC UI acceptance toolkit
=========================================

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

The member table must cover the whole consumed closure, not only a few named files: the entry
script, every module in scripts/ui-acceptance, every file under report-template, the complete
support payload, the complete driver payload, the contract, and the contract's fixtureAssets.
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
is refused with toolkit.scenario_dependency_missing before the support tool runs. The MainChain
media-tools declaration is also a member when it exists; an unlisted or changed declaration answers
Blocked with pinned_media_tools_declaration_unverified instead of being read unverified.

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

Boundaries
----------

- 'All' still refuses a registry entry that does not exist; no placeholder scenarios are added.
- The toolkit never falls back to a source tree, an old cache or a ZIP that is not in the package.
- Output is first-writers-only: an existing results leaf is refused, never overwritten.
- This toolkit does not publish the product or produce a release archive.
