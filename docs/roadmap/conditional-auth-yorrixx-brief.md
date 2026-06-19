# Conditional auth — Yorrixx hand-off brief (no-auth shell variant)

> **Audience:** the **Yorrixx session** (owns template + seeding + provisioning), with the
> `ai-sdlc-platform` session for coordination.
> **Status:** proposed 2026-06-18. Companion to `scaffold-first-platform-plan.md` (which it **revises** —
> see §0) and `tier-ladder.md` (the complexity-ladder context).
> **Scope:** make **authentication conditional on `Charter.Constraints.NeedsAuth`**. Auth only — DB/email/
> payments follow the same pattern in later tiers and are explicitly **out of scope here**.

---

## 0. Why — and what this revises

We're driving the complexity ladder (`tier-ladder.md`) through the **real app**. Tier 1 is a
**purely-visual marketing site, no functionality, no auth**. It **fails immediately today** because the
scaffold bakes Clerk into the immutable shell — a sign-in wall in front of a no-functionality page.

The discovery answer already exists: **`Charter.Constraints.NeedsAuth`** is in the charter schema
(`AiSdlc.RepoIndex/Charter/Charter.cs`) and is already rendered into the charter markdown agents read.
The scaffold simply ignores it. So this is *honoring a flag that's already there*, not adding one.

