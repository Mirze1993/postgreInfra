import { Component, inject, signal, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/auth.service';
import axios from 'axios';

interface ClusterOp {
  id: number;
  operationType: string;
  targetNode: string | null;
  status: string;
  requestedBy: string;
  requestedAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  log: string | null;
  error: string | null;
}

interface NodeInfo {
  url: string;
  role: string;
  state: string;
  timeline: number;
}

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h2>Klaster Əməliyyatları</h2>
          <p class="subtitle">Replica idarəetməsi, switchover, node əməliyyatları</p>
        </div>
      </div>

      <!-- Cari klaster vəziyyəti -->
      <div class="card">
        <h3>Cari klaster</h3>
        @if (nodes().length > 0) {
          <div class="node-grid">
            @for (n of nodes(); track n.url) {
              <div class="node-card" [class.leader]="isLeader(n.role)">
                <div class="node-name">{{ n.url.replace('http://', '') }}</div>
                <div class="node-role">{{ isLeader(n.role) ? 'PRIMARY' : 'REPLICA' }}</div>
                <div class="node-state">{{ n.state }}</div>
                <div class="node-actions">
                  @if (auth.auth().role === 'Admin') {
                    <button class="btn-sm" (click)="restartNode(n)">↻ Restart</button>
                    @if (!isLeader(n.role)) {
                      <button class="btn-sm danger" (click)="openRemove(n)">Sil</button>
                    }
                  }
                </div>
              </div>
            }
          </div>
        }
      </div>

      <!-- Əməliyyat düymələri -->
      @if (auth.auth().role === 'Admin') {
        <div class="card" style="margin-top:1rem;">
          <h3>Əməliyyatlar</h3>
          <div class="action-buttons">
            <button class="btn-primary" (click)="addReplica()">+ Replica əlavə et</button>
            <button class="btn-warning" (click)="openSwitchover()">⇄ Switchover</button>
          </div>
        </div>
      }

      <!-- Əməliyyat tarixçəsi -->
      <div class="card" style="margin-top:1rem;">
        <h3>Son əməliyyatlar</h3>
        @if (operations().length === 0) {
          <p class="empty">Heç bir əməliyyat yoxdur.</p>
        } @else {
          <table class="data-table">
            <thead>
              <tr>
                <th>ID</th>
                <th>Tip</th>
                <th>Node</th>
                <th>Status</th>
                <th>İstifadəçi</th>
                <th>Tarix</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              @for (op of operations(); track op.id) {
                <tr>
                  <td>{{ op.id }}</td>
                  <td>{{ op.operationType }}</td>
                  <td>{{ op.targetNode ?? '—' }}</td>
                  <td>
                    <span class="badge" [class]="op.status">{{ op.status }}</span>
                  </td>
                  <td>{{ op.requestedBy }}</td>
                  <td class="small">{{ formatDate(op.requestedAt) }}</td>
                  <td>
                    <button class="btn-sm" (click)="viewLog(op)">Log</button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>

      <!-- Switchover modal -->
      @if (showSwitchover()) {
        <div class="modal-overlay" (click)="showSwitchover.set(false)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h2>⇄ Switchover</h2>
              <button class="close-btn" (click)="showSwitchover.set(false)">×</button>
            </div>
            <div class="modal-body">
              <div class="alert alert-warning">
                Leader dəyişdiriləcək. Qısa fasilə (5-10 saniyə) olacaq.
              </div>
              <label>Cari leader:</label>
              <input [value]="currentLeader()" readonly class="form-input" />
              <label>Yeni leader:</label>
              <select [(ngModel)]="switchoverCandidate" class="form-input">
                @for (n of replicas(); track n.url) {
                  <option [value]="nodeName(n.url)">{{ nodeName(n.url) }}</option>
                }
              </select>
              <label>Təsdiq üçün "SWITCHOVER" yazın:</label>
              <input [(ngModel)]="switchoverConfirm" class="form-input" />
              <div class="modal-actions">
                <button class="btn-secondary" (click)="showSwitchover.set(false)">Ləğv et</button>
                <button
                  class="btn-danger"
                  [disabled]="switchoverConfirm !== 'SWITCHOVER' || busy()"
                  (click)="executeSwitchover()"
                >
                  {{ busy() ? 'İşləyir...' : 'Switchover et' }}
                </button>
              </div>
            </div>
          </div>
        </div>
      }

      <!-- Remove replica modal -->
      @if (showRemove(); as node) {
        <div class="modal-overlay" (click)="showRemove.set(null)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h2>Replica sil</h2>
              <button class="close-btn" (click)="showRemove.set(null)">×</button>
            </div>
            <div class="modal-body">
              <div class="alert alert-danger">
                <strong>{{ node }}</strong> node-u silinəcək. Davam edək?
              </div>
              <label>Təsdiq üçün "REMOVE" yazın:</label>
              <input [(ngModel)]="removeConfirm" class="form-input" />
              <div class="modal-actions">
                <button class="btn-secondary" (click)="showRemove.set(null)">Ləğv et</button>
                <button
                  class="btn-danger"
                  [disabled]="removeConfirm !== 'REMOVE' || busy()"
                  (click)="executeRemove(node)"
                >
                  {{ busy() ? 'Silinir...' : 'Sil' }}
                </button>
              </div>
            </div>
          </div>
        </div>
      }

      <!-- Log modal -->
      @if (viewingOp(); as op) {
        <div class="modal-overlay" (click)="viewingOp.set(null)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h2>Əməliyyat #{{ op.id }} — {{ op.operationType }}</h2>
              <button class="close-btn" (click)="viewingOp.set(null)">×</button>
            </div>
            <div class="modal-body">
              <p>
                <strong>Status:</strong>
                <span class="badge" [class]="op.status">{{ op.status }}</span>
              </p>
              @if (op.error) {
                <div class="alert alert-danger">{{ op.error }}</div>
              }
              <pre class="live-log">{{ op.log ?? 'Log yoxdur' }}</pre>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .page {
        max-width: 1200px;
      }
      .page-header {
        margin-bottom: 1.5rem;
      }
      .page-header h2 {
        margin: 0 0 0.25rem;
        font-size: 1.5rem;
      }
      .subtitle {
        margin: 0;
        color: #64748b;
        font-size: 0.9rem;
      }
      .card {
        background: #fff;
        padding: 1.5rem;
        border-radius: 10px;
        border: 1px solid #e2e8f0;
      }
      .card h3 {
        margin: 0 0 1rem;
        font-size: 1rem;
      }

      .node-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
        gap: 0.75rem;
      }
      .node-card {
        border: 1px solid #e2e8f0;
        border-radius: 8px;
        padding: 1rem;
      }
      .node-card.leader {
        border-left: 4px solid #16a34a;
        background: #f0fdf4;
      }
      .node-name {
        font-family: monospace;
        font-weight: 600;
        font-size: 0.85rem;
      }
      .node-role {
        font-size: 0.7rem;
        color: #64748b;
        text-transform: uppercase;
        margin: 0.25rem 0;
      }
      .node-state {
        font-size: 0.8rem;
        color: #475569;
      }
      .node-actions {
        display: flex;
        gap: 0.25rem;
        margin-top: 0.5rem;
      }

      .action-buttons {
        display: flex;
        gap: 0.5rem;
        flex-wrap: wrap;
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
      .btn-warning {
        background: #f59e0b;
        color: #fff;
        border: 0;
        padding: 0.5rem 1rem;
        border-radius: 6px;
        cursor: pointer;
        font-weight: 500;
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
      .btn-danger:disabled,
      .btn-warning:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }
      .btn-secondary {
        background: #f1f5f9;
        color: #475569;
        border: 1px solid #e2e8f0;
        padding: 0.5rem 1rem;
        border-radius: 6px;
        cursor: pointer;
      }
      .btn-sm {
        border: 0;
        padding: 0.3rem 0.6rem;
        border-radius: 4px;
        cursor: pointer;
        font-size: 0.8rem;
        background: #f1f5f9;
      }
      .btn-sm.danger {
        background: #fee2e2;
        color: #991b1b;
      }

      .badge {
        padding: 0.15rem 0.5rem;
        border-radius: 4px;
        font-size: 0.75rem;
        font-weight: 600;
      }
      .badge.pending {
        background: #fef3c7;
        color: #92400e;
      }
      .badge.running {
        background: #dbeafe;
        color: #1e40af;
      }
      .badge.completed {
        background: #dcfce7;
        color: #166534;
      }
      .badge.failed {
        background: #fee2e2;
        color: #991b1b;
      }

      .data-table {
        width: 100%;
        border-collapse: collapse;
      }
      .data-table th {
        text-align: left;
        padding: 0.75rem;
        background: #f1f5f9;
        font-size: 0.75rem;
        text-transform: uppercase;
        color: #475569;
      }
      .data-table td {
        padding: 0.75rem;
        border-top: 1px solid #e2e8f0;
        font-size: 0.85rem;
      }
      .data-table td.small {
        font-size: 0.8rem;
        color: #64748b;
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
        max-width: 600px;
        max-height: 90vh;
        overflow-y: auto;
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
      }
      .alert {
        padding: 0.75rem 1rem;
        border-radius: 6px;
        margin-bottom: 1rem;
        font-size: 0.85rem;
      }
      .alert-warning {
        background: #fef3c7;
        color: #92400e;
        border-left: 4px solid #f59e0b;
      }
      .alert-danger {
        background: #fef2f2;
        color: #991b1b;
        border-left: 4px solid #dc2626;
      }
      .live-log {
        background: #0f172a;
        color: #cbd5e1;
        padding: 1rem;
        border-radius: 6px;
        font-family: monospace;
        font-size: 0.8rem;
        max-height: 400px;
        overflow-y: auto;
        white-space: pre-wrap;
      }
      .empty {
        text-align: center;
        color: #94a3b8;
        padding: 2rem;
      }
    `,
  ],
})
export class SettingsComponent implements OnInit {
  auth = inject(AuthService);
  private http = axios.create({ baseURL: '/api' });

  operations = signal<ClusterOp[]>([]);
  nodes = signal<NodeInfo[]>([]);
  showSwitchover = signal(false);
  showRemove = signal<NodeInfo | null>(null);
  viewingOp = signal<ClusterOp | null>(null);
  busy = signal(false);

  switchoverCandidate = '';
  switchoverConfirm = '';
  removeConfirm = '';

  private get headers() {
    const t = this.auth.auth().token;
    return t ? { Authorization: `Bearer ${t}` } : {};
  }

  async ngOnInit() {
    await this.load();
  }

  async load() {
    try {
      const [ops, cluster] = await Promise.all([
        this.http.get('/cluster-operations', { headers: this.headers }),
        this.http.get('/cluster/status', { headers: this.headers }),
      ]);
      this.operations.set(ops.data);
      this.nodes.set(cluster.data.nodes ?? []);
    } catch {
      /* ignore */
    }
  }

  isLeader(role: string): boolean {
    return ['primary', 'master', 'leader'].includes(role.toLowerCase());
  }

  nodeName(url: string): string {
    return url.split(':')[1].replace('//', '');
  }

  currentLeader(): string {
    const l = this.nodes().find((n) => this.isLeader(n.role));
    return l ? this.nodeName(l.url) : '—';
  }

  replicas(): NodeInfo[] {
    return this.nodes().filter((n) => !this.isLeader(n.role));
  }

  async addReplica() {
    if (!confirm('Yeni replica əlavə edilsin?')) return;
    this.busy.set(true);
    try {
      const r = await this.http.post(
        '/cluster-operations/replicas/add',
        {},
        { headers: this.headers },
      );
      this.pollOperation(r.data.operationId);
    } catch (e: any) {
      alert(e.response?.data?.error ?? 'Xəta');
      this.busy.set(false);
    }
  }

  openSwitchover() {
    this.switchoverCandidate = this.replicas()[0]?.url ? this.nodeName(this.replicas()[0].url) : '';
    this.switchoverConfirm = '';
    this.showSwitchover.set(true);
  }

  async executeSwitchover() {
    this.busy.set(true);
    try {
      const r = await this.http.post(
        '/cluster-operations/switchover',
        {
          leader: this.currentLeader(),
          candidate: this.switchoverCandidate,
          confirmation: 'SWITCHOVER',
        },
        { headers: this.headers },
      );
      this.showSwitchover.set(false);
      this.pollOperation(r.data.operationId);
    } catch (e: any) {
      alert(e.response?.data?.error ?? 'Xəta');
      this.busy.set(false);
    }
  }

  openRemove(name: NodeInfo) {
    this.showRemove.set(name);
    this.removeConfirm = '';
  }

  async executeRemove(name: NodeInfo) {
    this.busy.set(true);
    try {
      const r = await this.http.post(
        '/cluster-operations/replicas/remove',
        {
          nodeName: name,
          confirmation: 'REMOVE',
        },
        { headers: this.headers },
      );
      this.showRemove.set(null);
      this.pollOperation(r.data.operationId);
    } catch (e: any) {
      alert(e.response?.data?.error ?? 'Xəta');
      this.busy.set(false);
    }
  }

  async restartNode(node: NodeInfo) {
    const name = this.nodeName(node.url);
    if (!confirm(`${name} restart edilsin?`)) return;
    try {
      await this.http.post(
        `/cluster-operations/nodes/${name}/restart`,
        {},
        { headers: this.headers },
      );
      await this.load();
    } catch (e: any) {
      alert(e.response?.data?.error ?? 'Xəta');
    }
  }

  private async pollOperation(id: number) {
    const poll = async () => {
      try {
        const r = await this.http.get(`/cluster-operations/${id}`, { headers: this.headers });
        const op = r.data as ClusterOp;
        if (op.status === 'completed' || op.status === 'failed') {
          this.busy.set(false);
          await this.load();
          if (op.status === 'failed') {
            alert(`Əməliyyat uğursuz: ${op.error}`);
          }
          this.viewingOp.set(op);
          return;
        }
        setTimeout(poll, 2000);
      } catch {
        this.busy.set(false);
      }
    };
    poll();
  }

  async viewLog(op: ClusterOp) {
    const r = await this.http.get(`/cluster-operations/${op.id}`, { headers: this.headers });
    this.viewingOp.set(r.data);
  }

  formatDate(iso: string) {
    return new Date(iso).toLocaleString('az-AZ', { timeZone: 'UTC' });
  }
}
