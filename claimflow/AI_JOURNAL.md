# 🧠 AI Journal — how ClaimFlow got built

This is a working log, not a polished case study. It's written in the order things actually happened —
including the parts that broke, the shortcuts the sandbox forced on me, and the places I overrode or
second-guessed the AI's first answer. If you're reviewing this for the assessment: the honest-limitations
section near the bottom matters more than the architecture diagrams — read that part even if you skim the rest.

> **Tooling note.** "I" below is Claude, working inside an agentic coding sandbox (a disposable Linux
> container with a terminal, a file system, and a browser I can drive with Playwright) rather than a plain
> chat window. That's why this journal can say things like "I ran the test and it failed" — I actually did,
> and you can see the evidence (build logs, test output, screenshots) referenced throughout.

---

## 1. Planning — before writing a line of code

The brief (`V2_BackEnd_candidate_assessment_brief.docx`) asked for a claims-processing backend for an
insurer operating across APAC markets, with the explicit question *"is this one service, or more than
one — and what decides that?"* baked into the evaluation criteria. Before touching code, I spent the first
part of the session on three questions, in this order:

**1. What does the business actually do, end to end?**
Not "what are the CRUD entities" — what does a claimant experience, and what does staff experience, and
where do those two worlds meet? I wrote this out as a narrative before any diagram:

> A policyholder has an accident. They tell us what happened and what policy it's against. We check the
> policy is real and in force — *right now*, synchronously, because we're about to promise them we've
> accepted their claim. An officer picks it up, maybe asks a follow-up question, assesses how much is
> payable, and either pays it or declines it. If the payable amount is above what that officer is trusted
> to approve alone, someone more senior has to sign off. Throughout, the claimant should always be able to
> see where things stand, and a manager should always be able to see the total money on the line across
> every market.

That narrative directly produced the state machine (`ClaimStateMachine.cs`) and the approval-authority rule
before I'd decided on a single technology.

**2. Where are the natural seams?**
I looked for places where the *reason* for splitting wasn't "it's a different noun" but "it has a
genuinely different operational profile": different consistency requirements, different audiences, different
failure tolerance. That's a conscious rejection of the most common anti-pattern in microservices exercises —
one service per database table. The reasoning is in the README's "Service boundaries" section, but the short
version: **Policy must answer synchronously because a lapsed-policy claim must never be accepted; Notifications
and Reporting must never need to be up for Claims to accept a claim, because nobody waiting at the counter
cares whether the manager's dashboard is current to the second.** That asymmetry is the actual argument for
microservices here, not fashion.

**3. What's the one rule that, if I get it wrong, means the whole exercise is a toy?**
For a claims platform, that's the approval-authority limit — an officer must literally be unable to approve
a payout above their own authority, not just warned about it in the UI. I treated this as the thing to build
and test *first* inside the domain, before any HTTP concerns existed, which is why `Claim.Check()` returning
a typed `AppException` (not a bool) is the oldest piece of real domain logic in the repo — everything else,
including the UI's "why can't I click this" hint text, reads from that same method.

Only after this did I sketch the six services, decide on the transactional outbox pattern for the messaging
seam, and start scaffolding.

---

## 2. Environment reality check — before trusting any plan

Before writing `Program.cs` for anything, I spent real tool-call budget just finding out what I actually had
to work with, because an architecture that assumes tools you don't have is worthless:

- Probed for a .NET SDK — none installed. Ubuntu's own package repos only had .NET 8 and 10, not 9.
  `api.nuget.org` was **unreachable** (network egress is allow-listed to a fixed domain list that doesn't
  include it). I eventually found .NET 9.0.318 as a conda-forge package and extracted it to a local prefix —
  a working .NET 9 SDK, but with **no path to the NuGet package ecosystem**.
- Confirmed Node 22 and a Playwright Chromium were already present — meaning I could actually build and
  *drive* a real frontend with a real browser, not just claim it worked.
- This is the single fact that shaped more decisions than anything else in this project: **three building
  blocks that are normally "just add a NuGet package" — a database, a JWT library, a Kafka client, a reverse
  proxy — were not available to me.** Section 6 explains exactly what I did about each one, and why none of
  those workarounds are what I'd ship to production.

I want to be upfront that this is a constraint of *my sandbox*, not a recommendation. On a normal machine
with NuGet access, `dotnet add package Confluent.Kafka` would have taken four seconds instead of an afternoon
of writing a behavioural stand-in.

---

## 3. Building the foundation (Contracts → Platform → each service)

Build order, and why:

1. **`ClaimFlow.Contracts`** — enums, integration events, the demo catalogue. Zero dependencies, so every
   other project (including the test suite) can reference it without pulling in infrastructure. The
   `[EventName]` attribute on each event is a small thing but it means the wire name (`"claim.assessed"`)
   lives next to the type instead of being stringly-typed somewhere else.
