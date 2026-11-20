import { Component, inject, signal, computed, OnInit, HostListener } from '@angular/core';
import { Router, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs/operators';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-header',
  standalone: true,
  template: `
    <header class="header">
      <div class="left">
        <h1 class="page-title">{{ pageTitle() }}</h1>
        <div class="breadcrumb">
          PG Control Plane / <span>{{ pageTitle() }}</span>
        </div>
      </div>

      <div class="right">
        <div class="status-indicator">
          <span class="dot online"></span>
          <span class="status-text">Cluster Online</span>
        </div>

        <div class="user-menu">
          <button class="user-btn" (click)="showMenu.set(!showMenu())">
            <div class="avatar">{{ userInitial() }}</div>
            <div class="user-info">
              <div class="name">{{ auth.auth().username }}</div>
              <div class="role">{{ auth.auth().role }}</div>
            </div>
            <svg viewBox="0 0 24 24" width="14" height="14">
              <path fill="currentColor" d="M7 10l5 5 5-5z" />
            </svg>
          </button>

          @if (showMenu()) {
            <div class="dropdown">
              <button (click)="logout()">
                <svg viewBox="0 0 24 24" width="14" height="14">
                  <path
                    fill="currentColor"
                    d="M17 7l-1.41 1.41L18.17 11H8v2h10.17l-2.58 2.58L17 17l5-5zM4 5h8V3H4c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h8v-2H4V5z"
                  />
                </svg>
                Çıxış
              </button>
            </div>
          }
        </div>
      </div>
    </header>
  `,
  styles: [
    `
      .header {
        height: 64px;
        background: #fff;
        border-bottom: 1px solid #e2e8f0;
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: 0 2rem;
        flex-shrink: 0;
      }
      .page-title {
        margin: 0;
        font-size: 1.1rem;
        color: #0f172a;
        font-weight: 600;
      }
      .breadcrumb {
        font-size: 0.75rem;
        color: #94a3b8;
        margin-top: 0.15rem;
      }
      .breadcrumb span {
        color: #475569;
      }
      .right {
        display: flex;
        align-items: center;
        gap: 1.5rem;
      }
      .status-indicator {
        display: flex;
        align-items: center;
        gap: 0.5rem;
        font-size: 0.8rem;
        color: #475569;
        padding: 0.35rem 0.75rem;
        background: #f8fafc;
        border-radius: 20px;
      }
      .dot {
        width: 8px;
        height: 8px;
        border-radius: 50%;
        background: #94a3b8;
      }
      .dot.online {
        background: #16a34a;
        box-shadow: 0 0 0 3px #dcfce7;
      }
      .user-menu {
        position: relative;
      }
      .user-btn {
        display: flex;
        align-items: center;
        gap: 0.75rem;
        background: transparent;
        border: 0;
        cursor: pointer;
        padding: 0.4rem 0.75rem;
        border-radius: 8px;
      }
      .user-btn:hover {
        background: #f1f5f9;
      }
      .avatar {
        width: 32px;
        height: 32px;
        border-radius: 50%;
        background: linear-gradient(135deg, #3b82f6, #6366f1);
        color: #fff;
        display: grid;
        place-items: center;
        font-weight: 600;
        font-size: 0.85rem;
      }
      .user-info {
        text-align: left;
      }
      .user-info .name {
        font-size: 0.85rem;
        font-weight: 500;
        color: #0f172a;
      }
      .user-info .role {
        font-size: 0.7rem;
        color: #64748b;
      }
      .dropdown {
        position: absolute;
        right: 0;
        top: calc(100% + 0.5rem);
        background: #fff;
        border: 1px solid #e2e8f0;
        border-radius: 8px;
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
        min-width: 160px;
        padding: 0.25rem;
        z-index: 10;
      }
      .dropdown button {
        width: 100%;
        text-align: left;
        padding: 0.5rem 0.75rem;
        background: transparent;
        border: 0;
        cursor: pointer;
        border-radius: 4px;
        font-size: 0.85rem;
        color: #dc2626;
        display: flex;
        align-items: center;
        gap: 0.5rem;
      }
      .dropdown button:hover {
        background: #fef2f2;
      }
    `,
  ],
})
export class HeaderComponent implements OnInit {
  auth = inject(AuthService);
  private router = inject(Router);
  showMenu = signal(false);
  currentUrl = signal('/dashboard');

  pageTitle = computed(() => {
    const url = this.currentUrl();
    if (url.includes('dashboard')) return 'Dashboard';
    if (url.includes('backups')) return 'Backups';
    if (url.includes('migrations')) return 'Migrations';
    if (url.includes('settings')) return 'Settings';
    return '';
  });

  userInitial = computed(() => {
    const u = this.auth.auth().username;
    return u ? u.charAt(0).toUpperCase() : '?';
  });

  ngOnInit() {
    this.router.events
      .pipe(filter((e) => e instanceof NavigationEnd))
      .subscribe((e: any) => this.currentUrl.set(e.urlAfterRedirects));
  }

  @HostListener('document:click', ['$event'])
  onDocClick(e: MouseEvent) {
    const target = e.target as HTMLElement;
    if (!target.closest('.user-menu')) this.showMenu.set(false);
  }

  logout() {
    this.auth.logout();
    this.router.navigate(['/login']);
  }
}
