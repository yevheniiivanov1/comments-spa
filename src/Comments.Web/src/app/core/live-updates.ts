import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { CommentDto } from './models';

export type LiveState = 'connecting' | 'live' | 'offline';

/**
 * WebSocket connection to the comments hub (SignalR, with fallbacks). Emits every comment posted anywhere,
 * reconnects with backoff and exposes the connection state for the header indicator.
 */
@Injectable({ providedIn: 'root' })
export class LiveUpdates {
  readonly state = signal<LiveState>('connecting');
  readonly commentPosted = new Subject<CommentDto>();

  private readonly connection: HubConnection = new HubConnectionBuilder()
    .withUrl('/hubs/comments')
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build();

  constructor() {
    this.connection.on('commentPosted', (comment: CommentDto) => this.commentPosted.next(comment));
    this.connection.onreconnecting(() => this.state.set('connecting'));
    this.connection.onreconnected(() => this.state.set('live'));
    this.connection.onclose(() => {
      this.state.set('offline');
      setTimeout(() => void this.start(), 15000);
    });
    inject(DestroyRef).onDestroy(() => void this.connection.stop());
    void this.start();
  }

  private async start(): Promise<void> {
    if (this.connection.state !== HubConnectionState.Disconnected) {
      return;
    }
    this.state.set('connecting');
    try {
      await this.connection.start();
      this.state.set('live');
    } catch {
      this.state.set('offline');
      setTimeout(() => void this.start(), 10000);
    }
  }
}
