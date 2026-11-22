import { Component, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  template: `
    <aside class="sidebar" [class.collapsed]="collapsed()">
      <div class="brand">
        <div class="logo">PG</div>
        @if (!collapsed()) {
          <div class="brand-text">
            <div class="name">Control Plane</div>
            <div class="sub">PostgreSQL HA</div>
          </div>
        }
        <button
          class="collapse-btn"
          (click)="collapsed.set(!collapsed())"
          [title]="collapsed() ? 'Aç' : 'Bağla'"
        >
          <svg viewBox="0 0 24 24" width="18" height="18">
            @if (collapsed()) {
              <path fill="currentColor" d="M10 6L8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z" />
            } @else {
              <path fill="currentColor" d="M14 6l-6 6 6 6 1.41-1.41L10.83 12l4.58-4.59z" />
            }
          </svg>
        </button>
      </div>

      <nav class="nav">
        <div class="nav-section" [class.hidden]="collapsed()">ƏSAS</div>
        <a routerLink="/dashboard" routerLinkActive="active" class="nav-item">
          <svg viewBox="0 0 24 24" width="20" height="20">
            <path
              fill="currentColor"
              d="M3 13h8V3H3v10zm0 8h8v-6H3v6zm10 0h8V11h-8v10zm0-18v6h8V3h-8z"
            />
          </svg>
          @if (!collapsed()) {
            <span>Dashboard</span>
          }
        </a>
        <a routerLink="/backups" routerLinkActive="active" class="nav-item">
          <svg viewBox="0 0 24 24" width="20" height="20">
            <path
              fill="currentColor"
              d="M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-7 14H7v-2h5v2zm5-4H7v-2h10v2zm0-4H7V7h10v2z"
            />
          </svg>
          @if (!collapsed()) {
            <span>Backups</span>
          }
        </a>
        <a routerLink="/migrations" routerLinkActive="active" class="nav-item">
          <svg viewBox="0 0 24 24" width="20" height="20">
            <path
              fill="currentColor"
              d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z"
            />
          </svg>
          @if (!collapsed()) {
            <span>Migrations</span>
          }
        </a>

        <div class="nav-section" [class.hidden]="collapsed()">SİSTEM</div>
        <a routerLink="/settings" routerLinkActive="active" class="nav-item">
          <svg viewBox="0 0 24 24" width="20" height="20">
            <path
              fill="currentColor"
              d="M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94L14.4 2.81c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z"
            />
          </svg>
          @if (!collapsed()) {
            <span>Settings</span>
          }
        </a>
      </nav>

      <div class="footer">
        @if (!collapsed()) {
          <div class="version">v0.3.0 · Phase 3</div>
        }
      </div>
    </aside>
  `,
  styles: [
    `
      .sidebar {
        width: 260px;
        min-width: 260px;
        height: 100%;
        min-height: 100vh;
        background: #0f172a;
        color: #cbd5e1;
        display: flex;
        flex-direction: column;
        transition:
          width 0.2s ease,
          min-width 0.2s ease;
        flex-shrink: 0;
        border-right: 1px solid #1e293b;
        overflow: hidden;
        box-sizing: border-box;
      }

      .sidebar.collapsed {
        width: 68px;
        min-width: 68px;
      }

      .brand {
        display: flex;
        align-items: center;
        gap: 0.75rem;
        padding: 1rem 0.75rem;
        border-bottom: 1px solid #1e293b;
        min-height: 64px;
        box-sizing: border-box;
        flex-shrink: 0;
      }

      .nav {
        flex: 1;
        min-height: 0;
        padding: 0.5rem 0;
        overflow-y: auto;
        overflow-x: hidden;
      }

      .nav-item {
        display: flex;
        align-items: center;
        gap: 0.75rem;
        padding: 0.7rem 1rem;
        margin: 0.15rem 0.5rem;
        border-radius: 6px;
        color: #cbd5e1;
        text-decoration: none;
        font-size: 0.875rem;
        transition: all 0.15s;
        white-space: nowrap;
      }

      .footer {
        padding: 1rem;
        border-top: 1px solid #1e293b;
        flex-shrink: 0;
      }

      .sidebar.collapsed .nav-item {
        justify-content: center;
        padding: 0.7rem;
      }
    `,
  ],
})
export class SidebarComponent {
  collapsed = signal(false);
}
