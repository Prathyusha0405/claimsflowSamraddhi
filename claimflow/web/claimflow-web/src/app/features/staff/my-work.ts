import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { ClaimStatus, ClaimSummary } from '../../core/models';
import { MoneyPipe, usd } from '../../core/ui';
import { Icon } from '../../shared/icon';
import { EmptyState, MarketChip, PriorityTag, SlaChip, TypeIcon } from '../../shared/widgets';

interface Col { key: ClaimStatus; title: string; icon: string; hint: string; color: string }

@Component({
  selector: 'cf-my-work',
  imports: [RouterLink, Icon, TypeIcon, MarketChip, PriorityTag, SlaChip, EmptyState, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-head fade-in">
        <div><h1>My work</h1><p>Everything assigned to you, grouped by what needs doing next. Status only changes through validated actions — open a claim to act.</p></div>
        <a class="btn btn-primary" routerLink="/staff/queue"><cf-icon name="inbox" [size]="16" /> Find more work</a>
      </div>

      <div class="grid g3">
        <div class="card card-pad"><div class="muted strong">Open claims</div><div class="kpi">{{ items()?.length ?? '–' }}</div></div>
        <div class="card card-pad"><div class="muted strong">Exposure you're handling</div><div class="kpi">{{ exposure() }}</div></div>
        <div class="card card-pad"><div class="muted strong">SLA breaches</div><div class="kpi" [style.color]="breached() ? '#dc2626' : '#059669'">{{ breached() }}</div></div>
      </div>

      @if (items() !== null && items()!.length === 0) {
        <div class="card mt-3"><cf-empty icon="briefcase" title="Nothing assigned to you yet" text="Pick up claims from the queue and they'll appear here.">
          <a class="btn btn-primary mt-2" routerLink="/staff/queue">Go to the queue</a></cf-empty></div>
      } @else {
        <div class="board mt-3">
          @for (col of cols; track col.key) {
            <section class="lane">
              <header><span class="li" [style.background]="col.color + '22'" [style.color]="col.color"><cf-icon [name]="col.icon" [size]="16" /></span>
                <b>{{ col.title }}</b><span class="cnt">{{ inCol(col.key).length }}</span></header>
              <small class="hint">{{ col.hint }}</small>
              <div class="cards">
                @for (c of inCol(col.key); track c.id) {
                  <a class="card kcard card-hover" [routerLink]="['/staff/claims', c.id]" [style.border-left-color]="col.color">
                    <div class="row spread"><b>{{ c.claimNumber }}</b><cf-type [type]="c.type" [size]="28" /></div>
                    <div class="who">{{ c.claimantName }} <cf-market [market]="c.market" /></div>
                    <div class="row spread mt-1"><b>{{ c.claimedAmount | money: c.currency : true }}</b><span class="row gap-1"><cf-priority [priority]="c.priority" /><cf-sla [sla]="c.sla" /></span></div>
                    @if (c.needsManagerApproval) { <div class="chip chip-warn mt-1"><cf-icon name="lock" [size]="12" /> Needs manager approval</div> }
                  </a>
                } @empty { <div class="none">Nothing here</div> }
              </div>
            </section>
          }
        </div>
      }
    </div>`,
  styles: [`
    .kpi{font-size:32px;font-weight:800;letter-spacing:-.02em;margin-top:4px}
    .board{display:grid;grid-template-columns:repeat(4,minmax(240px,1fr));gap:14px;overflow-x:auto;padding-bottom:6px}
    .lane{background:#e9edf8;border-radius:16px;padding:14px;display:flex;flex-direction:column;gap:6px;min-height:240px}
    .lane header{display:flex;align-items:center;gap:9px}.li{width:28px;height:28px;border-radius:9px;display:flex;align-items:center;justify-content:center}
    .cnt{margin-left:auto;background:#fff;border-radius:99px;padding:1px 10px;font-weight:700;font-size:12.5px}
    .hint{color:var(--ink-3);margin-bottom:6px}
    .cards{display:flex;flex-direction:column;gap:10px}
    .kcard{display:block;padding:13px 14px;border-left:4px solid;color:inherit;text-decoration:none!important;box-shadow:0 1px 2px rgba(15,27,61,.05)}
    .who{display:flex;align-items:center;gap:8px;color:var(--ink-2);margin-top:3px}
    .none{text-align:center;color:var(--ink-3);padding:26px 0;font-size:13px}
  `],
})
export class MyWork {
  private readonly api = inject(Api);
  protected readonly items = signal<ClaimSummary[] | null>(null);
  protected readonly cols: Col[] = [
    { key: 'UnderReview', title: 'Under review', icon: 'eye', hint: 'Assess, ask questions, or decide', color: '#7c5cf0' },
    { key: 'AwaitingInformation', title: 'Waiting on claimant', icon: 'message', hint: 'Paused until they reply', color: '#f59e0b' },
    { key: 'Assessed', title: 'Awaiting approval', icon: 'check-circle', hint: 'Assessed — approve or decline', color: '#a855f7' },
    { key: 'Approved', title: 'Ready to pay', icon: 'dollar', hint: 'Approved — issue the payment', color: '#10b981' },
  ];
  protected readonly exposure = computed(() => usd((this.items() ?? []).reduce((s, c) => s + c.reserveUsd, 0), true));
  protected readonly breached = computed(() => (this.items() ?? []).filter(c => c.sla.state === 'Breached').length);

  constructor() {
    const load = () => this.api.myWork().subscribe({ next: l => this.items.set(l), error: () => this.items.update(v => v ?? []) });
    load();
    const t = setInterval(load, 7000);
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }

  protected inCol(k: ClaimStatus) { return (this.items() ?? []).filter(c => c.status === k); }
}
