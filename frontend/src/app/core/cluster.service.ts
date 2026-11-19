import { inject, Injectable, signal } from '@angular/core';
import axios from 'axios';
import { AuthService } from './auth.service';

export interface ClusterStatus {
  leaderExists: boolean;
  leaderName: string | null;
  hasQuorum: boolean;
  nodes: Array<{ url: string; state: string; role: string; timeline: number }>;
  replication: {
    hasReplica: boolean;
    replayLagBytes: number;
    replayLagSeconds: number;
    state: string;
    syncState: string;
  };
}

@Injectable({ providedIn: 'root' })
export class ClusterService {
  private auth = inject(AuthService);
  private _status = signal<ClusterStatus | null>(null);
  readonly status = this._status.asReadonly();

  private client = axios.create({
    baseURL: '/api',
    headers: {},
  });

  private get headers() {
    const token = this.auth.auth().token;
    return token ? { Authorization: `Bearer ${token}` } : {};
  }

  async fetchStatus() {
    const res = await this.client.get('/cluster/status', { headers: this.headers });
    this._status.set(res.data);
  }

  async fetchNodes() {
    const res = await this.client.get('/cluster/nodes', { headers: this.headers });
    return res.data;
  }

  async fetchMetrics() {
    const res = await this.client.get('/cluster/metrics', { headers: this.headers });
    return res.data;
  }
}
