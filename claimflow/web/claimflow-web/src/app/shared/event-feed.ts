import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, signal } from '@angular/core';
import { Api } from '../core/api';
import { RecentEvent } from '../core/models';
import { AgoPipe } from '../core/ui';
import { Icon } from './icon';

const META: Record<string, { label: string; icon: string; color: string }> = {
  'claim.submitted': { label: 'Claim submitted', icon: 'plus', color: '#3b82f6' },
  'claim.assigned': { label: 'Assigned', icon: 'briefcase', color: '#7c5cf0' },
  'claim.information-requested': { label: 'Info requested', icon: 'message', color: '#f59e0b' },
  'claim.information-provided': { label: 'Claimant replied', icon: 'send', color: '#0ea5e9' },
  'claim.assessed': { label: 'Assessed', icon: 'file', color: '#a855f7' },
  'claim.approved': { label: 'Approved', icon: 'check-circle', color: '#10b981' },
  'claim.rejected': { label: 'Declined', icon: 'x-circle', color: '#ef4444' },
  'claim.settled': { label: 'Paid', icon: 'dollar', color: '#14b8a6' },
  'claim.withdrawn': { label: 'Withdrawn', icon: 'x', color: '#94a3b8' },
};

/** Live view of the events the Reporting service has consumed — makes the event-driven architecture visible. */
@Component({
  selector: 'cf-event-feed',
  imports: [Icon, AgoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ul class="feed">
      @for (e of events(); track e.id) {
        <li [class.fresh]="fresh(e)">
          <span class="ei" [style.background]="meta(e).color + '22'" [style.color]="meta(e).color"><cf-icon [name]="meta(e).icon" [size]="15" /></span>
          <div class="grow"><div><b>{{ meta(e).label }}</b> <span class="mono muted">{{ e.claimNumber }}</span></div><div class="muted">{{ e.summary }}</div></div>
          <small class="muted nowrap">{{ e.receivedAt | ago }}</small>
        </li>
      } @empty { <li class="muted" style="justify-content:center;padding:24px">No events consumed yet…</li> }
    </ul>`,
  styles: [`
    .feed{list-style:none;margin:0;padding:0;display:flex;flex-direction:column}
    li{display:flex;align-items:center;gap:12px;padding:10px 4px;border-bottom:1px solid #f0f2f9;transition:background 1s}
    li:last-child{border-bottom:0}.fresh{background:linear-gradient(90deg,#eef0ff,transparent);animation:fadeUp .4s both}
    .ei{width:32px;height:32px;border-radius:10px;display:flex;align-items:center;justify-content:center;flex:none}
    .grow{min-width:0}.grow .muted{font-size:12.5px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
  `],
})
export class EventFeed {
  private readonly api = inject(Api);
  readonly limit = input(10);
  protected readonly events = signal<RecentEvent[]>([]);

  constructor() {
    const load = () => this.api.recentEvents(this.limit()).subscribe({ next: e => this.events.set(e), error: () => {} });
    load();
    const t = setInterval(load, 3000);
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }

  protected meta(e: RecentEvent) { return META[e.eventType] ?? { label: e.eventType, icon: 'activity', color: '#64748b' }; }
  protected fresh(e: RecentEvent) { return Date.now() - new Date(e.receivedAt).getTime() < 7000; }
}
