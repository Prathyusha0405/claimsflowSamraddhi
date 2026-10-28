import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { ClaimStatus, ClaimSummary, Policy } from '../../core/models';
import { AgoPipe, DayPipe, MoneyPipe } from '../../core/ui';
import { Icon } from '../../shared/icon';
import { EmptyState, PriorityTag, StatusBadge, TypeIcon } from '../../shared/widgets';

const PROGRESS: Record<ClaimStatus, number> = {
  Submitted: 12, UnderReview: 38, AwaitingInformation: 38, Assessed: 62, Approved: 86, Settled: 100, Rejected: 100, Withdrawn: 100,
};
const BAR: Record<ClaimStatus, string> = {
  Submitted: '#3b82f6', UnderReview: '#7c5cf0', AwaitingInformation: '#f59e0b', Assessed: '#a855f7', Approved: '#10b981', Settled: '#14b8a6', Rejected: '#ef4444', Withdrawn: '#94a3b8',
};

@Component({
  selector: 'cf-my-claims',
  imports: [RouterLink, Icon, StatusBadge, TypeIcon, PriorityTag, MoneyPipe, AgoPipe, DayPipe, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-head fade-in">
        <div>
          <h1>Hi {{ first() }} 👋</h1>
          <p>Here's where your claims stand today.</p>
        </div>
        <a class="btn btn-primary" routerLink="/my/claims/new"><cf-icon name="plus" [size]="17" /> Report an incident</a>
      </div>

      <div class="grid g4">
        <div class="card card-pad stat fade-in"><span class="ic" style="background:#e8ecff;color:#4f46e5"><cf-icon name="file" /></span><div><b>{{ openCount() }}</b><small>Open claims</small></div></div>
        <div class="card card-pad stat fade-in" style="animation-delay:.05s"><span class="ic" style="background:#fff1d6;color:#c76a00"><cf-icon name="message" /></span><div><b>{{ needsReply().length }}</b><small>Need your reply</small></div></div>
        <div class="card card-pad stat fade-in" style="animation-delay:.1s"><span class="ic" style="background:#d9f7ea;color:#059669"><cf-icon name="check-circle" /></span><div><b>{{ paidCount() }}</b><small>Paid out</small></div></div>
        <div class="card card-pad stat fade-in" style="animation-delay:.15s"><span class="ic" style="background:#dff1fd;color:#0369a1"><cf-icon name="shield" /></span><div><b>{{ policies().length }}</b><small>Active policies</small></div></div>
      </div>

      @for (c of needsReply(); track c.id) {
        <a class="banner banner-warn mt-3 fade-in" [routerLink]="['/my/claims', c.id]" style="text-decoration:none">
          <cf-icon name="message" [size]="22" />
          <div class="grow"><b>{{ c.claimNumber }} needs your reply</b><div>Your claims officer has asked a question — answering quickly keeps your claim moving.</div></div>
          <span class="btn btn-sm" style="background:#fff">Reply now <cf-icon name="arrow-right" [size]="15" /></span>
        </a>
      }

      <h3 class="mt-4 mb-2">Your claims</h3>
      @if (claims() === null) {
        <div class="col"><div class="skel" style="height:96px"></div><div class="skel" style="height:96px"></div></div>
      } @else if (claims()!.length === 0) {
        <div class="card"><cf-empty icon="shield" title="No claims yet" text="Something happened? Report it in about two minutes — we'll keep you updated at every step.">
          <a class="btn btn-primary mt-2" routerLink="/my/claims/new"><cf-icon name="plus" [size]="16" /> Report an incident</a></cf-empty></div>
      } @else {
        <div class="col">
          @for (c of claims(); track c.id; let i = $index) {
            <a class="card card-hover claim fade-in" [routerLink]="['/my/claims', c.id]" [style.animation-delay.ms]="i * 40">
              <cf-type [type]="c.type" [size]="46" />
              <div class="grow">
                <div class="row gap-2 wrap"><b class="num">{{ c.claimNumber }}</b><cf-status [status]="c.status" /><cf-priority [priority]="c.priority" /></div>
                <div class="meta">{{ c.type }} claim · incident {{ c.incidentDate | d }} · updated {{ c.updatedAt | ago }}</div>
                <div class="track"><div class="fill" [style.width.%]="progress(c.status)" [style.background]="bar(c.status)"></div></div>
              </div>
              <div class="amt"><b>{{ c.claimedAmount | money: c.currency }}</b><small>claimed</small></div>
              <cf-icon name="chevron-right" class="muted" />
            </a>
          }
        </div>
      }

      <h3 class="mt-4 mb-2">Your policies</h3>
      <div class="grid g3">
        @for (p of policies(); track p.policyNumber) {
          <div class="card card-pad pol">
            <div class="row spread"><cf-type [type]="p.product" [size]="36" /><span class="chip" [class.chip-ok]="p.inForce" [class.chip-bad]="!p.inForce">{{ p.inForce ? 'In force' : p.status }}</span></div>
            <b class="mt-2" style="display:block">{{ p.productName }}</b>
            <div class="muted mono">{{ p.policyNumber }}</div>
            <div class="asset">{{ p.assetDescription }}</div>
            <div class="row spread mt-2"><span class="muted">Cover up to</span><b>{{ p.coverageLimit | money: p.currency : true }}</b></div>
            <div class="row spread"><span class="muted">Excess</span><b>{{ p.deductible | money: p.currency }}</b></div>
          </div>
        }
      </div>
    </div>`,
  styles: [`
    .stat{display:flex;align-items:center;gap:14px}.stat b{font-size:26px;display:block;line-height:1}.stat small{color:var(--ink-3);font-weight:600}
    .ic{width:44px;height:44px;border-radius:13px;display:flex;align-items:center;justify-content:center}
    .claim{display:flex;align-items:center;gap:16px;padding:16px 20px;color:inherit;text-decoration:none!important}
    .num{font-size:15px}.meta{color:var(--ink-3);font-size:12.5px;margin:3px 0 10px}
    .track{height:6px;border-radius:99px;background:#eef1f8;overflow:hidden}.fill{height:100%;border-radius:99px;transition:width .8s}
    .amt{text-align:right;display:flex;flex-direction:column}.amt small{color:var(--ink-3)}
    .asset{color:var(--ink-2);font-size:12.5px;margin-top:2px}
  `],
})
export class MyClaims {
  private readonly api = inject(Api);
  private readonly auth = inject(Auth);

  protected readonly claims = signal<ClaimSummary[] | null>(null);
  protected readonly policies = signal<Policy[]>([]);
  protected readonly first = computed(() => this.auth.user()?.name.split(' ')[0] ?? '');
  protected readonly needsReply = computed(() => (this.claims() ?? []).filter(c => c.awaitingClaimant));
  protected readonly openCount = computed(() => (this.claims() ?? []).filter(c => !['Rejected', 'Settled', 'Withdrawn'].includes(c.status)).length);
  protected readonly paidCount = computed(() => (this.claims() ?? []).filter(c => c.status === 'Settled').length);

  constructor() {
    const load = () => this.api.myClaims().subscribe({ next: c => this.claims.set(c), error: () => this.claims.update(v => v ?? []) });
    load();
    this.api.myPolicies().subscribe({ next: p => this.policies.set(p.filter(x => x.inForce)), error: () => {} });
    const t = setInterval(load, 6000);   // statuses change while officers work — keep the page live
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }

  protected progress(s: ClaimStatus) { return PROGRESS[s]; }
  protected bar(s: ClaimStatus) { return BAR[s]; }
}
