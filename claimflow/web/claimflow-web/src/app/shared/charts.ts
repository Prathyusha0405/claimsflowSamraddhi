import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

export interface Slice { label: string; value: number; color: string }

@Component({
  selector: 'cf-donut',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="wrap">
      <svg viewBox="0 0 200 200" [style.width.px]="size()" [style.height.px]="size()" role="img" aria-label="Distribution chart">
        <circle cx="100" cy="100" r="70" fill="none" stroke="#eef1f8" stroke-width="26" />
        @for (a of arcs(); track a.label) {
          <circle class="seg" cx="100" cy="100" r="70" fill="none" [attr.stroke]="a.color" stroke-width="26"
                  [attr.stroke-dasharray]="a.dash" [attr.stroke-dashoffset]="a.offset" transform="rotate(-90 100 100)" />
        }
        <text x="100" y="101" text-anchor="middle" class="cv">{{ centerValue() }}</text>
        <text x="100" y="121" text-anchor="middle" class="cl">{{ centerLabel() }}</text>
      </svg>
      <ul class="legend">
        @for (s of data(); track s.label) {
          <li><i [style.background]="s.color"></i><span>{{ s.label }}</span><b>{{ s.value }}</b></li>
        }
      </ul>
    </div>`,
  styles: [`
    .wrap{display:flex;align-items:center;gap:22px;flex-wrap:wrap}
    .seg{transition:stroke-dasharray .8s ease,stroke-dashoffset .8s ease}
    .cv{font-size:30px;font-weight:800;fill:#0f1b3d}.cl{font-size:11.5px;fill:#8590ad;font-weight:600;letter-spacing:.06em;text-transform:uppercase}
    .legend{list-style:none;margin:0;padding:0;display:flex;flex-direction:column;gap:7px;flex:1;min-width:150px}
    li{display:flex;align-items:center;gap:9px;font-size:13px;color:#4a5578}
    li i{width:10px;height:10px;border-radius:3px;flex:none}li span{flex:1}li b{color:#0f1b3d;font-variant-numeric:tabular-nums}
  `],
})
export class Donut {
  readonly data = input.required<Slice[]>();
  readonly size = input(170);
  readonly centerValue = input('');
  readonly centerLabel = input('');

  protected readonly arcs = computed(() => {
    const total = this.data().reduce((s, d) => s + d.value, 0);
    const C = 2 * Math.PI * 70;
    let cum = 0;
    return this.data().filter(d => d.value > 0).map(d => {
      const len = total ? (d.value / total) * C : 0;
      const arc = { label: d.label, color: d.color, dash: `${Math.max(0, len - 2.5)} ${C}`, offset: -cum };
      cum += len;
      return arc;
    });
  });
}

export interface BarRow { label: string; value: number; display: string; sub?: string; color?: string }

@Component({
  selector: 'cf-hbars',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="bars">
      @for (r of rows(); track r.label) {
        <div class="r">
          <div class="top"><span>{{ r.label }}</span><b>{{ r.display }}</b></div>
          <div class="track"><div class="fill" [style.width.%]="pct(r.value)" [style.background]="r.color || 'linear-gradient(90deg,#4f46e5,#7c3aed)'"></div></div>
          @if (r.sub) { <div class="sub">{{ r.sub }}</div> }
        </div>
      }
    </div>`,
  styles: [`
    .bars{display:flex;flex-direction:column;gap:15px}
    .top{display:flex;justify-content:space-between;font-size:13.5px;margin-bottom:6px}.top span{font-weight:600}.top b{font-variant-numeric:tabular-nums}
    .track{height:10px;background:#eef1f8;border-radius:99px;overflow:hidden}
    .fill{height:100%;border-radius:99px;transition:width .9s cubic-bezier(.2,.8,.2,1)}
    .sub{font-size:12px;color:#8590ad;margin-top:4px}
  `],
})
export class HBars {
  readonly rows = input.required<BarRow[]>();
  private readonly max = computed(() => Math.max(1, ...this.rows().map(r => r.value)));
  protected pct(v: number) { return Math.max(2, (v / this.max()) * 100); }
}

export interface Series { name: string; color: string; values: number[] }

let nextId = 0;

@Component({
  selector: 'cf-area',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="box" (mouseleave)="hover.set(null)">
      <div class="legend">@for (s of series(); track s.name) { <span><i [style.background]="s.color"></i>{{ s.name }}</span> }</div>
      <svg [attr.viewBox]="'0 0 ' + W + ' ' + H" preserveAspectRatio="none" role="img" aria-label="Trend chart">
        <defs>
          @for (s of series(); track s.name; let i = $index) {
            <linearGradient [attr.id]="uid + i" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0" [attr.stop-color]="s.color" stop-opacity="0.28" /><stop offset="1" [attr.stop-color]="s.color" stop-opacity="0.02" />
            </linearGradient>
          }
        </defs>
        @for (t of ticks(); track t.y) {
          <line [attr.x1]="L" [attr.x2]="W - R" [attr.y1]="t.y" [attr.y2]="t.y" stroke="#e8ecf5" stroke-dasharray="3 4" />
          <text [attr.x]="L - 8" [attr.y]="t.y + 4" text-anchor="end" class="ax">{{ t.label }}</text>
        }
        @for (s of paths(); track s.name; let i = $index) {
          <path [attr.d]="s.area" [attr.fill]="'url(#' + uid + i + ')'" />
          <path [attr.d]="s.line" fill="none" [attr.stroke]="s.color" stroke-width="2.6" stroke-linejoin="round" stroke-linecap="round" />
        }
        @for (l of xlabels(); track l.x) { <text [attr.x]="l.x" [attr.y]="H - 6" text-anchor="middle" class="ax">{{ l.text }}</text> }
        @if (hover() !== null) {
          <line [attr.x1]="xAt(hover()!)" [attr.x2]="xAt(hover()!)" [attr.y1]="T" [attr.y2]="H - B" stroke="#4f46e5" stroke-opacity=".35" />
          @for (s of series(); track s.name) { <circle [attr.cx]="xAt(hover()!)" [attr.cy]="yAt(s.values[hover()!])" r="4.5" fill="#fff" [attr.stroke]="s.color" stroke-width="2.5" /> }
        }
        @for (l of labels(); track $index; let i = $index) {
          <rect [attr.x]="xAt(i) - step() / 2" [attr.y]="T" [attr.width]="step()" [attr.height]="H - B - T" fill="transparent" (mouseenter)="hover.set(i)" />
        }
      </svg>
      @if (hover() !== null) {
        <div class="tip" [style.left.%]="(xAt(hover()!) / W) * 100">
          <b>{{ labels()[hover()!] }}</b>
          @for (s of series(); track s.name) { <div><i [style.background]="s.color"></i>{{ s.name }}: <strong>{{ s.values[hover()!] }}</strong></div> }
        </div>
      }
    </div>`,
  styles: [`
    .box{position:relative}svg{width:100%;height:230px;display:block}
    .ax{font-size:11px;fill:#8590ad}
    .legend{display:flex;gap:16px;justify-content:flex-end;font-size:12.5px;color:#4a5578;margin-bottom:4px}
    .legend i,.tip i{display:inline-block;width:9px;height:9px;border-radius:3px;margin-right:6px}
    .tip{position:absolute;top:34px;transform:translateX(-50%);background:#0f1b3d;color:#fff;padding:8px 12px;border-radius:10px;font-size:12.5px;pointer-events:none;white-space:nowrap;box-shadow:0 8px 20px rgba(15,27,61,.25)}
    .tip b{display:block;margin-bottom:3px}
  `],
})
export class AreaChart {
  readonly labels = input.required<string[]>();
  readonly series = input.required<Series[]>();

  protected readonly W = 640; protected readonly H = 230;
  protected readonly L = 34; protected readonly R = 10; protected readonly T = 12; protected readonly B = 26;
  protected readonly uid = `ag${nextId++}-`;
  protected readonly hover = signal<number | null>(null);

  private readonly top = computed(() => {
    const m = Math.max(0, ...this.series().flatMap(s => s.values));
    if (m <= 4) return 4;
    const p = 10 ** Math.floor(Math.log10(m));
    return Math.ceil(m / p) * p;
  });
  protected readonly step = computed(() => (this.W - this.L - this.R) / Math.max(1, this.labels().length - 1));
  protected xAt(i: number) { return this.L + i * this.step(); }
  protected yAt(v: number) { return this.T + (this.H - this.T - this.B) * (1 - v / this.top()); }

  protected readonly ticks = computed(() =>
    [0, 1, 2, 3, 4].map(k => ({ y: this.yAt((this.top() * k) / 4), label: String(Math.round((this.top() * k) / 4)) })));

  protected readonly xlabels = computed(() => {
    const n = this.labels().length;
    const every = Math.max(1, Math.ceil(n / 7));
    return this.labels().map((text, i) => ({ text, x: this.xAt(i), show: i % every === 0 || i === n - 1 })).filter(l => l.show);
  });

  protected readonly paths = computed(() => this.series().map(s => {
    const pts = s.values.map((v, i) => [this.xAt(i), this.yAt(v)] as const);
    const line = pts.map(([x, y], i) => `${i ? 'L' : 'M'}${x.toFixed(1)},${y.toFixed(1)}`).join(' ');
    const base = this.H - this.B;
    const area = pts.length ? `${line} L${pts[pts.length - 1][0].toFixed(1)},${base} L${pts[0][0].toFixed(1)},${base} Z` : '';
    return { name: s.name, color: s.color, line, area };
  }));
}
