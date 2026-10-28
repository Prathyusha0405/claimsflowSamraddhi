import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { ClaimSummary, ExposureReport, OfficerWorkload, Overview, SlaBreach, StatusCount, TrendPoint } from '../../core/models';
import { dur, money, MoneyPipe, STATUS_COLOR, STATUS_LABEL, usd } from '../../core/ui';
import { AreaChart, BarRow, Donut, HBars, Series, Slice } from '../../shared/charts';
import { EventFeed } from '../../shared/event-feed';
import { Icon } from '../../shared/icon';
import { Avatar, EmptyState, MarketChip, PriorityTag, TypeIcon } from '../../shared/widgets';

const MARKET_COLORS: Record<string, string> = {
  SG: 'linear-gradient(90deg,#4f46e5,#7c3aed)', HK: 'linear-gradient(90deg,#0ea5e9,#4f46e5)', MY: 'linear-gradient(90deg,#14b8a6,#0ea5e9)',
  TH: 'linear-gradient(90deg,#f59e0b,#ef4444)', ID: 'linear-gradient(90deg,#ec4899,#8b5cf6)', AU: 'linear-gradient(90deg,#10b981,#14b8a6)',
};

@Component({
  selector: 'cf-dashboard',
  imports: [RouterLink, Icon, Avatar, TypeIcon, MarketChip, PriorityTag, EmptyState, Donut, HBars, AreaChart, EventFeed, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-head fade-in">
        <div><h1>Good {{ part() }}, {{ first() }}</h1><p>Your live picture of outstanding claims and liability across all six markets.</p></div>
        <span class="chip chip-brand"><cf-icon name="radio" [size]="13" /> Live · updated {{ asOf() }}</span>
      </div>

      @if (overview(); as o) {
        <div class="grid g3">
          <div class="card card-pad kpi hero fade-in">
            <span class="ic"><cf-icon name="dollar" [size]="22" /></span>
            <div><small>Outstanding exposure</small><b>{{ usd(o.exposureUsd, true) }}</b><span>reserves on {{ o.openClaims }} open claims · ≈ USD at demo FX rates</span></div>
          </div>
          <div class="card card-pad kpi fade-in" style="animation-delay:.05s"><span class="ic" style="background:#e8ecff;color:#4f46e5"><cf-icon name="file" [size]="22" /></span>
            <div><small>Open claims</small><b>{{ o.openClaims }}</b><span>{{ o.unassigned }} waiting for an officer · {{ o.awaitingInformation }} waiting on claimants</span></div></div>
          <div class="card card-pad kpi fade-in" style="animation-delay:.1s"><span class="ic" [style.background]="o.slaBreaches ? '#fde4e4' : '#d9f7ea'" [style.color]="o.slaBreaches ? '#dc2626' : '#059669'"><cf-icon name="clock" [size]="22" /></span>
            <div><small>SLA breaches</small><b [style.color]="o.slaBreaches ? '#dc2626' : '#059669'">{{ o.slaBreaches }}</b><span>claims past their service-level target</span></div></div>
        </div>
        <div class="grid g4 mt-2">
          <div class="card card-pad mini"><small>Awaiting approval</small><b>{{ o.awaitingApproval }}</b><span>{{ o.approvedPendingPayment }} approved, not yet paid</span></div>
          <div class="card card-pad mini"><small>Avg. time to decision</small><b>{{ o.avgDaysToDecision ?? '—' }}<em>{{ o.avgDaysToDecision === null || o.avgDaysToDecision === undefined ? '' : ' days' }}</em></b><span>first touch in {{ o.avgHoursToFirstAssignment ?? '—' }}h</span></div>
          <div class="card card-pad mini"><small>Approval rate</small><b>{{ rate(o.approvalRate) }}</b><span>{{ o.decidedLast30d }} decided in 30 days</span></div>
          <div class="card card-pad mini"><small>Paid out (30 days)</small><b>{{ usd(o.paidLast30dUsd, true) }}</b><span>settled claims</span></div>
        </div>
      } @else { <div class="grid g3"><div class="skel" style="height:110px"></div><div class="skel" style="height:110px"></div><div class="skel" style="height:110px"></div></div> }

      <div class="grid g2 mt-3">
        <div class="card"><div class="card-head"><span class="card-title">Liability exposure by market</span><span class="muted">reserve in local currency · bar = USD</span></div>
          <div class="card-pad">@if (exposureBars().length) { <cf-hbars [rows]="exposureBars()" /> } @else { <span class="muted">No open exposure.</span> }</div></div>
        <div class="card"><div class="card-head"><span class="card-title">Claims by status</span></div>
          <div class="card-pad"><cf-donut [data]="slices()" [size]="170" [centerValue]="'' + total()" centerLabel="claims" /></div></div>
      </div>

      <div class="grid g2 mt-3">
        <div class="card"><div class="card-head"><span class="card-title">Submitted vs decided · 14 days</span></div>
          <div class="card-pad"><cf-area [labels]="labels()" [series]="series()" /></div></div>
        <div class="card"><div class="card-head"><span class="card-title">Needs your approval</span><span class="chip chip-warn">{{ approvals().length }}</span></div>
          <div class="card-pad col gap-2">
            @for (c of approvals(); track c.id) {
              <a class="apr" [routerLink]="['/staff/claims', c.id]">
                <cf-type [type]="c.type" [size]="38" />
                <div class="grow"><b>{{ c.claimNumber }}</b> <cf-market [market]="c.market" /> <cf-priority [priority]="c.priority" /><div class="muted">{{ c.claimantName }} · assessed by {{ c.assignedOfficerName }}</div></div>
                <div class="right"><b>{{ c.reserve | money: c.currency : true }}</b><div class="muted">≈ {{ usd(c.reserveUsd, true) }}</div></div>
              </a>
            } @empty { <cf-empty icon="check-circle" title="Nothing waiting on you" text="Claims above an officer's authority will show up here." /> }
          </div></div>
      </div>

      <div class="grid g2 mt-3">
        <div class="card"><div class="card-head"><span class="card-title">Team workload</span><a routerLink="/staff/team">Details <cf-icon name="chevron-right" [size]="14" /></a></div>
          <div class="table-wrap"><table class="table"><thead><tr><th>Officer</th><th class="right">Open</th><th class="right">Breaches</th><th class="right">Exposure</th></tr></thead><tbody>
            @for (w of workload(); track w.officerId) {
              <tr><td><div class="row gap-2"><cf-avatar [name]="w.officerName" [size]="30" /><b>{{ w.officerName }}</b></div></td><td class="right strong">{{ w.open }}</td>
                <td class="right"><span class="chip" [class.chip-bad]="w.slaBreaches > 0" [class.chip-ok]="w.slaBreaches === 0">{{ w.slaBreaches }}</span></td><td class="right mono">{{ usd(w.exposureUsd, true) }}</td></tr>
            }</tbody></table></div></div>
        <div class="card"><div class="card-head"><span class="card-title">Live event stream</span><a routerLink="/system">How it works <cf-icon name="chevron-right" [size]="14" /></a></div>
          <div class="card-pad"><cf-event-feed [limit]="7" /></div></div>
      </div>

      @if (breaches().length) {
        <div class="card mt-3"><div class="card-head"><span class="card-title">Most overdue</span></div>
          <div class="table-wrap"><table class="table"><tbody>
            @for (b of breaches().slice(0, 5); track b.claimId) {
              <tr class="clickable" [routerLink]="['/staff/claims', b.claimId]"><td><b>{{ b.claimNumber }}</b> <cf-market [market]="b.market" /></td><td>{{ b.stage }}</td><td>{{ b.officerName || 'Unassigned' }}</td><td class="right strong" style="color:#dc2626">{{ dur(b.overdueHours) }} overdue</td></tr>
            }</tbody></table></div></div>
      }
    </div>`,
  styles: [`
    .kpi{display:flex;gap:16px;align-items:center}.kpi small{display:block;color:var(--ink-3);font-weight:700;text-transform:uppercase;letter-spacing:.07em;font-size:11.5px}
    .kpi b{display:block;font-size:34px;letter-spacing:-.02em;line-height:1.1;margin:3px 0}.kpi span:not(.ic){color:var(--ink-3);font-size:12.5px}
    .ic{width:52px;height:52px;border-radius:15px;display:flex;align-items:center;justify-content:center;flex:none}
    .hero{background:linear-gradient(135deg,#312e81,#6d28d9);color:#fff;border:0}.hero small,.hero span:not(.ic){color:#d8d4ff!important}.hero .ic{background:rgba(255,255,255,.16);color:#fff}
    .mini small{display:block;color:var(--ink-3);font-weight:700;font-size:12px}.mini b{display:block;font-size:26px;margin:4px 0 2px}.mini b em{font-size:14px;font-style:normal;color:var(--ink-3)}.mini span{color:var(--ink-3);font-size:12.5px}
    .apr{display:flex;align-items:center;gap:12px;padding:10px 12px;border:1px solid var(--line);border-radius:12px;color:inherit;text-decoration:none!important;transition:.15s}.apr:hover{border-color:var(--brand);background:#fafaff}
  `],
})
export class Dashboard {
  private readonly api = inject(Api);
  private readonly auth = inject(Auth);

  protected readonly overview = signal<Overview | null>(null);
  protected readonly exposure = signal<ExposureReport | null>(null);
  protected readonly status = signal<StatusCount[]>([]);
  protected readonly trend = signal<TrendPoint[]>([]);
  protected readonly workload = signal<OfficerWorkload[]>([]);
  protected readonly breaches = signal<SlaBreach[]>([]);
  protected readonly approvals = signal<ClaimSummary[]>([]);
  protected readonly usd = usd;
  protected readonly dur = dur;

  protected readonly first = computed(() => this.auth.user()?.name.split(' ')[0] ?? '');
  protected readonly part = computed(() => { const h = new Date().getHours(); return h < 12 ? 'morning' : h < 18 ? 'afternoon' : 'evening'; });
  protected readonly asOf = computed(() => (this.overview() ? new Date(this.overview()!.asOf).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', second: '2-digit' }) : '…'));
  protected readonly total = computed(() => this.status().reduce((s, x) => s + x.count, 0));
  protected readonly slices = computed<Slice[]>(() => this.status().map(s => ({ label: STATUS_LABEL[s.status], value: s.count, color: STATUS_COLOR[s.status] })));
  protected readonly exposureBars = computed<BarRow[]>(() => (this.exposure()?.byMarket ?? []).map(m => ({
    label: m.label, value: m.reserveUsd, display: usd(m.reserveUsd, true),
    sub: `${money(m.reserve, m.currency, true)} · ${m.count} open claim${m.count === 1 ? '' : 's'}`, color: MARKET_COLORS[m.key],
  })));
  protected readonly labels = computed(() => this.trend().map(t => new Date(t.date).toLocaleDateString('en-GB', { day: '2-digit', month: 'short' })));
  protected readonly series = computed<Series[]>(() => [
    { name: 'Submitted', color: '#4f46e5', values: this.trend().map(t => t.submitted) },
    { name: 'Decided', color: '#10b981', values: this.trend().map(t => t.decided) },
  ]);

  constructor() {
    const load = () => {
      this.api.overview().subscribe({ next: v => this.overview.set(v), error: () => {} });
      this.api.exposure().subscribe({ next: v => this.exposure.set(v), error: () => {} });
      this.api.statusBreakdown().subscribe({ next: v => this.status.set(v), error: () => {} });
      this.api.trend(14).subscribe({ next: v => this.trend.set(v), error: () => {} });
      this.api.workload().subscribe({ next: v => this.workload.set(v), error: () => {} });
      this.api.slaBreaches().subscribe({ next: v => this.breaches.set(v), error: () => {} });
      this.api.search({ status: 'Assessed', pageSize: 50 }).subscribe({ next: r => this.approvals.set(r.items.filter(c => c.needsManagerApproval)), error: () => {} });
    };
    load();
    const t = setInterval(load, 6000);
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }

  protected rate(v?: number | null) { return v === null || v === undefined ? '—' : Math.round(v * 100) + '%'; }
}
