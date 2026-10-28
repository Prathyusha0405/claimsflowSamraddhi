import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { ClaimStatus, ClaimType, Market, Priority, Sla, TimelineEntry } from '../core/models';
import { AgoPipe, hue, initials, MARKET_NAME, slaText, STATUS_COLOR, STATUS_LABEL } from '../core/ui';
import { Icon } from './icon';

@Component({
  selector: 'cf-status',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span [class]="'badge st-' + status()">{{ label() }}</span>`,
})
export class StatusBadge {
  readonly status = input.required<ClaimStatus>();
  protected readonly label = computed(() => STATUS_LABEL[this.status()]);
}

@Component({
  selector: 'cf-priority',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (priority() !== 'Normal') {
    <span class="chip" [class.chip-warn]="priority() === 'High'" [class.chip-bad]="priority() === 'Urgent'">
      <cf-icon [name]="priority() === 'Urgent' ? 'alert' : 'flag'" [size]="13" /> {{ priority() }}
    </span>
  }`,
})
export class PriorityTag { readonly priority = input.required<Priority>(); }

@Component({
  selector: 'cf-sla',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (sla().state !== 'None') {
    <span class="chip" [class.chip-bad]="sla().state === 'Breached'" [class.chip-warn]="sla().state === 'DueSoon'"
          [class.pulse]="sla().state === 'Breached'" [title]="text()">
      <cf-icon name="clock" [size]="13" /> {{ short() }}
    </span>
  }`,
})
export class SlaChip {
  readonly sla = input.required<Sla>();
  protected readonly text = computed(() => slaText(this.sla()));
  protected readonly short = computed(() => {
    const t = this.text();
    return this.sla().state === 'Breached' ? t.replace(/^.*overdue by /, 'Overdue ') : t.replace(/^.*due in /, 'Due in ');
  });
}

@Component({
  selector: 'cf-avatar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="av" [style.width.px]="size()" [style.height.px]="size()" [style.font-size.px]="size() * 0.38"
    [style.background]="bg()">{{ ini() }}</span>`,
  styles: ['.av{display:inline-flex;align-items:center;justify-content:center;border-radius:50%;color:#fff;font-weight:700;flex:none}'],
})
export class Avatar {
  readonly name = input.required<string>();
  readonly size = input(34);
  protected readonly ini = computed(() => initials(this.name()));
  protected readonly bg = computed(() => `linear-gradient(135deg, hsl(${hue(this.name())} 70% 52%), hsl(${(hue(this.name()) + 40) % 360} 70% 42%))`);
}

@Component({
  selector: 'cf-market',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="chip chip-info" [title]="name()">{{ market() }}</span>`,
})
export class MarketChip {
  readonly market = input.required<Market>();
  protected readonly name = computed(() => MARKET_NAME[this.market()]);
}

@Component({
  selector: 'cf-type',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="ti" [class.prop]="type() === 'Property'" [style.width.px]="size()" [style.height.px]="size()">
    <cf-icon [name]="type() === 'Motor' ? 'car' : 'house'" [size]="size() * 0.55" /></span>`,
  styles: ['.ti{display:inline-flex;align-items:center;justify-content:center;border-radius:11px;background:#e8ecff;color:#4f46e5;flex:none}.ti.prop{background:#e3f6ee;color:#0f8a5f}'],
})
export class TypeIcon {
  readonly type = input.required<ClaimType>();
  readonly size = input(38);
}