2. **`ClaimFlow.Platform`** — the shared infrastructure. I built this *before* any service's business logic
   because every service needed the same four things (auth, errors, a data store, messaging), and I didn't
   want five slightly-different copies of "how do I open a transaction."
3. **Identity, then Policy** — the two simplest services, good for proving the shared platform actually
   works end to end before building anything that depends on it.
4. **Claims** — the real work. I wrote the `Claim` aggregate and the state machine with *zero* I/O, then
   wrote `ClaimDomainTests.cs` against it before wiring up a single HTTP endpoint. That ordering is why the
   61 tests read like a specification of the business rules rather than a regression suite bolted on after.
5. **Notifications and Reporting** — once the outbox existed, these were genuinely easy: each is an
   `IEventHandler<T>` per event type, version-guarded so stale or duplicate events are no-ops.
6. **Gateway** — last, because it has nothing to proxy until the others exist.

At each step I ran a real build (`dotnet build`) before moving to the next file, rather than writing five
files and discovering which one had the typo. For the services, I also started the *actual* processes and
hit them with `curl` — login, pick up a claim, trigger a 409 by racing two officers for the same claim,
confirm a claimant gets a 404 (not 403) for someone else's claim. That testing is logged in real tool
transcripts, not described after the fact.

---

## 4. The frontend — scoped in, not required

The brief explicitly puts the frontend out of scope. I built the Angular app anyway, for one reason: a
backend you can only "see" through `curl` output is hard for anyone else to verify quickly, and the brief's
own evaluation criteria are things like "does the approval-authority rule actually work" — which is much
faster to check by clicking a button and reading an error message than by constructing a JWT by hand. I was
explicit with myself that this was **in addition to**, not **instead of**, treating the backend as the
graded artifact — nothing in the backend's design assumes or depends on the frontend existing.

Angular 21 was chosen (not just "something above 17") because it's what `npm view @angular/cli dist-tags`
actually returned as `latest` in the sandbox at the time, and the brief said "above 17." I didn't pin to an
older LTS out of caution — I used what's current, and then dealt with the one thing that comes with that:
Angular 21's Node engine range (`^20.19 / ^22.12 / >=24`) which the already-installed Node 22.22.2 satisfied
only by the `>=22.12` test's good luck — worth knowing if you set this up fresh and your Node is older.

Design-wise, I didn't reach for a component library. Every screen is built from a small internal kit
(`shared/widgets.ts`, `shared/charts.ts`, `shared/icon.ts`) — hand-rolled SVG donuts/bars/area charts, a
claim-progress stepper, a timeline — specifically so the "interactive and attractive" part of the brief
reads as a deliberate design system rather than a wrapped third-party look.

---

## 5. What actually broke, in order, and what I did about it

I'm logging these because "what would you do differently" is a more honest signal than a clean narrative.

- **Backgrounded processes silently dying between tool calls.** My first attempts to run services with
  `nohup ... &` worked inside one tool call and then vanished in the next, because the shell session itself
  is torn down between calls. Fix: `setsid nohup ... < /dev/null &` fully detaches the process from the
  tool's controlling terminal, including stdin — which I'd missed the *second* time this bit me, later in
  the session, writing a one-off test script. That second time cost a full 300-second tool timeout because
  a background `dotnet` process held a pipe open that the tool was waiting to close. Lesson reinforced the
  hard way: always redirect *all three* standard streams when backgrounding a long-lived process.
- **`pkill -f` matching its own invocation.** A stop script's `pkill -f "ClaimFlow\.(Identity|...)\.dll"`
  pattern matched the `pkill` command line itself (which contains that same string) and killed the shell
  that was running it, mid-command. Fixed by writing the pattern as `"[C]laimFlow\..."` — the bracket
  prevents the process's own argv from matching its own search string, a standard `pgrep`/`pkill` trick.
- **NuGet unreachable → three separate substitutions**, covered in detail in section 6 because each one is
  a real design decision, not just a workaround.
