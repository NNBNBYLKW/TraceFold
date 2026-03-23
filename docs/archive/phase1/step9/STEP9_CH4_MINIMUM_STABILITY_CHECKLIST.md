# Step 9 Chapter 4 Minimum Stability Checklist

## Startup Stability

- Is the API health path explicit and stable?
- Is the Web startup dependency on the shared API clear?
- Is the Desktop startup path explicit through config and shell bootstrap?
- Is the Telegram startup path explicit through config and adapter bootstrap?
- Do script entry points state what they are and what they are not?

## Health / Status Visibility

- Can the API report a minimal health result?
- Can Web distinguish loading / empty / unavailable / invalid response?
- Can Desktop show minimal service status and current mode?
- Can Telegram report service status without pretending more capability?
- Can the acceptance runner show which stage failed?

## Error Observability

- Are invalid configs rejected at config load time?
- Do shared API failures return stable error semantics?
- Do Web failures avoid browser-opaque wording where possible?
- Do AI derivation states distinguish not generated vs failed?
- Do rule alert sections distinguish empty result vs fetch failure?

## Minimum Recovery Path

- If Telegram fails, can the user continue in Web?
- If Desktop shell is degraded, can the user still use the Web workbench directly?
- If AI derivation fails, can the formal record still be read?
- If pending action fails, can the user retry without polluting facts?
- If acceptance smoke fails, is the failed stage visible?

## No Silent Failure / No Fake Success

- Does any surface say `ready` when the shared service is actually unknown?
- Does any surface say `recorded` without formal API success?
- Does any screen collapse unavailable / empty / not generated into one state?
- Does any runner exit without saying which stage failed?

## Config / Script Clarity

- Are Desktop and Telegram env examples sufficient for minimum startup?
- Are repo-style ensure scripts described as ensure scripts, not a migration platform?
- Are acceptance scripts described as smoke orchestration, not a new test framework?

## Formal Fact Protection

- Do failure paths avoid writing to formal fact tables?
- Do pending action failures avoid false completion?
- Do AI/rule failures leave formal record readability intact?
- Is there still no second fact source or fake write success path?
