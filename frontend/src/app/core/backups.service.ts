import { Injectable, inject } from '@angular/core';
import axios from 'axios';
import { AuthService } from './auth.service';

export interface Backup {
  label: string;
  type: 'full' | 'diff' | 'incr';
  timestamp: { start: string; stop: string };
  wal: { start: string; stop: string };
  sizeBytes: number;
  repoSizePretty: string;
  hasError: boolean;
  sizePretty: string;
  repoSizeBytes: number;
  references: string[];
}

export interface BackupsResponse {
  stanza: string;
  status: { code: number; message: string };
  backups: Backup[];
  wal: { min: string | null; max: string | null };
}

export interface WalArchiveStatus {
  failedCount: number;
  secondsSinceLastArchive: number;
  isHealthy: boolean;
  status: string;
  isWarning: boolean;
}

export interface RestorePreview {
  valid: boolean;
  reason: string | null;
  backupLabel: string | null;
  backupType: string | null;
  backupStopTime: string | null;
  targetTime: string | null;
  ageAfterBackup: string | null;
  walStart: string | null;
  walStop: string | null;
  warning: string | null;
}

export interface RestoreJob {
  id: number;
  backupLabel: string | null;
  targetTime: string;
  targetAction: string;
  status: 'pending' | 'running' | 'completed' | 'failed' | 'cancelled';
  requestedBy: string;
  requestedAt: string;
  confirmedAt: string | null;
  startedAt: string | null;
  finishedAt: string | null;
  log: string | null;
  error: string | null;
}

@Injectable({ providedIn: 'root' })
export class BackupsService {
  private auth = inject(AuthService);
  private client = axios.create({ baseURL: '/api' });

  private get headers() {
    const token = this.auth.auth().token;
    return token ? { Authorization: `Bearer ${token}` } : {};
  }

  async list(): Promise<BackupsResponse> {
    const res = await this.client.get<BackupsResponse>('/backups', {
      headers: this.headers,
    });
    return res.data;
  }

  async walArchive(): Promise<WalArchiveStatus> {
    const res = await this.client.get<WalArchiveStatus>('/backups/wal-archive', {
      headers: this.headers,
    });
    return res.data;
  }
  async createBackup(type: 'full' | 'diff' | 'incr'): Promise<void> {
    await this.client.post('/backups', { type }, { headers: this.headers });
  }

  async verify(): Promise<void> {
    await this.client.post('/backups/verify', {}, { headers: this.headers });
  }

  async previewRestore(targetTime: string): Promise<RestorePreview> {
    const res = await this.client.post<RestorePreview>(
      '/backups/restore/preview',
      { targetTime },
      { headers: this.headers },
    );
    return res.data;
  }

  async startRestore(
    targetTime: string,
    targetAction: string,
    confirmation: string,
  ): Promise<{ jobId: number }> {
    const res = await this.client.post(
      '/backups/restore',
      { targetTime, targetAction, confirmation },
      { headers: this.headers },
    );
    return res.data;
  }

  async getRestoreJob(jobId: number): Promise<RestoreJob> {
    const res = await this.client.get<RestoreJob>(`/backups/restore/${jobId}`, {
      headers: this.headers,
    });
    return res.data;
  }

  async listRestoreJobs(): Promise<RestoreJob[]> {
    const res = await this.client.get<RestoreJob[]>('/backups/restore', { headers: this.headers });
    return res.data;
  }
}
