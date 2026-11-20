import { Component, inject, signal, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { MigrationsService, MigrationScript } from '../../core/migrations.service';

@Component({
  selector: 'app-migrations',
  standalone: true,
  imports: [FormsModule, RouterLink],
  template: `
    <div class="layout">
      <main>
        <div class="toolbar">
          <h2>Miqrasiya skriptləri</h2>
          <button class="btn-primary" (click)="showCreate.set(!showCreate())">
            {{ showCreate() ? 'Ləğv et' : '+ Yeni skript' }}
          </button>
        </div>

        <!-- Create form -->
        @if (showCreate()) {
          <div class="create-card">
            <h3>Yeni miqrasiya skripti</h3>
            <div class="row">
              <label
                >Versiya (V1, V2...)
                <input [(ngModel)]="newVersion" placeholder="V1" />
              </label>
              <label
                >Təsvir
                <input [(ngModel)]="newDescription" placeholder="create_users_table" />
              </label>
            </div>
            <label
              >SQL
              <textarea [(ngModel)]="newSql" rows="10" placeholder="CREATE TABLE ..."></textarea>
            </label>
            <label
              >Rollback SQL (opsional)
              <textarea [(ngModel)]="newRollback" rows="4" placeholder="DROP TABLE ..."></textarea>
            </label>
            @if (createError()) {
              <div class="error">{{ createError() }}</div>
            }
            <button class="btn-primary" (click)="create()" [disabled]="creating()">
              {{ creating() ? 'Yaradılır...' : 'Yarat (draft)' }}
            </button>
          </div>
        }

        <!-- List -->
        @if (items().length === 0) {
          <p class="empty">Heç bir miqrasiya skripti yoxdur.</p>
        } @else {
          <table>
            <thead>
              <tr>
                <th>Versiya</th>
                <th>Təsvir</th>
                <th>Status</th>
                <th>Yaradan</th>
                <th>Tarix</th>
                <th>Əməliyyat</th>
              </tr>
            </thead>
            <tbody>
              @for (m of items(); track m.id) {
                <tr [class.expanded]="expanded()?.id === m.id">
                  <td>
                    <code>{{ m.version }}</code>
                  </td>
                  <td>{{ m.description }}</td>
                  <td>
                    <span class="badge" [class]="m.status">{{ statusLabel(m.status) }}</span>
                  </td>
                  <td class="small">{{ m.createdBy }}</td>
                  <td class="small">{{ formatDate(m.createdAt) }}</td>
                  <td class="actions">
                    <button class="btn-sm" (click)="toggleExpand(m)">
                      {{ expanded()?.id === m.id ? 'Bağla' : 'Bax' }}
                    </button>

                    @if (m.status === 'draft') {
                      <button class="btn-sm primary" (click)="submit(m.id)">Təqdim et</button>
                    }
                    @if (m.status === 'pending' && auth.auth().role === 'Admin') {
                      <button class="btn-sm success" (click)="approve(m.id)">Təsdiqlə</button>
                      <button class="btn-sm danger" (click)="reject(m.id)">Rədd et</button>
                    }
                    @if (m.status === 'approved' && auth.auth().role === 'Admin') {
                      <button class="btn-sm danger" (click)="applyToProd(m.id)">
                        Production-a tətbiq et
                      </button>
                    }
                  </td>
                </tr>
                @if (expanded()?.id === m.id) {
                  <tr>
                    <td colspan="6" class="detail">
                      <h4>SQL:</h4>
                      <pre>{{ m.sqlContent }}</pre>
                      @if (m.rollbackSql) {
                        <h4>Rollback:</h4>
                        <pre>{{ m.rollbackSql }}</pre>
                      }
                      @if (m.validationLog) {
                        <h4>Validasiya log:</h4>
                        <pre class="log">{{ m.validationLog }}</pre>
                      }
                      @if (m.applyLog) {
                        <h4>Tətbiq log:</h4>
                        <pre class="log">{{ m.applyLog }}</pre>
                      }
                    </td>
                  </tr>
                }
              }
            </tbody>
          </table>
        }
      </main>
    </div>
  `,
  styles: [
    `
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
      .toolbar {
        display: flex;
        justify-content: space-between;
        align-items: center;
        margin-bottom: 1.5rem;
      }
      .toolbar h2 {
        margin: 0;
      }
      .btn-primary {
        background: #2563eb;
        color: #fff;
        border: 0;
        padding: 0.5rem 1rem;
        border-radius: 6px;
        cursor: pointer;
        font-weight: 500;
      }
      .btn-primary:disabled {
        opacity: 0.6;
        cursor: wait;
      }
      .create-card {
        background: #fff;
        padding: 1.5rem;
        border-radius: 8px;
        margin-bottom: 1.5rem;
        box-shadow: 0 2px 8px rgba(0, 0, 0, 0.05);
      }
      .create-card h3 {
        margin: 0 0 1rem;
        font-size: 1rem;
      }
      .create-card label {
        display: block;
        margin-bottom: 1rem;
        font-size: 0.9rem;
        font-weight: 500;
      }
      .create-card input,
      .create-card textarea {
        width: 100%;
        padding: 0.6rem;
        margin-top: 0.3rem;
        border: 1px solid #cbd5e1;
        border-radius: 6px;
        font-family: inherit;
      }
      .create-card textarea {
        font-family: ui-monospace, monospace;
        font-size: 0.85rem;
      }
      .row {
        display: grid;
        grid-template-columns: 1fr 2fr;
        gap: 1rem;
      }
      .error {
        background: #fef2f2;
        color: #dc2626;
        padding: 0.6rem;
        border-radius: 6px;
        margin-bottom: 1rem;
        font-size: 0.85rem;
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
      }
      td.actions {
        display: flex;
        gap: 0.4rem;
        flex-wrap: wrap;
      }
      td.detail {
        background: #f8fafc;
        padding: 1.5rem;
      }
      td.detail h4 {
        margin: 1rem 0 0.5rem;
        font-size: 0.85rem;
        color: #475569;
        text-transform: uppercase;
      }
      td.detail h4:first-child {
        margin-top: 0;
      }
      td.detail pre {
        background: #fff;
        padding: 1rem;
        border-radius: 6px;
        font-size: 0.8rem;
        font-family: ui-monospace, monospace;
        overflow-x: auto;
        max-height: 300px;
        white-space: pre-wrap;
      }
      td.detail pre.log {
        background: #0f172a;
        color: #cbd5e1;
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
      .badge.draft {
        background: #f3f4f6;
        color: #4b5563;
      }
      .badge.validating {
        background: #dbeafe;
        color: #1e40af;
      }
      .badge.validation_failed {
        background: #fee2e2;
        color: #991b1b;
      }
      .badge.pending {
        background: #fef3c7;
        color: #92400e;
      }
      .badge.approved {
        background: #e0e7ff;
        color: #3730a3;
      }
      .badge.applied {
        background: #dcfce7;
        color: #166534;
      }
      .badge.rejected {
        background: #f3f4f6;
        color: #4b5563;
      }
      .btn-sm {
        border: 0;
        padding: 0.3rem 0.6rem;
        border-radius: 4px;
        cursor: pointer;
        font-size: 0.8rem;
        background: #f1f5f9;
      }
      .btn-sm.primary {
        background: #2563eb;
        color: #fff;
      }
      .btn-sm.success {
        background: #16a34a;
        color: #fff;
      }
      .btn-sm.danger {
        background: #dc2626;
        color: #fff;
      }
      .empty {
        text-align: center;
        color: #94a3b8;
        margin-top: 2rem;
      }
    `,
  ],
})
export class MigrationsComponent implements OnInit {
  auth = inject(AuthService);
  private svc = inject(MigrationsService);
  private router = inject(Router);

  items = signal<MigrationScript[]>([]);
  expanded = signal<MigrationScript | null>(null);
  showCreate = signal(false);
  creating = signal(false);
  createError = signal<string | null>(null);

  newVersion = '';
  newDescription = '';
  newSql = '';
  newRollback = '';

  async ngOnInit() {
    await this.load();
  }

  async load() {
    try {
      this.items.set(await this.svc.list());
    } catch {
      /* ignore */
    }
  }

  toggleExpand(m: MigrationScript) {
    this.expanded.set(this.expanded()?.id === m.id ? null : m);
  }

  async create() {
    this.creating.set(true);
    this.createError.set(null);
    try {
      await this.svc.create({
        version: this.newVersion,
        description: this.newDescription,
        sqlContent: this.newSql,
        rollbackSql: this.newRollback || undefined,
      });
      this.newVersion = '';
      this.newDescription = '';
      this.newSql = '';
      this.newRollback = '';
      this.showCreate.set(false);
      await this.load();
    } catch (e: any) {
      this.createError.set(e.response?.data?.error ?? 'Yaradılmadı');
    } finally {
      this.creating.set(false);
    }
  }

  async submit(id: number) {
    if (!confirm('Skripti təqdim edək? Staging-də validasiya olunacaq.')) return;
    try {
      await this.svc.submit(id);
      await this.load();
    } catch (e: any) {
      alert(e.response?.data?.error ?? 'Xəta');
    }
  }

  async approve(id: number) {
    try {
      await this.svc.approve(id);
      await this.load();
    } catch (e: any) {
      alert(e.response?.data?.error ?? 'Xəta');
    }
  }

  async reject(id: number) {
    const reason = prompt('Rədd səbəbi:');
    if (!reason) return;
    try {
      await this.svc.reject(id, reason);
      await this.load();
    } catch (e: any) {
      alert(e.response?.data?.error ?? 'Xəta');
    }
  }

  async applyToProd(id: number) {
    if (!confirm('BU ƏMƏLİYYAT PRODUCTION-DA TƏTBİQ EDƏCƏK. Davam edək?')) return;
    try {
      await this.svc.apply(id);
      await this.load();
      alert('Uğurla tətbiq edildi!');
    } catch (e: any) {
      alert(e.response?.data?.error ?? 'Xəta');
    }
  }

  statusLabel(s: string): string {
    return (
      (
        {
          draft: 'Draft',
          validating: 'Validasiya',
          validation_failed: 'Uğursuz',
          pending: 'Gözləyir',
          approved: 'Təsdiqləndi',
          applied: 'Tətbiq edildi',
          rejected: 'Rədd edildi',
        } as Record<string, string>
      )[s] ?? s
    );
  }

  formatDate(iso: string): string {
    return new Date(iso).toLocaleString('az-AZ', { timeZone: 'UTC' });
  }

  logout() {
    this.auth.logout();
    this.router.navigate(['/login']);
  }
}
