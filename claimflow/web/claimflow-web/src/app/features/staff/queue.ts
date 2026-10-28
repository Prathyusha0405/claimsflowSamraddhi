import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { ClaimSummary, ClaimType, Market, Priority } from '../../core/models';
import { dur, errorText, MARKET_NAME, MARKETS, MoneyPipe, Toasts } from '../../core/ui';
import { Icon } from '../../shared/icon';
import { EmptyState, MarketChip, PriorityTag, SlaChip, TypeIcon } from '../../shared/widgets';

@Component({
  selector: 'cf-queue',
  imports: [RouterLink, Icon, TypeIcon, MarketChip, PriorityTag, SlaChip, EmptyState, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-head fade-in">
        <div><h1>Claim queue</h1><p>Unassigned claims — most urgent first, then oldest. Pick one up to make it yours.</p></div>
        <button class="btn" type="button" (click)="reload()"><cf-icon name="refresh" [size]="16" /> Refresh</button>
      </div>

      <div class="grid g4">
        <div class="card card-pad stat"><span class="ic" style="background:#e8ecff;color:#4f46e5"><cf-icon name="inbox" /></span><div><b>{{ items()?.length ?? '–' }}</b><small>Waiting for an officer</small></div></div>
        <div class="card card-pad stat"><span class="ic" style="background:#fde4e4;color:#dc2626"><cf-icon name="alert" /></span><div><b>{{ urgent() }}</b><small>Urgent / injury / unsafe</small></div></div>
        <div class="card card-pad stat"><span class="ic" style="background:#fff1d6;color:#c76a00"><cf-icon name="clock" /></span><div><b>{{ breached() }}</b><small>Past first-touch SLA</small></div></div>
        <div class="card card-pad stat"><span class="ic" style="background:#d9f7ea;color:#059669"><cf-icon name="trend" /></span><div><b>{{ oldest() }}</b><small>Longest wait</small></div></div>
      </div>

      <div class="filters card card-pad mt-3">
        <div class="fg"><span class="label">Market</span>
          <div class="row gap-1 wrap"><button type="button" class="pill-btn" [class.on]="market() === ''" (click)="market.set('')">All</button>
            @for (m of markets; track m) { <button type="button" class="pill-btn" [class.on]="market() === m" (click)="market.set(m)" [title]="names[m]">{{ m }}</button> }</div></div>
        <div class="fg"><span class="label">Type</span>
          <div class="row gap-1"><button type="button" class="pill-btn" [class.on]="type() === ''" (click)="type.set('')">All</button>
            <button type="button" class="pill-btn" [class.on]="type() === 'Motor'" (click)="type.set('Motor')">Motor</button>
            <button type="button" class="pill-btn" [class.on]="type() === 'Property'" (click)="type.set('Property')">Property</button></div></div>
        <div class="fg"><span class="label">Priority</span>
          <div class="row gap-1"><button type="button" class="pill-btn" [class.on]="priority() === ''" (click)="priority.set('')">All</button>
            <button type="button" class="pill-btn" [class.on]="priority() === 'Urgent'" (click)="priority.set('Urgent')">Urgent</button>
            <button type="button" class="pill-btn" [class.on]="priority() === 'High'" (click)="priority.set('High')">High</button>
            <button type="button" class="pill-btn" [class.on]="priority() === 'Normal'" (click)="priority.set('Normal')">Normal</button></div></div>
      </div>

      <div class="col mt-3">
        @if (items() === null) {
          <div class="skel" style="height:84px"></div><div class="skel" style="height:84px"></div><div class="skel" style="height:84px"></div>
        } @else if (items()!.length === 0) {
          <div class="card"><cf-empty icon="check-circle" title="Queue is clear" text="Nothing is waiting for an officer with these filters. Nice work!" /></div>
        } @else {
          @for (c of items(); track c.id; let i = $index) {
            <div class="card item fade-in" [class.urgent]="c.priority === 'Urgent'" [class.high]="c.priority === 'High'" [style.animation-delay.ms]="i * 30">
              <cf-type [type]="c.type" [size]="44" />
              <div class="grow">
                <div class="row gap-2 wrap">
                  <a class="num" [routerLink]="['/staff/claims', c.id]">{{ c.claimNumber }}</a>
                  <cf-market [market]="c.market" /><cf-priority [priority]="c.priority" /><cf-sla [sla]="c.sla" />
                </div>
                <div class="meta">{{ c.claimantName }} · {{ c.type }} · waiting {{ wait(c) }}</div>
              </div>
              <div class="amt"><b>{{ c.claimedAmount | money: c.currency : true }}</b><small>claimed</small></div>
              <button class="btn btn-primary" type="button" [disabled]="busyId() !== null" (click)="pickup(c)">
                {{ busyId() === c.id ? 'Picking up…' : 'Pick up' }} <cf-icon name="arrow-right" [size]="15" />
              </button>
            </div>
          }
        }
      </div>
    </div>`,
  styles: [`
    .stat{display:flex;align-items:center;gap:14px}.stat b{font-size:26px;display:block;line-height:1}.stat small{color:var(--ink-3);font-weight:600}
    .ic{width:44px;height:44px;border-radius:13px;display:flex;align-items:center;justify-content:center}
    .filters{display:flex;gap:28px;flex-wrap:wrap}.fg{display:flex;flex-direction:column;gap:8px}
    .item{display:flex;align-items:center;gap:16px;padding:15px 20px;border-left:5px solid #cdd5ec}.item.urgent{border-left-color:#ef4444}.item.high{border-left-color:#f59e0b}
    .num{font-weight:800;font-size:15px;color:var(--ink)}.meta{color:var(--ink-3);font-size:12.5px;margin-top:4px}
    .amt{text-align:right;display:flex;flex-direction:column}.amt small{color:var(--ink-3)}
  `],
})
export class Queue {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly toasts = inject(Toasts);

  protected readonly markets = MARKETS;
  protected readonly names = MARKET_NAME;
  protected readonly items = signal<ClaimSummary[] | null>(null);
  protected readonly market = signal<Market | ''>('');
  protected readonly type = signal<ClaimType | ''>('');
  protected readonly priority = signal<Priority | ''>('');
  protected readonly busyId = signal<string | null>(null);

  protected readonly urgent = computed(() => (this.items() ?? []).filter(c => c.priority === 'Urgent').length);
  protected readonly breached = computed(() => (this.items() ?? []).filter(c => c.sla.state === 'Breached').length);
  protected readonly oldest = computed(() => { const l = this.items() ?? []; return l.length ? dur(Math.max(...l.map(c => c.ageHours))) : '–'; });

  constructor() {
    effect(() => { this.market(); this.type(); this.priority(); this.reload(); });
    const t = setInterval(() => this.reload(), 8000);
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }

  protected wait(c: ClaimSummary) { return dur(c.ageHours); }

  protected reload() {
    this.api.queue({ market: this.market(), type: this.type(), priority: this.priority() }).subscribe({
      next: l => this.items.set(l), error: () => this.items.update(v => v ?? []),
    });
  }

  protected pickup(c: ClaimSummary) {
    this.busyId.set(c.id);
    this.api.pickup(c.id).subscribe({
      next: () => { this.busyId.set(null); this.toasts.success(`${c.claimNumber} is yours — let's get to work.`); void this.router.navigate(['/staff/claims', c.id]); },
      error: e => { this.busyId.set(null); this.toasts.error(errorText(e)); this.reload(); },   // e.g. someone else was faster → 409
    });
  }
}
