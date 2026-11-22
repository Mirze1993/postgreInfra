import { Component, inject, signal, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import {
  BackupsService,
  BackupsResponse,
  WalArchiveStatus,
  RestorePreview,
  RestoreJob,
} from '../../core/backups.service';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-backups',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div class="toolbar-actions">
          @if (auth.auth().role === 'Admin' || auth.auth().role === 'Operator') {
            <button class="btn-secondary" (click)="createBackup('full')" [disabled]="busy()">
              + Full Backup
            </button>
            <button class="btn-secondary" (click)="createBackup('diff')" [disabled]="busy()">
              + Diff
            </button>
            <button class="btn-secondary" (click)="verifyBackup()" [disabled]="busy()">
              ✓ Verify
            </button>
          }
          @if (auth.auth().role === 'Admin') {
            <button class="btn-danger" (click)="openRestoreWizard()">⚠ PITR Restore</button>
          }
        </div>
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
      @if (showRestoreWizard()) {
        <div class="modal-overlay" (click)="closeRestoreWizard()">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h2>⚠ Point-in-Time Recovery</h2>
              <button class="close-btn" (click)="closeRestoreWizard()">×</button>
            </div>

            @if (restoreStep() === 1) {
              <div class="modal-body">
                <div class="alert alert-danger">
                  <strong>XƏBƏRDARLIQ:</strong> Bu əməliyyat klasteri
                  <strong>tam dayandıracaq</strong>. Bütün aktiv bağlantılar kəsiləcək. Bərpa 5–15
                  dəqiqə çəkə bilər.
                </div>

                <label>Bərpa hədəfi (UTC vaxt):</label>
                <input type="datetime-local" [(ngModel)]="restoreTargetTime" class="form-input" />
                <p class="hint">Bu vaxtdan sonrakı bütün yazmalar İTƏCƏK.</p>

                <label>Target action:</label>
                <select [(ngModel)]="restoreTargetAction" class="form-input">
                  <option value="promote">promote (klaster primary kimi qalxır)</option>
                  <option value="pause">pause (recovery-də saxla)</option>
                  <option value="shutdown">shutdown (dayandır)</option>
                </select>

                <button class="btn-primary" (click)="previewRestore()" [disabled]="busy()">
                  {{ busy() ? 'Yoxlanılır...' : 'Preview →' }}
                </button>
              </div>
            }

            @if (restoreStep() === 2 && preview(); as p) {
              <div class="modal-body">
                @if (p.valid) {
                  <div class="alert alert-warning">
                    <strong>⚠ Preview nəticəsi:</strong><br />
                    Backup: <code>{{ p.backupLabel }}</code> ({{ p.backupType }})<br />
                    Backup bitiş vaxtı: <code>{{ p.backupStopTime }}</code><br />
                    Bərpa hədəfi: <code>{{ p.targetTime }}</code><br />
                    Backup-dan sonra: <code>{{ p.ageAfterBackup }}</code> WAL replay ediləcək
                  </div>

                  <div class="alert alert-danger">
                    <strong>Son təsdiq:</strong> Davam etmək üçün aşağıdakı sahəyə
                    <strong>RESTORE</strong> yazın.
                  </div>

                  <input
                    type="text"
                    [(ngModel)]="restoreConfirmation"
                    placeholder="RESTORE"
                    class="form-input"
                  />

                  <div class="modal-actions">
                    <button class="btn-secondary" (click)="restoreStep.set(1)">← Geri</button>
                    <button
                      class="btn-danger"
                      (click)="executeRestore()"
                      [disabled]="restoreConfirmation !== 'RESTORE' || busy()"
                    >
                      {{ busy() ? 'İşə salınır...' : '🔴 Bərpa et' }}
                    </button>
                  </div>
                } @else {
                  <div class="alert alert-danger">{{ p.reason }}</div>
                  <button class="btn-secondary" (click)="restoreStep.set(1)">← Geri</button>
                }
              </div>
            }

            @if (restoreStep() === 3 && currentJob(); as job) {
              <div class="modal-body">
                <div
                  class="alert"
                  [class.alert-success]="job.status === 'completed'"
                  [class.alert-danger]="job.status === 'failed'"
                  [class.alert-info]="job.status === 'running' || job.status === 'pending'"
                >
                  Status: <strong>{{ job.status.toUpperCase() }}</strong>
                  @if (job.status === 'running') {
                    <span class="spinner"></span>
                  }
                </div>

                <label>Canlı log:</label>
                <pre class="live-log">{{ job.log ?? 'Gözlənilir...' }}</pre>

                @if (job.error) {
                  <div class="alert alert-danger">{{ job.error }}</div>
                }

                @if (job.status === 'completed' || job.status === 'failed') {
                  <button class="btn-secondary" (click)="closeRestoreWizard(); load()">
                    Bağla
                  </button>
                }
              </div>
            }
          </div>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .toolbar-actions {
        display: flex;
        gap: 0.5rem;
        flex-wrap: wrap;
      }
      .btn-danger {
        background: #dc2626;
        color: #fff;
        border: 0;
        padding: 0.5rem 1rem;
        border-radius: 6px;
        cursor: pointer;
        font-weight: 500;
      }
      .btn-danger:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }
      .btn-primary {
        background: #2563eb;
        color: #fff;
        border: 0;
        padding: 0.5rem 1rem;
        border-radius: 6px;
        cursor: pointer;
      }

      .modal-overlay {
        position: fixed;
        inset: 0;
        background: rgba(0, 0, 0, 0.6);
        display: grid;
        place-items: center;
        z-index: 1000;
      }
      .modal {
        background: #fff;
        border-radius: 12px;
        width: 90%;
        max-width: 700px;
        max-height: 90vh;
        display: flex;
        flex-direction: column;
      }
      .modal-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: 1.25rem 1.5rem;
        border-bottom: 1px solid #e2e8f0;
      }
      .modal-header h2 {
        margin: 0;
        font-size: 1.1rem;
      }
      .close-btn {
        background: transparent;
        border: 0;
        font-size: 1.5rem;
        cursor: pointer;
        color: #64748b;
      }
      .modal-body {
        padding: 1.5rem;
        overflow-y: auto;
      }
      .modal-actions {
        display: flex;
        justify-content: flex-end;
        gap: 0.5rem;
        margin-top: 1.5rem;
      }
      .form-input {
        width: 100%;
        padding: 0.6rem;
        border: 1px solid #cbd5e1;
        border-radius: 6px;
        margin: 0.5rem 0 1rem;
        font-size: 0.9rem;
      }
      .hint {
        color: #64748b;
        font-size: 0.8rem;
        margin: -0.5rem 0 1rem;
      }
      .alert {
        padding: 0.75rem 1rem;
        border-radius: 6px;
        margin-bottom: 1rem;
        font-size: 0.85rem;
      }
      .alert-danger {
        background: #fef2f2;
        color: #991b1b;
        border-left: 4px solid #dc2626;
      }
      .alert-warning {
        background: #fef3c7;
        color: #92400e;
        border-left: 4px solid #f59e0b;
      }
      .alert-success {
        background: #dcfce7;
        color: #166534;
        border-left: 4px solid #16a34a;
      }
      .alert-info {
        background: #dbeafe;
        color: #1e40af;
        border-left: 4px solid #2563eb;
      }
      .live-log {
        background: #0f172a;
        color: #cbd5e1;
        padding: 1rem;
        border-radius: 6px;
        font-family: ui-monospace, monospace;
        font-size: 0.8rem;
        max-height: 300px;
        overflow-y: auto;
        white-space: pre-wrap;
      }
      .spinner {
        display: inline-block;
        width: 12px;
        height: 12px;
        margin-left: 0.5rem;
        border: 2px solid #2563eb;
        border-top-color: transparent;
        border-radius: 50%;
        animation: spin 1s linear infinite;
      }
      @keyframes spin {
        to {
          transform: rotate(360deg);
        }
      }
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
  showRestoreWizard = signal(false);
  restoreStep = signal<1 | 2 | 3>(1);
  restoreTargetTime = '';
  restoreTargetAction = 'promote';
  restoreConfirmation = '';
  preview = signal<RestorePreview | null>(null);
  currentJob = signal<RestoreJob | null>(null);
  busy = signal(false);
  private jobPollTimer: any;

  // Metodlar
  openRestoreWizard() {
    this.showRestoreWizard.set(true);
    this.restoreStep.set(1);
    this.restoreTargetTime = '';
    this.restoreTargetAction = 'promote';
    this.restoreConfirmation = '';
    this.preview.set(null);
    this.currentJob.set(null);
  }

  closeRestoreWizard() {
    if (this.currentJob()?.status === 'running') {
      if (!confirm('Bərpa davam edir. Bağlamaq istədiyinizə əminsiniz?')) return;
    }
    this.showRestoreWizard.set(false);
    if (this.jobPollTimer) clearInterval(this.jobPollTimer);
  }

  async previewRestore() {
    if (!this.restoreTargetTime) {
      alert('Vaxt seçin');
      return;
    }
    this.busy.set(true);
    try {
      // datetime-local → ISO string (UTC kimi şərh et)
      const dt = new Date(this.restoreTargetTime + 'Z').toISOString();
      const p = await this.svc.previewRestore(dt);
      this.preview.set(p);
      this.restoreStep.set(2);
    } catch (e: any) {
      alert(e.response?.data?.error ?? 'Preview xətası');
    } finally {
      this.busy.set(false);
    }
  }

  async executeRestore() {
    if (this.restoreConfirmation !== 'RESTORE') return;
    this.busy.set(true);
    try {
      const dt = new Date(this.restoreTargetTime + 'Z').toISOString();
      const { jobId } = await this.svc.startRestore(dt, this.restoreTargetAction, 'RESTORE');
      this.restoreStep.set(3);
      this.startPollingJob(jobId);
    } catch (e: any) {
      alert(e.response?.data?.error ?? 'Bərpa başladılmadı');
    } finally {
      this.busy.set(false);
    }
  }

  private startPollingJob(jobId: number) {
    const poll = async () => {
      try {
        const job = await this.svc.getRestoreJob(jobId);
        this.currentJob.set(job);
        if (job.status === 'completed' || job.status === 'failed') {
          clearInterval(this.jobPollTimer);
        }
      } catch {
        /* ignore */
      }
    };
    poll();
    this.jobPollTimer = setInterval(poll, 2000);
  }

  async createBackup(type: 'full' | 'diff' | 'incr') {
    this.busy.set(true);
    try {
      await this.svc.createBackup(type);
      setTimeout(() => this.load(), 3000);
      alert(`${type} backup başladıldı. 5–10 saniyə sonra siyahıda görünəcək.`);
    } catch (e: any) {
      alert(e.response?.data?.error ?? 'Xəta');
    } finally {
      this.busy.set(false);
    }
  }

  async verifyBackup() {
    this.busy.set(true);
    try {
      await this.svc.verify();
      alert('✓ Backup bütövlüyü təsdiqləndi');
    } catch (e: any) {
      alert(e.response?.data?.error ?? 'Verify uğursuz');
    } finally {
      this.busy.set(false);
    }
  }
}
