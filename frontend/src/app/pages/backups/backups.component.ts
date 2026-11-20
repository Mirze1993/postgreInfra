import { Component, inject, signal, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { BackupsService, BackupsResponse, WalArchiveStatus } from '../../core/backups.service';

@Component({
  selector: 'app-backups',
  standalone: true,
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h2>Backups</h2>
          <p class="subtitle">pgBackRest · WAL arxivi və backup tarixçəsi</p>
        </div>
        <button class="btn-secondary" (click)="load()">↻ Yenilə</button>
      </div>

      @if (wal(); as w) {
        <div
          class="banner"
          [class.ok]="w.isHealthy"
          [class.bad]="!w.isHealthy"
          [class.warn]="w.isWarning"
        >
          @if (w.isHealthy && !w.isWarning) {
            ✅ WAL arxivi işləyir — son arxivlənmə {{ w.secondsSinceLastArchive }}s əvvəl
          } @else if (w.isWarning) {
            ⚠ WAL arxivi ləngdir — son arxivlənmə {{ w.secondsSinceLastArchive }}s əvvəl
          } @else {
            ❌ WAL arxivi dayanıb — son uğurlu arxivlənmə {{ w.secondsSinceLastArchive }}s əvvəl
          }
        </div>
      }

      @if (data(); as d) {
        <div class="stats-grid">
          <div class="stat-card">
            <div class="stat-label">Stanza</div>
            <div class="stat-value">{{ d.stanza }}</div>
          </div>
          <div class="stat-card">
            <div class="stat-label">Status</div>
            <div class="stat-value" [class.good]="d.status.code === 0">
              {{ d.status.code === 0 ? 'OK' : d.status.message }}
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-label">Backup sayı</div>
            <div class="stat-value">{{ d.backups.length }}</div>
          </div>
          <div class="stat-card">
            <div class="stat-label">WAL aralığı</div>
            <div class="stat-value small">
              {{ d.wal.min?.slice(-8) ?? '—' }} → {{ d.wal.max?.slice(-8) ?? '—' }}
            </div>
          </div>
        </div>

        <div class="card">
          <h3>Backup tarixçəsi</h3>
          @if (d.backups.length === 0) {
            <p class="empty">Heç bir backup yoxdur.</p>
          } @else {
            <table class="data-table">
              <thead>
                <tr>
                  <th>Label</th>
                  <th>Tip</th>
                  <th>Başlama</th>
                  <th>Bitmə</th>
                  <th>Ölçü</th>
                  <th>Repo</th>
                  <th>WAL</th>
                </tr>
              </thead>
              <tbody>
                @for (b of d.backups; track b.label) {
                  <tr>
                    <td>
                      <code>{{ b.label }}</code>
                    </td>
                    <td>
                      <span class="badge" [class]="b.type">{{ b.type.toUpperCase() }}</span>
                    </td>
                    <td class="small">{{ formatDate(b.timestamp.start) }}</td>
                    <td class="small">{{ formatDate(b.timestamp.stop) }}</td>
                    <td>{{ b.sizePretty }}</td>
                    <td class="small">{{ b.repoSizePretty }}</td>
                    <td class="small">{{ shortWal(b.wal.start) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          }
        </div>
      } @else {
        <div class="loading">Yüklənir...</div>
      }
    </div>
  `,
  styles: [
    `
      .banner.warn {
        background: #fef3c7;
        color: #92400e;
      }
      .layout {
        min-height: 100vh;
        background: #f8fafc;
      }
      header {
        background: #0f172a;
        color: #fff;
        padding: 1rem 2rem;
        display: flex;
        align-items: center;
        gap: 2rem;
      }
      header h1 {
        margin: 0;
        font-size: 1.1rem;
      }
      nav {
        display: flex;
        gap: 1rem;
        flex: 1;
      }
      nav a {
        color: #cbd5e1;
        text-decoration: none;
        font-size: 0.9rem;
        padding: 0.3rem 0.6rem;
        border-radius: 4px;
      }
      nav a.active {
        background: #334155;
        color: #fff;
      }
      .user {
        display: flex;
        gap: 1rem;
        align-items: center;
        font-size: 0.85rem;
      }
      .user button {
        background: #334155;
        color: #fff;
        border: 0;
        padding: 0.4rem 0.8rem;
        border-radius: 4px;
        cursor: pointer;
      }
      main {
        padding: 2rem;
      }
      .banner {
        padding: 1rem 1.5rem;
        border-radius: 8px;
        margin-bottom: 1.5rem;
        font-weight: 500;
      }
      .banner.ok {
        background: #dcfce7;
        color: #166534;
      }
      .banner.bad {
        background: #fef2f2;
        color: #dc2626;
      }
      .summary {
        display: grid;
        grid-template-columns: repeat(4, 1fr);
        gap: 1rem;
        margin-bottom: 2rem;
      }
      .stat {
        background: #fff;
        padding: 1rem 1.5rem;
        border-radius: 8px;
        box-shadow: 0 2px 8px rgba(0, 0, 0, 0.05);
        display: flex;
        flex-direction: column;
      }
      .label {
        font-size: 0.75rem;
        color: #64748b;
        text-transform: uppercase;
        letter-spacing: 0.05em;
      }
      .value {
        font-size: 1.2rem;
        color: #0f172a;
        margin-top: 0.25rem;
      }
      .value.small {
        font-size: 0.85rem;
        font-family: monospace;
      }
      .value.good {
        color: #16a34a;
      }
      table {
        width: 100%;
        background: #fff;
        border-collapse: collapse;
        border-radius: 8px;
        overflow: hidden;
        box-shadow: 0 2px 8px rgba(0, 0, 0, 0.05);
      }
      th {
        background: #f1f5f9;
        text-align: left;
        padding: 0.75rem 1rem;
        font-size: 0.75rem;
        text-transform: uppercase;
        color: #475569;
      }
      td {
        padding: 0.75rem 1rem;
        border-top: 1px solid #e2e8f0;
        font-size: 0.9rem;
      }
      td.small {
        font-size: 0.8rem;
        color: #64748b;
        font-family: monospace;
      }
      code {
        background: #f1f5f9;
        padding: 0.15rem 0.4rem;
        border-radius: 4px;
        font-size: 0.8rem;
      }
      .badge {
        padding: 0.15rem 0.5rem;
        border-radius: 4px;
        font-size: 0.75rem;
        font-weight: 600;
      }
      .badge.full {
        background: #dbeafe;
        color: #1e40af;
      }
      .badge.diff {
        background: #fef3c7;
        color: #92400e;
      }
      .badge.incr {
        background: #e0e7ff;
        color: #3730a3;
      }
      .empty {
        text-align: center;
        color: #94a3b8;
        margin-top: 2rem;
      }
    `,
  ],
})
export class BackupsComponent implements OnInit {
  auth = inject(AuthService);
  private svc = inject(BackupsService);
  private router = inject(Router);

  data = signal<BackupsResponse | null>(null);
  wal = signal<WalArchiveStatus | null>(null);

  async ngOnInit() {
    await this.load();
  }

  async load() {
    try {
      const [d, w] = await Promise.all([this.svc.list(), this.svc.walArchive()]);
      this.data.set(d);
      this.wal.set(w);
    } catch {
      /* ignore */
    }
  }

  formatDate(iso: string): string {
    return new Date(iso).toLocaleString('az-AZ', { timeZone: 'UTC' });
  }

  shortWal(wal: string): string {
    // 000000010000000000000002 → ...000002
    return wal.length > 8 ? '…' + wal.slice(-8) : wal;
  }

  logout() {
    this.auth.logout();
    this.router.navigate(['/login']);
  }
}
