import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { OfficerWorkload, SlaBreach, StatusCount, TrendPoint } from '../../core/models';
import { dur, STATUS_COLOR, STATUS_LABEL, usd } from '../../core/ui';
import { AreaChart, BarRow, Donut, HBars, Series, Slice } from '../../shared/charts';
import { Icon } from '../../shared/icon';
import { Avatar, EmptyState, MarketChip, PriorityTag, StatusBadge } from '../../shared/widgets';

@Component({
  selector: 'cf-team',
  imports: [RouterLink, Icon, Avatar, Donut, HBars, AreaChart, StatusBadge, MarketChip, PriorityTag, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-head fade-in"><div><h1>Team &amp; SLA</h1><p>Workload, performance and anything running late — built from the event stream, so it updates within seconds.</p></div>
        <span class="chip chip-brand"><cf-icon name="radio" [size]="13" /> Live · from the reporting read model</span></div>

      <div class="grid g3">
        <div class="card"><div class="card-head"><span class="card-title">Claims by status</span></div>
          <div class="card-pad"><cf-donut [data]="slices()" [size]="150" [centerValue]="'' + total()" centerLabel="claims" /></div></div>
        <div class="card"><div class="card-head"><span class="card-title">Open claims per officer</span></div>
          <div class="card-pad">@if (bars().length) { <cf-hbars [rows]="bars()" /> } @else { <span class="muted">No assigned claims.</span> }</div></div>
        <div class="card"><div class="card-head"><span class="card-title">Last 14 days</span></div>
          <div class="card-pad"><cf-area [labels]="labels()" [series]="series()" /></div></div>
      </div>

      <div class="card mt-3">
        <div class="card-head"><span class="card-title">Officer workload &amp; performance</span></div>
        <div class="table-wrap"><table class="table">
          <thead><tr><th>Officer</th><th class="right">Open</th><th>Where their work sits</th><th class="right">SLA breaches</th><th class="right">Oldest</th><th class="right">Decided (30d)</th><th class="right">Approval rate</th><th class="right">Avg days to decide</th><th class="right">Exposure</th></tr></thead>
          <tbody>
            @for (w of workload(); track w.officerId) {
              <tr>
                <td><div class="row gap-2"><cf-avatar [name]="w.officerName" [size]="32" /><b>{{ w.officerName }}</b></div></td>
                <td class="right strong">{{ w.open }}</td>
                <td><div class="row gap-1 wrap">
                  <span class="chip" style="background:#ece9ff;color:#5b3fd6">{{ w.underReview }} review</span>
                  <span class="chip chip-warn">{{ w.awaitingInformation }} waiting</span>
                  <span class="chip" style="background:#f6e6ff;color:#9333ea">{{ w.assessed }} assessed</span>
                  <span class="chip chip-ok">{{ w.approvedPendingPayment }} to pay</span></div></td>
                <td class="right"><span class="chip" [class.chip-bad]="w.slaBreaches > 0" [class.chip-ok]="w.slaBreaches === 0">{{ w.slaBreaches }}</span></td>
                <td class="right mono">{{ w.oldestOpenHours === null || w.oldestOpenHours === undefined ? '—' : dur(w.oldestOpenHours) }}</td>
                <td class="right mono">{{ w.decidedLast30d }}</td>
                <td class="right mono">{{ pct(w.approvalRate) }}</td>
                <td class="right mono">{{ w.avgDaysToDecision ?? '—' }}</td>
                <td class="right mono">{{ usd(w.exposureUsd, true) }}</td>
              </tr>
            } @empty { <tr><td colspan="9"><cf-empty icon="users" title="No workload yet" text="Pick up claims from the queue and they'll show here." /></td></tr> }
          </tbody></table></div>
      </div>

      <div class="card mt-3">
        <div class="card-head"><span class="card-title">Claims past their SLA</span><span class="chip" [class.chip-bad]="breaches().length > 0" [class.chip-ok]="breaches().length === 0">{{ breaches().length }}</span></div>
        <div class="table-wrap">
          @if (breaches().length) {
            <table class="table"><thead><tr><th>Claim</th><th>Stage that is late</th><th>Status</th><th>Handler</th><th class="right">Overdue by</th></tr></thead><tbody>
              @for (b of breaches(); track b.claimId) {
                <tr class="clickable" [routerLink]="['/staff/claims', b.claimId]">
                  <td><b>{{ b.claimNumber }}</b> <cf-market [market]="b.market" /> <cf-priority [priority]="b.priority" /></td>
                  <td>{{ b.stage }}</td><td><cf-status [status]="b.status" /></td><td>{{ b.officerName || 'Unassigned' }}</td>
                  <td class="right strong" style="color:#dc2626">{{ dur(b.overdueHours) }}</td></tr>
              }</tbody></table>
          } @else { <cf-empty icon="check-circle" title="Everything is on track" text="No claim is past its service-level target." /> }
        </div>
      </div>
    </div>`,
})
export class Team {
  private readonly api = inject(Api);
  protected readonly workload = signal<OfficerWorkload[]>([]);
  protected readonly status = signal<StatusCount[]>([]);
  protected readonly trend = signal<TrendPoint[]>([]);
  protected readonly breaches = signal<SlaBreach[]>([]);
  protected readonly dur = dur;
  protected readonly usd = usd;

  protected readonly total = computed(() => this.status().reduce((s, x) => s + x.count, 0));
  protected readonly slices = computed<Slice[]>(() => this.status().map(s => ({ label: STATUS_LABEL[s.status], value: s.count, color: STATUS_COLOR[s.status] })));
  protected readonly bars = computed<BarRow[]>(() => this.workload().filter(w => w.open > 0).map(w => ({
    label: w.officerName, value: w.open, display: `${w.open} open`, sub: `${w.slaBreaches} breach${w.slaBreaches === 1 ? '' : 'es'} · exposure ${usd(w.exposureUsd, true)}`,
    color: w.slaBreaches > 1 ? 'linear-gradient(90deg,#f59e0b,#ef4444)' : undefined,
  })));
  protected readonly labels = computed(() => this.trend().map(t => new Date(t.date).toLocaleDateString('en-GB', { day: '2-digit', month: 'short' })));
  protected readonly series = computed<Series[]>(() => [
    { name: 'Submitted', color: '#4f46e5', values: this.trend().map(t => t.submitted) },
    { name: 'Decided', color: '#10b981', values: this.trend().map(t => t.decided) },
  ]);

  constructor() {
    const load = () => {
      this.api.workload().subscribe({ next: w => this.workload.set(w), error: () => {} });
      this.api.statusBreakdown().subscribe({ next: s => this.status.set(s), error: () => {} });
      this.api.trend(14).subscribe({ next: t => this.trend.set(t), error: () => {} });
      this.api.slaBreaches().subscribe({ next: b => this.breaches.set(b), error: () => {} });
    };
    load();
    const t = setInterval(load, 8000);
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }

  protected pct(v?: number | null) { return v === null || v === undefined ? '—' : Math.round(v * 100) + '%'; }
}
