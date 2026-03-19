# Step 9 Chapter 4 Minimum Recovery Paths

| Failure | User sees | Next step | Can other parts continue? | Manual intervention? | Fact-layer risk |
| --- | --- | --- | --- | --- | --- |
| API unavailable from Web | Error panel with unavailable wording | Retry later, check API service, use Desktop/Telegram only after API recovers | No shared write path, but local shell/adapters can still show status | yes | none |
| API unavailable from Desktop | `Service status: unavailable` + shell notification | Open workbench after API recovers, or start API directly | Shell remains alive; direct Web URL still exists | yes | none |
| API unavailable from Telegram | Short unavailable feedback | Retry later or continue in Web after API recovers | Yes, Web remains the main interface | yes | none |
| Pending action failure | Stable error response | Retry action from Web or Telegram | Yes | sometimes | none |
| AI derivation not generated | `not generated yet` wording + rerun CTA | Rerun derivation or ignore | Yes, formal record remains readable | no | none |
| AI derivation failed | Failure wording + rerun CTA | Rerun or continue reading formal record | Yes | no | none |
| Rule alert fetch failure | Error state on alert section | Retry later; keep using formal record page | Yes | no | none |
| Desktop shell degraded | Shell still only points to shared workbench | Open shared Web workbench directly | Yes | sometimes | none |
| Acceptance runner stage failure | Stage name + exit code | Fix that stage and rerun smoke | Yes, unrelated app areas may still be usable | yes | none |

## Principle

Minimum recovery in Phase 1 means:

- the user can tell what failed
- the user can tell whether formal data is still safe
- another shared surface usually remains available
- failure does not create a hidden write path or second fact source
