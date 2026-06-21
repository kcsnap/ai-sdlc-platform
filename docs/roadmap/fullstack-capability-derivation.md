# FullStack capability derivation — architecture design before build

> **Status:** proposed 2026-06-21. Generalises the conditional-auth (`NeedsAuth`) and Static-vs-FullStack
> stack-profile work into a full **capability profile** resolved *before build*. A FullStack app is not
> monolithic — it is a composition of capability slots (API, database, auth, payments, email, AI). Some
> slots are decided by explicit wizard answers; the rest the agents derive from the brief.
> **Decisions (locked 2026-06-21):** ambiguity bias = **Balanced** (for agent-decided axes); agent
> authority = **explicit-only** for wizard-gated capabilities (flag, never auto-add); conflicts =
> **honor explicit + flag** at the Product Owner gate; database = **agent-derived, no new wizard question**.

---

## 0. Why

The scaffold-first FullStack template ships **everything pre-wired** — Clerk, Cosmos, SendGrid — whether
the app needs it or not. Conditional-auth (`NeedsAuth`) proved the first slice of the fix: seed a shell
without a capability when it isn't needed, and tell every agent so via a posture doc. This generalises
that to the whole stack: **derive the minimal-yet-sufficient architecture from need**, the same way the
imagery-plan step decides photography per brand.

This is an **architecture-design step that runs before build** and resolves a definitive **Capability
Profile**. It is the FullStack analogue of: the imagery judgment (per-image), the Static stack profile
(per-app shell), and the auth posture (per-capability).

## 1. Current state (grounding)

`CharterConstraints` (`src/AiSdlc.RepoIndex/Charter/Charter.cs`) carries the **explicit wizard flags**:

- `NeedsAuth` → Clerk
- `NeedsPayments` → Stripe
- `NeedsEmail` → SendGrid
- `NeedsAIApi` → Anthropic API

plus rich derivation signal: `DataSensitivity`, `ExpectedScale`, `Features[]` (with `Priority`),
`Integrations[]`, free-text `AdditionalContext`.

**The gap:** there is **no `NeedsDatabase` flag**. The FullStack scaffold always ships Cosmos. So
"api-only vs api+db" is unasked today — the clearest candidate for agent derivation.

## 2. Capability taxonomy

Two classes, with different decision rules:

**Class A — wizard-gated (explicit-only; agent may flag, never auto-add):**

| Capability | Charter flag | Provider |
|---|---|---|
| Auth | `NeedsAuth` | Clerk |
| Payments | `NeedsPayments` | Stripe |
| Email | `NeedsEmail` | SendGrid |
| AI API | `NeedsAIApi` | Anthropic |

**Class B — agent-derived (no wizard question; Balanced bias):**

| Axis | Resolves to | Derived from |
|---|---|---|
| Persistence | None (api-only) \| Database (Cosmos) | do the features imply storing/retrieving data? |
| API shape | (FullStack implies an API; the real fork is api-only vs api+db) | as above |