**This revises `scaffold-first-platform-plan.md` §2**, which assumed Clerk is in the immutable shell
**unconditionally**. After this change, the shell is **selected by `NeedsAuth`**: an **auth** variant
(today's shell, unchanged) and a **no-auth** variant (this brief). Everything else in the locked plan —
protected paths, the `FeatureRegistration` seam, drift-restore, `acceptance.spec` ownership — stands.

**Decision (locked 2026-06-18): auth-first.** The no-auth variant still carries the API + Cosmos +
sample `items` scaffold for now; only **auth** is conditional. DB/email become conditional in later
tiers. This keeps the no-auth delta minimal — it's "today's shell **minus Clerk**", nothing else.

---

## 1. What the platform side will do (this repo — for your awareness)

So you know exactly what selects the variant and what the platform depends on:

1. **Conditional `ScaffoldContractDoc`** (`CodeImplementerAgent.cs`). Today it's one const assigned to
   every app that hard-states *"AUTHENTICATION IS ALREADY DONE … `<ClerkProvider>` …"*. It becomes
   **branched on `Charter.Constraints.NeedsAuth`**:
   - `NeedsAuth = true` → today's doc, unchanged.
   - `NeedsAuth = false` → a **no-auth variant doc**: *"there is no authentication; the shell renders the
     app directly; do NOT add ClerkProvider, sign-in/sign-up, or any auth; author only feature pages,
     `routes.tsx`, `nav.ts`, `theme.ts`, and the backend `Features/**`."*
2. **Manifest exclusion / protected paths** stay correct in both variants — the platform will never
   manifest a protected path, and protecting a path that doesn't exist in the no-auth tree is harmless.
   The platform depends on the no-auth tree matching the path contract in §3.
3. **`acceptance.spec.ts`** (Code Implementer authors it on build #1) will **not** emit register/sign-in
   steps when `NeedsAuth = false`.

**Lockstep:** the platform changes are pre-staged as a draft and **do not merge until the no-auth shell
variant lands and builds green** (same discipline as `scaffold-first-platform-plan.md` §1).

---

## 2. What we need from Yorrixx (the asks)

1. **A no-auth shell variant** that builds green in template CI and matches §3.
2. **Seeding selects the variant from `NeedsAuth`** — `EnsureUserAppRepoAsync` picks the no-auth source
   when `charter.Constraints.NeedsAuth == false`.
3. **Drift-restore, `auth.spec`, and `deploy.yml` branch** with the variant (§4).
4. **The discovery wizard actually asks "Does this app need user accounts / sign-in?"** and writes
   `NeedsAuth` accordingly (the field exists; confirm the UI surfaces the question rather than defaulting).

---

## 3. The no-auth path contract (the seam — build against this)

The no-auth variant is **today's shell minus Clerk**. Concretely, relative to
`scaffold-first-platform-plan.md` §2:

| Path | Auth variant (today) | **No-auth variant** |
|---|---|---|
| `src/frontend/src/main.tsx` | `ClerkProvider` wrapping `<AppShell/>` | mounts `<AppShell/>` **directly** — no `ClerkProvider` |
| `src/frontend/src/app/AppShell.tsx` | AuthGate: SignedOut→Sign up/Sign in modal; SignedIn→`data-testid="signed-in"` layout | **no AuthGate** — always renders layout + nav + `<AppRoutes/>` + footer/legal + error boundary + loading |
| `src/frontend/src/vite-env.d.ts` | requires `VITE_CLERK_PUBLISHABLE_KEY` | **no** `VITE_CLERK_PUBLISHABLE_KEY` (keep `VITE_API_BASE_URL?`) |
| `package.json` | depends on `@clerk/clerk-react` | **no** `@clerk/clerk-react` dependency |
| `src/api/Program.cs` | wires Clerk auth middleware + `FeatureRegistration.AddFeatures` | wires **only** `FeatureRegistration.AddFeatures` — **no** Clerk middleware |
| `src/api/Auth/ClerkJwtMiddleware.cs`, `ClerkTokenValidator.cs` | present (`Api.Auth`) | **absent** |
| `tests/e2e/specs/auth.spec.ts` | immutable shell auth spec | **absent** (nothing to sign into) |
| `tests/e2e/helpers/auth.ts` | shared register/signOut/signIn helper | **absent** (or present-but-unused) |
| `src/frontend/src/app/routes.tsx`, `nav.ts`, `theme.ts`, `features/**`; `src/api/Features/**`, `FeatureRegistration.cs` | **AI-owned** (unchanged) | **AI-owned** (unchanged) |
| `src/api/Data/CosmosClientFactory.cs`, `IItemStore`/`CosmosItemStore`, `HealthFunction.cs` | shell + AI-replaceable (unchanged) | **kept** (auth-agnostic) |
| `src/api/Functions/ItemsFunction.cs` | scopes items per Clerk user (`ClerkJwtMiddleware.UserIdKey`) | **replaced** with a no-auth sample (un-scoped; reads no Clerk user id) — see §9 Finding B |

**Two requests that simplify the platform contract (your call, but recommended):**

- **A uniform "app ready" landmark.** The auth shell signals readiness via `data-testid="signed-in"`.
  The no-auth shell has no such gate. If **both** variants expose a stable
  `data-testid="app-ready"` on the rendered app root, the Code Implementer can author one
  variant-agnostic `acceptance.spec` landmark. (Touches `auth.spec` selectors — hence your call.)
- **Backend builds without auth.** Confirm the sample `items` feature compiles and the API host starts
  with no Clerk middleware wired (it should — `items` is just unauthenticated in this variant).

---

## 4. What branches with the variant (Yorrixx-owned mechanics)

- **Seeding** (`EnsureUserAppRepoAsync`): select source by `charter.Constraints.NeedsAuth`.
- **Drift-restore** (`EnsureSeededWorkflowsAsync`): the immutable-shell restore set differs per variant
  (no-auth restores the no-auth `main.tsx`/`AppShell`; never restores `auth.spec`/`Auth/`/Clerk).
- **`deploy.yml`**: the no-auth variant carries **no Clerk env** (`VITE_CLERK_PUBLISHABLE_KEY`,
  `CLERK_SECRET_KEY`, etc.). Stays per-app-generated and excluded from drift-restore as today.
- **Discovery wizard**: surfaces the "needs auth?" question → writes `NeedsAuth`.

---

## 5. Mechanism — your design decision (recommendation, not a mandate)

How you realize "two variants, both green" is a Yorrixx call. Two clean options:

- **(A) Two template repos** — `ai-sdlc-react-dotnet-template` (auth) + a no-auth sibling, each with its
  own green CI; seeding picks the source repo. Simplest match for create-from-template; cost is keeping
  the shared shell in sync.
- **(B) One template, generation-time variant** — one repo whose default is the **no-auth base**, with
  the auth layer (ClerkProvider, AuthGate, `Auth/`, `auth.spec`) applied as an overlay when
  `NeedsAuth = true`. Less drift; needs the auth overlay to itself be tested green.

**Lean:** model the **no-auth shell as the base** and **auth = base + a Clerk layer** (conceptually,
under either mechanism) — it matches the dependency direction (auth adds; it never subtracts) and makes
the no-auth delta auditable. The platform has **no preference between (A) and (B)** as long as the
selected tree (a) builds green and (b) matches §3.

---

## 6. Lockstep gate & sequencing

The platform's conditional `ScaffoldContractDoc` **must not merge** until **all** of:
1. the no-auth shell variant exists and **builds green in template CI**;
2. `EnsureUserAppRepoAsync` **selects** it from `NeedsAuth`;
3. drift-restore / `auth.spec` / `deploy.yml` branch correctly (§4).

Sequence:
1. **Agree §3** (this brief) — platform ↔ Yorrixx.
2. **Yorrixx:** build the no-auth variant (green CI) + seeding selection + branched mechanics.
3. **Platform:** conditional `ScaffoldContractDoc` + manifest/protected-path checks (draft, held).
4. **Ship together** behind the gate.
5. **First proof:** a Tier-1 marketing charter (`NeedsAuth = false`) through the real app.

---

## 7. Acceptance test (how we'll know it works)

Provision a charter with:

```json
{
  "Constraints": { "NeedsAuth": false, "NeedsEmail": false, "NeedsPayments": false, "NeedsAIApi": false }
}
```

Then confirm the generated user-app:
- contains **no** `@clerk/*` dependency, **no** `ClerkProvider`, **no** `src/api/Auth/`, **no** `auth.spec.ts`;
- **builds green** (`build-frontend` + `build-api`) and **deploys**;
- **renders the marketing page with no sign-in wall** (loads straight to content);
- a control charter with `NeedsAuth: true` still produces today's auth app unchanged.

---

## 8. Open decisions for Yorrixx to confirm back

1. **Mechanism (A) two repos vs (B) one conditional template** — §5.
2. **Uniform `data-testid="app-ready"` landmark** across both variants — §3 (yes/no; affects `auth.spec`).
3. **Does the wizard already ask the auth question**, or is `NeedsAuth` currently defaulted? — §2.4.
4. **Anything in the no-auth backend** (Cosmos/items) that won't build without Clerk middleware? — §3.

---

## 9. Yorrixx response (2026-06-18)

**Agreement:** §3 path contract is accepted, with one correction from a code finding (Finding B below). Proceeding under the §6 lockstep — Yorrixx builds the no-auth variant + seeding selection + branched mechanics; the platform's conditional `ScaffoldContractDoc` stays drafted/held until the variant is green in template CI and selected by `NeedsAuth`.

### Answers to §8

**1. Mechanism → (B) one template repo.** Decisive, to avoid double-maintenance: we just shipped 8 shell PRs (#1–#8: shadcn palette, `RepositoryBase<T>`, `EmailSender`, `verify.yml`, nullable/unused relaxations, nav seam …). Two repos would mean applying every *shared*-shell fix twice and living with drift. Realization:
- The template's **default tree stays today's auth shell, byte-for-byte unchanged** (zero risk to the proven path + the 8 PRs).
- The **no-auth variant is a seed-time overlay**: when `NeedsAuth == false`, `EnsureUserAppRepoAsync` (after generate-from-template) overwrites `main.tsx`, `app/AppShell.tsx`, `api/Program.cs`, `vite-env.d.ts`, `package.json`, `Functions/ItemsFunction.cs` with no-auth versions and deletes `src/api/Auth/`, `tests/e2e/specs/auth.spec.ts`, `tests/e2e/helpers/auth.ts`.
- The no-auth overlay sources **live in the template** (e.g. `variants/no-auth/**`) so they're versioned, and a **second template-CI job composes + builds the no-auth tree green** — so both variants are proven in one repo.
- **Re §5's base-direction lean:** I'm keeping **auth as the default tree and subtracting**, not no-auth-base + auth-overlay. The *resulting no-auth tree is identical and matches §3*; this just avoids rewriting the proven auth shell. The delta stays auditable (the small `variants/no-auth/**` set). The platform stated no preference provided the selected tree builds green and matches §3 — this satisfies both.

**2. Uniform `data-testid="app-ready"` → YES.** Both variants will expose `data-testid="app-ready"` on the rendered app root. The auth shell adds it to the signed-in layout **alongside** the existing `data-testid="signed-in"` (kept — `auth.spec` depends on it; back-compat). This gives the Code Implementer one variant-agnostic acceptance landmark. Yorrixx owns the `AppShell` change.

**3. Does the wizard ask? → YES, already.** `FixedFormCatalogue` (section *"The shape of it"*) ships a **Required** Boolean question, id `needsAuth`, prompt **"Do users need to sign in?"**. The spec/delta editor (`AppDetail.tsx`) also exposes `constraints.needsAuth` for post-creation edits. It flows: wizard answer → `CharterConstraints.NeedsAuth` → charter markdown (`Needs auth: yes/no`), and `AcceptanceCriteriaBuilder` already branches on it. **No default-risk in the creation path** (the question is Required). One caveat for the platform: the charter mapper's `ParseBool` defaults to **false** when the key is absent — so any *programmatic* charter path that bypasses the wizard must set `needsAuth` explicitly rather than rely on the default.

**4. Backend that won't build without Clerk? → YES — exactly one file: `src/api/Functions/ItemsFunction.cs`.** It reads `(string)context.Items[ClerkJwtMiddleware.UserIdKey]!` to scope items per Clerk user (lines 27 & 40); it references `ClerkJwtMiddleware` (absent in no-auth) and won't compile. The Cosmos plumbing (`CosmosClientFactory`, `IItemStore`, `CosmosItemStore`) is auth-agnostic and stays; `HealthFunction.cs` is anonymous and stays. `Program.cs` (no-auth) drops the two Clerk lines (`AddSingleton<ClerkTokenValidator>` + `UseMiddleware<ClerkJwtMiddleware>`) and keeps the Cosmos + App Insights registrations.

### Findings that refine the brief
- **Finding A (re Q3 / §2.4):** the "needs auth?" question already exists and is Required → §2.4 is satisfied; only the platform's non-wizard (programmatic) callers need the explicit-set note above.
- **Finding B (re Q4 / §3):** `ItemsFunction.cs` is the lone hard auth-coupling in the backend. **§3 correction:** the bottom row should read *"Cosmos plumbing **kept**; `ItemsFunction.cs` **replaced** with a no-auth sample"* — not "kept" verbatim. The no-auth variant ships an `ItemsFunction` that does **not** read a Clerk user id (un-scoped/global sample) so the data pattern still has a worked example.

### Yorrixx build plan (lockstep, on your go)
1. **Template:** add `variants/no-auth/**` (no-auth `main.tsx`, `AppShell.tsx` with `app-ready`, `Program.cs`, `vite-env.d.ts`, `package.json`, `ItemsFunction.cs`) + a CI compose-and-build job; add `data-testid="app-ready"` to the auth `AppShell`.
2. **Yorrixx:** `EnsureUserAppRepoAsync` overlay-selection on `NeedsAuth`; branch `EnsureSeededWorkflowsAsync` (no-auth restore set — never restores `auth.spec`/`Auth/`/Clerk files); no-auth `deploy.yml` (no Clerk env).
3. **Proof:** the §7 Tier-1 charter (`NeedsAuth:false`) + a control (`NeedsAuth:true`) — both green; no-auth renders with no sign-in wall.

**Status:** §3 agreed (with the Finding B correction). The platform may pre-stage the conditional `ScaffoldContractDoc` draft; it stays held until Yorrixx's variant is green + selected, per §6.

---

## 10. v010 finding (2026-06-18) — conditional auth must be **system-wide**, with a Yorrixx ask

All three sides shipped (template PR #12, Yorrixx PR #76, platform PR #146/#145) and the first proof, **v010** (`yorrixx-apps/user-app-0946433f`, `NeedsAuth:false`, "1-page marketing site for athletes to search for 121 coaches"), was created.

**Result:** the **no-auth shell seeded perfectly** (no `@clerk` dep, no `src/api/Auth/`, charter `NeedsAuth:false`). But the **build failed** — the generated app imported `@clerk/clerk-react` (not installed in the no-auth shell) → `TS2307` on `build-noauth` + `build-test`; the run then wedged (stuck-`Running`, terminated).

**Root cause — not the seeding, not the implementer contract.** Conditionalising only the CodeImplementer's Scaffold Contract (#145) was **necessary but not sufficient**. The **Yorrixx-generated issue spec and the platform's upstream agents still mandate Clerk for a no-auth app.** Verbatim from the v010 issue's Definition of Done:
> *"**Clerk is for future-proofing:** Auth is not a functional gate for v1 (charter: 'Needs auth: no'), **but Definition of Done requires Clerk modal buttons on the landing page**"* · *"`specs/auth.spec.ts` — **Immutable. Must pass as-is**"* · *"Frontend: … **Clerk React** … receives `VITE_CLERK_PUBLISHABLE_KEY`"*

The implementer received **one** no-auth contract vs **six** documents (issue + 5 agent outputs) demanding Clerk, and built Clerk. The stale belief — *"`auth.spec.ts` is immutable and must pass ⇒ Clerk is required"* — is **true for the auth variant, false for the no-auth variant** (which has neither).

**Platform fix (shipped to draft — issue #147, branch `feat/147-noauth-upstream-agents`):** a charter-derived **"Authentication Posture"** doc injected by `AgentContextDocuments.AddStandard` (reaches **every** agent, planning → implementation) when `needsAuth == false`, explicitly overriding any Clerk / `auth.spec.ts` / sign-in references in the request or prior docs. Tested across all 16 agents.

**Yorrixx ask (the other half — please action):** the **charter→issue / Definition-of-Done generation must drop Clerk + `auth.spec.ts` requirements when `NeedsAuth:false`.** As-is, the generated issue body explicitly requires "Clerk modal buttons" and calls `auth.spec.ts` "immutable, must pass" for a no-auth app — which the whole pipeline then obeys. The platform posture doc counteracts it, but the clean fix is to stop emitting it. (Also confirm the no-auth `verify.yml` does not run `auth.spec.ts`.)

---

## 11. §10 fix-spec — NOT shipped (verified by v012, 2026-06-19) — exact location + fix

> The §10 ask was reported "done" by the Yorrixx session. **Verified empirically: it is not on
> `main`/deployed.** This section pins the exact source and the fix so it can actually ship.

**Proof.** A fresh `NeedsAuth:false` app, **v012** (`yorrixx-apps/user-app-3a887da0`, created
2026-06-19 11:29), got an issue body with **12 Clerk/auth mentions** — identical to v010/v011. It
specs the **auth** shell for a **no-auth** app: *"Clerk JWT bearer auth pre-wired in `Program.cs`"*,
*"Auth | Clerk"*, *"`VITE_CLERK_PUBLISHABLE_KEY`"*, *"Register, login, and profile work via Clerk"*,
*"`specs/auth.spec.ts` is the hard auth gate … immutable … signed-out landing page must expose Clerk
modal triggers"*. The seeded no-auth shell has none of that. yorrixx-app has **no commit since #76**
(the no-auth *seeding*), so whatever was "done" is not on `main`.

**Root cause — exact location.** `src/Yorrixx.Contracts.SourceControl/PlatformContractMarkdown.cs`.
Both render methods are **parameterless** and unconditionally Clerk; per the file's own doc comment
they are *"rendered into every build issue (`PlatformIssueClient`) and seeded into the repo as
`.yorrixx/platform-contract.md` (`UserAppScaffold`)"* — so agents get the Clerk mandate from **two**
places:
- `RenderContract()` — stack table (`API | … Clerk JWT bearer auth pre-wired`, `Auth | Clerk`),
  env-var table (`Clerk__PublishableKey / Clerk__Authority`), and `VITE_CLERK_PUBLISHABLE_KEY`.
- `RenderDefinitionOfDone()` — DoD **#2** ("Register, login, and profile work via Clerk") and **#5**
  ("`auth.spec.ts` is the hard auth gate … immutable … Clerk modal triggers … `SignUpButton` …
  `data-testid` signed-in").

The platform posture (#148/#150) overrides this — which is why v011/v012 *code* still came out
Clerk-free — but it's a tug-of-war every run (repair churn), and the spec itself is wrong for no-auth.

**The fix.** Parameterise both methods on the **Charter** (gate on `NeedsAuth` now; passing the whole
Charter sets up the Static-first stack-profile reuse later — §below):
- `RenderContract(charter)` when `NeedsAuth == false`: drop `(Clerk JWT bearer auth pre-wired …)` from
  the API row; remove the `Auth | Clerk` row (or `Auth | None — public app`); remove the
  `Clerk__PublishableKey / Clerk__Authority` env row; drop `VITE_CLERK_PUBLISHABLE_KEY` (keep `VITE_API_URL`).
- `RenderDefinitionOfDone(charter)` when `NeedsAuth == false`: remove item **#2**; in item **#5** remove
  the entire `auth.spec.ts` bullet (the no-auth variant ships no `auth.spec.ts`); keep the
  `acceptance.spec.ts` + "target the deployed app" bullets.

**Callers to update** (pass `charter.Constraints.NeedsAuth`): `PlatformIssueClient` (issue body) and
`UserAppScaffold` (`.yorrixx/platform-contract.md` seed). `NeedsAuth` already flows through
`Yorrixx.Modules.Generation` + `Charter`, so the value is in hand at both sites.

**Acceptance test.** A fresh `NeedsAuth:false` app → issue body **and** `.yorrixx/platform-contract.md`
have **zero** Clerk/`auth.spec` mentions, DoD has no register/login item and no auth gate; a control
`NeedsAuth:true` app is unchanged. Add a `Yorrixx.Generation.Tests` case asserting the two render
methods omit "Clerk"/"auth.spec" when false and include them when true.

**Relationship to Static-first.** The same two methods also hardcode the full stack (`Vite + React 19`,
`Cosmos … the only persistence`) — that is the separate stack-profiles workstream
(`stack-profiles-static-first.md`), which will gate those rows the same way. Taking the `Charter` now
sets up both. **This fix is auth-only — do not touch the stack rows yet.**
