import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { ClaimDetail } from '../../core/models';
import { AgoPipe, DatePipe2, DayPipe, errorText, MoneyPipe, Toasts } from '../../core/ui';
import { Icon } from '../../shared/icon';
import { ClaimStepper, EmptyState, Modal, StatusBadge, Timeline, TypeIcon } from '../../shared/widgets';

@Component({
  selector: 'cf-claimant-detail',
  imports: [FormsModule, RouterLink, Icon, StatusBadge, TypeIcon, ClaimStepper, Timeline, Modal, EmptyState, MoneyPipe, AgoPipe, DatePipe2, DayPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <a class="back" routerLink="/my/claims"><cf-icon name="arrow-left" [size]="16" /> My claims</a>

      @if (claim(); as c) {
        <div class="page-head fade-in">
          <div class="row gap-3">
            <cf-type [type]="c.type" [size]="54" />
            <div>
              <div class="row gap-2 wrap"><h1>{{ c.claimNumber }}</h1><cf-status [status]="c.status" /></div>
              <p>{{ c.type }} claim · reported {{ c.submittedAt | ago }} · policy <span class="mono">{{ c.policyNumber }}</span></p>
            </div>
          </div>
          @if (c.allowedActions.includes('Withdraw')) {
            <button class="btn btn-danger btn-sm" type="button" (click)="withdrawing.set(true)">Withdraw claim</button>
          }
        </div>

        <div class="card card-pad fade-in"><cf-stepper [status]="c.status" /></div>

        @if (openRequest(c); as r) {
          <div class="card ask mt-3 fade-in">
            <div class="row gap-3" style="align-items:flex-start">
              <span class="qi"><cf-icon name="message" [size]="22" /></span>
              <div class="grow">
                <div class="card-title" style="color:#c76a00">{{ r.requestedByName }} needs a little more information</div>
                <p class="q">“{{ r.question }}”</p>
                <textarea class="textarea" style="margin-top:12px" [ngModel]="reply()" (ngModelChange)="reply.set($event)" placeholder="Type your answer…"></textarea>
                <div class="row spread mt-2"><span class="hint">Asked {{ r.requestedAt | ago }}. Your claim is paused until you reply.</span>
                  <button class="btn btn-primary" type="button" [disabled]="busy() || reply().trim().length < 3" (click)="send(c, r.id)">{{ busy() ? 'Sending…' : 'Send reply' }} <cf-icon name="send" [size]="15" /></button></div>
              </div>
            </div>
          </div>
        }

        @if (c.status === 'Approved' || c.status === 'Settled') {
          <div class="card decision ok mt-3 fade-in">
            <div class="row gap-3">
              <span class="di"><cf-icon name="check-circle" [size]="28" /></span>
              <div class="grow">
                <div class="card-title" style="color:#059669">{{ c.status === 'Settled' ? 'Paid' : 'Approved' }}</div>
                <div class="big">{{ c.decision?.amount | money: c.currency }}</div>
                @if (c.status === 'Settled' && c.settlement; as s) { <div class="sub">Payment sent {{ s.settledAt | dt }} · reference <b class="mono">{{ s.paymentReference }}</b></div> }
                @else { <div class="sub">Payment is being arranged — you'll be notified when it's sent.</div> }
              </div>
            </div>
            @if (c.assessment; as a) {
              <div class="calc">
                <div><span>Assessed loss</span><b>{{ a.assessedLoss | money: c.currency }}</b></div>
                <div><span>Covered</span><b>{{ a.coveredPercent }}%</b></div>
                <div><span>Policy excess</span><b>− {{ a.deductibleApplied | money: c.currency }}</b></div>
                <div class="tot"><span>We pay</span><b>{{ a.payableAmount | money: c.currency }}</b></div>
              </div>
            }
          </div>
        }
        @if (c.status === 'Rejected') {
          <div class="card decision bad mt-3 fade-in">
            <div class="row gap-3"><span class="di"><cf-icon name="x-circle" [size]="28" /></span>
              <div class="grow"><div class="card-title" style="color:#dc2626">We couldn't accept this claim</div>
                <p class="q" style="color:#7f1d1d">{{ c.decision?.reason }}</p>
                <div class="sub mt-1">Decision by {{ c.decision?.decidedByName }} · {{ c.decision?.decidedAt | ago }}. If you disagree, reply to the notification or contact your market's claims team.</div></div></div>
          </div>
        }

        <div class="grid g-main mt-3">
          <div class="col">
            <div class="card">
              <div class="card-head"><span class="card-title">What you told us</span></div>
              <div class="card-pad">
                <dl class="kv">
                  <dt>Incident</dt><dd>{{ c.incidentDate | d }} · {{ c.incidentLocation }}</dd>
                  <dt>Description</dt><dd>{{ c.description }}</dd>
                  <dt>Claimed amount</dt><dd><b>{{ c.claimedAmount | money: c.currency }}</b></dd>
                  @if (c.motor; as m) {
                    <dt>Vehicle</dt><dd>{{ m.vehicleRegistration || '—' }}@if (m.policeReportNumber) { · police report {{ m.policeReportNumber }} }</dd>
                    <dt>Details</dt><dd>{{ m.thirdPartyInvolved ? 'Other party involved' : 'No other party' }} · {{ m.drivable ? 'Drivable' : 'Not drivable' }}@if (m.injuryInvolved) { · <span class="chip chip-bad">Injury</span> }</dd>
                  }
                  @if (c.property; as p) {
                    <dt>Property</dt><dd>{{ p.propertyAddress || '—' }}</dd>
                    <dt>Cause</dt><dd>{{ p.damageCause }} · {{ p.habitable ? 'Home is safe to live in' : 'Home not habitable' }}</dd>
                  }
                  <dt>Your cover</dt><dd>{{ c.policy.productName }} · up to {{ c.policy.coverageLimit | money: c.currency }} · excess {{ c.policy.deductible | money: c.currency }}</dd>
                  <dt>Handled by</dt><dd>{{ c.assignedOfficerName || 'Waiting for an officer to pick it up' }}</dd>
                </dl>
              </div>
            </div>

            @if (answered(c).length) {
              <div class="card">
                <div class="card-head"><span class="card-title">Questions & answers</span></div>
                <div class="card-pad col">
                  @for (r of answered(c); track r.id) {
                    <div class="qa"><div class="qq"><b>{{ r.requestedByName }}</b> asked · {{ r.requestedAt | ago }}<div>{{ r.question }}</div></div>
                      <div class="aa"><b>You</b> replied · {{ r.respondedAt | ago }}<div>{{ r.response }}</div></div></div>
                  }
                </div>
              </div>
            }
          </div>

          <div class="card">
            <div class="card-head"><span class="card-title">Progress</span></div>
            <div class="card-pad"><cf-timeline [entries]="c.timeline" /></div>
          </div>
        </div>

        @if (withdrawing()) {
          <cf-modal title="Withdraw this claim?" (closed)="withdrawing.set(false)">
            <p class="sub">We'll stop working on <b>{{ c.claimNumber }}</b>. You can always report a new claim later.</p>
            <div class="field"><label class="label" for="wr">Reason <span class="muted">(optional)</span></label>
              <textarea id="wr" class="textarea" [ngModel]="reason()" (ngModelChange)="reason.set($event)"></textarea></div>
            <ng-container footer>
              <button class="btn" type="button" (click)="withdrawing.set(false)">Keep my claim</button>
              <button class="btn btn-danger" type="button" [disabled]="busy()" (click)="withdraw(c)">Withdraw claim</button>
            </ng-container>
          </cf-modal>
        }
      } @else if (missing()) {
        <div class="card"><cf-empty icon="search" title="Claim not found" text="It may belong to someone else, or the link is wrong.">
          <a class="btn mt-2" routerLink="/my/claims">Back to my claims</a></cf-empty></div>
      } @else {
        <div class="skel" style="height:90px"></div><div class="skel mt-3" style="height:200px"></div>
      }
    </div>`,
  styles: [`
    .back{display:inline-flex;align-items:center;gap:6px;margin-bottom:14px;font-weight:600;color:var(--ink-2)}
    .ask{border-color:#f3d79c;background:linear-gradient(180deg,#fffaf0,#fff);padding:20px;box-shadow:0 10px 30px rgba(199,106,0,.12)}
    .qi{width:48px;height:48px;border-radius:14px;background:#fff1d6;color:#c76a00;display:flex;align-items:center;justify-content:center;flex:none}
    .q{font-size:16.5px;font-weight:600;margin-top:6px;line-height:1.4}
    .decision{padding:22px}.decision.ok{border-color:#a8e6cb;background:linear-gradient(180deg,#f2fdf8,#fff)}.decision.bad{border-color:#f4bcbc;background:linear-gradient(180deg,#fff6f6,#fff)}
    .di{width:52px;height:52px;border-radius:16px;display:flex;align-items:center;justify-content:center;flex:none}.ok .di{background:#d9f7ea;color:#059669}.bad .di{background:#fde4e4;color:#dc2626}
    .big{font-size:34px;font-weight:800;letter-spacing:-.02em;margin:2px 0}
    .calc{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin-top:18px}.calc div{background:#fff;border:1px solid var(--line);border-radius:12px;padding:11px 14px;display:flex;flex-direction:column}.calc span{color:var(--ink-3);font-size:12px;font-weight:600}.calc .tot{background:#059669;border-color:#059669;color:#fff}.calc .tot span{color:#c7f3e0}
    @media (max-width:700px){.calc{grid-template-columns:1fr 1fr}}
    .qa{display:flex;flex-direction:column;gap:8px}.qq,.aa{padding:11px 14px;border-radius:12px;font-size:13px;color:var(--ink-3)}.qq div,.aa div{color:var(--ink);font-size:14px;margin-top:3px}.qq{background:#f4f6fc}.aa{background:#eef0ff;margin-left:26px}
  `],
})
export class ClaimantDetail {
  private readonly api = inject(Api);
  private readonly toasts = inject(Toasts);

  readonly id = input.required<string>();
  protected readonly claim = signal<ClaimDetail | null>(null);
  protected readonly missing = signal(false);
  protected readonly reply = signal('');
  protected readonly reason = signal('');
  protected readonly busy = signal(false);
  protected readonly withdrawing = signal(false);
  protected readonly openRequest = (c: ClaimDetail) => c.informationRequests.find(r => !r.respondedAt) ?? null;
  protected readonly answered = (c: ClaimDetail) => c.informationRequests.filter(r => r.respondedAt);

  constructor() {
    effect(() => this.load(this.id()));
    const t = setInterval(() => this.load(this.id(), true), 5000);
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }

  private load(id: string, silent = false) {
    this.api.claim(id).subscribe({
      next: c => this.claim.set(c),
      error: e => { if (!silent) this.missing.set(true); void e; },
    });
  }

  protected send(c: ClaimDetail, requestId: string) {
    this.busy.set(true);
    this.api.respond(c.id, requestId, this.reply().trim()).subscribe({
      next: updated => { this.claim.set(updated); this.reply.set(''); this.busy.set(false); this.toasts.success('Thanks! Your reply has been sent.'); },
      error: e => { this.busy.set(false); this.toasts.error(errorText(e)); },
    });
  }

  protected withdraw(c: ClaimDetail) {
    this.busy.set(true);
    this.api.withdraw(c.id, this.reason().trim() || undefined).subscribe({
      next: updated => { this.claim.set(updated); this.busy.set(false); this.withdrawing.set(false); this.toasts.info('Your claim has been withdrawn.'); },
      error: e => { this.busy.set(false); this.toasts.error(errorText(e)); },
    });
  }
}