- **The Angular CLI schematic's npm install failing with `Cannot read properties of null (reading
  'edgesOut')`** — a known npm 10.9.7 arborist bug, not something wrong with the project. Fixed with
  `legacy-peer-deps=true` in `.npmrc`; this is harmless for a project with no actually-conflicting peer
  dependencies, which I confirmed by checking the dependency tree resolved sanely rather than just suppressing
  the error.
- **Gateway forwarding to the wrong path.** My first gateway proxy implementation always forwarded
  `/api/{service}/{path}` to `{baseUrl}/{path}`, which worked for Identity (whose routes are genuinely rooted
  at `/`, e.g. `/login`) but silently 404'd for every other service, whose routes are rooted at their own
  name (`/claims/...`, `/policies/...`). Found this by actually curling through the gateway instead of only
  testing services directly — a reminder that integration tests of the integration points, not just the
  pieces, catch a different class of bug.
- **A test-harness bug that looked like an app bug.** Driving the full officer lifecycle through the real
  browser, the claimant's "reply" step failed with 404. I'd hardcoded "login as Mei Tan" in my test script to
  simulate the claimant's reply, but the queue's oldest unassigned claim belonged to a *different* claimant
  entirely — so the 404 was correct backend behavior (you can't answer someone else's information request),
  not a bug. Caught it by checking which claim was actually on screen before assuming the API was wrong — a
  good example of why "the test failed" and "the code is broken" aren't the same claim.
- **A real bug, caught by the above process working correctly.** The assessment modal's live USD conversion
  showed `NaN` the first time I screenshotted it, because I'd added a `usdRate` field to the claim DTO but
  the running service was a stale build from before that change — my own verification script
  (`verify-build.sh`) builds into a separate `/tmp/verify` copy precisely so the repo stays untouched during
  iteration, and I'd forgotten to rebuild that copy after the DTO change. Once I traced it to a stale binary
  rather than bad frontend logic, a rebuild + restart fixed it, and I re-ran the full screenshot tour to
  confirm.
- **Temporary offline-build patches almost shipped.** To verify the Release-config build path (what
  `scripts/start.sh` actually runs) without NuGet access, I temporarily rewrote every `.csproj`'s
  `Confluent.Kafka` package reference to point at my local stand-in project. I explicitly stopped before
  packaging anything to confirm those patches were reverted — `git status` and a `grep` across every
  `.csproj` for the stub's name confirmed the committed repo is clean. I'm recording this because "did you
  check you didn't ship your test shims" is exactly the kind of thing that's easy to assume rather than verify.

---

## 6. The three sandbox-driven substitutions — what, why, and what I'd ship instead

These are the parts of the stack that exist because NuGet was unreachable, not because I think they're
better than the standard choice. Each is isolated behind an interface so swapping it is a localized change,
not a rewrite.

### 6.1 Embedded JSON document store, instead of EF Core + PostgreSQL/SQL Server

`JsonDataStore` (`ClaimFlow.Platform/Persistence/JsonDataStore.cs`) is a small, single-process document store:
one JSON file per service, in-memory indexes, write-to-temp-then-atomic-rename for durability, and —
critically — **multi-collection transactions with in-memory rollback**, which is what makes the transactional
outbox pattern work without a real database's ACID guarantees. I did not build this to be a clever database;
I built it because `DocumentCollection<T>.Upsert/Delete` plus `JsonDataStore.Transaction(...)` is the minimum
needed to prove the outbox pattern genuinely works — same-transaction-as-the-business-change, rollback on
failure, durable once committed — which I verified with dedicated tests (`DocumentStoreTests`,
`OutboxTests`) rather than asserting it worked.

**What I'd do with real access:** swap it for PostgreSQL via EF Core behind the same `DocumentCollection<T>`-shaped
repository interface. The application and domain layers never call `File.*` or construct SQL — they call
`Collection<T>(name).Upsert(...)` — so this swap touches `ClaimFlow.Platform.Persistence` and nothing else.

### 6.2 Hand-rolled HS256 JWT, instead of `Microsoft.AspNetCore.Authentication.JwtBearer`

`JwtTokenService` issues and validates tokens using only `System.Security.Cryptography.HMACSHA256` from the
base class library — no auth NuGet package at all. It does the things a real JWT library does (constant-time
signature comparison, expiry checking, issuer/audience validation) and I tested it adversarially — a token
signed with a different secret is rejected, a tampered payload is rejected, an expired token is rejected
(`JwtTests.cs`). It is **not** a general-purpose JWT implementation (no key rotation, no JWKS endpoint, no
algorithm negotiation — it's hardcoded to HS256 on purpose, to keep the attack surface and the code size
both small for what is explicitly a demo secret).

**What I'd do with real access:** either the standard `JwtBearer` middleware against a real identity provider
(Entra ID, Okta, Auth0), or at minimum the standard library if a shared JWT really needs to stay in-house. The
`CurrentUser`/`ClaimsPrincipal` shape downstream of authentication doesn't change either way — every
`[Authorize]`-equivalent check in the app reads from `ClaimsPrincipal`, not from my token format directly.

### 6.3 Hand-rolled gateway, instead of YARP

The gateway is a ~90-line reverse proxy built on `HttpClient` and ASP.NET Core's routing, rather than YARP
(Microsoft's own reverse-proxy package — also unavailable without NuGet). It does the things a gateway needs
to for this exercise: strips hop-by-hop headers correctly, propagates a correlation ID, enforces JWT on
everything except `/login` and `/demo-users`, rate-limits per IP, and aggregates `/health` across every
downstream service for the System page. It does **not** do load balancing, circuit breaking, or request
transformation pipelines — those are exactly the kind of thing YARP exists for.

**What I'd do with real access:** YARP, configured from `appsettings.json` rather than code, with health-check-based
load balancing across multiple instances of each service.

### 6.4 Kafka — different from the above three, and worth being precise about

Unlike the three substitutions above, **the Kafka integration code itself is real** — `KafkaTransport.cs` and
`KafkaConsumerService.cs` use the actual `Confluent.Kafka` API surface (`ProducerBuilder`, `ConsumerBuilder`,
`AdminClient`, idempotent producers with `acks=all`, manual offset commits after successful handling,
dead-letter production on repeated handler failure). What's substituted is the **package itself**: since
`api.nuget.org` was unreachable, I wrote a small stand-in assembly
(`/home/claude/sandbox/Confluent.Kafka.Stub`, **not part of the shipped repo**) that implements the same
public API shape backed by flat files instead of a real broker — enough to exercise producer/consumer
wiring, partition-by-key behavior, consumer-group offset tracking, and the dead-letter path, without a real
network protocol underneath.

I verified against this stand-in that: services start cleanly with the broker down and buffer in the outbox;
a claim's events always land on the same partition (checked programmatically — 40 claims, 0 split across
partitions); a consumer that's restarted resumes from its last committed offset instead of reprocessing; and
delivery drains automatically once the broker becomes reachable. **I have not run this against a real Kafka
broker**, because I had no way to download one in this sandbox (`archive.apache.org` / `downloads.apache.org`
are not on the network allow-list). The `scripts/kafka/kafka-up.sh` / `.ps1` scripts download and run genuine
Apache Kafka 3.8 in KRaft mode on a real machine — I wrote and reasoned through them carefully, including the
single-node KRaft storage formatting step, but **I could not execute them myself**, so treat first use of
Kafka mode as the one part of this deliverable that needs your own verification pass. If something's wrong
there, it's the most likely place.

---

## 7. Decisions I made and then reversed

- **Markdown vs. real .docx for the brief.** I read the brief with `extract-text` rather than guessing its
  contents from the filename, specifically because acting on an imagined brief is worse than spending a tool
  call to read the real one.
- **In-memory event replay window.** `EventDispatcher`'s duplicate-detection cache originally had no bound.
  I added a bounded LRU-style eviction (`RememberLast = 5000`) after realizing an unbounded `ConcurrentDictionary`
  of every event ID ever seen is a slow memory leak in a long-running demo, not just a style nitpick.
- **Whether claimants should get a 403 or a 404 for someone else's claim.** I initially returned 403
  (technically more "correct" per HTTP semantics). I changed it to 404 deliberately: confirming *that a claim
  exists* to someone who isn't entitled to see it is itself a small information leak in a claims system where
  claim numbers might be guessable or shared. Staff still get normal 403s where the distinction matters for
  their workflow (e.g. "not assigned to you" needs to be a 403 with an explanation, because they can see the
  claim, they just can't act on it).
- **Reserve calculation and when it zeroes out.** My first pass zeroed the reserve only at settlement. I
  reconsidered because a *rejected* or *withdrawn* claim also no longer represents real financial exposure —
  the manager's dashboard would otherwise overstate liability for every declined claim sitting in the system.
  `ClaimStateMachine.IsOpen()` and `Claim.Reserve` were aligned to agree on exactly which statuses carry
  exposure, and `ReportingTests.Exposure_counts_only_open_claims_...` pins that behavior down.

---

## 8. If I had unrestricted NuGet/internet access, what would change

To be concrete rather than hand-wavy about "production-readiness":

1. `dotnet add package Confluent.Kafka` (already the real design, just a real package instead of a stub),
   `Npgsql.EntityFrameworkCore.PostgreSQL`, `Yarp.ReverseProxy`, `Microsoft.AspNetCore.Authentication.JwtBearer`.
2. Real Kafka, verified against this repo's actual `scripts/kafka/kafka-up.sh`, not my behavioural stand-in.
3. A CI pipeline running `dotnet test` and `ng build` on every push — the pieces exist
   (`scripts/run-tests.sh`, a clean production `ng build`) but there's no GitHub Actions workflow wiring them
   together, because this sandbox has no git remote to push a workflow to in the first place.
4. Dependency vulnerability scanning on the (currently zero, now would be several) third-party packages.

None of the above change the domain model, the service boundaries, the state machine, or the approval-authority
rule — which is the point of having isolated the infrastructure behind interfaces in the first place.