**Extensible (out of scope now — note, don't build):** file/blob storage, background/scheduled jobs,
search, real-time/websockets, SMS. Add as Class-B axes later when a brief demands one.

## 3. Resolution pipeline (the pre-build architecture step)

1. **Read explicit flags** (Class A) — authoritative.
2. **Derive Class-B axes** from brief/features/`AdditionalContext`, **Balanced bias** (include when
   plausibly useful for the stated features). Database is the headline decision.
3. **Cross-check Class A against the brief** — detect likely gaps (brief strongly implies a capability
   whose explicit flag is false/unset). On a conflict (explicit "no" vs strong signal): **honor the
   explicit answer**, attach a prominent **flag** to the architecture output, surfaced at the existing
   **Product Owner human-approval gate**. Never auto-add a Class-A capability.
4. **Apply the capability dependency graph** (§4).
5. **Emit the resolved Capability Profile** — the present/absent set (+ providers), a one-line rationale
   per decision, and any flags. This drives the scaffold composition and the CodeImplementer contract.

## 4. Capability dependency graph

### 4a. Hard invariants (ENFORCED — not subject to derivation, bias, or flagging)

These are structural truths of the stack; the resolver MUST apply them unconditionally, overriding any
Balanced-bias derivation. They are **not** flaggable gaps — there is no valid app without them:

- **`NeedsPayments` ⟹ `Database` (present).** A payments app must persist orders/customers, so the
  resolver forces `Database = present` whenever `NeedsPayments` is true. The Architect may NOT derive
  api-only in this case — the invariant wins over the brief and over Balanced bias.
- **`Database` (present) ⟹ API.** No datastore without the API that fronts it (already true in the shell).
- **`NeedsAIApi` ⟹ API.** AI calls run server-side.
- **`NeedsAuth` ⟹ API** (token validation) — already true in the shell.

### 4b. Soft implications (Balanced-derived or flagged, not enforced)

- Payments (Stripe) usually ⟹ **Email** (receipts). Email is Class-A (explicit-only), so this is NOT
  auto-added: if `NeedsPayments` is true and `NeedsEmail` is false, raise a **flagged gap** at the PO
  gate — honor the explicit "no email", do not silently switch it on.

Rule of thumb: an explicit capability that implies a **Class-B** axis **forces** it (§4a). An explicit
capability that implies **another explicit** capability that's off is a **flagged gap** (§7) — honor the
explicit answer, surface it for the human, never silently flip it.

## 5. Where derivation lives

**Recommendation: the Architect agent derives it in-pipeline** (it already runs pre-build and already
feeds the CodeImplementer via `architectOutput` → "Architecture Review"). This matches the user's exact
framing — "an architecture design prior to build" and "let the agents decide." The resolved profile is
part of/adjacent to the Architect's output, threaded the same way, and visible to the human at the PO
gate.

- **Not** Yorrixx-stamped like `stackProfile`: Yorrixx doesn't carry the agent reasoning, and derivation
  is explicitly the agents' job here.
- Persist the resolved profile into the run (audit + a posture doc) so generation **and** repair stay
  consistent within a run. (Stamping to the repo for cross-run rebuild idempotency is a later nicety.)

## 6. Scaffold & contract implications

**Composable scaffold (the heavy lift — Yorrixx, cross-session).** Today's template is all-in;
conditional-auth proved the overlay pattern (Yorrixx seeds a no-Clerk shell when `NeedsAuth=false`).
Generalising: each capability becomes an **includable module** (frontend + backend + tests + env
wiring), and Yorrixx provisioning **composes the shell from the resolved profile**. This is the main
cross-session dependency.

**CodeImplementer contract (platform).** Extend the `ScaffoldContract` selection (today: Static / Auth /
NoAuth) to reflect the profile — e.g. api-only ⟹ "no Cosmos, no `RepositoryBase`, stateless/in-memory";
payments present ⟹ "Stripe is wired at X, use it." Because N capabilities give 2^N combinations,
**prefer composable contract fragments** (a base contract + per-capability fragments assembled at
runtime from the profile) over a fixed set of hardcoded variant docs. Inject a **Capability Posture**
doc into every agent via `AgentContextDocuments.AddStandard` (same mechanism as auth/stack posture).

## 7. Conflict & gap surfacing

Define a structured flag: `{ capability, signal (brief evidence), explicitValue, recommendation }`.
Flags ride on the Architect output and render prominently in the Product Owner approval comment (the
human gate), so a "this looks like a store but payments are off" gap is caught before build.

## 8. TODO (deferred — not yet designed): change-request feedback loop

When the architecture step detects a **likely-needed but explicitly-disabled/unasked Class-A capability**,
it should feed back a **structured change-request recommendation** prompting the user to explicitly enable
it (amend the charter / re-answer the wizard question), which then re-enters the pipeline. The mechanism —
where it surfaces, how the user acts on it, how the amended charter re-triggers design/build — is **not
designed yet**. Captured as a TODO; revisit before relying on agent-flagged gaps to actually change scope.

## 9. Sequencing & first step

1. **Database axis first** (highest value, clearest gap): Architect derives api-only vs api+db (Balanced);
   add a `Database` field to the resolved profile; CodeImplementer contract reflects present/absent. The
   *decision* needs no Yorrixx change; *seeding* a DB-less shell does (cross-session).
2. **Class-A cross-check + flags**: detect brief-vs-explicit gaps for auth/payments/email; surface at the
   PO gate.
3. **Composable scaffold** (Yorrixx, cross-session): seed shell modules per profile.
4. **Extend taxonomy** (file storage, jobs, …) when a brief demands it.
5. **Design the §8 change-request feedback loop.**

**Proof:** a brief that clearly needs no persistence (e.g. a stateless calculator/marketing+contact app)
→ derived **api-only**, no Cosmos in the shell, contract says stateless, build green — vs a brief that
implies saved records → **api+db**. And a "no payments" answer on an obvious store → built without Stripe
but **flagged** at the PO gate.

---

## Relationship to other roadmap docs

- `conditional-auth-yorrixx-brief.md` — the first capability made conditional (`NeedsAuth`); this
  generalises its posture-doc + no-overlay pattern.
- `stack-profiles-static-first.md` — Static vs FullStack (the outer choice); capability derivation is the
  *inner* composition once FullStack is chosen. Note: per `project_marketing_page_universal`, a marketing
  1-pager ships with **every** app regardless of profile.
- `static-design-quality.md` — the imagery-plan judgment is the per-asset analogue of this per-capability
  judgment.
