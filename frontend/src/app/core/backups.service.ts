import { Injectable, inject } from '@angular/core';
import axios from 'axios';
import { AuthService } from './auth.service';

export interface Backup {
  label: string;
  type: 'full' | 'diff' | 'incr';
  timestamp: { start: string; stop: string };
  wal: { start: string; stop: string };
  sizeBytes: number;
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
}