/** Customer-facing progress tracker. */
@Component({
  selector: 'cf-stepper',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ol class="steps">
      @for (s of steps(); track s.label; let i = $index) {
        <li [class]="'step ' + s.state">
          <span class="node">
            @switch (s.state) {
              @case ('done') { <cf-icon name="check" [size]="15" /> }
              @case ('bad') { <cf-icon name="x" [size]="15" /> }
              @case ('warn') { <cf-icon name="message" [size]="14" /> }
              @default { {{ i + 1 }} }
            }
          </span>
          <span class="lbl">{{ s.label }}</span>
          @if (i < steps().length - 1) { <span class="bar"></span> }
        </li>
      }
    </ol>`,
  styles: [`
    .steps{display:flex;list-style:none;margin:0;padding:0}
    .step{flex:1;display:flex;flex-direction:column;align-items:center;position:relative;text-align:center;gap:8px;min-width:0}
    .node{width:32px;height:32px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-weight:700;font-size:13px;background:#eef1f8;color:#8590ad;z-index:1;border:2px solid #e4e8f2;transition:.3s}
    .lbl{font-size:12.5px;font-weight:600;color:#8590ad}
    .bar{position:absolute;top:15px;left:calc(50% + 18px);right:calc(-50% + 18px);height:3px;background:#e4e8f2;border-radius:3px}
    .done .node{background:#10b981;border-color:#10b981;color:#fff}.done .lbl{color:#0f1b3d}.done .bar{background:#10b981}
    .current .node{background:#4f46e5;border-color:#4f46e5;color:#fff;box-shadow:0 0 0 5px rgba(79,70,229,.18)}.current .lbl{color:#4f46e5}
    .warn .node{background:#f59e0b;border-color:#f59e0b;color:#fff;box-shadow:0 0 0 5px rgba(245,158,11,.22);animation:pulse 1.8s infinite}.warn .lbl{color:#c76a00}
    .bad .node{background:#ef4444;border-color:#ef4444;color:#fff}.bad .lbl{color:#dc2626}
    .muted .node{background:#eceff5;color:#94a3b8}
    @media (max-width:640px){.lbl{font-size:11px}}
  `],
})
export class ClaimStepper {
  readonly status = input.required<ClaimStatus>();

  protected readonly steps = computed(() => {
    type S = { label: string; state: 'done' | 'current' | 'todo' | 'warn' | 'bad' | 'muted' };
    const labels = ['Reported', 'In review', 'Assessed', 'Decision', 'Paid'];
    const st = this.status();
    const at: Record<ClaimStatus, number> = { Submitted: 1, UnderReview: 1, AwaitingInformation: 1, Assessed: 2, Approved: 4, Settled: 5, Rejected: 3, Withdrawn: 1 };
    const cur = at[st];
    return labels.map((label, i): S => {
      if (st === 'Withdrawn') return { label: i === 1 ? 'Withdrawn' : label, state: i === 0 ? 'done' : 'muted' };
      if (st === 'Rejected') return i < 3 ? { label, state: 'done' } : i === 3 ? { label: 'Declined', state: 'bad' } : { label, state: 'muted' };
      if (i < cur) return { label, state: 'done' };
      if (i === cur) return { label: st === 'AwaitingInformation' && i === 1 ? 'Needs your reply' : label, state: st === 'AwaitingInformation' ? 'warn' : 'current' };
      return { label, state: 'todo' };
    });
  });
}

@Component({
  selector: 'cf-timeline',
  imports: [AgoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ul class="tl">
      @for (e of ordered(); track $index) {
        <li>
          <span class="pt" [style.background]="color(e)"></span>
          <div>
            <div class="msg">{{ e.message }}</div>
            <div class="meta">{{ e.actorName }} · {{ e.at | ago }}</div>
          </div>
        </li>
      }
    </ul>`,
  styles: [`
    .tl{list-style:none;margin:0;padding:0 0 0 8px;position:relative}
    .tl::before{content:'';position:absolute;left:13px;top:8px;bottom:8px;width:2px;background:#e4e8f2}
    li{display:flex;gap:14px;padding:9px 0;position:relative}
    .pt{width:12px;height:12px;border-radius:50%;margin-top:4px;flex:none;box-shadow:0 0 0 4px #fff;z-index:1}
    .msg{font-weight:600}.meta{color:#8590ad;font-size:12.5px;margin-top:2px}
  `],
})
export class Timeline {
  readonly entries = input.required<TimelineEntry[]>();
  protected readonly ordered = computed(() => [...this.entries()].reverse());
  protected color(e: TimelineEntry) { return STATUS_COLOR[e.to]; }
}

@Component({
  selector: 'cf-modal',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'closed.emit()' },
  template: `
    <div class="backdrop" (click)="closed.emit()">
      <div class="dlg" role="dialog" aria-modal="true" [style.max-width.px]="width()" (click)="$event.stopPropagation()">
        <header>
          <h3>{{ title() }}</h3>
          <button class="x" type="button" (click)="closed.emit()" aria-label="Close"><cf-icon name="x" /></button>
        </header>
        <div class="body"><ng-content /></div>
        <footer><ng-content select="[footer]" /></footer>
      </div>
    </div>`,
  styles: [`
    .backdrop{position:fixed;inset:0;background:rgba(11,20,55,.5);backdrop-filter:blur(3px);display:flex;align-items:center;justify-content:center;z-index:100;padding:20px;animation:fadeUp .2s both}
    .dlg{background:#fff;border-radius:18px;width:100%;box-shadow:var(--shadow-lg);max-height:92vh;display:flex;flex-direction:column;animation:fadeUp .25s both}
    header{display:flex;justify-content:space-between;align-items:center;padding:18px 22px;border-bottom:1px solid var(--line)}
    .x{border:0;background:#f1f3fa;border-radius:8px;width:32px;height:32px;cursor:pointer;color:#4a5578;display:flex;align-items:center;justify-content:center}
    .body{padding:20px 22px;overflow:auto;display:flex;flex-direction:column;gap:14px}
    footer{padding:14px 22px;border-top:1px solid var(--line);display:flex;justify-content:flex-end;gap:10px;background:#fafbfe;border-radius:0 0 18px 18px}
  `],
})
export class Modal {
  readonly title = input.required<string>();
  readonly width = input(520);
  readonly closed = output<void>();
}

@Component({
  selector: 'cf-empty',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div class="empty"><span class="ic"><cf-icon [name]="icon()" [size]="26" /></span><h3>{{ title() }}</h3><p>{{ text() }}</p><ng-content /></div>`,
  styles: [`.empty{display:flex;flex-direction:column;align-items:center;text-align:center;gap:8px;padding:44px 20px;color:#4a5578}
    .ic{width:58px;height:58px;border-radius:18px;background:#eef0ff;color:#4f46e5;display:flex;align-items:center;justify-content:center;margin-bottom:6px}
    p{max-width:380px;color:#8590ad}`],
})
export class EmptyState {
  readonly icon = input('inbox');
  readonly title = input.required<string>();
  readonly text = input('');
}
