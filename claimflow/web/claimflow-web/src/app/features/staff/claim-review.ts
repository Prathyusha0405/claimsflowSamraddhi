import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { ClaimDetail, User } from '../../core/models';
import { AgoPipe, DayPipe, errorText, MoneyPipe, Toasts, usd } from '../../core/ui';
import { Icon } from '../../shared/icon';
import { Avatar, EmptyState, MarketChip, Modal, PriorityTag, SlaChip, StatusBadge, Timeline, TypeIcon } from '../../shared/widgets';

type Dialog = 'info' | 'assess' | 'approve' | 'reject' | 'settle' | 'assign' | 'note' | null;

@Component({
  selector: 'cf-claim-review',
  imports: [FormsModule, RouterLink, Icon, Avatar, TypeIcon, StatusBadge, MarketChip, PriorityTag, SlaChip, Timeline, Modal, EmptyState, MoneyPipe, AgoPipe, DayPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <button class="back" type="button" (click)="location.back()"><cf-icon name="arrow-left" [size]="16" /> Back</button>

      @if (claim(); as c) {
        <div class="page-head fade-in">
          <div class="row gap-3">
            <cf-type [type]="c.type" [size]="56" />
            <div>
              <div class="row gap-2 wrap"><h1>{{ c.claimNumber }}</h1><cf-status [status]="c.status" /><cf-priority [priority]="c.priority" /><cf-market [market]="c.market" /><cf-sla [sla]="c.sla" /></div>
              <p>{{ c.claimantName }} · {{ c.type }} claim · reported {{ c.submittedAt | ago }} · policy <span class="mono">{{ c.policyNumber }}</span></p>
            </div>
          </div>
        </div>

        <div class="grid g-main">
          <!-- ───────────── left: the facts ───────────── -->
          <div class="col">
            <div class="card">
              <div class="card-head"><span class="card-title">Incident</span></div>
              <div class="card-pad">
                <dl class="kv">
                  <dt>When &amp; where</dt><dd>{{ c.incidentDate | d }} · {{ c.incidentLocation }}</dd>
                  <dt>What happened</dt><dd>{{ c.description }}</dd>
                  @if (c.motor; as m) {
                    <dt>Vehicle</dt><dd>{{ m.vehicleRegistration || '—' }}@if (m.policeReportNumber) { · police report <span class="mono">{{ m.policeReportNumber }}</span> }</dd>
                    <dt>Circumstances</dt><dd class="row gap-1 wrap">
                      <span class="chip">{{ m.thirdPartyInvolved ? 'Third party involved' : 'No third party' }}</span>
                      <span class="chip">{{ m.drivable ? 'Drivable' : 'Not drivable' }}</span>
                      @if (m.injuryInvolved) { <span class="chip chip-bad"><cf-icon name="alert" [size]="12" /> Injury reported</span> }</dd>
                  }
                  @if (c.property; as p) {
                    <dt>Property</dt><dd>{{ p.propertyAddress || '—' }}</dd>
                    <dt>Cause</dt><dd class="row gap-1 wrap"><span class="chip">{{ p.damageCause }}</span>
                      @if (!p.habitable) { <span class="chip chip-bad"><cf-icon name="alert" [size]="12" /> Not habitable</span> } @else { <span class="chip">Habitable</span> }</dd>
                  }
                </dl>
              </div>
            </div>

            <div class="card">
              <div class="card-head"><span class="card-title">Policy terms (captured at submission)</span></div>
              <div class="card-pad grid g3">
                <div><div class="muted strong">Product</div><div>{{ c.policy.productName }}</div><small class="muted">{{ c.policy.assetDescription }}</small></div>
                <div><div class="muted strong">Sum insured</div><div class="strong">{{ c.policy.coverageLimit | money: c.currency }}</div></div>
                <div><div class="muted strong">Excess</div><div class="strong">{{ c.policy.deductible | money: c.currency }}</div></div>
              </div>
            </div>

            @if (c.assessment; as a) {
              <div class="card">
                <div class="card-head"><span class="card-title">Assessment</span><span class="muted">{{ a.assessedByName }} · {{ a.assessedAt | ago }}</span></div>
                <div class="card-pad">
                  <div class="calc">
                    <div><span>Assessed loss</span><b>{{ a.assessedLoss | money: c.currency }}</b></div>
                    <div><span>× Covered</span><b>{{ a.coveredPercent }}%</b></div>
                    <div><span>− Excess</span><b>{{ a.deductibleApplied | money: c.currency }}</b></div>
                    <div class="tot"><span>= Payable</span><b>{{ a.payableAmount | money: c.currency }}</b></div>
                  </div>
                  @if (a.notes) { <p class="sub mt-2">“{{ a.notes }}”</p> }
                  @if (a.requiresManagerApproval) { <div class="banner banner-warn mt-2"><cf-icon name="lock" [size]="18" /><div>This amount exceeded the assessor's approval authority, so a <b>manager must approve</b> it.</div></div> }
                </div>
              </div>
            }

            @if (c.decision; as d) {
              <div class="card" [class.okc]="d.approved" [class.badc]="!d.approved">
                <div class="card-head"><span class="card-title">Decision</span><span class="muted">{{ d.decidedByName }} · {{ d.decidedAt | ago }}</span></div>
                <div class="card-pad">
                  @if (d.approved) { <div class="strong">Approved for {{ d.amount | money: c.currency }}</div>@if (d.notes) { <p class="sub">“{{ d.notes }}”</p> } }
                  @else { <div class="strong">Declined</div><p class="sub">{{ d.reason }}</p> }
                  @if (c.settlement; as s) { <div class="chip chip-ok mt-2"><cf-icon name="check" [size]="12" /> Paid {{ s.paidAmount | money: c.currency }} · ref {{ s.paymentReference }} · {{ s.settledAt | ago }}</div> }
                </div>
              </div>
            }

            <div class="card">
              <div class="card-head"><span class="card-title">Questions to the claimant</span></div>
              <div class="card-pad col">
                @for (r of c.informationRequests; track r.id) {
                  <div class="qa">
                    <div class="qq"><b>{{ r.requestedByName }}</b> · {{ r.requestedAt | ago }}<div>{{ r.question }}</div></div>
                    @if (r.response) { <div class="aa"><b>{{ c.claimantName }}</b> · {{ r.respondedAt | ago }}<div>{{ r.response }}</div></div> }
                    @else { <div class="aa wait"><cf-icon name="clock" [size]="14" /> Waiting for the claimant to reply…</div> }
                  </div>
                } @empty { <span class="muted">No questions asked yet.</span> }
              </div>
            </div>

            <div class="card">
              <div class="card-head"><span class="card-title">Internal notes <span class="muted" style="text-transform:none;letter-spacing:0">(never visible to the claimant)</span></span>
                @if (can('AddNote')) { <button class="btn btn-sm" type="button" (click)="open('note')"><cf-icon name="edit" [size]="14" /> Add note</button> }</div>
              <div class="card-pad col">
                @for (n of c.notes ?? []; track n.id) {
                  <div class="note"><b>{{ n.authorName }}</b> <span class="muted">· {{ n.at | ago }}</span><div>{{ n.text }}</div></div>
                } @empty { <span class="muted">No internal notes.</span> }
              </div>
            </div>

            <div class="card">
              <div class="card-head"><span class="card-title">Audit trail</span></div>
              <div class="card-pad"><cf-timeline [entries]="c.timeline" /></div>
            </div>
          </div>

          <!-- ───────────── right: what you can do ───────────── -->
          <aside class="col sticky">
            <div class="card">
              <div class="card-head"><span class="card-title">Actions</span></div>
              <div class="card-pad col gap-2">
                @if (otherHandler(c)) {
                  <div class="banner banner-info"><cf-icon name="info" [size]="18" /><div>Handled by <b>{{ c.assignedOfficerName }}</b>. You can read this claim but only they (or a manager) can act on it.</div></div>
                }
                @if (can('PickUp')) { <button class="btn btn-primary btn-block" type="button" [disabled]="busy()" (click)="pickup(c)"><cf-icon name="briefcase" [size]="16" /> Pick up this claim</button> }
                @if (can('RequestInformation')) { <button class="btn btn-block" type="button" (click)="open('info')"><cf-icon name="message" [size]="16" /> Request information</button> }
                @if (can('Assess')) { <button class="btn btn-primary btn-block" type="button" (click)="open('assess')"><cf-icon name="file" [size]="16" /> Record assessment</button> }
                @if (can('Approve')) { <button class="btn btn-ok btn-block" type="button" (click)="open('approve')"><cf-icon name="check-circle" [size]="16" /> Approve {{ c.assessment?.payableAmount | money: c.currency : true }}</button> }
                @if (hint('Approve'); as h) {
                  <div class="banner banner-warn"><cf-icon name="lock" [size]="18" /><div><b>Manager approval needed</b><div>{{ h }}</div></div></div>
                }
                @if (can('Settle')) { <button class="btn btn-ok btn-block" type="button" (click)="open('settle')"><cf-icon name="dollar" [size]="16" /> Issue payment</button> }
                @if (can('Reject')) { <button class="btn btn-danger btn-block" type="button" (click)="open('reject')"><cf-icon name="x-circle" [size]="16" /> Decline claim</button> }
                @if (can('Assign')) { <button class="btn btn-block" type="button" (click)="open('assign')"><cf-icon name="users" [size]="16" /> {{ c.assignedOfficerId ? 'Reassign' : 'Assign to officer' }}</button> }
                @if (!anyAction(c)) {
                  <div class="muted center" style="padding:6px 0">@if (isClosed(c)) { This claim is closed. } @else { No actions available for you right now. }</div>
                }
              </div>
            </div>

            <div class="card">
              <div class="card-head"><span class="card-title">Financials</span></div>
              <div class="card-pad col gap-2">
                <div class="row spread"><span class="muted">Claimed</span><b>{{ c.claimedAmount | money: c.currency }}</b></div>
                <div class="row spread"><span class="muted">Reserve (exposure)</span><b>{{ c.reserve | money: c.currency }}</b></div>
                <div class="row spread"><span class="muted">≈ in USD</span><b>{{ reserveUsd(c) }}</b></div>
                @if (c.settlement) { <div class="row spread"><span class="muted">Paid</span><b style="color:#059669">{{ c.settlement.paidAmount | money: c.currency }}</b></div> }
                <div class="hint">Reserve = what we expect to pay. It drops to zero when the claim is paid, declined or withdrawn.</div>
              </div>
            </div>

            <div class="card card-pad row gap-2">
              @if (c.assignedOfficerName) { <cf-avatar [name]="c.assignedOfficerName" [size]="38" /><div><div class="muted strong">Handler</div><b>{{ c.assignedOfficerName }}</b></div> }
              @else { <span class="muted">Not assigned yet</span> }
            </div>
          </aside>
        </div>

        <!-- ───────────── dialogs ───────────── -->
        @switch (dialog()) {
          @case ('info') {
            <cf-modal title="Request information" (closed)="close()">
              <p class="sub">The claim pauses and the claimant is notified immediately.</p>
              <div class="field"><label class="label" for="q">Your question</label>
                <textarea id="q" class="textarea" [ngModel]="question()" (ngModelChange)="question.set($event)" placeholder="e.g. Please upload the repair quotation from the workshop."></textarea>
                <span class="hint" [class.err]="question().trim().length > 0 && question().trim().length < 10">At least 10 characters</span></div>
              <ng-container footer><button class="btn" type="button" (click)="close()">Cancel</button>
                <button class="btn btn-primary" type="button" [disabled]="busy() || question().trim().length < 10" (click)="act(api.requestInfo(c.id, question().trim()), 'Question sent to the claimant.')">Send request</button></ng-container>
            </cf-modal>
          }
          @case ('assess') {
            <cf-modal title="Record assessment" [width]="600" (closed)="close()">
              <div class="grid g2">
                <div class="field"><label class="label" for="ls">Assessed loss ({{ c.currency }})</label>
                  <input id="ls" class="input" type="number" min="0" step="any" [ngModel]="loss()" (ngModelChange)="loss.set($event)" /></div>
                <div class="field"><label class="label" for="cv">Covered share: <b>{{ covered() }}%</b></label>
                  <input id="cv" type="range" min="0" max="100" step="5" [ngModel]="covered()" (ngModelChange)="covered.set($event)" style="accent-color:#4f46e5;margin-top:10px" />
                  <span class="hint">Reduce below 100% for partial cover, under-insurance or contributory fault.</span></div>
              </div>
              @if (calc(); as k) {
                <div class="calc live">
                  <div><span>Loss × {{ covered() }}%</span><b>{{ k.gross | money: c.currency }}</b></div>
                  <div><span>− Excess</span><b>{{ c.policy.deductible | money: c.currency }}</b></div>
                  <div><span>Sum insured cap</span><b>{{ c.policy.coverageLimit | money: c.currency : true }}</b></div>
                  <div class="tot"><span>= Payable</span><b>{{ k.payable | money: c.currency }}</b></div>
                </div>
                @if (k.capped) { <div class="banner banner-info"><cf-icon name="info" [size]="18" /><div>Capped at the sum insured.</div></div> }
                @if (k.needsManager) {
                  <div class="banner banner-warn"><cf-icon name="lock" [size]="18" /><div><b>Needs manager approval.</b> ≈ {{ fmtUsd(k.usdVal) }} is above your authority of {{ fmtUsd(authority()) }}. You can still record the assessment; a manager will approve.</div></div>
                } @else {
                  <div class="banner banner-ok"><cf-icon name="check-circle" [size]="18" /><div>≈ {{ fmtUsd(k.usdVal) }} — within your approval authority.</div></div>
                }
              }
              <div class="field"><label class="label" for="an">Assessment notes <span class="muted">(internal)</span></label>
                <textarea id="an" class="textarea" [ngModel]="notes()" (ngModelChange)="notes.set($event)" placeholder="What did you verify? Which estimate did you use?"></textarea></div>
              <ng-container footer><button class="btn" type="button" (click)="close()">Cancel</button>
                <button class="btn btn-primary" type="button" [disabled]="busy() || !(loss() !== null && loss()! >= 0)" (click)="act(api.assess(c.id, +loss()!, covered(), notes()), 'Assessment recorded.')">Save assessment</button></ng-container>
            </cf-modal>
          }
          @case ('approve') {
            <cf-modal title="Approve payment" (closed)="close()">
              <div class="banner banner-ok"><cf-icon name="check-circle" [size]="20" /><div>You are approving <b>{{ c.assessment?.payableAmount | money: c.currency }}</b> for {{ c.claimantName }}. The claimant is notified right away.</div></div>
              <div class="field"><label class="label" for="ap">Note <span class="muted">(optional)</span></label>
                <textarea id="ap" class="textarea" [ngModel]="notes()" (ngModelChange)="notes.set($event)"></textarea></div>
              <ng-container footer><button class="btn" type="button" (click)="close()">Cancel</button>
                <button class="btn btn-ok" type="button" [disabled]="busy()" (click)="act(api.approve(c.id, notes()), 'Claim approved.')">Approve</button></ng-container>
            </cf-modal>
          }
          @case ('reject') {
            <cf-modal title="Decline claim" (closed)="close()">
              <p class="sub">Write a reason the claimant can understand — they will see it exactly as written.</p>
              <div class="row gap-1 wrap">@for (r of reasons; track r.label) { <button type="button" class="pill-btn" (click)="reason.set(r.text)">{{ r.label }}</button> }</div>
              <div class="field"><label class="label" for="rj">Reason</label>
                <textarea id="rj" class="textarea" [ngModel]="reason()" (ngModelChange)="reason.set($event)"></textarea>
                <span class="hint" [class.err]="reason().trim().length > 0 && reason().trim().length < 10">At least 10 characters</span></div>
              <ng-container footer><button class="btn" type="button" (click)="close()">Cancel</button>
                <button class="btn btn-danger" type="button" [disabled]="busy() || reason().trim().length < 10" (click)="act(api.reject(c.id, reason().trim()), 'Claim declined.')">Decline claim</button></ng-container>
            </cf-modal>
          }
          @case ('settle') {
            <cf-modal title="Issue payment" (closed)="close()">
              <div class="banner banner-ok"><cf-icon name="dollar" [size]="20" /><div>Pay <b>{{ c.decision?.amount | money: c.currency }}</b> to {{ c.claimantName }}. This releases the reserve and closes the claim.</div></div>
              <div class="field"><label class="label" for="pr">Payment reference <span class="muted">(leave empty to auto-generate)</span></label>
                <input id="pr" class="input" [ngModel]="payRef()" (ngModelChange)="payRef.set($event)" placeholder="PAY-…" /></div>
              <ng-container footer><button class="btn" type="button" (click)="close()">Cancel</button>
                <button class="btn btn-ok" type="button" [disabled]="busy()" (click)="act(api.settle(c.id, payRef().trim()), 'Payment issued — claim closed.')">Confirm payment</button></ng-container>
            </cf-modal>
          }
          @case ('assign') {
            <cf-modal title="Assign to officer" (closed)="close()">
              <div class="field"><label class="label" for="of">Officer</label>
                <select id="of" class="select" [ngModel]="officerId()" (ngModelChange)="officerId.set($event)">
                  <option value="">Choose…</option>
                  @for (o of staff(); track o.id) { <option [value]="o.id">{{ o.name }} — {{ o.role === 'Manager' ? 'Manager' : 'authority ' + fmtUsd(o.authorityLimitUsd) }}</option> }
                </select></div>
              <ng-container footer><button class="btn" type="button" (click)="close()">Cancel</button>
                <button class="btn btn-primary" type="button" [disabled]="busy() || !officerId()" (click)="act(api.assign(c.id, officerId()), 'Claim assigned.')">Assign</button></ng-container>
            </cf-modal>
          }
          @case ('note') {
            <cf-modal title="Add internal note" (closed)="close()">
              <div class="field"><label class="label" for="nt">Note <span class="muted">(staff only)</span></label>
                <textarea id="nt" class="textarea" [ngModel]="noteText()" (ngModelChange)="noteText.set($event)"></textarea></div>
              <ng-container footer><button class="btn" type="button" (click)="close()">Cancel</button>
                <button class="btn btn-primary" type="button" [disabled]="busy() || noteText().trim().length < 2" (click)="act(api.addNote(c.id, noteText().trim()), 'Note added.')">Save note</button></ng-container>
            </cf-modal>
          }
        }
      } @else if (missing()) {
        <div class="card"><cf-empty icon="search" title="Claim not found" text="It may have been removed, or the link is wrong."><a class="btn mt-2" routerLink="/staff/claims">Search claims</a></cf-empty></div>
      } @else {
        <div class="skel" style="height:80px"></div><div class="skel mt-3" style="height:260px"></div>
      }
    </div>`,
  styles: [`
    .back{display:inline-flex;align-items:center;gap:6px;margin-bottom:14px;font-weight:600;color:var(--ink-2);border:0;background:none;cursor:pointer;padding:0}
    .sticky{position:sticky;top:78px}
    .calc{display:grid;grid-template-columns:repeat(4,1fr);gap:10px}.calc div{background:#f6f8fd;border:1px solid var(--line);border-radius:12px;padding:11px 14px;display:flex;flex-direction:column}
    .calc span{color:var(--ink-3);font-size:12px;font-weight:600}.calc b{font-size:15px}.calc .tot{background:var(--brand);border-color:var(--brand);color:#fff}.calc .tot span{color:#d9dcff}
    .calc.live{grid-template-columns:repeat(2,1fr)}
    .okc{border-color:#a8e6cb}.badc{border-color:#f4bcbc}
    .qa{display:flex;flex-direction:column;gap:8px}.qq,.aa{padding:11px 14px;border-radius:12px;font-size:12.5px;color:var(--ink-3)}.qq div,.aa div{color:var(--ink);font-size:14px;margin-top:3px}
    .qq{background:#f4f6fc}.aa{background:#eef0ff;margin-left:26px}.aa.wait{background:#fff1d6;color:#c76a00;display:flex;gap:8px;align-items:center}
    .note{background:#fffbe9;border:1px solid #f4e6a8;border-radius:12px;padding:11px 14px}.note div{margin-top:3px}
    @media (max-width:1100px){.sticky{position:static}.calc{grid-template-columns:1fr 1fr}}
  `],
})
export class ClaimReview {
  protected readonly api = inject(Api);
  private readonly auth = inject(Auth);
  private readonly toasts = inject(Toasts);
  protected readonly location = inject(Location);

  readonly id = input.required<string>();

  protected readonly claim = signal<ClaimDetail | null>(null);
  protected readonly missing = signal(false);
  protected readonly busy = signal(false);
  protected readonly dialog = signal<Dialog>(null);
  protected readonly staff = signal<User[]>([]);

  // form state
  protected readonly question = signal('');
  protected readonly loss = signal<number | null>(null);
  protected readonly covered = signal(100);
  protected readonly notes = signal('');
  protected readonly reason = signal('');
  protected readonly payRef = signal('');
  protected readonly officerId = signal('');
  protected readonly noteText = signal('');
  protected readonly reasons = [
    { label: 'Wear & tear', text: 'The damage is the result of gradual wear and tear, which is excluded under your policy wording.' },
    { label: 'Not covered', text: 'The cause of loss is not covered under the terms of your policy.' },
    { label: 'Below excess', text: 'The assessed loss is below your policy excess, so no payment is due on this claim.' },
  ];

  protected readonly authority = computed(() => this.auth.user()?.authorityLimitUsd ?? 0);
  protected readonly calc = computed(() => {
    const c = this.claim();
    if (!c) return null;
    const gross = (Number(this.loss()) || 0) * this.covered() / 100;
    const afterExcess = Math.max(0, gross - c.policy.deductible);
    const payable = Math.round(Math.min(afterExcess, c.policy.coverageLimit) * 100) / 100;
    const usdVal = payable * c.usdRate;
    return { gross, payable, capped: afterExcess > c.policy.coverageLimit, usdVal, needsManager: !this.auth.isManager() && usdVal > this.authority() };
  });

  constructor() {
    effect(() => this.load(this.id()));
    this.api.staff().subscribe({ next: s => this.staff.set(s), error: () => {} });
    const t = setInterval(() => { if (!this.dialog() && !this.busy()) this.load(this.id(), true); }, 6000);
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }

  private load(id: string, silent = false) {
    this.api.claim(id).subscribe({ next: c => this.claim.set(c), error: () => { if (!silent) this.missing.set(true); } });
  }

  protected can(a: string) { return !!this.claim()?.allowedActions.includes(a); }
  protected hint(a: string) { return this.claim()?.actionHints[a] ?? null; }
  protected anyAction(c: ClaimDetail) { return c.allowedActions.some(a => a !== 'AddNote') || !!c.actionHints['Approve']; }
  protected isClosed(c: ClaimDetail) { return ['Rejected', 'Settled', 'Withdrawn'].includes(c.status); }
  protected otherHandler(c: ClaimDetail) { return !!c.assignedOfficerId && c.assignedOfficerId !== this.auth.user()?.id && !this.auth.isManager(); }
  protected reserveUsd(c: ClaimDetail) { return usd(c.reserveUsd); }
  protected fmtUsd(v: number) { return usd(Math.round(v)); }

  protected open(d: Exclude<Dialog, null>) {
    const c = this.claim();
    if (d === 'assess' && c) { this.loss.set(c.assessment?.assessedLoss ?? c.claimedAmount); this.covered.set(c.assessment?.coveredPercent ?? 100); this.notes.set(''); }
    if (d === 'approve') this.notes.set('');
    this.question.set(''); this.reason.set(''); this.payRef.set(''); this.officerId.set(''); this.noteText.set('');
    this.dialog.set(d);
  }
  protected close() { this.dialog.set(null); }

  protected pickup(c: ClaimDetail) { this.act(this.api.pickup(c.id), 'Claim picked up — it is now yours.'); }

  /** Runs a command; on success swaps in the fresh server state, on failure explains and re-syncs. */
  protected act(call: Observable<ClaimDetail>, success: string) {
    this.busy.set(true);
    call.subscribe({
      next: c => { this.claim.set(c); this.busy.set(false); this.dialog.set(null); this.toasts.success(success); },
      error: e => { this.busy.set(false); this.toasts.error(errorText(e)); this.load(this.id(), true); },
    });
  }
}
