import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastHost } from './layout/shell';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ToastHost],
  template: `<router-outlet /><cf-toasts />`,
})
export class App {}
