import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { ServiceHealth, SystemHealth } from '../../core/models';
import { Icon } from '../../shared/icon';
import { EventFeed } from '../../shared/event-feed';

const ROLE: Record<string, string> = {
  identity: 'Sign-in & JWT tokens', policy: 'Policies · sync coverage check (REST)', claims: 'Core domain · state machine · outbox',
  notifications: 'Event subscriber → in-app / e-mail', reporting: 'Event subscriber → read model (CQRS)',
};

@Component({
  selector: 'cf-system',
  imports: [Icon, EventFeed],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-head fade-in"><div><h1>System &amp; events</h1><p>Every service, how it's healthy, and how events travel between them right now.</p></div>
        <span class="chip" [class.chip-ok]="allUp()" [class.chip-bad]="!allUp()"><span class="dot" [class.ok]="allUp()" [class.bad]="!allUp()"></span> {{ allUp() ? 'All services healthy' : 'Some services are down' }}</span></div>

      <div class="card card-pad flow fade-in">
        <div class="row spread wrap gap-3">
          <div><div class="card-title">Event transport</div><h2 style="margin-top:4px">{{ mode() === 'Kafka' ? 'Apache Kafka' : 'Direct HTTP fan-out' }}</h2>
            <p class="sub" style="max-width:560px;margin-top:4px">{{ mode() === 'Kafka'
              ? 'Claims writes events to its outbox, then publishes them to the claims.events topic (keyed by claim id). Notifications and Reporting each consume with their own consumer group.'
              : 'Claims writes events to its outbox, then the dispatcher POSTs each one to every subscriber. No broker to install — same envelope, same delivery guarantees.' }}</p></div>
          <span class="mode" [class.k]="mode() === 'Kafka'">{{ mode() === 'Kafka' ? 'KAFKA' : 'HTTP' }}</span>
        </div>
        <div class="diagram">
          <div class="node a"><cf-icon name="briefcase" [size]="20" /><b>Claims</b><small>state change + outbox row in ONE transaction</small></div>
          <div class="arrow"><span>outbox dispatcher</span></div>
          <div class="node bus" [class.k]="mode() === 'Kafka'"><cf-icon name="radio" [size]="20" /><b>{{ mode() === 'Kafka' ? 'claims.events' : 'HTTP POST' }}</b><small>{{ mode() === 'Kafka' ? 'topic · 3 partitions · key = claimId' : '/internal/events on each subscriber' }}</small></div>
          <div class="arrow"><span>{{ mode() === 'Kafka' ? 'consumer groups' : 'fan-out' }}</span></div>
          <div class="subs">
            <div class="node"><cf-icon name="bell" [size]="18" /><b>Notifications</b><small>idempotent handler</small></div>
            <div class="node"><cf-icon name="chart" [size]="18" /><b>Reporting</b><small>version-guarded projection</small></div>
          </div>
        </div>
        <div class="hint mt-2">Switch modes without code changes: set <code>Messaging:Provider</code> to <code>Http</code> or <code>Kafka</code> (see README → “Run with or without Kafka”).</div>
      </div>

      <h3 class="mt-4 mb-2">Services</h3>
      <div class="grid g3">
        @for (s of services(); track s.name) {
          <div class="card card-pad svc fade-in">
            <div class="row spread"><div class="row gap-2"><span class="dot" [class.ok]="s.status === 'Healthy'" [class.bad]="s.status !== 'Healthy'"></span><b class="nm">{{ s.name }}</b></div>
              <span class="chip" [class.chip-ok]="s.status === 'Healthy'" [class.chip-bad]="s.status !== 'Healthy'">{{ s.status }}</span></div>
            <div class="muted" style="margin:4px 0 10px">{{ role(s.name) }}</div>
            <div class="row spread"><span class="muted">Port</span><span class="mono">{{ port(s) }}</span></div>
            <div class="row spread"><span class="muted">Latency</span><span class="mono">{{ s.latencyMs }} ms</span></div>
            <div class="row spread"><span class="muted">Uptime</span><span class="mono">{{ uptime(s) }}</span></div>
            @if (s.details?.checks?.messaging; as m) {
              <div class="divider" style="margin:10px 0"></div>
              @if (s.name === 'claims') {
                <div class="row spread"><span class="muted">Outbox waiting</span><span class="chip" [class.chip-warn]="m.outboxPending > 0" [class.chip-ok]="m.outboxPending === 0">{{ m.outboxPending }}</span></div>
                <div class="row spread"><span class="muted">Events published</span><span class="mono">{{ m.published }}</span></div>
              } @else {
                <div class="row spread"><span class="muted">Events consumed</span><span class="mono">{{ m.consumed }}</span></div>
              }
              @if (m.failed > 0) { <div class="row spread"><span class="muted">Delivery retries</span><span class="mono" style="color:#c76a00">{{ m.failed }}</span></div> }
              @if (m.lastError) { <div class="hint" style="color:#c76a00;margin-top:6px">⚠ {{ m.lastError }}</div> }
            }
          </div>
        } @empty { <div class="skel" style="height:140px"></div><div class="skel" style="height:140px"></div><div class="skel" style="height:140px"></div> }
      </div>

      @if (auth.isStaff()) {
        <div class="card mt-4"><div class="card-head"><span class="card-title">Live event stream (as consumed by Reporting)</span><span class="chip chip-brand"><cf-icon name="radio" [size]="12" /> Do something in another tab and watch it appear</span></div>
          <div class="card-pad"><cf-event-feed [limit]="12" /></div></div>
      }
    </div>`,
  styles: [`
    .flow{overflow:hidden}.mode{font-size:13px;font-weight:800;letter-spacing:.12em;padding:8px 16px;border-radius:99px;background:var(--brand-soft);color:var(--brand)}.mode.k{background:#d9f7ea;color:#059669}
    .diagram{display:flex;align-items:center;gap:10px;margin-top:20px;flex-wrap:wrap}
    .node{display:flex;flex-direction:column;gap:3px;padding:14px 16px;border-radius:14px;border:1.5px solid var(--line);background:#fafbff;min-width:150px;max-width:230px}.node small{color:var(--ink-3);font-size:12px}
    .node.a{border-color:#c7ccf7;background:var(--brand-soft)}.node.bus{border-color:#a5b0ff;background:#f1f2ff}.node.bus.k{border-color:#8de0bf;background:#effbf6}
    .arrow{display:flex;flex-direction:column;align-items:center;color:var(--ink-3);font-size:11.5px;min-width:90px}.arrow::after{content:'';display:block;width:100%;height:2px;background:repeating-linear-gradient(90deg,#b4bde6 0 6px,transparent 6px 11px);margin-top:4px;animation:flowx 1s linear infinite}
    @keyframes flowx{to{background-position:11px 0}}
    .subs{display:flex;flex-direction:column;gap:8px}
    .svc .nm{text-transform:capitalize;font-size:16px}
  `],
})
export class System {
  private readonly api = inject(Api);
  protected readonly auth = inject(Auth);
  protected readonly health = signal<SystemHealth | null>(null);
  protected readonly services = computed(() => this.health()?.services ?? []);
  protected readonly allUp = computed(() => this.services().every(s => s.status === 'Healthy'));
  protected readonly mode = computed(() => (this.health()?.messagingProvider === 'Kafka' ? 'Kafka' : 'Http'));

  constructor() {
    const load = () => this.api.health().subscribe({ next: h => this.health.set(h), error: () => {} });
    load();
    const t = setInterval(load, 3000);
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }

  protected role(n: string) { return ROLE[n] ?? ''; }
  protected port(s: ServiceHealth) { return s.url.split(':').pop(); }
  protected uptime(s: ServiceHealth) {
    const sec = s.details?.uptimeSeconds ?? 0;
    return sec < 120 ? `${sec}s` : sec < 7200 ? `${Math.round(sec / 60)}m` : `${Math.round(sec / 3600)}h`;
  }
}
