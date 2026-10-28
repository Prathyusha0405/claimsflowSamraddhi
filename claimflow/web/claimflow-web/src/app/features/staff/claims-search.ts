import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { ClaimStatus, ClaimSummary, ClaimType, Market, Paged, User } from '../../core/models';
import { AgoPipe, MARKETS, MARKET_NAME, MoneyPipe, STATUSES, STATUS_LABEL } from '../../core/ui';
import { Icon } from '../../shared/icon';
import { EmptyState, MarketChip, PriorityTag, SlaChip, StatusBadge, TypeIcon } from '../../shared/widgets';

@Component({
  selector: 'cf-claims-search',
  imports: [FormsModule, RouterLink, Icon, TypeIcon, StatusBadge, MarketChip, PriorityTag, SlaChip, EmptyState, MoneyPipe, AgoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-head fade-in">
        <div><h1>{{ status() === 'Assessed' ? 'Approvals' : 'All claims' }}</h1>
          <p>{{ status() === 'Assessed' ? 'Claims that have been assessed and are waiting for a decision.' : 'Search every claim across all markets.' }}</p></div>
      </div>

      <div class="card card-pad filters">
        <div class="field grow" style="min-width:220px"><label class="label" for="q">Search</label>
          <div class="input-group"><span class="prefix"><cf-icon name="search" [size]="16" /></span>
            <input id="q" class="input" placeholder="Claim no., claimant, policy…" [ngModel]="text()" (ngModelChange)="onText($event)" /></div></div>
        <div class="field"><label class="label" for="s">Status</label>
          <select id="s" class="select" [ngModel]="statusF()" (ngModelChange)="statusF.set($event); page.set(1)">
            <option value="">Any</option>@for (s of statuses; track s) { <option [value]="s">{{ labels[s] }}</option> }</select></div>
        <div class="field"><label class="label" for="m">Market</label>
          <select id="m" class="select" [ngModel]="marketF()" (ngModelChange)="marketF.set($event); page.set(1)">
            <option value="">All</option>@for (m of markets; track m) { <option [value]="m">{{ names[m] }}</option> }</select></div>
        <div class="field"><label class="label" for="t">Type</label>
          <select id="t" class="select" [ngModel]="typeF()" (ngModelChange)="typeF.set($event); page.set(1)">
            <option value="">All</option><option value="Motor">Motor</option><option value="Property">Property</option></select></div>
        <div class="field"><label class="label" for="o">Handler</label>
          <select id="o" class="select" [ngModel]="officerF()" (ngModelChange)="officerF.set($event); page.set(1)">
            <option value="">Anyone</option>@for (o of officers(); track o.id) { <option [value]="o.id">{{ o.name }}</option> }</select></div>
      </div>

      <div class="card mt-3">
        @if (result() === null) {
          <div class="card-pad col"><div class="skel" style="height:44px"></div><div class="skel" style="height:44px"></div><div class="skel" style="height:44px"></div></div>
        } @else if (result()!.items.length === 0) {
          <cf-empty icon="search" title="No claims match" text="Try clearing a filter or searching for something else." />
        } @else {
          <div class="table-wrap">
            <table class="table">
              <thead><tr><th>Claim</th><th>Status</th><th>Priority / SLA</th><th>Market</th><th class="right">Claimed</th><th>Handler</th><th>Updated</th></tr></thead>
              <tbody>
                @for (c of result()!.items; track c.id) {
                  <tr class="clickable" [routerLink]="['/staff/claims', c.id]">
                    <td><div class="row gap-2"><cf-type [type]="c.type" [size]="34" /><div><b>{{ c.claimNumber }}</b><div class="muted">{{ c.claimantName }}</div></div></div></td>
                    <td><cf-status [status]="c.status" />@if (c.needsManagerApproval) { <div class="chip chip-warn" style="margin-top:4px"><cf-icon name="lock" [size]="11" /> Manager</div> }</td>
                    <td><div class="row gap-1 wrap"><cf-priority [priority]="c.priority" /><cf-sla [sla]="c.sla" /></div></td>
                    <td><cf-market [market]="c.market" /></td>
                    <td class="right mono">{{ c.claimedAmount | money: c.currency : true }}</td>
                    <td>{{ c.assignedOfficerName || '—' }}</td>
                    <td class="muted nowrap">{{ c.updatedAt | ago }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <div class="row spread card-pad" style="border-top:1px solid var(--line)">
            <span class="muted">Showing {{ from() }}–{{ to() }} of {{ result()!.total }}</span>
            <div class="row gap-2">
              <button class="btn btn-sm" type="button" [disabled]="page() <= 1" (click)="page.set(page() - 1)"><cf-icon name="arrow-left" [size]="14" /> Prev</button>
              <button class="btn btn-sm" type="button" [disabled]="to() >= result()!.total" (click)="page.set(page() + 1)">Next <cf-icon name="arrow-right" [size]="14" /></button>
            </div>
          </div>
        }
      </div>
    </div>`,
  styles: [`.filters{display:flex;gap:14px;flex-wrap:wrap;align-items:flex-end}.filters .field:not(.grow){min-width:130px}`],
})
export class ClaimsSearch {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  /** Bound from the URL (?status=Assessed) via withComponentInputBinding(). */
  readonly status = input<string>();

  protected readonly statuses = STATUSES;
  protected readonly labels = STATUS_LABEL;
  protected readonly markets = MARKETS;
  protected readonly names = MARKET_NAME;
  protected readonly officers = signal<User[]>([]);
  protected readonly result = signal<Paged<ClaimSummary> | null>(null);

  protected readonly text = signal('');
  protected readonly statusF = signal<ClaimStatus | ''>('');
  protected readonly marketF = signal<Market | ''>('');
  protected readonly typeF = signal<ClaimType | ''>('');
  protected readonly officerF = signal('');
  protected readonly page = signal(1);
  private readonly debounced = signal('');
  private timer: ReturnType<typeof setTimeout> | undefined;

  protected readonly from = computed(() => ((this.result()?.page ?? 1) - 1) * (this.result()?.pageSize ?? 25) + 1);
  protected readonly to = computed(() => Math.min((this.result()?.total ?? 0), this.from() + (this.result()?.items.length ?? 0) - 1));

  constructor() {
    this.api.staff().subscribe({ next: s => this.officers.set(s.filter(u => u.role === 'ClaimsOfficer')), error: () => {} });
    effect(() => { const s = this.status(); untracked(() => { this.statusF.set((s ?? '') as ClaimStatus | ''); this.page.set(1); }); });
    effect(() => {
      const f = { status: this.statusF(), market: this.marketF(), type: this.typeF(), officerId: this.officerF(), q: this.debounced(), page: this.page(), pageSize: 25 };
      this.api.search(f).subscribe({ next: r => this.result.set(r), error: () => this.result.update(v => v ?? { items: [], total: 0, page: 1, pageSize: 25 }) });
    });
  }

  protected onText(v: string) {
    this.text.set(v);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => { this.debounced.set(v); this.page.set(1); }, 300);
  }
}
