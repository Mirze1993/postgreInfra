import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { SidebarComponent } from './sidebar.component';
import { HeaderComponent } from './header.component';

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [RouterOutlet, SidebarComponent, HeaderComponent],
  template: `
    <div class="app-layout">
      <app-sidebar />
      <div class="main-area">
        <app-header />
        <main class="content">
          <router-outlet />
        </main>
      </div>
    </div>
  `,
  styles: [
    `
      .app-layout {
        display: flex;
        height: 100vh;
        background: #f8fafc;
        overflow: hidden;
      }
      .main-area {
        flex: 1;
        display: flex;
        flex-direction: column;
        min-width: 0;
        overflow: hidden;
      }
      .content {
        flex: 1;
        overflow-y: auto;
        padding: 2rem;
      }
      @media (max-width: 768px) {
        .content {
          padding: 1rem;
        }
      }
    `,
  ],
})
export class LayoutComponent {}
